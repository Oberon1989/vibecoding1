#ifndef DC_QEF_INCLUDED
#define DC_QEF_INCLUDED

#include "DC_Normal.hlsl"


// ============================================================
// QEF CONSTRAINT
// ============================================================

void AccumulateConstraint(
    float3 positionA,
    float densityA,
    float3 positionB,
    float densityB,
    float3 cellCenter,

    inout int intersectionCount,
    inout int constraintCount,

    inout float3 intersectionSum,
    inout float3 constraintPointSum,

    inout float a00,
    inout float a01,
    inout float a02,

    inout float a11,
    inout float a12,

    inout float a22,

    inout float b0,
    inout float b1,
    inout float b2)
{
    if (!IsSignChange(
        densityA,
        densityB))
    {
        return;
    }


    float denominator =
        densityA -
        densityB;


    if (abs(denominator) < EPSILON)
    {
        return;
    }


    float t =
        densityA /
        denominator;


    t =
        clamp(
            t,
            0.0f,
            1.0f
        );


    float3 intersection =
        lerp(
            positionA,
            positionB,
            t
        );


    // Keep the same intersection statistics
    // as CPU DualContouringMesher.
    intersectionSum +=
        intersection;

    intersectionCount++;


    float3 normal =
        EstimateNormal(
            intersection
        );


    if (dot(normal, normal) < EPSILON)
    {
        return;
    }


    normal =
        normalize(
            normal
        );


    float3 localPoint =
        intersection -
        cellCenter;


    // Same local point used by CPU QEF solver.
    constraintPointSum +=
        localPoint;

    constraintCount++;


    // --------------------------------------------------------
    // ATA
    // --------------------------------------------------------

    a00 +=
        normal.x *
        normal.x;


    a01 +=
        normal.x *
        normal.y;


    a02 +=
        normal.x *
        normal.z;


    a11 +=
        normal.y *
        normal.y;


    a12 +=
        normal.y *
        normal.z;


    a22 +=
        normal.z *
        normal.z;


    // --------------------------------------------------------
    // ATB
    // --------------------------------------------------------

    float d =
        dot(
            normal,
            localPoint
        );


    b0 +=
        normal.x *
        d;


    b1 +=
        normal.y *
        d;


    b2 +=
        normal.z *
        d;
}


// ============================================================
// ACCUMULATE ONE CELL
// ============================================================
//
// Accumulates all 12 edges of one DC cell.
//
// cellCenter is the center of the FINAL QEF region.
// Therefore the resulting constraints can be combined with
// constraints from other cells into one merged QEF.
//
// ============================================================

