#ifndef DC_COMMON_INCLUDED
#define DC_COMMON_INCLUDED

int CellCount;

float3 Origin;
float Step;

float3 PlanetCenter;
float PlanetRadius;

int NoiseSeed;
float NoiseFrequency;
float NoiseHeight;

int VertexBaseOffset;
int IndexBaseOffset;
int CounterSlot;


// ============================================================
// QEF DATA
// ============================================================
//
// One record per local DC cell.
//
// CPU/GPU layout:
//
// float3 IntersectionSum       12 bytes
// float3 ConstraintPointSum    12 bytes
//
// float A00                    4
// float A01                    4
// float A02                    4
//
// float A11                    4
// float A12                    4
// float A22                    4
//
// float B0                     4
// float B1                     4
// float B2                     4
//
// uint IntersectionCount       4
// uint ConstraintCount         4
// uint Padding                 4
//
// Total = 72 bytes.
//
// ============================================================

struct QefData
{
    float3 IntersectionSum;

    float3 ConstraintPointSum;

    float A00;
    float A01;
    float A02;

    float A11;
    float A12;

    float A22;

    float B0;
    float B1;
    float B2;

    uint IntersectionCount;
    uint ConstraintCount;

    uint Padding;
};


RWStructuredBuffer<QefData>
QefDataBuffer;


// ============================================================
// LOD MERGE
// ============================================================

struct MergeGroup
{
    uint Member0;
    uint Member1;
    uint Member2;
    uint Member3;
};


RWStructuredBuffer<MergeGroup>
MergeGroupBuffer;


RWStructuredBuffer<uint>
MergeGroupIndexBuffer;


RWStructuredBuffer<uint>
MergeGroupCountBuffer;


// ============================================================
// MAIN DC BUFFERS
// ============================================================

RWStructuredBuffer<float3>
VertexBuffer;


RWStructuredBuffer<float3>
NormalBuffer;


RWStructuredBuffer<uint>
VertexValid;


RWStructuredBuffer<uint>
VertexIndexBuffer;


RWStructuredBuffer<uint>
VertexCountBuffer;


RWStructuredBuffer<uint>
IndexBuffer;


RWStructuredBuffer<uint>
IndexCountBuffer;


// ============================================================
// CONSTANTS
// ============================================================

static const float EPSILON =
    0.000001f;


// ============================================================
// VERTEX INDEX
// ============================================================

uint GetVertexIndex(
    int x,
    int y,
    int z)
{
    return
        (uint)
        (
            x +
            CellCount *
            (
                y +
                CellCount * z
            )
        );
}


uint GetVertexBufferIndex(
    int x,
    int y,
    int z)
{
    return
        (uint)
        (
            VertexBaseOffset +
            GetVertexIndex(
                x,
                y,
                z
            )
        );
}


uint GetVertexCountIndex()
{
    return
        (uint)
        CounterSlot;
}


// ============================================================
// INDEX BUFFER
// ============================================================

uint GetIndexBufferIndex(
    uint localIndex)
{
    return
        (uint)
        (
            IndexBaseOffset +
            localIndex
        );
}

#endif