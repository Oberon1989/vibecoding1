using UnityEngine;

public class GpuChunkBuilder
{
    private readonly ComputeShader computeShader;

    private readonly int vertexKernel;

    private readonly int xFaceKernel;

    private readonly int yFaceKernel;

    private readonly int zFaceKernel;

    private readonly int seamVertexKernel;

    private readonly int seamEdgeKernel;


    // ============================================================
    // MERGE MAP SCRATCH
    // ============================================================

    private GpuMergeGroup[] mergeGroups;

    private uint[] mergeGroupIndices;

    private readonly uint[] mergeGroupCountData =
    {
        0u
    };


    // ============================================================
    // SEAM CLEAR SCRATCH
    // ============================================================

    private uint[] seamValidClear;


    public GpuChunkBuilder(
        ComputeShader computeShader)
    {
        this.computeShader =
            computeShader;


        vertexKernel =
            computeShader.FindKernel(
                "CSBuildVertices"
            );


        xFaceKernel =
            computeShader.FindKernel(
                "CSBuildXFaces"
            );


        yFaceKernel =
            computeShader.FindKernel(
                "CSBuildYFaces"
            );


        zFaceKernel =
            computeShader.FindKernel(
                "CSBuildZFaces"
            );


        seamVertexKernel =
            computeShader.FindKernel(
                "CSBuildSeamVertices"
            );


        seamEdgeKernel =
            computeShader.FindKernel(
                "CSBuildSeamEdges"
            );
    }


    // ============================================================
    // REGULAR CHUNK BUILD
    // ============================================================

    public void Build(
        GpuChunk chunk,
        GpuChunkBuildBuffers buffers,
        Vector3 planetCenter,
        float planetRadius,
        int noiseSeed,
        float noiseFrequency,
        float noiseHeight)
    {
        int cellCount =
            buffers.CellCount;


        computeShader.SetInt(
            "CellCount",
            cellCount
        );


        computeShader.SetVector(
            "Origin",
            chunk.WorldMin
        );


        computeShader.SetFloat(
            "Step",
            chunk.VoxelSize
        );


        computeShader.SetVector(
            "PlanetCenter",
            planetCenter
        );


        computeShader.SetFloat(
            "PlanetRadius",
            planetRadius
        );


        computeShader.SetInt(
            "NoiseSeed",
            noiseSeed
        );


        computeShader.SetFloat(
            "NoiseFrequency",
            noiseFrequency
        );


        computeShader.SetFloat(
            "NoiseHeight",
            noiseHeight
        );


        computeShader.SetInt(
            "VertexBaseOffset",
            0
        );


        computeShader.SetInt(
            "IndexBaseOffset",
            0
        );


        computeShader.SetInt(
            "CounterSlot",
            0
        );


        // --------------------------------------------------------
        // PREPARE LOD MERGE MAP
        // --------------------------------------------------------

        PrepareMergeMap(
            chunk,
            buffers
        );


        BindVertexKernel(
            buffers
        );


        BindFaceKernel(
            xFaceKernel,
            buffers
        );


        BindFaceKernel(
            yFaceKernel,
            buffers
        );


        BindFaceKernel(
            zFaceKernel,
            buffers
        );


        ClearBuffers(
            buffers
        );


        int vertexGroups =
            CeilDiv(
                cellCount,
                8
            );


        computeShader.Dispatch(
            vertexKernel,
            vertexGroups,
            vertexGroups,
            vertexGroups
        );


        int faceGroups =
            CeilDiv(
                chunk.Resolution,
                8
            );


        computeShader.Dispatch(
            xFaceKernel,
            faceGroups,
            faceGroups,
            faceGroups
        );


        computeShader.Dispatch(
            yFaceKernel,
            faceGroups,
            faceGroups,
            faceGroups
        );


        computeShader.Dispatch(
            zFaceKernel,
            faceGroups,
            faceGroups,
            faceGroups
        );
    }


    // ============================================================
    // SEAM BUILD
    // ============================================================
    //
    // CPU provides topology only:
    //
    //   SeamData.Nodes
    //   SeamData.Edges
    //
    // GPU evaluates:
    //
    //   density
    //   Hermite intersections
    //   normals
    //   QEF
    //   seam indices
    //
    // ============================================================

