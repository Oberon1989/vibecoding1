using System.Collections.Generic;
using UnityEngine;

public partial class GpuVoxelWorld
{
    // ============================================================
    // LOD UPDATE
    // ============================================================

    private void TryUpdateLod()
    {
        float distanceSqr =
            (
                player.position -
                lastLodPlayerPosition
            ).sqrMagnitude;


        float updateDistanceSqr =
            lodUpdateDistance *
            lodUpdateDistance;


        if (distanceSqr <
            updateDistanceSqr)
        {
            return;
        }


        lastLodPlayerPosition =
            player.position;


        UpdateOctreeNode(
            octreeRoot
        );


        RebuildPendingChunkList();


        StartBuild();


        LogLodSummary();
    }


    // ============================================================
    // BUILD LIST
    // ============================================================

    private void RebuildPendingChunkList()
    {
        leafNodes.Clear();


        GetLeafNodes(
            octreeRoot,
            leafNodes
        );


        pendingChunks.Clear();

        pendingRemovedChunks.Clear();


        pendingRenderIndexCounts.Clear();


        HashSet<OctreeNode> currentLeafSet =
            new(
                leafNodes
            );


        Dictionary<OctreeNode, GpuChunk> currentChunks =
            new(
                leafNodes.Count
            );


        for (int i = 0;
             i < leafNodes.Count;
             i++)
        {
            OctreeNode node =
                leafNodes[i];


            if (activeChunkMap.TryGetValue(
                    node,
                    out GpuChunk activeChunk))
            {
                currentChunks[node] =
                    activeChunk;

                continue;
            }


            int resolution =
                Mathf.RoundToInt(
                    node.Size /
                    node.VoxelSize
                );


            GpuChunk chunk =
                new GpuChunk(
                    node.Center,
                    node.Size,
                    node.Lod,
                    resolution
                );


            pendingChunks.Add(
                chunk
            );


            currentChunks[node] =
                chunk;
        }


        // --------------------------------------------------------
        // Update neighbor information for all current chunks.
        // --------------------------------------------------------

        UpdateChunkNeighbors(
            currentChunks
        );


        // --------------------------------------------------------
        // Build nearest chunks first.
        // --------------------------------------------------------

        pendingChunks.Sort(
            (a, b) =>
            {
                float distanceA =
                    a.WorldBounds.SqrDistance(
                        player.position
                    );


                float distanceB =
                    b.WorldBounds.SqrDistance(
                        player.position
                    );


                return
                    distanceA.CompareTo(
                        distanceB
                    );
            }
        );


        foreach (
            KeyValuePair<OctreeNode, GpuChunk> pair
            in activeChunkMap)
        {
            if (currentLeafSet.Contains(
                    pair.Key))
            {
                continue;
            }


            pendingRemovedChunks.Add(
                pair.Value
            );
        }
    }


    // ============================================================
    // CHUNK NEIGHBORS
    // ============================================================

    private void UpdateChunkNeighbors(
        Dictionary<OctreeNode, GpuChunk> currentChunks)
    {
        if (currentChunks.Count == 0)
        {
            return;
        }


        List<GpuChunk> chunks =
            new(
                currentChunks.Values
            );


        for (int i = 0;
             i < chunks.Count;
             i++)
        {
            GpuChunk chunk =
                chunks[i];


            int negX =
                -1;

            int posX =
                -1;

            int negY =
                -1;

            int posY =
                -1;

            int negZ =
                -1;

            int posZ =
                -1;


            for (int j = 0;
                 j < chunks.Count;
                 j++)
            {
                if (i == j)
                {
                    continue;
                }


                GpuChunk other =
                    chunks[j];


                if (SharesXFace(
                        chunk,
                        other))
                {
                    if (other.WorldMax.x <=
                        chunk.WorldMin.x + NeighborEpsilon)
                    {
                        negX =
                            GetFinestNeighborLod(
                                negX,
                                other.Lod
                            );
                    }


                    if (other.WorldMin.x >=
                        chunk.WorldMax.x - NeighborEpsilon)
                    {
                        posX =
                            GetFinestNeighborLod(
                                posX,
                                other.Lod
                            );
                    }
                }


                if (SharesYFace(
                        chunk,
                        other))
                {
                    if (other.WorldMax.y <=
                        chunk.WorldMin.y + NeighborEpsilon)
                    {
                        negY =
                            GetFinestNeighborLod(
                                negY,
                                other.Lod
                            );
                    }


                    if (other.WorldMin.y >=
                        chunk.WorldMax.y - NeighborEpsilon)
                    {
                        posY =
                            GetFinestNeighborLod(
                                posY,
                                other.Lod
                            );
                    }
                }


                if (SharesZFace(
                        chunk,
                        other))
                {
                    if (other.WorldMax.z <=
                        chunk.WorldMin.z + NeighborEpsilon)
                    {
                        negZ =
                            GetFinestNeighborLod(
                                negZ,
                                other.Lod
                            );
                    }


                    if (other.WorldMin.z >=
                        chunk.WorldMax.z - NeighborEpsilon)
                    {
                        posZ =
                            GetFinestNeighborLod(
                                posZ,
                                other.Lod
                            );
                    }
                }
            }


            chunk.SetNeighborLods(
                negX,
                posX,
                negY,
                posY,
                negZ,
                posZ
            );
        }
    }


    private static int GetFinestNeighborLod(
        int currentLod,
        int candidateLod)
    {
        if (currentLod < 0)
        {
            return candidateLod;
        }


        return
            Mathf.Min(
                currentLod,
                candidateLod
            );
    }


