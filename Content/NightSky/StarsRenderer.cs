﻿using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RealisticSky.Assets;
using RealisticSky.Common.DataStructures;
using RealisticSky.Common.Utilities;
using RealisticSky.Content.Atmosphere;
using RealisticSky.Content.Sun;
using RealisticSky.Core.CrossCompatibility.Inbound;
using Terraria;
using Terraria.GameContent;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;
using SpecialStar = RealisticSky.Common.DataStructures.Star;

namespace RealisticSky.Content.NightSky;

public class StarsRenderer : ModSystem
{
    /// <summary>
    /// The set of all stars in the sky.
    /// </summary>
    internal static SpecialStar[] Stars;

    /// <summary>
    /// The vertex buffer that contains all star information.
    /// </summary>
    internal static VertexBuffer StarVertexBuffer;

    /// <summary>
    /// The index buffer that contains all vertex pointers for <see cref="StarVertexBuffer"/>.
    /// </summary>
    internal static IndexBuffer StarIndexBuffer;

    /// <summary>
    /// The minimum brightness that a star can be at as a result of twinkling.
    /// </summary>
    public const float MinTwinkleBrightness = 0.2f;

    /// <summary>
    /// The maximum brightness that a star can be at as a result of twinkling.
    /// </summary>
    public const float MaxTwinkleBrightness = 3.37f;

    /// <summary>
    /// The identifier key for the sky's star shader.
    /// </summary>
    public const string StarShaderKey = "RealisticSky:StarShader";

    public override void OnModLoad()
    {
        // Initialize the star shader.
        GameShaders.Misc[StarShaderKey] = new MiscShaderData(ModContent.Request<Effect>("RealisticSky/Assets/Effects/StarPrimitiveShader"), "AutoloadPass");

        // Generate stars.
        GenerateStars(RealisticSkyConfig.Instance.NightSkyStarCount);
    }

    internal static void GenerateStars(int starCount, bool ignoreIfSameCount = true)
    {
        if (ignoreIfSameCount && Stars?.Length == starCount)
            return;

        Stars = new SpecialStar[starCount];
        if (starCount <= 0)
            return;

        for (int i = 0; i < Stars.Length; i++)
        {
            StarProfile profile = new(Main.rand);
            Color color = StarProfile.TemperatureToColor(profile.Temperature);
            color.A = 0;

            float latitude = Main.rand.NextFloat(-MathHelper.PiOver2, MathHelper.PiOver2) * MathF.Sqrt(Main.rand.NextFloat());
            float longitude = Main.rand.NextFloat(-MathHelper.Pi, MathHelper.Pi);
            float radius = profile.Scale * 3.1f;
            Stars[i] = new(latitude, longitude, color * MathF.Pow(radius / 6f, 1.5f), radius);
        }

        Main.QueueMainThreadAction(RegenerateBuffers);
    }

    internal static void RegenerateBuffers()
    {
        RegenerateVertexBuffer();
        RegenerateIndexBuffer();
    }

    internal static void RegenerateVertexBuffer()
    {
        // Initialize the star buffer if necessary.
        StarVertexBuffer?.Dispose();
        StarVertexBuffer = new VertexBuffer(Main.instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, Stars.Length * 4, BufferUsage.WriteOnly);

        // Generate vertex data.
        VertexPositionColorTexture[] vertices = new VertexPositionColorTexture[Stars.Length * 4];
        for (int i = 0; i < Stars.Length; i++)
        {
            // Acquire vertices for the star.
            Quad<VertexPositionColorTexture> quad = Stars[i].GenerateVertices(1f);

            int bufferIndex = i * 4;
            vertices[bufferIndex] = quad.TopLeft;
            vertices[bufferIndex + 1] = quad.TopRight;
            vertices[bufferIndex + 2] = quad.BottomRight;
            vertices[bufferIndex + 3] = quad.BottomLeft;
        }

        // Send the vertices to the buffer.
        StarVertexBuffer.SetData(vertices);
    }

    internal static void RegenerateIndexBuffer()
    {
        // Initialize the star buffer if necessary.
        StarIndexBuffer?.Dispose();
        StarIndexBuffer = new(Main.instance.GraphicsDevice, IndexElementSize.ThirtyTwoBits, Stars.Length * 6, BufferUsage.WriteOnly);

        // Generate index data.
        int[] indices = new int[Stars.Length * 6];
        for (int i = 0; i < Stars.Length; i++)
        {
            int bufferIndex = i * 6;
            int vertexIndex = i * 4;
            indices[bufferIndex] = vertexIndex;
            indices[bufferIndex + 1] = vertexIndex + 1;
            indices[bufferIndex + 2] = vertexIndex + 2;
            indices[bufferIndex + 3] = vertexIndex + 2;
            indices[bufferIndex + 4] = vertexIndex + 3;
            indices[bufferIndex + 5] = vertexIndex;
        }

        StarIndexBuffer.SetData(indices);
    }

