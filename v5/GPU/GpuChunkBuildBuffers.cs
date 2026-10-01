using System;
using UnityEngine;


// ============================================================
// GPU QEF DATA
// ============================================================
//
// Stores the accumulated QEF state for one DC cell.
//
// Total stride = 72 bytes.
//
// ============================================================

public struct GpuQefData
{
    public Vector3 IntersectionSum;

    public Vector3 ConstraintPointSum;

    public float A00;
    public float A01;
    public float A02;

    public float A11;
    public float A12;

    public float A22;

    public float B0;
    public float B1;
    public float B2;

    public uint IntersectionCount;
    public uint ConstraintCount;

    public uint Padding;
}


// ============================================================
// GPU MERGE GROUP
// ============================================================
//
// Four fine cells belonging to one 2:1 LOD transition patch.
//
// Face:
//
// 0 = -X
// 1 = +X
// 2 = -Y
// 3 = +Y
// 4 = -Z
// 5 = +Z
//
// GPU stride = 20 bytes.
//
// ============================================================

public struct GpuMergeGroup
{
    public uint Member0;
    public uint Member1;
    public uint Member2;
    public uint Member3;

    public uint Face;


    // --------------------------------------------------------
    // Legacy constructor.
    //
    // Kept temporarily so the current GpuChunkBuilder continues
    // to compile until the face-aware merge map is connected.
    // --------------------------------------------------------

    public GpuMergeGroup(
        uint member0,
        uint member1,
        uint member2,
        uint member3)
    {
        Member0 =
            member0;

        Member1 =
            member1;

        Member2 =
            member2;

        Member3 =
            member3;

        Face =
            0u;
    }


    // --------------------------------------------------------
    // Face-aware constructor.
    // --------------------------------------------------------

    public GpuMergeGroup(
        uint member0,
        uint member1,
        uint member2,
        uint member3,
        uint face)
    {
        Member0 =
            member0;

        Member1 =
            member1;

        Member2 =
            member2;

        Member3 =
            member3;

        Face =
            face;
    }
}


public class GpuChunkBuildBuffers
{
    public GraphicsBuffer VertexBuffer { get; private set; }

    public GraphicsBuffer NormalBuffer { get; private set; }

    public GraphicsBuffer VertexValidBuffer { get; private set; }

    public GraphicsBuffer VertexIndexBuffer { get; private set; }

    public GraphicsBuffer VertexCountBuffer { get; private set; }


    public GraphicsBuffer IndexBuffer { get; private set; }

    public GraphicsBuffer IndexCountBuffer { get; private set; }


    // ============================================================
    // QEF DATA
    // ============================================================

    public GraphicsBuffer QefDataBuffer { get; private set; }


    // ============================================================
    // LOD MERGE
    // ============================================================

    public GraphicsBuffer MergeGroupBuffer { get; private set; }

    public GraphicsBuffer MergeGroupIndexBuffer { get; private set; }

    public GraphicsBuffer MergeGroupCountBuffer { get; private set; }

    public int MergeGroupCapacity { get; private set; }


    // ============================================================
    // BASIC GRID
    // ============================================================

    public int Resolution { get; private set; }

    public int CellCount { get; private set; }

    public int CellCountTotal { get; private set; }

    public int MaxIndexCount { get; private set; }


    // ============================================================
    // CREATE
    // ============================================================

    public void Create(
        int resolution)
    {
        Release();


        if (resolution <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolution)
            );
        }


        Resolution =
            resolution;


        CellCount =
            resolution + 1;


        long cellCountTotalLong =
            (long)CellCount *
            CellCount *
            CellCount;


        long maxIndexCountLong =
            18L *
            resolution *
            resolution *
            resolution;


        if (cellCountTotalLong >
            int.MaxValue)
        {
            throw new InvalidOperationException(
                "GPU scratch vertex buffer is too large."
            );
        }


        if (maxIndexCountLong >
            int.MaxValue)
        {
            throw new InvalidOperationException(
                "GPU scratch index buffer is too large."
            );
        }


        CellCountTotal =
            (int)cellCountTotalLong;


        MaxIndexCount =
            (int)maxIndexCountLong;


        // ========================================================
        // MERGE GROUP CAPACITY
        // ========================================================

        int groupsPerAxis =
            resolution / 2;


        long mergeGroupCapacityLong =
            6L *
            groupsPerAxis *
            groupsPerAxis;


        if (mergeGroupCapacityLong >
            int.MaxValue)
        {
            throw new InvalidOperationException(
                "GPU scratch merge group buffer is too large."
            );
        }


        MergeGroupCapacity =
            Mathf.Max(
                1,
                (int)mergeGroupCapacityLong
            );


        // ========================================================
        // VERTICES
        // ========================================================

        VertexBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopySource,
                CellCountTotal,
                sizeof(float) * 3
            );


        // ========================================================
        // NORMALS
        // ========================================================

        NormalBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopySource,
                CellCountTotal,
                sizeof(float) * 3
            );


        // ========================================================
        // VALIDITY
        // ========================================================

        VertexValidBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                CellCountTotal,
                sizeof(uint)
            );


        // ========================================================
        // LOCAL -> COMPACT VERTEX INDEX
        // ========================================================

        VertexIndexBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                CellCountTotal,
                sizeof(uint)
            );


        // ========================================================
        // VERTEX COUNTER
        // ========================================================

        VertexCountBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint)
            );


        // ========================================================
        // INDEX BUFFER
        // ========================================================

        IndexBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopySource,
                MaxIndexCount,
                sizeof(uint)
            );


        // ========================================================
        // INDEX COUNTER
        // ========================================================

        IndexCountBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint)
            );


        // ========================================================
        // QEF DATA
        // ========================================================

        QefDataBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                CellCountTotal,
                72
            );


        // ========================================================
        // MERGE GROUPS
        //
        // 4 uint members + 1 uint face.
        // ========================================================

        MergeGroupBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                MergeGroupCapacity,
                sizeof(uint) * 5
            );


        // ========================================================
        // CELL -> MERGE GROUP
        // ========================================================

        MergeGroupIndexBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                CellCountTotal,
                sizeof(uint)
            );


        // ========================================================
        // MERGE GROUP COUNTER
        // ========================================================

        MergeGroupCountBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint)
            );
    }


    // ============================================================
    // RELEASE
    // ============================================================

    public void Release()
    {
        VertexBuffer?.Release();

        NormalBuffer?.Release();

        VertexValidBuffer?.Release();

        VertexIndexBuffer?.Release();

        VertexCountBuffer?.Release();


        IndexBuffer?.Release();

        IndexCountBuffer?.Release();


        QefDataBuffer?.Release();


        MergeGroupBuffer?.Release();

        MergeGroupIndexBuffer?.Release();

        MergeGroupCountBuffer?.Release();


        VertexBuffer =
            null;

        NormalBuffer =
            null;

        VertexValidBuffer =
            null;

        VertexIndexBuffer =
            null;

        VertexCountBuffer =
            null;


        IndexBuffer =
            null;

        IndexCountBuffer =
            null;


        QefDataBuffer =
            null;


        MergeGroupBuffer =
            null;

        MergeGroupIndexBuffer =
            null;

        MergeGroupCountBuffer =
            null;


        Resolution =
            0;

        CellCount =
            0;

        CellCountTotal =
            0;

        MaxIndexCount =
            0;

        MergeGroupCapacity =
            0;
    }
}