    public void BuildSeam(
        GpuSeamBuilder.SeamData seam,
        GpuSeamBuildBuffers buffers,
        Vector3 planetCenter,
        float planetRadius,
        int noiseSeed,
        float noiseFrequency,
        float noiseHeight)
    {
        if (seam == null)
        {
            return;
        }


        int nodeCount =
            seam.Nodes.Count;


        int edgeCount =
            seam.Edges.Count;


        if (nodeCount == 0)
        {
            ClearSeamCounters(
                buffers
            );

            return;
        }


        if (nodeCount >
            buffers.NodeCapacity)
        {
            throw new System.InvalidOperationException(
                "GPU seam node buffer capacity was exceeded."
            );
        }


        if (edgeCount >
            buffers.EdgeCapacity)
        {
            throw new System.InvalidOperationException(
                "GPU seam edge buffer capacity was exceeded."
            );
        }


        // --------------------------------------------------------
        // GLOBAL TERRAIN PARAMETERS
        // --------------------------------------------------------

        computeShader.SetVector(
            "PlanetCenter",
            planetCenter
        );


        computeShader.SetFloat(
            "PlanetRadius",
            planetRadius
        );


        computeShader.SetInt(
            "NoiseSeed",
            noiseSeed
        );


        computeShader.SetFloat(
            "NoiseFrequency",
            noiseFrequency
        );


        computeShader.SetFloat(
            "NoiseHeight",
            noiseHeight
        );


        // These are not used by the seam kernels directly,
        // but keep the shared DC globals in a known state.
        computeShader.SetInt(
            "VertexBaseOffset",
            0
        );


        computeShader.SetInt(
            "IndexBaseOffset",
            0
        );


        computeShader.SetInt(
            "CounterSlot",
            0
        );


        // --------------------------------------------------------
        // UPLOAD TOPOLOGY
        // --------------------------------------------------------

        GpuSeamBuildBuffers.GpuSeamNodeData[] gpuNodes =
            BuildSeamNodes(
                seam
            );


        GpuSeamBuildBuffers.GpuSeamEdgeData[] gpuEdges =
            seam.Edges.ToArray();


        buffers.NodeBuffer.SetData(
            gpuNodes,
            0,
            0,
            nodeCount
        );


        if (edgeCount > 0)
        {
            buffers.EdgeBuffer.SetData(
                gpuEdges,
                0,
                0,
                edgeCount
            );
        }


        // --------------------------------------------------------
        // CLEAR OUTPUT STATE
        // --------------------------------------------------------

        ClearSeamBuffers(
            buffers,
            nodeCount
        );


        // --------------------------------------------------------
        // SET COUNTS
        // --------------------------------------------------------

        computeShader.SetInt(
            "SeamNodeCount",
            nodeCount
        );


        computeShader.SetInt(
            "SeamEdgeCount",
            edgeCount
        );


        // --------------------------------------------------------
        // BIND VERTEX KERNEL
        // --------------------------------------------------------

        BindSeamVertexKernel(
            buffers
        );


        // --------------------------------------------------------
        // BUILD SEAM VERTICES
        // --------------------------------------------------------

        int vertexGroups =
            CeilDiv(
                nodeCount,
                64
            );


        computeShader.Dispatch(
            seamVertexKernel,
            vertexGroups,
            1,
            1
        );


        // --------------------------------------------------------
        // BUILD SEAM EDGES
        // --------------------------------------------------------

        if (edgeCount > 0)
        {
            BindSeamEdgeKernel(
                buffers
            );


            int edgeGroups =
                CeilDiv(
                    edgeCount,
                    64
                );


            computeShader.Dispatch(
                seamEdgeKernel,
                edgeGroups,
                1,
                1
            );
        }
    }


