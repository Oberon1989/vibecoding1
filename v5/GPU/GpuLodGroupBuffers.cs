using System;
using System.Collections.Generic;
using UnityEngine;

public class GpuLodGroupBuffers
{
    public GraphicsBuffer VertexBuffer { get; private set; }
    public GraphicsBuffer NormalBuffer { get; private set; }
    public GraphicsBuffer VertexValidBuffer { get; private set; }
    public GraphicsBuffer VertexCountBuffer { get; private set; }

    public GraphicsBuffer IndexComputeBuffer { get; private set; }
    public GraphicsBuffer IndexRenderBuffer { get; private set; }
    public GraphicsBuffer IndexCountBuffer { get; private set; }

    public int MaxChunks { get; private set; }

    // Legacy fixed-slot values.
    public int Resolution { get; private set; }
    public int CellCount { get; private set; }
    public int VertexCapacityPerChunk { get; private set; }
    public int IndexCapacityPerChunk { get; private set; }

    // New compact-pool values.
    public int VertexCapacity { get; private set; }
    public int IndexCapacity { get; private set; }

    private int[] vertexOffsets;
    private int[] vertexCounts;

    private int[] indexOffsets;
    private int[] indexCounts;


    // ============================================================
    // SEAM BUFFERS
    // ============================================================
    //
    // Seam geometry is intentionally stored separately from the
    // regular compact chunk pools.
    //
    // Each active chunk gets an exact-size seam buffer when a seam
    // mesh actually exists. This avoids reserving the worst-case
    // seam size for every chunk.
    //
    // These are allocated lazily by CreateSeamSlot().
    //

    private GraphicsBuffer[] seamVertexBuffers;
    private GraphicsBuffer[] seamNormalBuffers;
    private GraphicsBuffer[] seamIndexComputeBuffers;
    private GraphicsBuffer[] seamIndexRenderBuffers;

    private int[] seamVertexCounts;
    private int[] seamIndexCounts;


    private readonly List<FreeRange> freeVertexRanges =
        new List<FreeRange>();

    private readonly List<FreeRange> freeIndexRanges =
        new List<FreeRange>();

    private int nextVertexOffset;
    private int nextIndexOffset;

    private struct FreeRange
    {
        public int Offset;
        public int Count;

        public FreeRange(
            int offset,
            int count)
        {
            Offset = offset;
            Count = count;
        }
    }


    // ------------------------------------------------------------------------
    // NEW COMPACT POOL
    // ------------------------------------------------------------------------

    public void Create(
        int vertexCapacity,
        int indexCapacity,
        int maxChunks)
    {
        Release();

        if (vertexCapacity <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(vertexCapacity));

        if (indexCapacity <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(indexCapacity));

        if (maxChunks <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxChunks));


        MaxChunks =
            maxChunks;

        VertexCapacity =
            vertexCapacity;

        IndexCapacity =
            indexCapacity;


        vertexOffsets =
            new int[maxChunks];

        vertexCounts =
            new int[maxChunks];

        indexOffsets =
            new int[maxChunks];

        indexCounts =
            new int[maxChunks];


        // ----------------------------------------------------
        // SEAM SLOT STATE
        // ----------------------------------------------------

        seamVertexBuffers =
            new GraphicsBuffer[maxChunks];

        seamNormalBuffers =
            new GraphicsBuffer[maxChunks];

        seamIndexComputeBuffers =
            new GraphicsBuffer[maxChunks];

        seamIndexRenderBuffers =
            new GraphicsBuffer[maxChunks];

        seamVertexCounts =
            new int[maxChunks];

        seamIndexCounts =
            new int[maxChunks];


        nextVertexOffset =
            0;

        nextIndexOffset =
            0;


        freeVertexRanges.Clear();
        freeIndexRanges.Clear();


        // ----------------------------------------------------
        // COMPACT VERTICES
        // ----------------------------------------------------

        VertexBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopyDestination,
                vertexCapacity,
                sizeof(float) * 3);


        // ----------------------------------------------------
        // COMPACT NORMALS
        // ----------------------------------------------------

        NormalBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopyDestination,
                vertexCapacity,
                sizeof(float) * 3);


        // ----------------------------------------------------
        // LEGACY / DEBUG VALID BUFFER
        // ----------------------------------------------------

        VertexValidBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                vertexCapacity,
                sizeof(uint));


        // ----------------------------------------------------
        // VERTEX COUNTERS
        // ----------------------------------------------------

        VertexCountBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                maxChunks,
                sizeof(uint));


        // ----------------------------------------------------
        // COMPACT COMPUTE INDEX POOL
        //
        // Compute Shader writes here.
        // It can also be copied to IndexRenderBuffer.
        // ----------------------------------------------------

        IndexComputeBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopySource |
                GraphicsBuffer.Target.CopyDestination,
                indexCapacity,
                sizeof(uint));


        // ----------------------------------------------------
        // RENDER INDEX POOL
        //
        // This remains a pure Index buffer so that
        // Graphics.RenderPrimitivesIndexed can use it.
        // ----------------------------------------------------

        IndexRenderBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Index |
                GraphicsBuffer.Target.CopyDestination,
                indexCapacity,
                sizeof(uint));


        // ----------------------------------------------------
        // INDEX COUNTERS
        // ----------------------------------------------------

        IndexCountBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                maxChunks,
                sizeof(uint));
    }


    // ------------------------------------------------------------------------
    // ALLOCATE COMPACT RANGE
    // ------------------------------------------------------------------------

    public void AllocateSlot(
        int slot,
        int vertexCount,
        int indexCount)
    {
        ValidateSlot(slot);


        if (vertexCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(vertexCount));


        if (indexCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(indexCount));


        if (vertexCounts[slot] != 0 ||
            indexCounts[slot] != 0)
        {
            throw new InvalidOperationException(
                $"GPU slot {slot} is already allocated.");
        }


        if (!TryAllocateRange(
                freeVertexRanges,
                ref nextVertexOffset,
                VertexCapacity,
                vertexCount,
                out int vertexOffset))
        {
            throw new InvalidOperationException(
                $"GPU vertex pool is full. " +
                $"Requested {vertexCount}, " +
                $"capacity {VertexCapacity}.");
        }


        if (!TryAllocateRange(
                freeIndexRanges,
                ref nextIndexOffset,
                IndexCapacity,
                indexCount,
                out int indexOffset))
        {
            AddFreeRange(
                freeVertexRanges,
                vertexOffset,
                vertexCount);


            throw new InvalidOperationException(
                $"GPU index pool is full. " +
                $"Requested {indexCount}, " +
                $"capacity {IndexCapacity}.");
        }


        vertexOffsets[slot] =
            vertexOffset;

        vertexCounts[slot] =
            vertexCount;


        indexOffsets[slot] =
            indexOffset;

        indexCounts[slot] =
            indexCount;
    }


    // ------------------------------------------------------------------------
    // CREATE EXACT SEAM BUFFERS
    // ------------------------------------------------------------------------
    //
    // The seam does not use the normal compact pool.
    // Buffers are allocated only when a real seam mesh exists.
    //
    // ------------------------------------------------------------------------

    public void CreateSeamSlot(
        int slot,
        int vertexCount,
        int indexCount)
    {
        ValidateSlot(slot);


        if (vertexCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(vertexCount));


        if (indexCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(indexCount));


        if (seamVertexBuffers[slot] != null ||
            seamIndexRenderBuffers[slot] != null)
        {
            throw new InvalidOperationException(
                $"GPU seam slot {slot} is already allocated.");
        }


        seamVertexBuffers[slot] =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopyDestination,
                vertexCount,
                sizeof(float) * 3);


        seamNormalBuffers[slot] =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopyDestination,
                vertexCount,
                sizeof(float) * 3);


        // Compute shader writes seam indices here.
        seamIndexComputeBuffers[slot] =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopySource |
                GraphicsBuffer.Target.CopyDestination,
                indexCount,
                sizeof(uint));


        // Render-only index buffer.
        seamIndexRenderBuffers[slot] =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Index |
                GraphicsBuffer.Target.CopyDestination,
                indexCount,
                sizeof(uint));


        seamVertexCounts[slot] =
            vertexCount;


        seamIndexCounts[slot] =
            indexCount;
    }


    // ------------------------------------------------------------------------
    // FREE COMPACT + SEAM RANGE
    // ------------------------------------------------------------------------

    public void FreeSlot(
        int slot)
    {
        ValidateSlot(slot);


        int vertexCount =
            vertexCounts[slot];


        if (vertexCount > 0)
        {
            AddFreeRange(
                freeVertexRanges,
                vertexOffsets[slot],
                vertexCount);
        }


        int indexCount =
            indexCounts[slot];


        if (indexCount > 0)
        {
            AddFreeRange(
                freeIndexRanges,
                indexOffsets[slot],
                indexCount);
        }


        ReleaseSeamSlot(
            slot
        );


        vertexOffsets[slot] =
            0;

        vertexCounts[slot] =
            0;


        indexOffsets[slot] =
            0;

        indexCounts[slot] =
            0;
    }


    // ------------------------------------------------------------------------
    // RELEASE EXACT SEAM BUFFERS
    // ------------------------------------------------------------------------

    public void ReleaseSeamSlot(
        int slot)
    {
        ValidateSlot(slot);


        if (seamVertexBuffers[slot] != null)
        {
            seamVertexBuffers[slot].Release();
            seamVertexBuffers[slot] = null;
        }


        if (seamNormalBuffers[slot] != null)
        {
            seamNormalBuffers[slot].Release();
            seamNormalBuffers[slot] = null;
        }


        if (seamIndexComputeBuffers[slot] != null)
        {
            seamIndexComputeBuffers[slot].Release();
            seamIndexComputeBuffers[slot] = null;
        }


        if (seamIndexRenderBuffers[slot] != null)
        {
            seamIndexRenderBuffers[slot].Release();
            seamIndexRenderBuffers[slot] = null;
        }


        seamVertexCounts[slot] =
            0;


        seamIndexCounts[slot] =
            0;
    }


    // ------------------------------------------------------------------------
    // SEAM STATE
    // ------------------------------------------------------------------------

    public bool HasSeamSlot(
        int slot)
    {
        ValidateSlot(slot);


        return
            seamVertexBuffers[slot] != null &&
            seamIndexRenderBuffers[slot] != null &&
            seamVertexCounts[slot] > 0 &&
            seamIndexCounts[slot] > 0;
    }


    public int GetSeamVertexCount(
        int slot)
    {
        ValidateSlot(slot);

        return
            seamVertexCounts[slot];
    }


    public int GetSeamIndexCount(
        int slot)
    {
        ValidateSlot(slot);

        return
            seamIndexCounts[slot];
    }


    public GraphicsBuffer GetSeamVertexBuffer(
        int slot)
    {
        ValidateSlot(slot);

        return
            seamVertexBuffers[slot];
    }


    public GraphicsBuffer GetSeamNormalBuffer(
        int slot)
    {
        ValidateSlot(slot);

        return
            seamNormalBuffers[slot];
    }


    public GraphicsBuffer GetSeamIndexComputeBuffer(
        int slot)
    {
        ValidateSlot(slot);

        return
            seamIndexComputeBuffers[slot];
    }


    public GraphicsBuffer GetSeamIndexRenderBuffer(
        int slot)
    {
        ValidateSlot(slot);

        return
            seamIndexRenderBuffers[slot];
    }


    // ------------------------------------------------------------------------
    // SLOT STATE
    // ------------------------------------------------------------------------

    public bool IsSlotAllocated(
        int slot)
    {
        ValidateSlot(slot);


        return
            vertexCounts[slot] > 0 ||
            indexCounts[slot] > 0;
    }


    // ------------------------------------------------------------------------
    // OFFSETS / COUNTS
    // ------------------------------------------------------------------------

    public int GetVertexOffset(
        int slot)
    {
        ValidateSlot(slot);

        return
            vertexOffsets[slot];
    }


    public int GetVertexCount(
        int slot)
    {
        ValidateSlot(slot);

        return
            vertexCounts[slot];
    }


    public int GetIndexOffset(
        int slot)
    {
        ValidateSlot(slot);

        return
            indexOffsets[slot];
    }


    public int GetIndexCount(
        int slot)
    {
        ValidateSlot(slot);

        return
            indexCounts[slot];
    }


    // ------------------------------------------------------------------------
    // LEGACY FIXED-SLOT MODE
    // ------------------------------------------------------------------------

    public void Create(
        int resolution,
        int maxChunks)
    {
        Release();


        if (resolution <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(resolution));


        if (maxChunks <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxChunks));


        Resolution =
            resolution;

        MaxChunks =
            maxChunks;

        CellCount =
            resolution + 1;


        long vertexCapacityLong =
            (long)CellCount *
            CellCount *
            CellCount;


        long indexCapacityLong =
            18L *
            resolution *
            resolution *
            resolution;


        long totalVertexCapacityLong =
            vertexCapacityLong *
            maxChunks;


        long totalIndexCapacityLong =
            indexCapacityLong *
            maxChunks;


        if (vertexCapacityLong > int.MaxValue ||
            totalVertexCapacityLong > int.MaxValue)
        {
            throw new InvalidOperationException(
                "GPU vertex buffer is too large.");
        }


        if (indexCapacityLong > int.MaxValue ||
            totalIndexCapacityLong > int.MaxValue)
        {
            throw new InvalidOperationException(
                "GPU index buffer is too large.");
        }


        VertexCapacityPerChunk =
            (int)vertexCapacityLong;


        IndexCapacityPerChunk =
            (int)indexCapacityLong;


        VertexCapacity =
            (int)totalVertexCapacityLong;


        IndexCapacity =
            (int)totalIndexCapacityLong;


        vertexOffsets =
            new int[maxChunks];

        vertexCounts =
            new int[maxChunks];

        indexOffsets =
            new int[maxChunks];

        indexCounts =
            new int[maxChunks];


        seamVertexBuffers =
            new GraphicsBuffer[maxChunks];

        seamNormalBuffers =
            new GraphicsBuffer[maxChunks];

        seamIndexComputeBuffers =
            new GraphicsBuffer[maxChunks];

        seamIndexRenderBuffers =
            new GraphicsBuffer[maxChunks];

        seamVertexCounts =
            new int[maxChunks];

        seamIndexCounts =
            new int[maxChunks];


        for (int i = 0; i < maxChunks; i++)
        {
            vertexOffsets[i] =
                i * VertexCapacityPerChunk;

            vertexCounts[i] =
                VertexCapacityPerChunk;

            indexOffsets[i] =
                i * IndexCapacityPerChunk;

            indexCounts[i] =
                IndexCapacityPerChunk;
        }


        VertexBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopyDestination,
                VertexCapacity,
                sizeof(float) * 3);


        NormalBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopyDestination,
                VertexCapacity,
                sizeof(float) * 3);


        VertexValidBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                VertexCapacity,
                sizeof(uint));


        VertexCountBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                maxChunks,
                sizeof(uint));


        IndexComputeBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopySource |
                GraphicsBuffer.Target.CopyDestination,
                IndexCapacity,
                sizeof(uint));


        IndexRenderBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Index |
                GraphicsBuffer.Target.CopyDestination,
                IndexCapacity,
                sizeof(uint));


        IndexCountBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                maxChunks,
                sizeof(uint));
    }


    // ------------------------------------------------------------------------
    // RELEASE
    // ------------------------------------------------------------------------

    public void Release()
    {
        // ----------------------------------------------------
        // RELEASE SEAM BUFFERS FIRST
        // ----------------------------------------------------

        if (seamVertexBuffers != null)
        {
            for (int i = 0;
                 i < seamVertexBuffers.Length;
                 i++)
            {
                if (seamVertexBuffers[i] != null)
                {
                    seamVertexBuffers[i].Release();
                    seamVertexBuffers[i] = null;
                }
            }
        }


        if (seamNormalBuffers != null)
        {
            for (int i = 0;
                 i < seamNormalBuffers.Length;
                 i++)
            {
                if (seamNormalBuffers[i] != null)
                {
                    seamNormalBuffers[i].Release();
                    seamNormalBuffers[i] = null;
                }
            }
        }


        if (seamIndexComputeBuffers != null)
        {
            for (int i = 0;
                 i < seamIndexComputeBuffers.Length;
                 i++)
            {
                if (seamIndexComputeBuffers[i] != null)
                {
                    seamIndexComputeBuffers[i].Release();
                    seamIndexComputeBuffers[i] = null;
                }
            }
        }


        if (seamIndexRenderBuffers != null)
        {
            for (int i = 0;
                 i < seamIndexRenderBuffers.Length;
                 i++)
            {
                if (seamIndexRenderBuffers[i] != null)
                {
                    seamIndexRenderBuffers[i].Release();
                    seamIndexRenderBuffers[i] = null;
                }
            }
        }


        seamVertexBuffers =
            null;

        seamNormalBuffers =
            null;

        seamIndexComputeBuffers =
            null;

        seamIndexRenderBuffers =
            null;

        seamVertexCounts =
            null;

        seamIndexCounts =
            null;


        // ----------------------------------------------------
        // REGULAR BUFFERS
        // ----------------------------------------------------

        VertexBuffer?.Release();
        NormalBuffer?.Release();
        VertexValidBuffer?.Release();
        VertexCountBuffer?.Release();

        IndexComputeBuffer?.Release();
        IndexRenderBuffer?.Release();
        IndexCountBuffer?.Release();


        VertexBuffer =
            null;

        NormalBuffer =
            null;

        VertexValidBuffer =
            null;

        VertexCountBuffer =
            null;


        IndexComputeBuffer =
            null;

        IndexRenderBuffer =
            null;

        IndexCountBuffer =
            null;


        MaxChunks =
            0;


        Resolution =
            0;

        CellCount =
            0;


        VertexCapacityPerChunk =
            0;

        IndexCapacityPerChunk =
            0;


        VertexCapacity =
            0;

        IndexCapacity =
            0;


        vertexOffsets =
            null;

        vertexCounts =
            null;

        indexOffsets =
            null;

        indexCounts =
            null;


        nextVertexOffset =
            0;

        nextIndexOffset =
            0;


        freeVertexRanges.Clear();
        freeIndexRanges.Clear();
    }


    // ------------------------------------------------------------------------
    // ALLOCATOR
    // ------------------------------------------------------------------------

    private static bool TryAllocateRange(
        List<FreeRange> freeRanges,
        ref int nextOffset,
        int totalCapacity,
        int requestedCount,
        out int allocatedOffset)
    {
        for (int i = 0;
             i < freeRanges.Count;
             i++)
        {
            FreeRange range =
                freeRanges[i];


            if (range.Count < requestedCount)
                continue;


            allocatedOffset =
                range.Offset;


            int remaining =
                range.Count -
                requestedCount;


            if (remaining == 0)
            {
                freeRanges.RemoveAt(i);
            }
            else
            {
                freeRanges[i] =
                    new FreeRange(
                        range.Offset +
                        requestedCount,
                        remaining);
            }


            return true;
        }


        if (nextOffset + requestedCount >
            totalCapacity)
        {
            allocatedOffset =
                -1;

            return false;
        }


        allocatedOffset =
            nextOffset;


        nextOffset +=
            requestedCount;


        return true;
    }


    private static void AddFreeRange(
        List<FreeRange> freeRanges,
        int offset,
        int count)
    {
        if (count <= 0)
            return;


        freeRanges.Add(
            new FreeRange(
                offset,
                count));


        freeRanges.Sort(
            (a, b) =>
                a.Offset.CompareTo(
                    b.Offset));


        for (int i =
                 freeRanges.Count - 1;
             i > 0;
             i--)
        {
            FreeRange previous =
                freeRanges[i - 1];

            FreeRange current =
                freeRanges[i];


            int previousEnd =
                previous.Offset +
                previous.Count;


            if (previousEnd !=
                current.Offset)
            {
                continue;
            }


            previous.Count +=
                current.Count;


            freeRanges[i - 1] =
                previous;


            freeRanges.RemoveAt(i);
        }
    }


    private void ValidateSlot(
        int slot)
    {
        if (slot < 0 ||
            slot >= MaxChunks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                "Invalid GPU chunk slot.");
        }
    }
}