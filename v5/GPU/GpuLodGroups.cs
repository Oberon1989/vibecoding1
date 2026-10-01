using System;

public class GpuLodGroups
{
    public GpuLodGroup Lod0To3 { get; }
    public GpuLodGroup Lod4To9 { get; }
    public GpuLodGroup Lod10Plus { get; }


    private const int VertexCapacityMultiplier = 4;
    private const int IndexCapacityMultiplier = 24;


    public GpuLodGroups(
        int resolution,
        int lod0To3ChunkCount,
        int lod4To9ChunkCount,
        int lod10PlusChunkCount)
    {
        if (resolution <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(resolution));

        if (lod0To3ChunkCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(lod0To3ChunkCount));

        if (lod4To9ChunkCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(lod4To9ChunkCount));

        if (lod10PlusChunkCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(lod10PlusChunkCount));


        int lod0To3VertexCapacity =
            CalculateVertexCapacity(
                resolution,
                lod0To3ChunkCount);


        int lod0To3IndexCapacity =
            CalculateIndexCapacity(
                resolution,
                lod0To3ChunkCount);


        int lod4To9VertexCapacity =
            CalculateVertexCapacity(
                resolution,
                lod4To9ChunkCount);


        int lod4To9IndexCapacity =
            CalculateIndexCapacity(
                resolution,
                lod4To9ChunkCount);


        int lod10PlusVertexCapacity =
            CalculateVertexCapacity(
                resolution,
                lod10PlusChunkCount);


        int lod10PlusIndexCapacity =
            CalculateIndexCapacity(
                resolution,
                lod10PlusChunkCount);


        Lod0To3 =
            new GpuLodGroup(
                0,
                3,
                lod0To3ChunkCount,
                lod0To3VertexCapacity,
                lod0To3IndexCapacity
            );


        Lod4To9 =
            new GpuLodGroup(
                4,
                9,
                lod4To9ChunkCount,
                lod4To9VertexCapacity,
                lod4To9IndexCapacity
            );


        Lod10Plus =
            new GpuLodGroup(
                10,
                int.MaxValue,
                lod10PlusChunkCount,
                lod10PlusVertexCapacity,
                lod10PlusIndexCapacity
            );
    }


    public GpuLodGroup GetGroup(
        int lod)
    {
        if (Lod0To3.Contains(lod))
            return Lod0To3;


        if (Lod4To9.Contains(lod))
            return Lod4To9;


        if (Lod10Plus.Contains(lod))
            return Lod10Plus;


        return null;
    }


    public void Release()
    {
        Lod0To3.Release();

        Lod4To9.Release();

        Lod10Plus.Release();
    }


    // ============================================================
    // CAPACITY
    // ============================================================

    private static int CalculateVertexCapacity(
        int resolution,
        int maxChunks)
    {
        long resolutionSquared =
            (long)resolution *
            resolution;


        long perChunk =
            resolutionSquared *
            VertexCapacityMultiplier;


        long total =
            perChunk *
            maxChunks;


        if (total <= 0 ||
            total > int.MaxValue)
        {
            throw new InvalidOperationException(
                "GPU vertex pool capacity is too large.");
        }


        return (int)total;
    }


    private static int CalculateIndexCapacity(
        int resolution,
        int maxChunks)
    {
        long resolutionSquared =
            (long)resolution *
            resolution;


        long perChunk =
            resolutionSquared *
            IndexCapacityMultiplier;


        long total =
            perChunk *
            maxChunks;


        if (total <= 0 ||
            total > int.MaxValue)
        {
            throw new InvalidOperationException(
                "GPU index pool capacity is too large.");
        }


        return (int)total;
    }
}