    private GpuSeamBuildBuffers.GpuSeamNodeData[] BuildSeamNodes(
        GpuSeamBuilder.SeamData seam)
    {
        int count =
            seam.Nodes.Count;


        GpuSeamBuildBuffers.GpuSeamNodeData[] gpuNodes =
            new GpuSeamBuildBuffers.GpuSeamNodeData[count];


        for (int i = 0;
             i < count;
             i++)
        {
            GpuSeamNode node =
                seam.Nodes[i];


            gpuNodes[i] =
                new GpuSeamBuildBuffers.GpuSeamNodeData
                {
                    Center =
                        node.Center,

                    Size =
                        node.Size,

                    Lod =
                        node.Lod,

                    Padding =
                        0
                };
        }


        return gpuNodes;
    }


    // ============================================================
    // SEAM VERTEX KERNEL
    // ============================================================

    private void BindSeamVertexKernel(
        GpuSeamBuildBuffers buffers)
    {
        computeShader.SetBuffer(
            seamVertexKernel,
            "SeamNodeBuffer",
            buffers.NodeBuffer
        );


        computeShader.SetBuffer(
            seamVertexKernel,
            "SeamVertexBuffer",
            buffers.VertexBuffer
        );


        computeShader.SetBuffer(
            seamVertexKernel,
            "SeamNormalBuffer",
            buffers.NormalBuffer
        );


        computeShader.SetBuffer(
            seamVertexKernel,
            "SeamVertexValidBuffer",
            buffers.VertexValidBuffer
        );


        computeShader.SetBuffer(
            seamVertexKernel,
            "SeamVertexIndexBuffer",
            buffers.VertexIndexBuffer
        );


        computeShader.SetBuffer(
            seamVertexKernel,
            "SeamVertexCountBuffer",
            buffers.VertexCountBuffer
        );


        computeShader.SetBuffer(
            seamVertexKernel,
            "SeamIndexCountBuffer",
            buffers.IndexCountBuffer
        );
    }


    // ============================================================
    // SEAM EDGE KERNEL
    // ============================================================

    private void BindSeamEdgeKernel(
        GpuSeamBuildBuffers buffers)
    {
        computeShader.SetBuffer(
            seamEdgeKernel,
            "SeamNodeBuffer",
            buffers.NodeBuffer
        );


        computeShader.SetBuffer(
            seamEdgeKernel,
            "SeamEdgeBuffer",
            buffers.EdgeBuffer
        );


        computeShader.SetBuffer(
            seamEdgeKernel,
            "SeamVertexValidBuffer",
            buffers.VertexValidBuffer
        );


        computeShader.SetBuffer(
            seamEdgeKernel,
            "SeamVertexIndexBuffer",
            buffers.VertexIndexBuffer
        );


        computeShader.SetBuffer(
            seamEdgeKernel,
            "SeamIndexBuffer",
            buffers.IndexBuffer
        );


        computeShader.SetBuffer(
            seamEdgeKernel,
            "SeamIndexCountBuffer",
            buffers.IndexCountBuffer
        );
    }


    // ============================================================
    // CLEAR SEAM
    // ============================================================

    private void ClearSeamBuffers(
        GpuSeamBuildBuffers buffers,
        int nodeCount)
    {
        uint[] zero =
        {
            0u
        };


        buffers.VertexCountBuffer.SetData(
            zero
        );


        buffers.IndexCountBuffer.SetData(
            zero
        );


        if (nodeCount <= 0)
        {
            return;
        }


        if (seamValidClear == null ||
            seamValidClear.Length <
            nodeCount)
        {
            seamValidClear =
                new uint[
                    Mathf.Max(
                        nodeCount,
                        1
                    )
                ];
        }


        System.Array.Clear(
            seamValidClear,
            0,
            nodeCount
        );


        buffers.VertexValidBuffer.SetData(
            seamValidClear,
            0,
            0,
            nodeCount
        );
    }


    private void ClearSeamCounters(
        GpuSeamBuildBuffers buffers)
    {
        uint[] zero =
        {
            0u
        };


        buffers.VertexCountBuffer.SetData(
            zero
        );


        buffers.IndexCountBuffer.SetData(
            zero
        );
    }


    // ============================================================
    // MERGE MAP
    // ============================================================

