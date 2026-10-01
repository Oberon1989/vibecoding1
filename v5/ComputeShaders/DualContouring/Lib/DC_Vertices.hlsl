#ifndef DC_VERTICES_INCLUDED
#define DC_VERTICES_INCLUDED

#include "DC_Common.hlsl"
#include "DC_Density.hlsl"
#include "DC_Normal.hlsl"
#include "DC_QEF.hlsl"


// ============================================================
// BUILD VERTEX
// ============================================================

void DC_BuildVertex(
    uint3 id)
{
    int x =
        (int) id.x;

    int y =
        (int) id.y;

    int z =
        (int) id.z;


    if (x >= CellCount ||
        y >= CellCount ||
        z >= CellCount)
    {
        return;
    }


    uint localVertexIndex =
        GetVertexIndex(
            x,
            y,
            z
        );


    // ========================================================
    // CLEAR QEF SLOT
    // ========================================================
    //
    // Every valid dispatched cell initializes its own QEF slot.
    // This prevents stale data when the scratch buffer is reused
    // for another chunk.
    //
    // ========================================================

    QefData emptyQef;


    emptyQef.IntersectionSum =
        float3(
            0.0f,
            0.0f,
            0.0f
        );


    emptyQef.ConstraintPointSum =
        float3(
            0.0f,
            0.0f,
            0.0f
        );


    emptyQef.A00 =
        0.0f;

    emptyQef.A01 =
        0.0f;

    emptyQef.A02 =
        0.0f;

    emptyQef.A11 =
        0.0f;

    emptyQef.A12 =
        0.0f;

    emptyQef.A22 =
        0.0f;


    emptyQef.B0 =
        0.0f;

    emptyQef.B1 =
        0.0f;

    emptyQef.B2 =
        0.0f;


    emptyQef.IntersectionCount =
        0u;


    emptyQef.ConstraintCount =
        0u;


    emptyQef.Padding =
        0u;


    QefDataBuffer[
        localVertexIndex
    ] =
        emptyQef;


    // ========================================================
    // CORNERS
    // ========================================================

    float3 p0 =
        Origin +
        float3(
            x,
            y,
            z
        ) *
        Step;


    float3 p1 =
        Origin +
        float3(
            x + 1,
            y,
            z
        ) *
        Step;


    float3 p2 =
        Origin +
        float3(
            x,
            y + 1,
            z
        ) *
        Step;


    float3 p3 =
        Origin +
        float3(
            x + 1,
            y + 1,
            z
        ) *
        Step;


    float3 p4 =
        Origin +
        float3(
            x,
            y,
            z + 1
        ) *
        Step;


    float3 p5 =
        Origin +
        float3(
            x + 1,
            y,
            z + 1
        ) *
        Step;


    float3 p6 =
        Origin +
        float3(
            x,
            y + 1,
            z + 1
        ) *
        Step;


    float3 p7 =
        Origin +
        float3(
            x + 1,
            y + 1,
            z + 1
        ) *
        Step;


    // ========================================================
    // DENSITIES
    // ========================================================

    float d0 =
        SampleDensity(
            p0
        );


    float d1 =
        SampleDensity(
            p1
        );


    float d2 =
        SampleDensity(
            p2
        );


    float d3 =
        SampleDensity(
            p3
        );


    float d4 =
        SampleDensity(
            p4
        );


    float d5 =
        SampleDensity(
            p5
        );


    float d6 =
        SampleDensity(
            p6
        );


    float d7 =
        SampleDensity(
            p7
        );


    // ========================================================
    // INSIDE / OUTSIDE
    // ========================================================

    bool hasInside =
        d0 < 0.0f ||
        d1 < 0.0f ||
        d2 < 0.0f ||
        d3 < 0.0f ||
        d4 < 0.0f ||
        d5 < 0.0f ||
        d6 < 0.0f ||
        d7 < 0.0f;


    bool hasOutside =
        d0 >= 0.0f ||
        d1 >= 0.0f ||
        d2 >= 0.0f ||
        d3 >= 0.0f ||
        d4 >= 0.0f ||
        d5 >= 0.0f ||
        d6 >= 0.0f ||
        d7 >= 0.0f;


    if (!hasInside ||
        !hasOutside)
    {
        return;
    }


    // ========================================================
    // CELL CENTER
    // ========================================================

    float3 cellCenter =
        Origin +
        (
            float3(
                x,
                y,
                z
            )
            +
            float3(
                0.5f,
                0.5f,
                0.5f
            )
        )
        *
        Step;


    // ========================================================
    // QEF ACCUMULATORS
    // ========================================================

    int intersectionCount =
        0;


    int constraintCount =
        0;


    float3 intersectionSum =
        float3(
            0.0f,
            0.0f,
            0.0f
        );


    float3 constraintPointSum =
        float3(
            0.0f,
            0.0f,
            0.0f
        );


    float a00 = 0.0f;
    float a01 = 0.0f;
    float a02 = 0.0f;

    float a11 = 0.0f;
    float a12 = 0.0f;

    float a22 = 0.0f;


    float b0 = 0.0f;
    float b1 = 0.0f;
    float b2 = 0.0f;


    // ========================================================
    // EDGES
    // ========================================================

    AccumulateConstraint(
        p0,
        d0,
        p1,
        d1,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p0,
        d0,
        p2,
        d2,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p0,
        d0,
        p4,
        d4,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p1,
        d1,
        p3,
        d3,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p1,
        d1,
        p5,
        d5,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p2,
        d2,
        p3,
        d3,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p2,
        d2,
        p6,
        d6,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p3,
        d3,
        p7,
        d7,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p4,
        d4,
        p5,
        d5,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p4,
        d4,
        p6,
        d6,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p5,
        d5,
        p7,
        d7,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    AccumulateConstraint(
        p6,
        d6,
        p7,
        d7,
        cellCenter,

        intersectionCount,
        constraintCount,

        intersectionSum,
        constraintPointSum,

        a00,
        a01,
        a02,

        a11,
        a12,

        a22,

        b0,
        b1,
        b2
    );


    // ========================================================
    // SAVE QEF DATA
    // ========================================================

    QefData qefData;


    qefData.IntersectionSum =
        intersectionSum;


    qefData.ConstraintPointSum =
        constraintPointSum;


    qefData.A00 =
        a00;

    qefData.A01 =
        a01;

    qefData.A02 =
        a02;


    qefData.A11 =
        a11;

    qefData.A12 =
        a12;


    qefData.A22 =
        a22;


    qefData.B0 =
        b0;

    qefData.B1 =
        b1;

    qefData.B2 =
        b2;


    qefData.IntersectionCount =
        (uint) intersectionCount;


    qefData.ConstraintCount =
        (uint) constraintCount;


    qefData.Padding =
        0u;


    QefDataBuffer[
        localVertexIndex
    ] =
        qefData;


    if (intersectionCount == 0)
    {
        return;
    }


    // ========================================================
    // SOLVE QEF
    // ========================================================

    float3 vertex;


    if (constraintCount > 0)
    {
        float3 localSolution;


        bool solved =
            SolveQEF(
                a00,
                a01,
                a02,

                a11,
                a12,

                a22,

                b0,
                b1,
                b2,

                localSolution
            );


        if (solved)
        {
            vertex =
                cellCenter +
                localSolution;
        }
        else
        {
            float3 average =
                constraintPointSum /
                (float) constraintCount;


            vertex =
                cellCenter +
                average;
        }
    }
    else
    {
        vertex =
            cellCenter;
    }


    // ========================================================
    // CELL BOUNDS
    // ========================================================

    float3 cellMin =
        Origin +
        float3(
            x,
            y,
            z
        ) *
        Step;


    float3 cellMax =
        cellMin +
        float3(
            Step,
            Step,
            Step
        );


    bool outside =
        vertex.x < cellMin.x ||
        vertex.x > cellMax.x ||
        vertex.y < cellMin.y ||
        vertex.y > cellMax.y ||
        vertex.z < cellMin.z ||
        vertex.z > cellMax.z;


    if (outside)
    {
        float3 averageIntersection =
            intersectionSum /
            (float) intersectionCount;


        vertex =
            averageIntersection;
    }


    // ========================================================
    // NORMAL
    // ========================================================

    float3 vertexNormal =
        EstimateNormal(
            vertex
        );


    if (dot(vertexNormal, vertexNormal) < EPSILON)
    {
        vertexNormal =
            float3(
                0.0f,
                1.0f,
                0.0f
            );
    }
    else
    {
        vertexNormal =
            normalize(
                vertexNormal
            );
    }


    // ========================================================
    // WRITE COMPACT VERTEX
    // ========================================================

    uint compactVertexIndex;


    InterlockedAdd(
        VertexCountBuffer[
            CounterSlot
        ],
        1u,
        compactVertexIndex
    );


    VertexBuffer[
        compactVertexIndex
    ] =
        vertex;


    NormalBuffer[
        compactVertexIndex
    ] =
        vertexNormal;


    VertexValid[
        localVertexIndex
    ] =
        1u;


    VertexIndexBuffer[
        localVertexIndex
    ] =
        compactVertexIndex;
}

#endif