#ifndef DC_NORMAL_INCLUDED
#define DC_NORMAL_INCLUDED

#include "DC_Density.hlsl"


// ============================================================
// NORMAL
//
// Same finite-difference approach as CPU.
//
// h = max(step * 0.25, 0.001)
// ============================================================

float3 EstimateNormal(
    float3 position)
{
    float h =
        max(
            Step * 0.25f,
            0.001f
        );


    float dx =
        SampleDensity(
            position +
            float3(
                h,
                0.0f,
                0.0f
            )
        )
        -
        SampleDensity(
            position -
            float3(
                h,
                0.0f,
                0.0f
            )
        );


    float dy =
        SampleDensity(
            position +
            float3(
                0.0f,
                h,
                0.0f
            )
        )
        -
        SampleDensity(
            position -
            float3(
                0.0f,
                h,
                0.0f
            )
        );


    float dz =
        SampleDensity(
            position +
            float3(
                0.0f,
                0.0f,
                h
            )
        )
        -
        SampleDensity(
            position -
            float3(
                0.0f,
                0.0f,
                h
            )
        );


    float3 normal =
        float3(
            dx,
            dy,
            dz
        );


    if (dot(normal, normal) < EPSILON)
    {
        return float3(
            0.0f,
            1.0f,
            0.0f
        );
    }


    return normalize(
        normal
    );
}

#endif