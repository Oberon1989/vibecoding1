#ifndef DC_FACES_INCLUDED
#define DC_FACES_INCLUDED

#include "DC_Common.hlsl"
#include "DC_Density.hlsl"


// ============================================================
// ADD QUAD
// ============================================================

void AddQuad(
    uint a,
    uint b,
    uint c,
    uint d,

    bool validA,
    bool validB,
    bool validC,
    bool validD,

    float da,
    float db,

    bool baseNormalMatchesPositiveAxis)
{
    if (!validA ||
        !validB ||
        !validC ||
        !validD)
    {
        return;
    }


    bool insideToOutside =
        da < 0.0f &&
        db >= 0.0f;


    bool reverse =
        insideToOutside !=
        baseNormalMatchesPositiveAxis;


    uint baseIndex =
        0u;


    InterlockedAdd(
        IndexCountBuffer[
            CounterSlot
        ],
        6u,
        baseIndex
    );


    uint writeIndex =
        GetIndexBufferIndex(
            baseIndex
        );


    if (reverse)
    {
        IndexBuffer[
            writeIndex + 0u
        ] =
            a;


        IndexBuffer[
            writeIndex + 1u
        ] =
            c;


        IndexBuffer[
            writeIndex + 2u
        ] =
            b;


        IndexBuffer[
            writeIndex + 3u
        ] =
            a;


        IndexBuffer[
            writeIndex + 4u
        ] =
            d;


        IndexBuffer[
            writeIndex + 5u
        ] =
            c;
    }
    else
    {
        IndexBuffer[
            writeIndex + 0u
        ] =
            a;


        IndexBuffer[
            writeIndex + 1u
        ] =
            b;


        IndexBuffer[
            writeIndex + 2u
        ] =
            c;


        IndexBuffer[
            writeIndex + 3u
        ] =
            a;


        IndexBuffer[
            writeIndex + 4u
        ] =
            c;


        IndexBuffer[
            writeIndex + 5u
        ] =
            d;
    }
}


// ============================================================
// X FACE
// ============================================================

void DC_BuildXFace(
    uint3 id)
{
    int resolution =
        CellCount - 1;


    int x =
        (int) id.x;

    int y =
        (int) id.y + 1;

    int z =
        (int) id.z + 1;


    if (x >= resolution ||
        y > resolution ||
        z > resolution)
    {
        return;
    }


    float3 sampleA =
        Origin +
        float3(
            x,
            y,
            z
        ) *
        Step;


    float3 sampleB =
        Origin +
        float3(
            x + 1,
            y,
            z
        ) *
        Step;


    float da =
        SampleDensity(
            sampleA
        );


    float db =
        SampleDensity(
            sampleB
        );


    if (!IsSignChange(
        da,
        db))
    {
        return;
    }


    // --------------------------------------------------------
    // LOCAL CELL INDICES
    // --------------------------------------------------------

    uint localA =
        GetVertexIndex(
            x,
            y - 1,
            z - 1
        );


    uint localB =
        GetVertexIndex(
            x,
            y,
            z - 1
        );


    uint localC =
        GetVertexIndex(
            x,
            y,
            z
        );


    uint localD =
        GetVertexIndex(
            x,
            y - 1,
            z
        );


    // --------------------------------------------------------
    // VALIDITY
    // --------------------------------------------------------

    bool validA =
        VertexValid[localA] != 0u;


    bool validB =
        VertexValid[localB] != 0u;


    bool validC =
        VertexValid[localC] != 0u;


    bool validD =
        VertexValid[localD] != 0u;


    if (!validA ||
        !validB ||
        !validC ||
        !validD)
    {
        return;
    }


    // --------------------------------------------------------
    // COMPACT VERTEX INDICES
    // --------------------------------------------------------

    uint a =
        VertexIndexBuffer[localA];


    uint b =
        VertexIndexBuffer[localB];


    uint c =
        VertexIndexBuffer[localC];


    uint d =
        VertexIndexBuffer[localD];


    AddQuad(
        a,
        b,
        c,
        d,

        true,
        true,
        true,
        true,

        da,
        db,

        true
    );
}


// ============================================================
// Y FACE
// ============================================================