    private void PrepareMergeMap(
        GpuChunk chunk,
        GpuChunkBuildBuffers buffers)
    {
        EnsureMergeScratch(
            buffers
        );


        for (int i = 0;
             i < mergeGroupIndices.Length;
             i++)
        {
            mergeGroupIndices[i] =
                uint.MaxValue;
        }


        int groupCount =
            0;


        if (chunk.NeedsMergeNegX)
        {
            groupCount =
                AddNegXGroups(
                    chunk.Resolution,
                    groupCount
                );
        }


        if (chunk.NeedsMergePosX)
        {
            groupCount =
                AddPosXGroups(
                    chunk.Resolution,
                    groupCount
                );
        }


        if (chunk.NeedsMergeNegY)
        {
            groupCount =
                AddNegYGroups(
                    chunk.Resolution,
                    groupCount
                );
        }


        if (chunk.NeedsMergePosY)
        {
            groupCount =
                AddPosYGroups(
                    chunk.Resolution,
                    groupCount
                );
        }


        if (chunk.NeedsMergeNegZ)
        {
            groupCount =
                AddNegZGroups(
                    chunk.Resolution,
                    groupCount
                );
        }


        if (chunk.NeedsMergePosZ)
        {
            groupCount =
                AddPosZGroups(
                    chunk.Resolution,
                    groupCount
                );
        }


        mergeGroupCountData[0] =
            (uint)groupCount;


        buffers.MergeGroupCountBuffer.SetData(
            mergeGroupCountData
        );


        if (groupCount > 0)
        {
            buffers.MergeGroupBuffer.SetData(
                mergeGroups,
                0,
                0,
                groupCount
            );
        }


        buffers.MergeGroupIndexBuffer.SetData(
            mergeGroupIndices
        );
    }


    private void EnsureMergeScratch(
        GpuChunkBuildBuffers buffers)
    {
        if (mergeGroups == null ||
            mergeGroups.Length !=
            buffers.MergeGroupCapacity)
        {
            mergeGroups =
                new GpuMergeGroup[
                    buffers.MergeGroupCapacity
                ];
        }


        if (mergeGroupIndices == null ||
            mergeGroupIndices.Length !=
            buffers.CellCountTotal)
        {
            mergeGroupIndices =
                new uint[
                    buffers.CellCountTotal
                ];
        }
    }


    // ============================================================
    // + / - X
    // ============================================================

    private int AddNegXGroups(
        int resolution,
        int groupCount)
    {
        int x =
            0;


        for (int z = 0;
             z + 1 < resolution;
             z += 2)
        {
            for (int y = 0;
                 y + 1 < resolution;
                 y += 2)
            {
                uint member0 =
                    GetCellIndex(
                        x,
                        y,
                        z
                    );


                uint member1 =
                    GetCellIndex(
                        x,
                        y + 1,
                        z
                    );


                uint member2 =
                    GetCellIndex(
                        x,
                        y,
                        z + 1
                    );


                uint member3 =
                    GetCellIndex(
                        x,
                        y + 1,
                        z + 1
                    );


                groupCount =
                    TryAddMergeGroup(
                        member0,
                        member1,
                        member2,
                        member3,
                        groupCount
                    );
            }
        }


        return groupCount;
    }


    private int AddPosXGroups(
        int resolution,
        int groupCount)
    {
        int x =
            resolution - 1;


        for (int z = 0;
             z + 1 < resolution;
             z += 2)
        {
            for (int y = 0;
                 y + 1 < resolution;
                 y += 2)
            {
                uint member0 =
                    GetCellIndex(
                        x,
                        y,
                        z
                    );


                uint member1 =
                    GetCellIndex(
                        x,
                        y + 1,
                        z
                    );


                uint member2 =
                    GetCellIndex(
                        x,
                        y,
                        z + 1
                    );


                uint member3 =
                    GetCellIndex(
                        x,
                        y + 1,
                        z + 1
                    );


                groupCount =
                    TryAddMergeGroup(
                        member0,
                        member1,
                        member2,
                        member3,
                        groupCount
                    );
            }
        }


        return groupCount;
    }


    // ============================================================
    // + / - Y
    // ============================================================

