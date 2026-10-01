using System.Collections.Generic;
using UnityEngine;

public partial class GpuVoxelWorld
{
    // ============================================================
    // SEAM BUILD STATE
    // ============================================================

    private readonly List<GpuChunk> pendingSeamChunks =
        new List<GpuChunk>();

    private readonly List<GpuChunk> pendingSeamAllChunks =
        new List<GpuChunk>();

    private int pendingSeamIndex;

    private bool seamBuildInProgress;

    private bool seamDiagnosticLogged;


    // ============================================================
    // START BUILD
    // ============================================================

    private void StartBuild()
    {
        if (buildInProgress)
        {
            return;
        }


        PreparePendingIndexCounts();


        if (pendingChunks.Count == 0)
        {
            CommitPendingBuild();

            return;
        }


        buildVersion++;


        buildInProgress =
            true;


        waitingForChunkReadback =
            false;


        pendingBuildFailed =
            false;


        pendingBuildIndex =
            0;


        pendingRenderIndexCounts.Clear();


        pendingSeamChunks.Clear();

        pendingSeamAllChunks.Clear();

        pendingSeamIndex =
            0;

        seamBuildInProgress =
            false;

        seamDiagnosticLogged =
            false;
    }


    // ============================================================
    // PREPARE PENDING COUNTS
    // ============================================================

    private void PreparePendingIndexCounts()
    {
        pendingLod0To3IndexCounts =
            (uint[])lod0To3IndexCounts.Clone();


        pendingLod4To9IndexCounts =
            (uint[])lod4To9IndexCounts.Clone();


        pendingLod10PlusIndexCounts =
            (uint[])lod10PlusIndexCounts.Clone();


        for (int i = 0;
             i < pendingRemovedChunks.Count;
             i++)
        {
            GpuChunk removedChunk =
                pendingRemovedChunks[i];


            GpuLodGroup group =
                lodGroups.GetGroup(
                    removedChunk.Lod
                );


            if (group == null ||
                removedChunk.Slot < 0)
            {
                continue;
            }


            uint[] counts =
                GetPendingIndexCounts(
                    group
                );


            if (counts == null)
            {
                continue;
            }


            if (removedChunk.Slot <
                counts.Length)
            {
                counts[
                    removedChunk.Slot
                ] = 0u;
            }
        }
    }


    // ============================================================
    // PROCESS BUILD
    // ============================================================

    private void ProcessPendingBuilds()
    {
        if (!buildInProgress)
        {
            return;
        }


        if (waitingForChunkReadback)
        {
            return;
        }


        if (seamBuildInProgress)
        {
            ProcessPendingSeamBuilds();

            return;
        }


        if (pendingBuildIndex >=
            pendingChunks.Count)
        {
            BeginSeamBuildPhase();

            return;
        }


        int buildsThisFrame =
            0;


        int buildLimit =
            Mathf.Max(
                1,
                maxGpuChunkBuildsPerFrame
            );


        while (
            buildsThisFrame < buildLimit &&
            pendingBuildIndex < pendingChunks.Count &&
            !waitingForChunkReadback)
        {
            GpuChunk chunk =
                pendingChunks[
                    pendingBuildIndex
                ];


            builder.Build(
                chunk,
                buildBuffers,
                planetCenter,
                planetRadius,
                noiseSeed,
                noiseFrequency,
                noiseHeight
            );


            int currentChunkIndex =
                pendingBuildIndex;


            int currentBuildVersion =
                buildVersion;


            waitingForChunkReadback =
                true;


            readback.RequestCounts(
                buildBuffers.VertexCountBuffer,
                buildBuffers.IndexCountBuffer,
                (vertexCount, indexCount) =>
                {
                    if (shuttingDown ||
                        currentBuildVersion !=
                        buildVersion)
                    {
                        return;
                    }


                    if (!buildInProgress)
                    {
                        return;
                    }


                    waitingForChunkReadback =
                        false;


                    // ------------------------------------------------
                    // EMPTY CHUNK
                    // ------------------------------------------------

                    if (vertexCount == 0 ||
                        indexCount == 0)
                    {
                        pendingBuildIndex++;


                        if (pendingBuildIndex >=
                            pendingChunks.Count)
                        {
                            BeginSeamBuildPhase();
                        }


                        return;
                    }


                    // ------------------------------------------------
                    // SAFETY CHECK
                    // ------------------------------------------------

                    if (currentChunkIndex < 0 ||
                        currentChunkIndex >=
                        pendingChunks.Count)
                    {
                        pendingBuildFailed =
                            true;


                        FinishBuild();

                        return;
                    }


                    GpuChunk completedChunk =
                        pendingChunks[
                            currentChunkIndex
                        ];


                    GpuLodGroup group =
                        lodGroups.GetGroup(
                            completedChunk.Lod
                        );


                    if (group == null)
                    {
                        pendingBuildFailed =
                            true;


                        FinishBuild();

                        return;
                    }


                    try
                    {
                        // ------------------------------------------------
                        // COMPACT ALLOCATION
                        // ------------------------------------------------

                        int slot =
                            group.AcquireSlot(
                                (int)vertexCount,
                                (int)indexCount
                            );


                        completedChunk.AssignSlot(
                            slot
                        );


                        // ------------------------------------------------
                        // SCRATCH -> COMPACT GPU POOL
                        // ------------------------------------------------

                        renderer.UploadChunk(
                            completedChunk,
                            group,
                            buildBuffers,
                            (int)vertexCount,
                            (int)indexCount
                        );


                        TrySpawnPlayerAfterSurface();


                        // ------------------------------------------------
                        // RENDER STATE
                        // ------------------------------------------------

                        pendingRenderIndexCounts[
                            completedChunk
                        ] =
                            indexCount;


                        uint[] counts =
                            GetPendingIndexCounts(
                                group
                            );


                        if (counts == null ||
                            slot >= counts.Length)
                        {
                            throw new System.InvalidOperationException(
                                "Invalid pending index count array."
                            );
                        }


                        counts[
                            slot
                        ] =
                            indexCount;


                        pendingBuildIndex++;


                        if (pendingBuildIndex >=
                            pendingChunks.Count)
                        {
                            BeginSeamBuildPhase();
                        }
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(
                            exception
                        );


                        pendingBuildFailed =
                            true;


                        FinishBuild();
                    }
                }
            );


            buildsThisFrame++;
        }
    }


