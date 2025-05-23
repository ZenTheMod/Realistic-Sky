﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RealisticSky.Common.DataStructures;
using RealisticSky.Common.Utilities;
using RealisticSky.Content.Atmosphere;
using RealisticSky.Content.Clouds;
using RealisticSky.Content.NightSky;
using RealisticSky.Content.Sun;
using Terraria;
using Terraria.Graphics.Effects;
using Terraria.ModLoader;

namespace RealisticSky.Content;

public class RealisticSkyManager : CustomSky
{
    private bool skyActive;

    /// <summary>
    /// Whether this sky should be disabled this frame.
    /// </summary>
    internal static bool TemporarilyDisabled;

    /// <summary>
    /// The general opacity of this sky.
    /// </summary>
    internal static new float Opacity;

    /// <summary>
    /// The identifier key for this sky.
    /// </summary>
    public const string SkyKey = "RealisticSky:Sky";

    /// <summary>
    /// How long, in frames, that sunrises should last for the purposes of this sky's visuals.
    /// </summary>
    public const int DawnDuration = 6700;

    /// <summary>
    /// How long, in frames, that sunsets should last for the purposes of this sky's visuals.
    /// </summary>
    public const int DuskDuration = 6700;

    /// <summary>
    /// Where the space interpolant begins as a 0-1 ratio.
    /// </summary>
    /// <remarks>
    /// In this context, "ratio" refers to the world height. A value of 0.05 would, for example, correspond to the upper 5% of the world's height.
    /// </remarks>
    public const float SpaceYRatioStart = 0.074f;

    /// <summary>
    /// Where the space interpolant is considered at its maximum as a 0-1 ratio.
    /// </summary>
    /// <remarks>
    /// In this context, "ratio" refers to the world height. A value of 0.05 would, for example, correspond to the upper 5% of the world's height.
    /// </remarks>
    public const float SpaceYRatioEnd = 0.024f;

    /// <summary>
    /// The brightness of the sky, calculated from the average of the RGB components in <see cref="Main.ColorOfTheSkies"/>. This does not account for brightness values created as a result of <see cref="NightSkyBrightnessManager.NightSkyBrightnessBoost"/>.
    /// </summary>
    public static float SkyBrightness => (Main.ColorOfTheSkies.R + Main.ColorOfTheSkies.G + Main.ColorOfTheSkies.B) / 765f - NightSkyBrightnessManager.NightSkyBrightnessBoost;

    /// <summary>
    /// The intensity of light based on dawn or dusk as a 0-1 ratio.
    /// </summary>
    public static float SunlightIntensityByTime
    {
        get
        {
            // Return 0 immediately if it's night time, since night time does not count towards dawn or dusk.
            if (!Main.dayTime)
                return 0f;

            // If the time is less than the dawn duration, interpolate between it.
            // This will make the slope of this function go up from 0 to 1.
            float dawnDuskInterpolant = Utils.GetLerpValue(0f, DawnDuration, (float)Main.time, true);

            // If the time is greater than the dawn duration, account for the dusk duration instead.
            // Since this is a multiplication, it will be multiplying the previous result (which is 1, since again, this only happens after dawn is over) by
            // the dusk interpolant. This will make the value's slow go down as dusk's progression increases, until eventually it's 0 again by night time.
            if (Main.time > DawnDuration)
                dawnDuskInterpolant *= Utils.GetLerpValue((float)Main.dayLength, (float)Main.dayLength - DuskDuration, (float)Main.time, true);

            return dawnDuskInterpolant;
        }
    }

    /// <summary>
    /// How far up in space the player is, on a 0-1 interpolant.
    /// </summary>
    public static float SpaceHeightInterpolant
    {
        get
        {
            SkyPlayerSnapshot player = SkyPlayerSnapshot.TakeSnapshot();
            float worldYInterpolant = player.Center.Y / player.MaxTilesY / 16f;
            float spaceInterpolant = Utils.GetLerpValue(SpaceYRatioStart, SpaceYRatioEnd, worldYInterpolant, true);

            // Apply a smoothstep function to the space interpolant, since that helps make the transitions more natural.
            return MathHelper.SmoothStep(0f, 1f, spaceInterpolant);
        }
    }

    /// <summary>
    /// The rotation of the star scene as a result of gradual planetary rotation.
    /// </summary>
    public static float StarViewRotation => DaysCounterSystem.DayCounter * -2.3f;

    /// <summary>
    /// Whether the overall scene can render.
    /// </summary>
    /// 
    /// <remarks>
    /// This depends on the player's position in the world, preventing rendering when below a certain position, to ensure that the sky does not appear in the underworld.<br>
    /// </remarks>
    public static bool CanRender
    {
        get
        {
            SkyPlayerSnapshot player = SkyPlayerSnapshot.TakeSnapshot();
            if (player.InEternalGardenSubworld)
                return false;

            return player.Center.Y / 16f < player.WorldSurface + 250;
        }
    }

    public override void Draw(SpriteBatch spriteBatch, float minDepth, float maxDepth)
    {
        // Safety check to ensure that the mod doesn't attempt to render anything when mods are unloading on the title screen.
        if (RealisticSkyConfig.Instance is null)
            return;

        if (TemporarilyDisabled)
            return;

        // Prevent rendering beyond the back layer.
        if (maxDepth < float.MaxValue || minDepth >= float.MaxValue)
            return;

        // Prevent rendering where otherwise not permitting.
        if (!CanRender)
            return;

        spriteBatch.End(out SpriteBatchSnapshot snapshot);
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearWrap, DepthStencilState.None, Main.Rasterizer, null, Main.BackgroundViewMatrix.EffectMatrix);

        // Draw the atmosphere.
        AtmosphereRenderer.RenderFromTarget();

        // Draw bloom over the sun.
        if (Main.dayTime)
            SunRenderer.Render(1f - SunlightIntensityByTime);

        // Draw clouds
        CloudsRenderer.Render();

        // Return to standard drawing.
        spriteBatch.End();
        spriteBatch.Begin(in snapshot);
    }

    public override void Update(GameTime gameTime)
    {
        // Increase or decrease the opacity of this sky based on whether it's active or not, stopping at 0-1 bounds.
        Opacity = MathUtils.Saturate(Opacity + skyActive.ToDirectionInt() * 0.1f);

        SunRenderer.SunBloomOpacity = MathUtils.Saturate(SunRenderer.SunBloomOpacity + 0.02f);
    }

    #region Boilerplate
    public override void Deactivate(params object[] args) => skyActive = false;

    public override void Reset() => skyActive = false;

    public override bool IsActive() => skyActive || Opacity > 0f;

    public override void Activate(Vector2 position, params object[] args) => skyActive = true;

    // Ensure that cloud opacities are not disturbed by this sky effect.
    public override float GetCloudAlpha() => 1f;
    #endregion Boilerplate
}