    private int AddNegYGroups(
        int resolution,
        int groupCount)
    {
        int y =
            0;


        for (int z = 0;
             z + 1 < resolution;
             z += 2)
        {
            for (int x = 0;
                 x + 1 < resolution;
                 x += 2)
            {
                uint member0 =
                    GetCellIndex(
                        x,
                        y,
                        z
                    );


                uint member1 =
                    GetCellIndex(
                        x + 1,
                        y,
                        z
                    );


                uint member2 =
                    GetCellIndex(
                        x,
                        y,
                        z + 1
                    );


                uint member3 =
                    GetCellIndex(
                        x + 1,
                        y,
                        z + 1
                    );


                groupCount =
                    TryAddMergeGroup(
                        member0,
                        member1,
                        member2,
                        member3,
                        groupCount
                    );
            }
        }


        return groupCount;
    }


    private int AddPosYGroups(
        int resolution,
        int groupCount)
    {
        int y =
            resolution - 1;


        for (int z = 0;
             z + 1 < resolution;
             z += 2)
        {
            for (int x = 0;
                 x + 1 < resolution;
                 x += 2)
            {
                uint member0 =
                    GetCellIndex(
                        x,
                        y,
                        z
                    );


                uint member1 =
                    GetCellIndex(
                        x + 1,
                        y,
                        z
                    );


                uint member2 =
                    GetCellIndex(
                        x,
                        y,
                        z + 1
                    );


                uint member3 =
                    GetCellIndex(
                        x + 1,
                        y,
                        z + 1
                    );


                groupCount =
                    TryAddMergeGroup(
                        member0,
                        member1,
                        member2,
                        member3,
                        groupCount
                    );
            }
        }


        return groupCount;
    }


    // ============================================================
    // + / - Z
    // ============================================================

    private int AddNegZGroups(
        int resolution,
        int groupCount)
    {
        int z =
            0;


        for (int y = 0;
             y + 1 < resolution;
             y += 2)
        {
            for (int x = 0;
                 x + 1 < resolution;
                 x += 2)
            {
                uint member0 =
                    GetCellIndex(
                        x,
                        y,
                        z
                    );


                uint member1 =
                    GetCellIndex(
                        x + 1,
                        y,
                        z
                    );


                uint member2 =
                    GetCellIndex(
                        x,
                        y + 1,
                        z
                    );


                uint member3 =
                    GetCellIndex(
                        x + 1,
                        y + 1,
                        z
                    );


                groupCount =
                    TryAddMergeGroup(
                        member0,
                        member1,
                        member2,
                        member3,
                        groupCount
                    );
            }
        }


        return groupCount;
    }


    private int AddPosZGroups(
        int resolution,
        int groupCount)
    {
        int z =
            resolution - 1;


        for (int y = 0;
             y + 1 < resolution;
             y += 2)
        {
            for (int x = 0;
                 x + 1 < resolution;
                 x += 2)
            {
                uint member0 =
                    GetCellIndex(
                        x,
                        y,
                        z
                    );


                uint member1 =
                    GetCellIndex(
                        x + 1,
                        y,
                        z
                    );


                uint member2 =
                    GetCellIndex(
                        x,
                        y + 1,
                        z
                    );


                uint member3 =
                    GetCellIndex(
                        x + 1,
                        y + 1,
                        z
                    );


                groupCount =
                    TryAddMergeGroup(
                        member0,
                        member1,
                        member2,
                        member3,
                        groupCount
                    );
            }
        }


        return groupCount;
    }


    // ============================================================
    // ADD ONE GROUP
    // ============================================================

    private int TryAddMergeGroup(
        uint member0,
        uint member1,
        uint member2,
        uint member3,
        int groupCount)
    {
        if (groupCount >=
            mergeGroups.Length)
        {
            throw new System.InvalidOperationException(
                "GPU merge group buffer capacity was exceeded."
            );
        }


        if (mergeGroupIndices[
                member0] != uint.MaxValue)
        {
            return groupCount;
        }


        if (mergeGroupIndices[
                member1] != uint.MaxValue)
        {
            return groupCount;
        }


        if (mergeGroupIndices[
                member2] != uint.MaxValue)
        {
            return groupCount;
        }


        if (mergeGroupIndices[
                member3] != uint.MaxValue)
        {
            return groupCount;
        }


        mergeGroups[
            groupCount
        ] =
            new GpuMergeGroup(
                member0,
                member1,
                member2,
                member3
            );


        uint groupIndex =
            (uint)groupCount;


        mergeGroupIndices[
            member0
        ] =
            groupIndex;


        mergeGroupIndices[
            member1
        ] =
            groupIndex;


        mergeGroupIndices[
            member2
        ] =
            groupIndex;


        mergeGroupIndices[
            member3
        ] =
            groupIndex;


        return
            groupCount + 1;
    }