    // ============================================================
    // BEGIN SEAM BUILD PHASE
    // ============================================================

    private void BeginSeamBuildPhase()
    {
        if (!buildInProgress)
        {
            return;
        }


        if (pendingBuildFailed)
        {
            FinishBuild();

            return;
        }


        pendingSeamChunks.Clear();

        pendingSeamAllChunks.Clear();


        HashSet<GpuChunk> removedSet =
            new HashSet<GpuChunk>(
                pendingRemovedChunks
            );


        // --------------------------------------------------------
        // Current active chunks which will survive the commit.
        // --------------------------------------------------------

        for (int i = 0;
             i < chunks.Count;
             i++)
        {
            GpuChunk chunk =
                chunks[i];


            if (chunk == null ||
                chunk.Slot < 0)
            {
                continue;
            }


            if (removedSet.Contains(
                    chunk))
            {
                continue;
            }


            pendingSeamAllChunks.Add(
                chunk
            );
        }


        // --------------------------------------------------------
        // Newly built chunks.
        // --------------------------------------------------------

        for (int i = 0;
             i < pendingChunks.Count;
             i++)
        {
            GpuChunk chunk =
                pendingChunks[i];


            if (chunk == null ||
                chunk.Slot < 0)
            {
                continue;
            }


            pendingSeamAllChunks.Add(
                chunk
            );
        }


        // --------------------------------------------------------
        // Every current chunk gets its seam rebuilt.
        // --------------------------------------------------------

        for (int i = 0;
             i < pendingSeamAllChunks.Count;
             i++)
        {
            GpuChunk chunk =
                pendingSeamAllChunks[i];


            if (chunk == null ||
                chunk.Slot < 0)
            {
                continue;
            }


            pendingSeamChunks.Add(
                chunk
            );
        }


        pendingSeamIndex =
            0;


        if (pendingSeamChunks.Count == 0)
        {
            seamBuildInProgress =
                false;


            CommitPendingBuild();

            return;
        }


        seamBuildInProgress =
            true;
    }


    // ============================================================
    // PROCESS SEAM BUILDS
    // ============================================================

