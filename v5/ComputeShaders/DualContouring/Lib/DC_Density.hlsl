#ifndef DC_DENSITY_INCLUDED
#define DC_DENSITY_INCLUDED

#include "DC_Common.hlsl"
#include "../../Noise/TerrainNoisePerlin.hlsl"

// ============================================================
// SIGN CHANGE
// ============================================================

bool IsSignChange(
    float a,
    float b)
{
    return
        (a < 0.0f && b >= 0.0f) ||
        (a >= 0.0f && b < 0.0f);
}

// ============================================================
// DENSITY
// ============================================================

float SampleDensity(
    float3 position)
{
    float sphereDensity =
        distance(
            position,
            PlanetCenter
        )
        -
        PlanetRadius;

    // Fast path.
    // This also means NoiseHeight = 0
    // really disables noise calculations.
    if (NoiseHeight == 0.0f)
    {
        return sphereDensity;
    }

    float3 direction =
        normalize(
            position -
            PlanetCenter
        );

    float noiseValue =
        TerrainNoisePerlin3D(
            direction,
            NoiseFrequency,
            (uint) NoiseSeed
        );

    return
        sphereDensity -
        noiseValue *
        NoiseHeight;
}

#endif