    // ============================================================
    // CELL INDEX
    // ============================================================

    private static uint GetCellIndex(
        int x,
        int y,
        int z)
    {
        // Current GPU resolution = 40,
        // therefore CellCount = 41.
        const int cellCount =
            41;


        return
            (uint)
            (
                x +
                cellCount *
                (
                    y +
                    cellCount *
                    z
                )
            );
    }


    // ============================================================
    // VERTEX KERNEL
    // ============================================================

    private void BindVertexKernel(
        GpuChunkBuildBuffers buffers)
    {
        computeShader.SetBuffer(
            vertexKernel,
            "VertexBuffer",
            buffers.VertexBuffer
        );


        computeShader.SetBuffer(
            vertexKernel,
            "NormalBuffer",
            buffers.NormalBuffer
        );


        computeShader.SetBuffer(
            vertexKernel,
            "VertexValid",
            buffers.VertexValidBuffer
        );


        computeShader.SetBuffer(
            vertexKernel,
            "VertexIndexBuffer",
            buffers.VertexIndexBuffer
        );


        computeShader.SetBuffer(
            vertexKernel,
            "VertexCountBuffer",
            buffers.VertexCountBuffer
        );


        // --------------------------------------------------------
        // QEF DATA
        // --------------------------------------------------------

        computeShader.SetBuffer(
            vertexKernel,
            "QefDataBuffer",
            buffers.QefDataBuffer
        );


        // --------------------------------------------------------
        // LOD MERGE MAP
        // --------------------------------------------------------

        computeShader.SetBuffer(
            vertexKernel,
            "MergeGroupBuffer",
            buffers.MergeGroupBuffer
        );


        computeShader.SetBuffer(
            vertexKernel,
            "MergeGroupIndexBuffer",
            buffers.MergeGroupIndexBuffer
        );


        computeShader.SetBuffer(
            vertexKernel,
            "MergeGroupCountBuffer",
            buffers.MergeGroupCountBuffer
        );
    }


    // ============================================================
    // FACE KERNEL
    // ============================================================

    private void BindFaceKernel(
        int kernel,
        GpuChunkBuildBuffers buffers)
    {
        computeShader.SetBuffer(
            kernel,
            "VertexBuffer",
            buffers.VertexBuffer
        );


        computeShader.SetBuffer(
            kernel,
            "VertexValid",
            buffers.VertexValidBuffer
        );


        computeShader.SetBuffer(
            kernel,
            "VertexIndexBuffer",
            buffers.VertexIndexBuffer
        );


        computeShader.SetBuffer(
            kernel,
            "IndexBuffer",
            buffers.IndexBuffer
        );


        computeShader.SetBuffer(
            kernel,
            "IndexCountBuffer",
            buffers.IndexCountBuffer
        );


        // --------------------------------------------------------
        // LOD MERGE MAP
        // --------------------------------------------------------

        computeShader.SetBuffer(
            kernel,
            "MergeGroupIndexBuffer",
            buffers.MergeGroupIndexBuffer
        );
    }


    // ============================================================
    // CLEAR
    // ============================================================

    private void ClearBuffers(
        GpuChunkBuildBuffers buffers)
    {
        uint[] zero =
        {
            0u
        };


        buffers.VertexCountBuffer.SetData(
            zero
        );


        buffers.IndexCountBuffer.SetData(
            zero
        );
    }


    // ============================================================
    // DIVISION
    // ============================================================

    private static int CeilDiv(
        int value,
        int divisor)
    {
        return
            (value + divisor - 1) /
            divisor;
    }
}