    private void ProcessPendingSeamBuilds()
    {
        if (!buildInProgress ||
            !seamBuildInProgress)
        {
            return;
        }


        if (waitingForChunkReadback)
        {
            return;
        }


        if (pendingSeamIndex >=
            pendingSeamChunks.Count)
        {
            seamBuildInProgress =
                false;


            pendingSeamChunks.Clear();

            pendingSeamAllChunks.Clear();


            CommitPendingBuild();

            return;
        }


        GpuChunk seamChunk =
            pendingSeamChunks[
                pendingSeamIndex
            ];


        if (seamChunk == null ||
            seamChunk.Slot < 0)
        {
            pendingSeamIndex++;

            return;
        }


        try
        {
            GpuSeamBuilder.SeamData seamData =
                seamBuilder.Build(
                    seamChunk,
                    pendingSeamAllChunks
                );


            // --------------------------------------------------------
            // SEAM DIAGNOSTIC
            // --------------------------------------------------------

            if (!seamDiagnosticLogged)
            {
                Debug.Log(
                    "GPU SEAM DIAGNOSTIC: " +
                    $"chunk={seamChunk.Center} " +
                    $"lod={seamChunk.Lod} " +
                    $"nodes={seamData.Nodes.Count} " +
                    $"edges={seamData.Edges.Count}"
                );


                seamDiagnosticLogged =
                    true;
            }


            builder.BuildSeam(
                seamData,
                seamBuildBuffers,
                planetCenter,
                planetRadius,
                noiseSeed,
                noiseFrequency,
                noiseHeight
            );


            int currentBuildVersion =
                buildVersion;


            waitingForChunkReadback =
                true;


            readback.RequestSeamCounts(
                seamBuildBuffers.VertexCountBuffer,
                seamBuildBuffers.IndexCountBuffer,
                (vertexCount, indexCount) =>
                {
                    if (shuttingDown ||
                        currentBuildVersion !=
                        buildVersion)
                    {
                        return;
                    }


                    if (!buildInProgress ||
                        !seamBuildInProgress)
                    {
                        return;
                    }


                    waitingForChunkReadback =
                        false;


                    // ------------------------------------------------
                    // SEAM RESULT DIAGNOSTIC
                    // ------------------------------------------------

                    if (seamDiagnosticLogged)
                    {
                        Debug.Log(
                            "GPU SEAM RESULT: " +
                            $"vertices={vertexCount} " +
                            $"indices={indexCount}"
                        );
                    }


                    try
                    {
                        GpuChunk currentChunk =
                            seamChunk;


                        GpuLodGroup group =
                            lodGroups.GetGroup(
                                currentChunk.Lod
                            );


                        if (group != null &&
                            currentChunk.Slot >= 0)
                        {
                            GpuLodGroupBuffers buffers =
                                group.Buffers;


                            // ------------------------------------------------
                            // Replace previous seam.
                            // ------------------------------------------------

                            buffers.ReleaseSeamSlot(
                                currentChunk.Slot
                            );


                            currentChunk.ClearSeam();


                            if (vertexCount > 0u &&
                                indexCount >= 3u)
                            {
                                buffers.CreateSeamSlot(
                                    currentChunk.Slot,
                                    (int)vertexCount,
                                    (int)indexCount
                                );


                                renderer.UploadSeam(
                                    currentChunk,
                                    group,
                                    seamBuildBuffers,
                                    (int)vertexCount,
                                    (int)indexCount
                                );


                                currentChunk.SetSeamCounts(
                                    (int)vertexCount,
                                    (int)indexCount
                                );
                            }
                        }
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(
                            exception
                        );


                        // ------------------------------------------------
                        // Seam failure must not invalidate the normal
                        // chunk build.
                        // ------------------------------------------------

                        GpuLodGroup group =
                            lodGroups.GetGroup(
                                seamChunk.Lod
                            );


                        if (group != null &&
                            seamChunk.Slot >= 0)
                        {
                            group.Buffers.ReleaseSeamSlot(
                                seamChunk.Slot
                            );
                        }


                        seamChunk.ClearSeam();
                    }


                    pendingSeamIndex++;


                    if (pendingSeamIndex >=
                        pendingSeamChunks.Count)
                    {
                        seamBuildInProgress =
                            false;


                        pendingSeamChunks.Clear();

                        pendingSeamAllChunks.Clear();


                        CommitPendingBuild();
                    }
                }
            );
        }
        catch (System.Exception exception)
        {
            Debug.LogException(
                exception
            );


            // ----------------------------------------------------
            // Seam generation failure should not destroy the
            // ordinary chunk build.
            // ----------------------------------------------------

            if (seamChunk != null &&
                seamChunk.Slot >= 0)
            {
                GpuLodGroup group =
                    lodGroups.GetGroup(
                        seamChunk.Lod
                    );


                if (group != null)
                {
                    group.Buffers.ReleaseSeamSlot(
                        seamChunk.Slot
                    );
                }


                seamChunk.ClearSeam();
            }


            pendingSeamIndex++;


            if (pendingSeamIndex >=
                pendingSeamChunks.Count)
            {
                seamBuildInProgress =
                    false;


                pendingSeamChunks.Clear();

                pendingSeamAllChunks.Clear();


                CommitPendingBuild();
            }
        }
    }


    // ============================================================
    // FINISH BUILD
    // ============================================================

