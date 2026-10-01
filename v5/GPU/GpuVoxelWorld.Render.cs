using System.Collections.Generic;

public partial class GpuVoxelWorld
{
    // ============================================================
    // RENDER ALL CHUNKS
    // ============================================================

    private void RenderAllChunks()
    {
        // --------------------------------------------------------
        // ACTIVE CHUNKS
        // --------------------------------------------------------

        for (int i = 0;
             i < chunks.Count;
             i++)
        {
            GpuChunk chunk =
                chunks[i];


            GpuLodGroup group =
                lodGroups.GetGroup(
                    chunk.Lod
                );


            if (group == null)
            {
                continue;
            }


            uint[] counts =
                GetIndexCounts(
                    group
                );


            if (counts == null)
            {
                continue;
            }


            if (chunk.Slot < 0 ||
                chunk.Slot >= counts.Length)
            {
                continue;
            }


            uint indexCount =
                counts[
                    chunk.Slot
                ];


            RenderChunk(
                chunk,
                group,
                indexCount
            );
        }


        // --------------------------------------------------------
        // PENDING CHUNKS
        //
        // These chunks are already uploaded to the compact GPU
        // pool and can be rendered before the whole build commits.
        // Their seam may not exist yet, so RenderChunk() safely
        // skips it until it becomes ready.
        // --------------------------------------------------------

        foreach (
            KeyValuePair<GpuChunk, uint> pair
            in pendingRenderIndexCounts)
        {
            GpuChunk chunk =
                pair.Key;


            if (chunk == null ||
                chunk.Slot < 0)
            {
                continue;
            }


            GpuLodGroup group =
                lodGroups.GetGroup(
                    chunk.Lod
                );


            if (group == null)
            {
                continue;
            }


            RenderChunk(
                chunk,
                group,
                pair.Value
            );
        }
    }


    // ============================================================
    // RENDER ONE CHUNK
    // ============================================================

    private void RenderChunk(
        GpuChunk chunk,
        GpuLodGroup group,
        uint indexCount)
    {
        indexCount -=
            indexCount % 3u;


        if (indexCount < 3u)
        {
            return;
        }


        // --------------------------------------------------------
        // REGULAR CHUNK
        // --------------------------------------------------------

        renderer.Render(
            chunk,
            group,
            (int)indexCount
        );


        // --------------------------------------------------------
        // LOD SEAM
        //
        // RenderSeam() internally checks whether this chunk has
        // a valid dedicated seam buffer and non-zero seam indices.
        // --------------------------------------------------------

        renderer.RenderSeam(
            chunk,
            group
        );
    }


    // ============================================================
    // ACTIVE INDEX COUNTS
    // ============================================================

    private uint[] GetIndexCounts(
        GpuLodGroup group)
    {
        if (group ==
            lodGroups.Lod0To3)
        {
            return lod0To3IndexCounts;
        }


        if (group ==
            lodGroups.Lod4To9)
        {
            return lod4To9IndexCounts;
        }


        if (group ==
            lodGroups.Lod10Plus)
        {
            return lod10PlusIndexCounts;
        }


        return null;
    }


    // ============================================================
    // PENDING INDEX COUNTS
    // ============================================================

    private uint[] GetPendingIndexCounts(
        GpuLodGroup group)
    {
        if (group ==
            lodGroups.Lod0To3)
        {
            return pendingLod0To3IndexCounts;
        }


        if (group ==
            lodGroups.Lod4To9)
        {
            return pendingLod4To9IndexCounts;
        }


        if (group ==
            lodGroups.Lod10Plus)
        {
            return pendingLod10PlusIndexCounts;
        }


        return null;
    }
}