sampler starTexture : register(s1);
sampler atmosphereTexture : register(s2);

bool invertedGravity;
float opacity;
float glowIntensity;
float globalTime;
float distanceFadeoff;
float minTwinkleBrightness;
float maxTwinkleBrightness;
float2 sunPosition;
float2 screenSize;
matrix projection;

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

VertexShaderOutput VertexShaderFunction(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput) 0;
    float4 pos = mul(input.Position, projection);
    output.Position = pos;
    
    output.Color = input.Color;
    output.TextureCoordinates = input.TextureCoordinates;

    return output;
}


float4 PixelShaderFunction(VertexShaderOutput input) : COLOR0
{
    // Calculate various coordinates in advance.
    float2 coords = input.TextureCoordinates;
    float2 position = input.Position.xy;
    float2 screenCoords = position / screenSize;
    
    // Account for the pesky gravity potions...
    if (invertedGravity)
    {
        position.y = screenSize.y - position.y;
        screenCoords.y = 1 - screenCoords.y;
    }
    
    // Calculate the base color of the star.
    float4 color = input.Color * tex2D(starTexture, coords);
    
    // Add an inner glow to the texture with a twinkle.
    float2 centerOffset = coords - 0.5;
    float distanceSqrFromCenter = dot(centerOffset, centerOffset);
    float twinkle = lerp(minTwinkleBrightness, maxTwinkleBrightness, cos(globalTime + color.b * 12 + color.r * 13) * 0.5 + 0.5);
    float glow = (color.r + color.b) * twinkle / (distanceSqrFromCenter * 4 + 2);
    
    // Calculate the opacity, getting weaker near the sun and when behind the atmosphere.
    float distanceSqrFromSun = dot(position - sunPosition, position - sunPosition);
    float atmosphereInterpolant = dot(tex2D(atmosphereTexture, screenCoords).rgb, 0.333);
    float localOpacity = saturate(opacity - smoothstep(57600, 21500, distanceSqrFromSun / distanceFadeoff) - atmosphereInterpolant * 2.05);
    
    return (color + glow) * localOpacity;
}

technique Technique1
{
    pass AutoloadPass
    {
        VertexShader = compile vs_3_0 VertexShaderFunction();
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