void DC_BuildYFace(
    uint3 id)
{
    int resolution =
        CellCount - 1;


    int x =
        (int) id.x + 1;

    int y =
        (int) id.y;

    int z =
        (int) id.z + 1;


    if (x > resolution ||
        y >= resolution ||
        z > resolution)
    {
        return;
    }


    float3 sampleA =
        Origin +
        float3(
            x,
            y,
            z
        ) *
        Step;


    float3 sampleB =
        Origin +
        float3(
            x,
            y + 1,
            z
        ) *
        Step;


    float da =
        SampleDensity(
            sampleA
        );


    float db =
        SampleDensity(
            sampleB
        );


    if (!IsSignChange(
        da,
        db))
    {
        return;
    }


    // --------------------------------------------------------
    // LOCAL CELL INDICES
    // --------------------------------------------------------

    uint localA =
        GetVertexIndex(
            x - 1,
            y,
            z - 1
        );


    uint localB =
        GetVertexIndex(
            x,
            y,
            z - 1
        );


    uint localC =
        GetVertexIndex(
            x,
            y,
            z
        );


    uint localD =
        GetVertexIndex(
            x - 1,
            y,
            z
        );


    // --------------------------------------------------------
    // VALIDITY
    // --------------------------------------------------------

    bool validA =
        VertexValid[localA] != 0u;


    bool validB =
        VertexValid[localB] != 0u;


    bool validC =
        VertexValid[localC] != 0u;


    bool validD =
        VertexValid[localD] != 0u;


    if (!validA ||
        !validB ||
        !validC ||
        !validD)
    {
        return;
    }


    // --------------------------------------------------------
    // COMPACT VERTEX INDICES
    // --------------------------------------------------------

    uint a =
        VertexIndexBuffer[localA];


    uint b =
        VertexIndexBuffer[localB];


    uint c =
        VertexIndexBuffer[localC];


    uint d =
        VertexIndexBuffer[localD];


    AddQuad(
        a,
        b,
        c,
        d,

        true,
        true,
        true,
        true,

        da,
        db,

        false
    );
}


// ============================================================
// Z FACE
// ============================================================

void DC_BuildZFace(
    uint3 id)
{
    int resolution =
        CellCount - 1;


    int x =
        (int) id.x + 1;

    int y =
        (int) id.y + 1;

    int z =
        (int) id.z;


    if (x > resolution ||
        y > resolution ||
        z >= resolution)
    {
        return;
    }


    float3 sampleA =
        Origin +
        float3(
            x,
            y,
            z
        ) *
        Step;


    float3 sampleB =
        Origin +
        float3(
            x,
            y,
            z + 1
        ) *
        Step;


    float da =
        SampleDensity(
            sampleA
        );


    float db =
        SampleDensity(
            sampleB
        );


    if (!IsSignChange(
        da,
        db))
    {
        return;
    }


    // --------------------------------------------------------
    // LOCAL CELL INDICES
    // --------------------------------------------------------

    uint localA =
        GetVertexIndex(
            x - 1,
            y - 1,
            z
        );


    uint localB =
        GetVertexIndex(
            x,
            y - 1,
            z
        );


    uint localC =
        GetVertexIndex(
            x,
            y,
            z
        );


    uint localD =
        GetVertexIndex(
            x - 1,
            y,
            z
        );


    // --------------------------------------------------------
    // VALIDITY
    // --------------------------------------------------------

    bool validA =
        VertexValid[localA] != 0u;


    bool validB =
        VertexValid[localB] != 0u;


    bool validC =
        VertexValid[localC] != 0u;


    bool validD =
        VertexValid[localD] != 0u;


    if (!validA ||
        !validB ||
        !validC ||
        !validD)
    {
        return;
    }


    // --------------------------------------------------------
    // COMPACT VERTEX INDICES
    // --------------------------------------------------------

    uint a =
        VertexIndexBuffer[localA];


    uint b =
        VertexIndexBuffer[localB];


    uint c =
        VertexIndexBuffer[localC];


    uint d =
        VertexIndexBuffer[localD];


    AddQuad(
        a,
        b,
        c,
        d,

        true,
        true,
        true,
        true,

        da,
        db,

        true
    );
}

#endif