    internal static Matrix CalculatePerspectiveMatrix()
    {
        // Rotate stars as time passes in the world.
        Matrix rotation = Matrix.CreateRotationZ(RealisticSkyManager.StarViewRotation);

        // Project the stars onto the screen.
        float height = Main.instance.GraphicsDevice.Viewport.Height / (float)Main.instance.GraphicsDevice.Viewport.Width;
        Matrix projection = Matrix.CreateOrthographicOffCenter(-1f, 1f, height, -height, -1f, 0f);

        // Zoom in slightly on the stars, so that the sphere does not abruptly end at the bounds of the screen.
        Matrix screenStretch = Matrix.CreateScale(1.15f, 1.15f, 1f);

        // Combine matrices together.
        return rotation * projection * screenStretch;
    }

    public static void Render(float opacity, Matrix backgroundMatrix)
    {
        // Don't waste resources rendering anything if there are no stars to draw.
        if (RealisticSkyConfig.Instance.NightSkyStarCount <= 0)
            return;

        // Make vanilla's stars disappear. They are not needed.
        // This only applies if the player is on the surface so that the shimmer stars are not interfered with.
        // TODO -- Consider making custom stars for the Aether?
        SkyPlayerSnapshot player = SkyPlayerSnapshot.TakeSnapshot();
        for (int i = 0; i < Main.maxStars; i++)
        {
            if (Main.star[i] is null)
                continue;

            Main.star[i].hidden = player.Center.Y <= player.WorldSurface * 16f && !CalamityModCompatibility.InAstralBiome(Main.LocalPlayer);
        }

        // Calculate the star opacity. If it's zero, don't waste resources rendering anything.
        float starOpacity = MathUtils.Saturate(MathF.Pow(1f - Main.atmo, 3f) + MathF.Pow(1f - RealisticSkyManager.SkyBrightness, 5f)) * opacity;
        if (starOpacity <= 0f)
            return;

        
        Effect starShader = EffectsRegistry.StarPrimitiveShader.Value;
        if (starShader?.IsDisposed ?? true)
            return;

        GraphicsDevice gd = Main.instance.GraphicsDevice;

        // Prepare the star shader.
        Vector2 screenSize = new(gd.Viewport.Width, gd.Viewport.Height);
        starShader.Parameters["opacity"]?.SetValue(starOpacity);
        starShader.Parameters["projection"]?.SetValue(CalculatePerspectiveMatrix() * backgroundMatrix);
        starShader.Parameters["globalTime"]?.SetValue(Main.GlobalTimeWrappedHourly * 0.9f);
        starShader.Parameters["sunPosition"]?.SetValue(Main.dayTime ? SunPositionSaver.SunPosition : Vector2.One * 50000f);
        starShader.Parameters["invertedGravity"]?.SetValue(player.InvertedGravity);
        starShader.Parameters["minTwinkleBrightness"]?.SetValue(MinTwinkleBrightness);
        starShader.Parameters["maxTwinkleBrightness"]?.SetValue(MaxTwinkleBrightness);
        starShader.Parameters["distanceFadeoff"]?.SetValue(Main.eclipse ? 0.11f : 1f);
        starShader.Parameters["screenSize"]?.SetValue(screenSize);
        starShader.CurrentTechnique.Passes[0].Apply();

        // Request the atmosphere target.
        AtmosphereRenderer.AtmosphereTarget?.Request();

        // Supply textures.
        gd.Textures[1] = TexturesRegistry.BloomCircle.Value;
        gd.SamplerStates[1] = SamplerState.LinearWrap;

        // Supply the atmophere target content if it is ready, otherwise use a solid color.
        gd.Textures[2] = AtmosphereRenderer.AtmosphereTarget?.IsReady ?? true ? AtmosphereRenderer.AtmosphereTarget?.GetTarget() : TextureAssets.MagicPixel.Value;
        gd.SamplerStates[2] = SamplerState.LinearClamp;
        gd.RasterizerState = RasterizerState.CullNone;

        // Render the stars.
        gd.Indices = StarIndexBuffer;
        gd.SetVertexBuffer(StarVertexBuffer);
        gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, StarVertexBuffer.VertexCount, 0, StarIndexBuffer.IndexCount / 3);
        gd.SetVertexBuffer(null);
        gd.Indices = null;
    }
}