    private static bool SharesXFace(
        GpuChunk a,
        GpuChunk b)
    {
        bool touchesFace =
            Mathf.Abs(
                a.WorldMax.x -
                b.WorldMin.x
            ) <= NeighborEpsilon
            ||
            Mathf.Abs(
                a.WorldMin.x -
                b.WorldMax.x
            ) <= NeighborEpsilon;


        if (!touchesFace)
        {
            return false;
        }


        return
            OverlapLength(
                a.WorldMin.y,
                a.WorldMax.y,
                b.WorldMin.y,
                b.WorldMax.y
            ) > NeighborEpsilon
            &&
            OverlapLength(
                a.WorldMin.z,
                a.WorldMax.z,
                b.WorldMin.z,
                b.WorldMax.z
            ) > NeighborEpsilon;
    }


    private static bool SharesYFace(
        GpuChunk a,
        GpuChunk b)
    {
        bool touchesFace =
            Mathf.Abs(
                a.WorldMax.y -
                b.WorldMin.y
            ) <= NeighborEpsilon
            ||
            Mathf.Abs(
                a.WorldMin.y -
                b.WorldMax.y
            ) <= NeighborEpsilon;


        if (!touchesFace)
        {
            return false;
        }


        return
            OverlapLength(
                a.WorldMin.x,
                a.WorldMax.x,
                b.WorldMin.x,
                b.WorldMax.x
            ) > NeighborEpsilon
            &&
            OverlapLength(
                a.WorldMin.z,
                a.WorldMax.z,
                b.WorldMin.z,
                b.WorldMax.z
            ) > NeighborEpsilon;
    }


    private static bool SharesZFace(
        GpuChunk a,
        GpuChunk b)
    {
        bool touchesFace =
            Mathf.Abs(
                a.WorldMax.z -
                b.WorldMin.z
            ) <= NeighborEpsilon
            ||
            Mathf.Abs(
                a.WorldMin.z -
                b.WorldMax.z
            ) <= NeighborEpsilon;


        if (!touchesFace)
        {
            return false;
        }


        return
            OverlapLength(
                a.WorldMin.x,
                a.WorldMax.x,
                b.WorldMin.x,
                b.WorldMax.x
            ) > NeighborEpsilon
            &&
            OverlapLength(
                a.WorldMin.y,
                a.WorldMax.y,
                b.WorldMin.y,
                b.WorldMax.y
            ) > NeighborEpsilon;
    }


    private static float OverlapLength(
        float minA,
        float maxA,
        float minB,
        float maxB)
    {
        float min =
            Mathf.Max(
                minA,
                minB
            );


        float max =
            Mathf.Min(
                maxA,
                maxB
            );


        return
            max - min;
    }


    private const float NeighborEpsilon =
        0.001f;


    // ============================================================
    // LOD SUMMARY
    // ============================================================

    private void LogLodSummary()
    {
        Dictionary<int, int> counts =
            new();


        for (int i = 0;
             i < leafNodes.Count;
             i++)
        {
            int lod =
                leafNodes[i].Lod;


            if (!counts.ContainsKey(lod))
            {
                counts.Add(
                    lod,
                    0
                );
            }


            counts[lod]++;
        }


        string summary =
            "";


        for (int i = 0;
             i < lodLevels.Length;
             i++)
        {
            int lod =
                lodLevels[i].Lod;


            if (!counts.TryGetValue(
                    lod,
                    out int count))
            {
                continue;
            }


            summary +=
                $"LOD {lod}: {count}  ";
        }


        if (summary ==
            lastLodSummary)
        {
            return;
        }


        lastLodSummary =
            summary;


        Debug.Log(
            $"GPU Octree: {summary}"
        );
    }


    // ============================================================
    // LOD LEVELS
    // ============================================================

    private void InitializeLodLevels()
    {
        int levelCount =
            19;


        lodLevels =
            new VoxelLodLevel[
                levelCount
            ];


        float voxelSize =
            0.5f;


        for (int i = 0;
             i < levelCount;
             i++)
        {
            lodLevels[i] =
                new VoxelLodLevel
                {
                    Lod = i,
                    VoxelSize = voxelSize
                };


            voxelSize *=
                2f;
        }
    }


    // ============================================================
    // LOD FOR DISTANCE
    // ============================================================

    private int GetLodForDistance(
        float distance)
    {
        for (int i = 0;
             i < lodLevels.Length;
             i++)
        {
            float maxDistance =
                lodLevels[i].VoxelSize *
                40f;


            if (distance <
                maxDistance)
            {
                return lodLevels[i].Lod;
            }
        }


        return lodLevels[
            lodLevels.Length - 1
        ].Lod;
    }


    // ============================================================
    // OCTREE UPDATE
    // ============================================================

    private void UpdateOctreeNode(
        OctreeNode node)
    {
        float distance =
            Mathf.Sqrt(
                node.Bounds.SqrDistance(
                    player.position
                )
            );


        int requiredLod =
            GetLodForDistance(
                distance
            );


        if (requiredLod <
            node.Lod)
        {
            if (node.IsLeaf)
            {
                node.Subdivide();
            }
        }
        else
        {
            if (!node.IsLeaf)
            {
                node.Merge();

                return;
            }
        }


        if (!node.IsLeaf)
        {
            for (int i = 0;
                 i < node.Children.Length;
                 i++)
            {
                UpdateOctreeNode(
                    node.Children[i]
                );
            }
        }
    }


    // ============================================================
    // GET LEAF NODES
    // ============================================================

    private void GetLeafNodes(
        OctreeNode node,
        List<OctreeNode> result)
    {
        if (node.IsLeaf)
        {
            result.Add(
                node
            );

            return;
        }


        for (int i = 0;
             i < node.Children.Length;
             i++)
        {
            GetLeafNodes(
                node.Children[i],
                result
            );
        }
    }
}