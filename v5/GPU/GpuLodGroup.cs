using System;

public class GpuLodGroup
{
    public int MinLod { get; }
    public int MaxLod { get; }
    public GpuLodGroupBuffers Buffers { get; }

    private readonly bool[] slotUsed;

    public GpuLodGroup(
        int minLod,
        int maxLod,
        int resolution,
        int maxChunks)
    {
        MinLod = minLod;
        MaxLod = maxLod;

        Buffers = new GpuLodGroupBuffers();
        Buffers.Create(resolution, maxChunks);

        slotUsed = new bool[maxChunks];
    }

    public GpuLodGroup(
        int minLod,
        int maxLod,
        int maxChunks,
        int vertexCapacity,
        int indexCapacity)
    {
        MinLod = minLod;
        MaxLod = maxLod;

        Buffers = new GpuLodGroupBuffers();

        Buffers.Create(
            vertexCapacity,
            indexCapacity,
            maxChunks);

        slotUsed = new bool[maxChunks];
    }

    public bool Contains(int lod)
    {
        return lod >= MinLod && lod <= MaxLod;
    }

    public int AcquireSlot()
    {
        for (int i = 0; i < slotUsed.Length; i++)
        {
            if (slotUsed[i])
                continue;

            slotUsed[i] = true;
            return i;
        }

        throw new InvalidOperationException(
            $"GPU LOD group {MinLod}-{MaxLod} has no free chunk slots."
        );
    }

    public int AcquireSlot(
        int vertexCount,
        int indexCount)
    {
        for (int i = 0; i < slotUsed.Length; i++)
        {
            if (slotUsed[i])
                continue;

            try
            {
                Buffers.AllocateSlot(
                    i,
                    vertexCount,
                    indexCount);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            slotUsed[i] = true;
            return i;
        }

        throw new InvalidOperationException(
            $"GPU LOD group {MinLod}-{MaxLod} cannot allocate " +
            $"{vertexCount} vertices and {indexCount} indices."
        );
    }

    public void ReleaseSlot(int slot)
    {
        if (slot < 0 || slot >= slotUsed.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));

        if (!slotUsed[slot])
            return;

        if (Buffers.IsSlotAllocated(slot))
            Buffers.FreeSlot(slot);

        slotUsed[slot] = false;
    }

    public bool IsSlotUsed(int slot)
    {
        if (slot < 0 || slot >= slotUsed.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));

        return slotUsed[slot];
    }

    public void Release()
    {
        if (Buffers != null)
            Buffers.Release();
    }
}