    private void FinishBuild()
    {
        waitingForChunkReadback =
            false;


        if (pendingBuildFailed)
        {
            ReleasePendingSlots();


            pendingRenderIndexCounts.Clear();


            pendingChunks.Clear();

            pendingRemovedChunks.Clear();


            pendingSeamChunks.Clear();

            pendingSeamAllChunks.Clear();


            seamBuildInProgress =
                false;


            buildInProgress =
                false;


            return;
        }


        if (pendingBuildIndex <
            pendingChunks.Count)
        {
            return;
        }


        if (!seamBuildInProgress)
        {
            BeginSeamBuildPhase();

            return;
        }
    }


    // ============================================================
    // COMMIT
    // ============================================================

    private void CommitPendingBuild()
    {
        // --------------------------------------------------------
        // Remove old chunks only after replacement + seam build
        // are ready.
        // --------------------------------------------------------

        for (int i = 0;
             i < pendingRemovedChunks.Count;
             i++)
        {
            GpuChunk removedChunk =
                pendingRemovedChunks[i];


            RemoveActiveChunk(
                removedChunk
            );
        }


        // --------------------------------------------------------
        // Newly built chunks become active.
        // --------------------------------------------------------

        for (int i = 0;
             i < pendingChunks.Count;
             i++)
        {
            GpuChunk newChunk =
                pendingChunks[i];


            OctreeNode node =
                FindLeafNodeForChunk(
                    newChunk
                );


            if (node == null)
            {
                if (newChunk.Slot >= 0)
                {
                    GpuLodGroup group =
                        lodGroups.GetGroup(
                            newChunk.Lod
                        );


                    if (group != null)
                    {
                        group.ReleaseSlot(
                            newChunk.Slot
                        );
                    }
                }


                continue;
            }


            activeChunkMap[
                node
            ] =
                newChunk;


            if (newChunk.Slot >= 0)
            {
                chunks.Add(
                    newChunk
                );
            }
        }


        lod0To3IndexCounts =
            pendingLod0To3IndexCounts;


        lod4To9IndexCounts =
            pendingLod4To9IndexCounts;


        lod10PlusIndexCounts =
            pendingLod10PlusIndexCounts;


        pendingChunks.Clear();

        pendingRemovedChunks.Clear();


        pendingRenderIndexCounts.Clear();


        pendingSeamChunks.Clear();

        pendingSeamAllChunks.Clear();


        pendingLod0To3IndexCounts =
            null;

        pendingLod4To9IndexCounts =
            null;

        pendingLod10PlusIndexCounts =
            null;


        buildInProgress =
            false;


        waitingForChunkReadback =
            false;


        pendingBuildIndex =
            0;


        pendingBuildFailed =
            false;


        pendingSeamIndex =
            0;


        seamBuildInProgress =
            false;
    }


    // ============================================================
    // ACTIVE CHUNK MANAGEMENT
    // ============================================================

    private void RemoveActiveChunk(
        GpuChunk removedChunk)
    {
        if (removedChunk == null)
        {
            return;
        }


        GpuLodGroup group =
            lodGroups.GetGroup(
                removedChunk.Lod
            );


        if (group != null &&
            removedChunk.Slot >= 0)
        {
            group.ReleaseSlot(
                removedChunk.Slot
            );
        }


        for (int i = chunks.Count - 1;
             i >= 0;
             i--)
        {
            if (chunks[i] !=
                removedChunk)
            {
                continue;
            }


            chunks.RemoveAt(i);

            break;
        }


        OctreeNode activeNode =
            null;


        foreach (
            KeyValuePair<OctreeNode, GpuChunk> pair
            in activeChunkMap)
        {
            if (pair.Value !=
                removedChunk)
            {
                continue;
            }


            activeNode =
                pair.Key;

            break;
        }


        if (activeNode != null)
        {
            activeChunkMap.Remove(
                activeNode
            );
        }
    }


    // ============================================================
    // RELEASE PENDING SLOTS
    // ============================================================

    private void ReleasePendingSlots()
    {
        for (int i = 0;
             i < pendingChunks.Count;
             i++)
        {
            GpuChunk chunk =
                pendingChunks[i];


            if (chunk == null ||
                chunk.Slot < 0)
            {
                continue;
            }


            GpuLodGroup group =
                lodGroups.GetGroup(
                    chunk.Lod
                );


            if (group != null)
            {
                group.ReleaseSlot(
                    chunk.Slot
                );
            }
        }
    }


    // ============================================================
    // FIND NODE
    // ============================================================

    private OctreeNode FindLeafNodeForChunk(
        GpuChunk chunk)
    {
        for (int i = 0;
             i < leafNodes.Count;
             i++)
        {
            OctreeNode node =
                leafNodes[i];


            if (node.Lod !=
                chunk.Lod)
            {
                continue;
            }


            if (node.Center !=
                chunk.Center)
            {
                continue;
            }


            if (!Mathf.Approximately(
                    node.Size,
                    chunk.Size))
            {
                continue;
            }


            return node;
        }


        return null;
    }
}