void AccumulateCellQEF(
    int x,
    int y,
    int z,

    float cellStep,
    float3 mergedCenter,

    inout int intersectionCount,
    inout int constraintCount,

    inout float3 intersectionSum,
    inout float3 constraintPointSum,

    inout float a00,
    inout float a01,
    inout float a02,

    inout float a11,
    inout float a12,

    inout float a22,

    inout float b0,
    inout float b1,
    inout float b2)
{
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
        cellStep;


    float3 p1 =
        Origin +
        float3(
            x + 1,
            y,
            z
        ) *
        cellStep;


    float3 p2 =
        Origin +
        float3(
            x,
            y + 1,
            z
        ) *
        cellStep;


    float3 p3 =
        Origin +
        float3(
            x + 1,
            y + 1,
            z
        ) *
        cellStep;


    float3 p4 =
        Origin +
        float3(
            x,
            y,
            z + 1
        ) *
        cellStep;


    float3 p5 =
        Origin +
        float3(
            x + 1,
            y,
            z + 1
        ) *
        cellStep;


    float3 p6 =
        Origin +
        float3(
            x,
            y + 1,
            z + 1
        ) *
        cellStep;


    float3 p7 =
        Origin +
        float3(
            x + 1,
            y + 1,
            z + 1
        ) *
        cellStep;


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
    // EDGES
    // ========================================================

    AccumulateConstraint(
        p0,
        d0,
        p1,
        d1,
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
        mergedCenter,

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
}


// ============================================================
// ACCUMULATE FOUR CELLS INTO ONE QEF
// ============================================================
//
// The four cells are expected to form a 2x2 boundary patch:
//
// cell 0 = (x0, y0, z0)
// cell 1 = (x1, y1, z1)
// cell 2 = (x2, y2, z2)
// cell 3 = (x3, y3, z3)
//
// All constraints are expressed relative to the same
// mergedCenter.
//
// ============================================================

void AccumulateMergedQEF4(
    int x0,
    int y0,
    int z0,

    int x1,
    int y1,
    int z1,

    int x2,
    int y2,
    int z2,

    int x3,
    int y3,
    int z3,

    float cellStep,
    float3 mergedCenter,

    inout int intersectionCount,
    inout int constraintCount,

    inout float3 intersectionSum,
    inout float3 constraintPointSum,

    inout float a00,
    inout float a01,
    inout float a02,

    inout float a11,
    inout float a12,

    inout float a22,

    inout float b0,
    inout float b1,
    inout float b2)
{
    AccumulateCellQEF(
        x0,
        y0,
        z0,

        cellStep,
        mergedCenter,

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


    AccumulateCellQEF(
        x1,
        y1,
        z1,

        cellStep,
        mergedCenter,

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


    AccumulateCellQEF(
        x2,
        y2,
        z2,

        cellStep,
        mergedCenter,

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


    AccumulateCellQEF(
        x3,
        y3,
        z3,

        cellStep,
        mergedCenter,

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
}


// ============================================================
// QEF SOLVER
//
// Gaussian elimination with partial pivoting.
// Matches CPU QefSolver structure.
// ============================================================

bool SolveQEF(
    float a00,
    float a01,
    float a02,

    float a11,
    float a12,

    float a22,

    float b0,
    float b1,
    float b2,

    out float3 result)
{
    float4 row0 =
        float4(
            a00,
            a01,
            a02,
            b0
        );


    float4 row1 =
        float4(
            a01,
            a11,
            a12,
            b1
        );


    float4 row2 =
        float4(
            a02,
            a12,
            a22,
            b2
        );


    // ========================================================
    // COLUMN 0
    // ========================================================

    float pivot0 =
        abs(row0.x);

    float pivot1 =
        abs(row1.x);

    float pivot2 =
        abs(row2.x);


    if (pivot1 > pivot0 &&
        pivot1 >= pivot2)
    {
        float4 temp =
            row0;

        row0 =
            row1;

        row1 =
            temp;

        pivot0 =
            pivot1;
    }
    else if (pivot2 > pivot0 &&
             pivot2 > pivot1)
    {
        float4 temp =
            row0;

        row0 =
            row2;

        row2 =
            temp;

        pivot0 =
            pivot2;
    }


    if (pivot0 < EPSILON)
    {
        result =
            float3(
                0.0f,
                0.0f,
                0.0f
            );

        return false;
    }


    float divisor =
        row0.x;


    row0 /=
        divisor;


    float factor1 =
        row1.x;


    row1 -=
        factor1 *
        row0;


    float factor2 =
        row2.x;


    row2 -=
        factor2 *
        row0;


    // ========================================================
    // COLUMN 1
    // ========================================================

    float secondPivot0 =
        abs(row1.y);

    float secondPivot1 =
        abs(row2.y);


    if (secondPivot1 > secondPivot0)
    {
        float4 temp =
            row1;

        row1 =
            row2;

        row2 =
            temp;

        secondPivot0 =
            secondPivot1;
    }


    if (secondPivot0 < EPSILON)
    {
        result =
            float3(
                0.0f,
                0.0f,
                0.0f
            );

        return false;
    }


    divisor =
        row1.y;


    row1 /=
        divisor;


    factor1 =
        row0.y;


    row0 -=
        factor1 *
        row1;


    factor2 =
        row2.y;


    row2 -=
        factor2 *
        row1;


    // ========================================================
    // COLUMN 2
    // ========================================================

    float thirdPivot =
        abs(row2.z);


    if (thirdPivot < EPSILON)
    {
        result =
            float3(
                0.0f,
                0.0f,
                0.0f
            );

        return false;
    }


    divisor =
        row2.z;


    row2 /=
        divisor;


    factor1 =
        row0.z;


    row0 -=
        factor1 *
        row2;


    factor2 =
        row1.z;


    row1 -=
        factor2 *
        row2;


    // ========================================================
    // RESULT
    // ========================================================

    result =
        float3(
            row0.w,
            row1.w,
            row2.w
        );


    return true;
}

#endif