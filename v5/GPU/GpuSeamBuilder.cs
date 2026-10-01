using System;
using System.Collections.Generic;
using UnityEngine;

public class GpuSeamBuilder
{
    // ============================================================
    // SEAM DATA
    // ============================================================

    public sealed class SeamData
    {
        public readonly List<GpuSeamNode> Nodes =
            new List<GpuSeamNode>();

        public readonly List<GpuSeamBuildBuffers.GpuSeamEdgeData> Edges =
            new List<GpuSeamBuildBuffers.GpuSeamEdgeData>();

        public void Clear()
        {
            Nodes.Clear();
            Edges.Clear();
        }
    }


    // ============================================================
    // VIRTUAL SEAM OCTREE NODE
    // ============================================================

    private sealed class SeamOctreeNode
    {
        public Vector3Int Min;
        public int Size;

        // >= 0 = real seam leaf
        // -1   = internal node
        public int SourceNodeIndex;

        public SeamOctreeNode[] Children;

        // Bit mask of source chunks contained by this subtree.
        public ulong ChunkMask;

        public bool IsLeaf
        {
            get
            {
                return SourceNodeIndex >= 0;
            }
        }

        public SeamOctreeNode(
            Vector3Int min,
            int size,
            int sourceNodeIndex,
            ulong chunkMask)
        {
            Min =
                min;

            Size =
                size;

            SourceNodeIndex =
                sourceNodeIndex;

            ChunkMask =
                chunkMask;

            Children =
                null;
        }

        public static SeamOctreeNode CreateInternal(
            Vector3Int min,
            int size)
        {
            return new SeamOctreeNode(
                min,
                size,
                -1,
                0UL)
            {
                Children =
                    new SeamOctreeNode[8]
            };
        }
    }


    // ============================================================
    // LEAF RECORD
    // ============================================================

    private readonly struct LeafRecord
    {
        public readonly int SourceNodeIndex;

        public readonly Vector3Int Min;

        public readonly int Size;

        public readonly int ChunkId;

        public LeafRecord(
            int sourceNodeIndex,
            Vector3Int min,
            int size,
            int chunkId)
        {
            SourceNodeIndex =
                sourceNodeIndex;

            Min =
                min;

            Size =
                size;

            ChunkId =
                chunkId;
        }
    }


    // ============================================================
    // EDGE KEY
    // ============================================================

    private readonly struct EdgeKey :
        IEquatable<EdgeKey>
    {
        public readonly int Axis;
        public readonly int A;
        public readonly int B;
        public readonly int C;
        public readonly int D;

        public EdgeKey(
            int axis,
            int a,
            int b,
            int c,
            int d)
        {
            Axis =
                axis;

            A =
                a;

            B =
                b;

            C =
                c;

            D =
                d;
        }

        public bool Equals(
            EdgeKey other)
        {
            return
                Axis == other.Axis &&
                A == other.A &&
                B == other.B &&
                C == other.C &&
                D == other.D;
        }

        public override bool Equals(
            object obj)
        {
            return
                obj is EdgeKey other &&
                Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash =
                    Axis;

                hash =
                    hash * 397 +
                    A;

                hash =
                    hash * 397 +
                    B;

                hash =
                    hash * 397 +
                    C;

                hash =
                    hash * 397 +
                    D;

                return hash;
            }
        }
    }


    // ============================================================
    // BUILD
    // ============================================================

    public SeamData Build(
        GpuChunk ownerChunk,
        IReadOnlyList<GpuChunk> allChunks)
    {
        SeamData result =
            new SeamData();

        if (!Application.isPlaying)
        {
            return result;
        }

        if (ownerChunk == null)
        {
            return result;
        }

        if (allChunks == null)
        {
            return result;
        }


        // --------------------------------------------------------
        // Find whether THIS chunk is the coarse side.
        //
        // A seam belongs to the coarse chunk. Therefore we only
        // generate a seam when a finer chunk touches the owner.
        // --------------------------------------------------------

        bool needsSeam =
            false;


        int ownerTouchMask =
            0;


        for (int i = 0;
             i < allChunks.Count;
             i++)
        {
            GpuChunk sourceChunk =
                allChunks[i];

            if (sourceChunk == null ||
                ReferenceEquals(
                    sourceChunk,
                    ownerChunk))
            {
                continue;
            }


            if (sourceChunk.Lod >=
                ownerChunk.Lod)
            {
                continue;
            }


            int touchMask =
                GetBoundaryTouchMask(
                    ownerChunk,
                    sourceChunk);


            if (touchMask == 0)
            {
                continue;
            }


            needsSeam =
                true;

            ownerTouchMask |=
                touchMask;
        }


        if (!needsSeam ||
            ownerTouchMask == 0)
        {
            return result;
        }


        HashSet<NodeKey> uniqueNodes =
            new HashSet<NodeKey>();


        // --------------------------------------------------------
        // Collect owner boundary nodes on ALL seam sides.
        // --------------------------------------------------------

        AddOwnerBoundaryNodes(
            ownerChunk,
            ownerTouchMask,
            result.Nodes,
            uniqueNodes);


        // --------------------------------------------------------
        // Collect:
        //
        // - finer neighbors;
        // - same-LOD neighbors touching the seam area.
        //
        // Same-LOD neighbors are useful for edge / corner
        // continuity around the transition.
        // Coarser neighbors are deliberately ignored because this
        // owner is the fine side relative to them.
        // --------------------------------------------------------

        for (int i = 0;
             i < allChunks.Count;
             i++)
        {
            GpuChunk sourceChunk =
                allChunks[i];

            if (sourceChunk == null ||
                ReferenceEquals(
                    sourceChunk,
                    ownerChunk))
            {
                continue;
            }


            if (sourceChunk.Lod >
                ownerChunk.Lod)
            {
                continue;
            }


            int touchMask =
                GetBoundaryTouchMask(
                    ownerChunk,
                    sourceChunk);


            if (touchMask == 0)
            {
                continue;
            }


            AddNeighborBoundaryNodes(
                ownerChunk,
                sourceChunk,
                touchMask,
                result.Nodes,
                uniqueNodes);
        }


        if (result.Nodes.Count < 4)
        {
            return result;
        }


        // --------------------------------------------------------
        // Build the temporary seam octree.
        // --------------------------------------------------------

        SeamOctreeNode root;


        if (!TryBuildSeamOctree(
                result,
                out root))
        {
            result.Edges.Clear();
            return result;
        }


        if (root == null)
        {
            result.Edges.Clear();
            return result;
        }


        // --------------------------------------------------------
        // Run normal Dual Contouring topology traversal.
        // --------------------------------------------------------

        HashSet<EdgeKey> emittedEdges =
            new HashSet<EdgeKey>();


        ContourCellProc(
            root,
            result,
            emittedEdges);


        return result;
    }


    // ============================================================
    // GPU NODE DATA
    // ============================================================

    public GpuSeamBuildBuffers.GpuSeamNodeData[] BuildGpuNodes(
        SeamData seam)
    {
        if (seam == null)
        {
            throw new ArgumentNullException(
                nameof(seam));
        }

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

            if (node == null)
            {
                continue;
            }

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
    // GPU EDGE DATA
    // ============================================================

    public GpuSeamBuildBuffers.GpuSeamEdgeData[] BuildGpuEdges(
        SeamData seam)
    {
        if (seam == null)
        {
            throw new ArgumentNullException(
                nameof(seam));
        }

        return seam.Edges.ToArray();
    }


    // ============================================================
    // BUILD SEAM OCTREE
    // ============================================================

    private bool TryBuildSeamOctree(
        SeamData seam,
        out SeamOctreeNode root)
    {
        root =
            null;


        if (seam == null ||
            seam.Nodes.Count < 4)
        {
            return false;
        }


        // --------------------------------------------------------
        // Find finest cell size and global minimum.
        // --------------------------------------------------------

        float baseSize =
            float.PositiveInfinity;


        Vector3 globalMin =
            new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);


        int maxX =
            0;

        int maxY =
            0;

        int maxZ =
            0;

        int maxLeafSize =
            1;


        for (int i = 0;
             i < seam.Nodes.Count;
             i++)
        {
            GpuSeamNode node =
                seam.Nodes[i];


            if (node == null)
            {
                continue;
            }


            if (!IsFinite(node.Size) ||
                node.Size <= 0f)
            {
                continue;
            }


            Vector3 min =
                node.WorldMin;


            if (!IsFinite(min.x) ||
                !IsFinite(min.y) ||
                !IsFinite(min.z))
            {
                continue;
            }


            baseSize =
                Mathf.Min(
                    baseSize,
                    node.Size);


            globalMin.x =
                Mathf.Min(
                    globalMin.x,
                    min.x);

            globalMin.y =
                Mathf.Min(
                    globalMin.y,
                    min.y);

            globalMin.z =
                Mathf.Min(
                    globalMin.z,
                    min.z);
        }


        if (!IsFinite(baseSize) ||
            baseSize <= 0f)
        {
            return false;
        }


        if (!IsFinite(globalMin.x) ||
            !IsFinite(globalMin.y) ||
            !IsFinite(globalMin.z))
        {
            return false;
        }


        // --------------------------------------------------------
        // Assign compact chunk IDs.
        // --------------------------------------------------------

        Dictionary<GpuChunk, int> chunkIds =
            new Dictionary<GpuChunk, int>();


        // --------------------------------------------------------
        // Quantize seam leaves relative to globalMin.
        // --------------------------------------------------------

        List<LeafRecord> leaves =
            new List<LeafRecord>(
                seam.Nodes.Count);


        for (int i = 0;
             i < seam.Nodes.Count;
             i++)
        {
            GpuSeamNode node =
                seam.Nodes[i];


            if (node == null ||
                node.SourceChunk == null)
            {
                continue;
            }


            if (!chunkIds.TryGetValue(
                    node.SourceChunk,
                    out int chunkId))
            {
                chunkId =
                    chunkIds.Count;


                if (chunkId >= 63)
                {
                    return false;
                }


                chunkIds.Add(
                    node.SourceChunk,
                    chunkId);
            }


            if (!TryQuantizeNode(
                    node,
                    globalMin,
                    baseSize,
                    out int x,
                    out int y,
                    out int z,
                    out int size))
            {
                continue;
            }


            maxX =
                Mathf.Max(
                    maxX,
                    x + size);

            maxY =
                Mathf.Max(
                    maxY,
                    y + size);

            maxZ =
                Mathf.Max(
                    maxZ,
                    z + size);

            maxLeafSize =
                Mathf.Max(
                    maxLeafSize,
                    size);


            leaves.Add(
                new LeafRecord(
                    i,
                    new Vector3Int(
                        x,
                        y,
                        z),
                    size,
                    chunkId));
        }


        if (leaves.Count < 4)
        {
            return false;
        }


        // --------------------------------------------------------
        // Root size.
        // --------------------------------------------------------

        int requiredSize =
            Mathf.Max(
                maxLeafSize,
                maxX,
                maxY,
                maxZ);


        if (requiredSize <= 0)
        {
            return false;
        }


        int rootSize =
            NextPowerOfTwo(
                requiredSize);


        if (rootSize <= 0)
        {
            return false;
        }


        // --------------------------------------------------------
        // Create real leaves.
        // --------------------------------------------------------

        List<SeamOctreeNode> nodes =
            new List<SeamOctreeNode>(
                leaves.Count);


        for (int i = 0;
             i < leaves.Count;
             i++)
        {
            LeafRecord leaf =
                leaves[i];


            ulong chunkMask =
                1UL <<
                leaf.ChunkId;


            nodes.Add(
                new SeamOctreeNode(
                    leaf.Min,
                    leaf.Size,
                    leaf.SourceNodeIndex,
                    chunkMask));
        }


        // --------------------------------------------------------
        // Construct mixed-size parents.
        // --------------------------------------------------------

        nodes.Sort(
            CompareNodeSize);


        while (nodes.Count > 0 &&
               nodes[0].Size !=
               nodes[nodes.Count - 1].Size)
        {
            int currentSize =
                nodes[0].Size;


            int runEnd =
                1;


            while (runEnd < nodes.Count &&
                   nodes[runEnd].Size ==
                   currentSize)
            {
                runEnd++;
            }


            List<SeamOctreeNode> run =
                new List<SeamOctreeNode>(
                    runEnd);


            for (int i = 0;
                 i < runEnd;
                 i++)
            {
                run.Add(
                    nodes[i]);
            }


            List<SeamOctreeNode> parents =
                ConstructParents(
                    run,
                    currentSize * 2);


            List<SeamOctreeNode> newNodes =
                new List<SeamOctreeNode>(
                    parents.Count +
                    (
                        nodes.Count -
                        runEnd
                    ));


            for (int i = 0;
                 i < parents.Count;
                 i++)
            {
                newNodes.Add(
                    parents[i]);
            }


            for (int i = runEnd;
                 i < nodes.Count;
                 i++)
            {
                newNodes.Add(
                    nodes[i]);
            }


            nodes =
                newNodes;


            nodes.Sort(
                CompareNodeSize);
        }


        if (nodes.Count == 0)
        {
            return false;
        }


        // --------------------------------------------------------
        // All remaining nodes have equal size.
        // --------------------------------------------------------

        int parentSize =
            nodes[0].Size * 2;


        while (parentSize <= rootSize)
        {
            List<SeamOctreeNode> parents =
                ConstructParents(
                    nodes,
                    parentSize);


            if (parents.Count == 0)
            {
                return false;
            }


            nodes =
                parents;


            parentSize *= 2;
        }


        if (nodes.Count != 1)
        {
            return false;
        }


        root =
            nodes[0];


        return
            root != null;
    }


    // ============================================================
    // CONSTRUCT PARENTS
    // ============================================================

    private List<SeamOctreeNode> ConstructParents(
        List<SeamOctreeNode> children,
        int parentSize)
    {
        List<SeamOctreeNode> result =
            new List<SeamOctreeNode>();


        if (children == null ||
            children.Count == 0 ||
            parentSize <= 0)
        {
            return result;
        }


        int childSize =
            parentSize / 2;


        if (childSize <= 0)
        {
            return result;
        }


        Dictionary<Vector3Int, SeamOctreeNode> parentMap =
            new Dictionary<Vector3Int, SeamOctreeNode>();


        for (int i = 0;
             i < children.Count;
             i++)
        {
            SeamOctreeNode child =
                children[i];


            if (child == null)
            {
                continue;
            }


            Vector3Int parentMin =
                new Vector3Int(
                    FloorToMultiple(
                        child.Min.x,
                        parentSize),

                    FloorToMultiple(
                        child.Min.y,
                        parentSize),

                    FloorToMultiple(
                        child.Min.z,
                        parentSize));


            if (!parentMap.TryGetValue(
                    parentMin,
                    out SeamOctreeNode parent))
            {
                parent =
                    SeamOctreeNode.CreateInternal(
                        parentMin,
                        parentSize);


                parentMap.Add(
                    parentMin,
                    parent);


                result.Add(
                    parent);
            }


            int childIndex =
                GetChildIndex(
                    child.Min,
                    parentMin,
                    childSize);


            if (childIndex < 0 ||
                childIndex >= 8)
            {
                continue;
            }


            if (parent.Children[childIndex] != null)
            {
                // Preserve the first valid child.
                continue;
            }


            parent.Children[childIndex] =
                child;


            parent.ChunkMask |=
                child.ChunkMask;
        }


        return result;
    }


    // ============================================================
    // CHILD INDEX
    // ============================================================

    private static int GetChildIndex(
        Vector3Int min,
        Vector3Int parentMin,
        int childSize)
    {
        if (childSize <= 0)
        {
            return -1;
        }


        int index =
            0;


        if (min.x >=
            parentMin.x +
            childSize)
        {
            index |= 4;
        }


        if (min.y >=
            parentMin.y +
            childSize)
        {
            index |= 2;
        }


        if (min.z >=
            parentMin.z +
            childSize)
        {
            index |= 1;
        }


        return index;
    }


    // ============================================================
    // CONTOUR CELL PROC
    // ============================================================

    private void ContourCellProc(
        SeamOctreeNode node,
        SeamData seam,
        HashSet<EdgeKey> emittedEdges)
    {
        if (node == null ||
            node.IsLeaf)
        {
            return;
        }


        // --------------------------------------------------------
        // Children.
        // --------------------------------------------------------

        for (int i = 0;
             i < 8;
             i++)
        {
            ContourCellProc(
                node.Children[i],
                seam,
                emittedEdges);
        }


        // --------------------------------------------------------
        // Internal faces.
        // --------------------------------------------------------

        for (int i = 0;
             i < 12;
             i++)
        {
            int c0 =
                CellProcFaceMask[
                    i,
                    0];

            int c1 =
                CellProcFaceMask[
                    i,
                    1];

            int dir =
                CellProcFaceMask[
                    i,
                    2];


            SeamOctreeNode[] faceNodes =
            {
                node.Children[c0],
                node.Children[c1]
            };


            ContourFaceProc(
                faceNodes[0],
                faceNodes[1],
                dir,
                seam,
                emittedEdges);
        }


        // --------------------------------------------------------
        // Internal edges.
        // --------------------------------------------------------

        for (int i = 0;
             i < 6;
             i++)
        {
            SeamOctreeNode[] edgeNodes =
            {
                node.Children[
                    CellProcEdgeMask[
                        i,
                        0]
                ],

                node.Children[
                    CellProcEdgeMask[
                        i,
                        1]
                ],

                node.Children[
                    CellProcEdgeMask[
                        i,
                        2]
                ],

                node.Children[
                    CellProcEdgeMask[
                        i,
                        3]
                ]
            };


            int dir =
                CellProcEdgeMask[
                    i,
                    4];


            ContourEdgeProc(
                edgeNodes,
                dir,
                seam,
                emittedEdges);
        }
    }


    // ============================================================
    // CONTOUR FACE PROC
    // ============================================================

    private void ContourFaceProc(
        SeamOctreeNode node0,
        SeamOctreeNode node1,
        int dir,
        SeamData seam,
        HashSet<EdgeKey> emittedEdges)
    {
        if (node0 == null ||
            node1 == null)
        {
            return;
        }


        ulong combinedMask =
            node0.ChunkMask |
            node1.ChunkMask;


        if (!HasMultipleChunks(
                combinedMask))
        {
            return;
        }


        bool branch0 =
            !node0.IsLeaf;

        bool branch1 =
            !node1.IsLeaf;


        if (branch0 ||
            branch1)
        {
            // ----------------------------------------------------
            // Four sub-faces.
            // ----------------------------------------------------

            for (int i = 0;
                 i < 4;
                 i++)
            {
                int c0 =
                    FaceProcFaceMask[
                        dir,
                        i,
                        0];

                int c1 =
                    FaceProcFaceMask[
                        dir,
                        i,
                        1];

                int nextDir =
                    FaceProcFaceMask[
                        dir,
                        i,
                        2];


                SeamOctreeNode next0 =
                    branch0
                        ? node0.Children[c0]
                        : node0;


                SeamOctreeNode next1 =
                    branch1
                        ? node1.Children[c1]
                        : node1;


                ContourFaceProc(
                    next0,
                    next1,
                    nextDir,
                    seam,
                    emittedEdges);
            }


            // ----------------------------------------------------
            // Four sub-edges.
            // ----------------------------------------------------

            int[,] orders =
            {
                { 0, 0, 1, 1 },
                { 0, 1, 0, 1 }
            };


            for (int i = 0;
                 i < 4;
                 i++)
            {
                SeamOctreeNode[] edgeNodes =
                {
                    null,
                    null,
                    null,
                    null
                };


                int orderIndex =
                    FaceProcEdgeMask[
                        dir,
                        i,
                        0];


                int[] childCodes =
                {
                    FaceProcEdgeMask[
                        dir,
                        i,
                        1],

                    FaceProcEdgeMask[
                        dir,
                        i,
                        2],

                    FaceProcEdgeMask[
                        dir,
                        i,
                        3],

                    FaceProcEdgeMask[
                        dir,
                        i,
                        4]
                };


                int nextDir =
                    FaceProcEdgeMask[
                        dir,
                        i,
                        5];


                for (int j = 0;
                     j < 4;
                     j++)
                {
                    int order =
                        orders[
                            orderIndex,
                            j];


                    SeamOctreeNode sourceNode =
                        order == 0
                            ? node0
                            : node1;


                    if (sourceNode == null)
                    {
                        continue;
                    }


                    if (sourceNode.IsLeaf)
                    {
                        edgeNodes[j] =
                            sourceNode;
                    }
                    else
                    {
                        edgeNodes[j] =
                            sourceNode.Children[
                                childCodes[j]
                            ];
                    }
                }


                ContourEdgeProc(
                    edgeNodes,
                    nextDir,
                    seam,
                    emittedEdges);
            }
        }
    }


    // ============================================================
    // CONTOUR EDGE PROC
    // ============================================================

    private void ContourEdgeProc(
        SeamOctreeNode[] nodes,
        int dir,
        SeamData seam,
        HashSet<EdgeKey> emittedEdges)
    {
        if (nodes == null ||
            nodes.Length != 4)
        {
            return;
        }


        for (int i = 0;
             i < 4;
             i++)
        {
            if (nodes[i] == null)
            {
                return;
            }
        }


        ulong combinedMask =
            nodes[0].ChunkMask |
            nodes[1].ChunkMask |
            nodes[2].ChunkMask |
            nodes[3].ChunkMask;


        if (!HasMultipleChunks(
                combinedMask))
        {
            return;
        }


        bool branch0 =
            !nodes[0].IsLeaf;

        bool branch1 =
            !nodes[1].IsLeaf;

        bool branch2 =
            !nodes[2].IsLeaf;

        bool branch3 =
            !nodes[3].IsLeaf;


        if (!branch0 &&
            !branch1 &&
            !branch2 &&
            !branch3)
        {
            AddContourEdge(
                nodes,
                dir,
                seam,
                emittedEdges);

            return;
        }


        // --------------------------------------------------------
        // Recurse into smaller edge segments.
        // --------------------------------------------------------

        for (int i = 0;
             i < 2;
             i++)
        {
            SeamOctreeNode[] edgeNodes =
            {
                null,
                null,
                null,
                null
            };


            int[] childCodes =
            {
                EdgeProcEdgeMask[
                    dir,
                    i,
                    0],

                EdgeProcEdgeMask[
                    dir,
                    i,
                    1],

                EdgeProcEdgeMask[
                    dir,
                    i,
                    2],

                EdgeProcEdgeMask[
                    dir,
                    i,
                    3]
            };


            int nextDir =
                EdgeProcEdgeMask[
                    dir,
                    i,
                    4];


            bool[] branches =
            {
                branch0,
                branch1,
                branch2,
                branch3
            };


            for (int j = 0;
                 j < 4;
                 j++)
            {
                if (!branches[j])
                {
                    edgeNodes[j] =
                        nodes[j];
                }
                else
                {
                    edgeNodes[j] =
                        nodes[j].Children[
                            childCodes[j]
                        ];
                }
            }


            ContourEdgeProc(
                edgeNodes,
                nextDir,
                seam,
                emittedEdges);
        }
    }


    // ============================================================
    // ADD CONTOUR EDGE
    // ============================================================

    private void AddContourEdge(
        SeamOctreeNode[] nodes,
        int dir,
        SeamData seam,
        HashSet<EdgeKey> emittedEdges)
    {
        if (nodes == null ||
            nodes.Length != 4)
        {
            return;
        }


        for (int i = 0;
             i < 4;
             i++)
        {
            if (nodes[i] == null)
            {
                return;
            }
        }


        int n0 =
            nodes[0].SourceNodeIndex;

        int n1 =
            nodes[1].SourceNodeIndex;

        int n2 =
            nodes[2].SourceNodeIndex;

        int n3 =
            nodes[3].SourceNodeIndex;


        if (n0 < 0 ||
            n1 < 0 ||
            n2 < 0 ||
            n3 < 0)
        {
            return;
        }


        if (n0 >= seam.Nodes.Count ||
            n1 >= seam.Nodes.Count ||
            n2 >= seam.Nodes.Count ||
            n3 >= seam.Nodes.Count)
        {
            return;
        }


        GpuChunk chunk0 =
            seam.Nodes[n0].SourceChunk;

        GpuChunk chunk1 =
            seam.Nodes[n1].SourceChunk;

        GpuChunk chunk2 =
            seam.Nodes[n2].SourceChunk;

        GpuChunk chunk3 =
            seam.Nodes[n3].SourceChunk;


        if (chunk0 == null ||
            chunk1 == null ||
            chunk2 == null ||
            chunk3 == null)
        {
            return;
        }


        bool sameChunk =
            ReferenceEquals(
                chunk0,
                chunk1)
            &&
            ReferenceEquals(
                chunk0,
                chunk2)
            &&
            ReferenceEquals(
                chunk0,
                chunk3);


        if (sameChunk)
        {
            return;
        }


        seam.Edges.Add(
            new GpuSeamBuildBuffers.GpuSeamEdgeData
            {
                Node0 =
                    (uint)n0,

                Node1 =
                    (uint)n1,

                Node2 =
                    (uint)n2,

                Node3 =
                    (uint)n3,

                Axis =
                    (uint)dir
            });
    }


    // ============================================================
    // SORT FOUR VALUES
    // ============================================================

    private static void Sort4(
        ref int a,
        ref int b,
        ref int c,
        ref int d)
    {
        if (a > b)
        {
            int temp =
                a;

            a =
                b;

            b =
                temp;
        }

        if (c > d)
        {
            int temp =
                c;

            c =
                d;

            d =
                temp;
        }

        if (a > c)
        {
            int temp =
                a;

            a =
                c;

            c =
                temp;
        }

        if (b > d)
        {
            int temp =
                b;

            b =
                d;

            d =
                temp;
        }

        if (b > c)
        {
            int temp =
                b;

            b =
                c;

            c =
                temp;
        }
    }


    // ============================================================
    // HAS MULTIPLE CHUNKS
    // ============================================================

    private static bool HasMultipleChunks(
        ulong mask)
    {
        if (mask == 0UL)
        {
            return false;
        }


        return
            (mask &
             (mask - 1UL)) != 0UL;
    }


    // ============================================================
    // QUANTIZE NODE
    // ============================================================

    private bool TryQuantizeNode(
        GpuSeamNode node,
        Vector3 origin,
        float baseSize,
        out int x,
        out int y,
        out int z,
        out int size)
    {
        x =
            0;

        y =
            0;

        z =
            0;

        size =
            0;


        if (node == null ||
            !IsFinite(node.Size) ||
            node.Size <= 0f ||
            !IsFinite(baseSize) ||
            baseSize <= 0f)
        {
            return false;
        }


        Vector3 min =
            node.WorldMin;


        if (!IsFinite(min.x) ||
            !IsFinite(min.y) ||
            !IsFinite(min.z))
        {
            return false;
        }


        float relativeX =
            (
                min.x -
                origin.x
            ) /
            baseSize;


        float relativeY =
            (
                min.y -
                origin.y
            ) /
            baseSize;


        float relativeZ =
            (
                min.z -
                origin.z
            ) /
            baseSize;


        float relativeSize =
            node.Size /
            baseSize;


        if (!IsFinite(relativeX) ||
            !IsFinite(relativeY) ||
            !IsFinite(relativeZ) ||
            !IsFinite(relativeSize))
        {
            return false;
        }


        if (relativeX < 0f ||
            relativeY < 0f ||
            relativeZ < 0f ||
            relativeSize <= 0f)
        {
            return false;
        }


        if (relativeX >
                int.MaxValue ||
            relativeY >
                int.MaxValue ||
            relativeZ >
                int.MaxValue ||
            relativeSize >
                int.MaxValue)
        {
            return false;
        }


        x =
            Mathf.RoundToInt(
                relativeX);

        y =
            Mathf.RoundToInt(
                relativeY);

        z =
            Mathf.RoundToInt(
                relativeZ);

        size =
            Mathf.Max(
                1,
                Mathf.RoundToInt(
                    relativeSize));


        return
            size > 0;
    }


    // ============================================================
    // OWNER BOUNDARY NODES
    // ============================================================

    private void AddOwnerBoundaryNodes(
        GpuChunk ownerChunk,
        int touchMask,
        List<GpuSeamNode> output,
        HashSet<NodeKey> uniqueNodes)
    {
        int resolution =
            ownerChunk.Resolution;

        float step =
            ownerChunk.VoxelSize;


        if (resolution <= 0 ||
            !IsFinite(step) ||
            step <= 0f)
        {
            return;
        }


        Vector3 worldMin =
            ownerChunk.WorldMin;


        for (int z = 0;
             z < resolution;
             z++)
        {
            for (int y = 0;
                 y < resolution;
                 y++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    bool include =
                        ((touchMask & TouchNegX) != 0 &&
                         x == 0)
                        ||
                        ((touchMask & TouchPosX) != 0 &&
                         x == resolution - 1)
                        ||
                        ((touchMask & TouchNegY) != 0 &&
                         y == 0)
                        ||
                        ((touchMask & TouchPosY) != 0 &&
                         y == resolution - 1)
                        ||
                        ((touchMask & TouchNegZ) != 0 &&
                         z == 0)
                        ||
                        ((touchMask & TouchPosZ) != 0 &&
                         z == resolution - 1);


                    if (!include)
                    {
                        continue;
                    }


                    AddNode(
                        ownerChunk,
                        x,
                        y,
                        z,
                        worldMin,
                        step,
                        output,
                        uniqueNodes);
                }
            }
        }
    }


    // ============================================================
    // BOUNDARY TOUCH MASK
    // ============================================================

    private int GetBoundaryTouchMask(
        GpuChunk ownerChunk,
        GpuChunk sourceChunk)
    {
        Bounds owner =
            ownerChunk.WorldBounds;

        Bounds source =
            sourceChunk.WorldBounds;


        int mask =
            0;


        // --------------------------------------------------------
        // -X
        // --------------------------------------------------------

        if (Mathf.Abs(
                source.max.x -
                owner.min.x) <=
            NeighborEpsilon)
        {
            if (HasPositiveOverlap(
                    owner.min.y,
                    owner.max.y,
                    source.min.y,
                    source.max.y) &&
                HasPositiveOverlap(
                    owner.min.z,
                    owner.max.z,
                    source.min.z,
                    source.max.z))
            {
                mask |=
                    TouchNegX;
            }
        }


        // --------------------------------------------------------
        // +X
        // --------------------------------------------------------

        if (Mathf.Abs(
                source.min.x -
                owner.max.x) <=
            NeighborEpsilon)
        {
            if (HasPositiveOverlap(
                    owner.min.y,
                    owner.max.y,
                    source.min.y,
                    source.max.y) &&
                HasPositiveOverlap(
                    owner.min.z,
                    owner.max.z,
                    source.min.z,
                    source.max.z))
            {
                mask |=
                    TouchPosX;
            }
        }


        // --------------------------------------------------------
        // -Y
        // --------------------------------------------------------

        if (Mathf.Abs(
                source.max.y -
                owner.min.y) <=
            NeighborEpsilon)
        {
            if (HasPositiveOverlap(
                    owner.min.x,
                    owner.max.x,
                    source.min.x,
                    source.max.x) &&
                HasPositiveOverlap(
                    owner.min.z,
                    owner.max.z,
                    source.min.z,
                    source.max.z))
            {
                mask |=
                    TouchNegY;
            }
        }


        // --------------------------------------------------------
        // +Y
        // --------------------------------------------------------

        if (Mathf.Abs(
                source.min.y -
                owner.max.y) <=
            NeighborEpsilon)
        {
            if (HasPositiveOverlap(
                    owner.min.x,
                    owner.max.x,
                    source.min.x,
                    source.max.x) &&
                HasPositiveOverlap(
                    owner.min.z,
                    owner.max.z,
                    source.min.z,
                    source.max.z))
            {
                mask |=
                    TouchPosY;
            }
        }


        // --------------------------------------------------------
        // -Z
        // --------------------------------------------------------

        if (Mathf.Abs(
                source.max.z -
                owner.min.z) <=
            NeighborEpsilon)
        {
            if (HasPositiveOverlap(
                    owner.min.x,
                    owner.max.x,
                    source.min.x,
                    source.max.x) &&
                HasPositiveOverlap(
                    owner.min.y,
                    owner.max.y,
                    source.min.y,
                    source.max.y))
            {
                mask |=
                    TouchNegZ;
            }
        }


        // --------------------------------------------------------
        // +Z
        // --------------------------------------------------------

        if (Mathf.Abs(
                source.min.z -
                owner.max.z) <=
            NeighborEpsilon)
        {
            if (HasPositiveOverlap(
                    owner.min.x,
                    owner.max.x,
                    source.min.x,
                    source.max.x) &&
                HasPositiveOverlap(
                    owner.min.y,
                    owner.max.y,
                    source.min.y,
                    source.max.y))
            {
                mask |=
                    TouchPosZ;
            }
        }


        return mask;
    }


    // ============================================================
    // NEIGHBOR BOUNDARY NODES
    // ============================================================

    private void AddNeighborBoundaryNodes(
        GpuChunk ownerChunk,
        GpuChunk sourceChunk,
        int touchMask,
        List<GpuSeamNode> output,
        HashSet<NodeKey> uniqueNodes)
    {
        int resolution =
            sourceChunk.Resolution;

        float step =
            sourceChunk.VoxelSize;


        if (resolution <= 0 ||
            !IsFinite(step) ||
            step <= 0f)
        {
            return;
        }


        Vector3 worldMin =
            sourceChunk.WorldMin;

        Bounds ownerBounds =
            ownerChunk.WorldBounds;


        for (int z = 0;
             z < resolution;
             z++)
        {
            for (int y = 0;
                 y < resolution;
                 y++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    Vector3 nodeMin =
                        worldMin +
                        new Vector3(
                            x * step,
                            y * step,
                            z * step);


                    Vector3 nodeMax =
                        nodeMin +
                        Vector3.one *
                        step;


                    // ------------------------------------------------
                    // Contact sides.
                    // ------------------------------------------------

                    if ((touchMask & TouchNegX) != 0 &&
                        Mathf.Abs(
                            nodeMax.x -
                            ownerBounds.min.x) >
                        NeighborEpsilon)
                    {
                        continue;
                    }


                    if ((touchMask & TouchPosX) != 0 &&
                        Mathf.Abs(
                            nodeMin.x -
                            ownerBounds.max.x) >
                        NeighborEpsilon)
                    {
                        continue;
                    }


                    if ((touchMask & TouchNegY) != 0 &&
                        Mathf.Abs(
                            nodeMax.y -
                            ownerBounds.min.y) >
                        NeighborEpsilon)
                    {
                        continue;
                    }


                    if ((touchMask & TouchPosY) != 0 &&
                        Mathf.Abs(
                            nodeMin.y -
                            ownerBounds.max.y) >
                        NeighborEpsilon)
                    {
                        continue;
                    }


                    if ((touchMask & TouchNegZ) != 0 &&
                        Mathf.Abs(
                            nodeMax.z -
                            ownerBounds.min.z) >
                        NeighborEpsilon)
                    {
                        continue;
                    }


                    if ((touchMask & TouchPosZ) != 0 &&
                        Mathf.Abs(
                            nodeMin.z -
                            ownerBounds.max.z) >
                        NeighborEpsilon)
                    {
                        continue;
                    }


                    // ------------------------------------------------
                    // Non-contact axes must overlap the owner.
                    // ------------------------------------------------

                    if ((touchMask & TouchNegX) == 0 &&
                        (touchMask & TouchPosX) == 0 &&
                        OverlapLength(
                            nodeMin.x,
                            nodeMax.x,
                            ownerBounds.min.x,
                            ownerBounds.max.x) <=
                        NeighborEpsilon)
                    {
                        continue;
                    }


                    if ((touchMask & TouchNegY) == 0 &&
                        (touchMask & TouchPosY) == 0 &&
                        OverlapLength(
                            nodeMin.y,
                            nodeMax.y,
                            ownerBounds.min.y,
                            ownerBounds.max.y) <=
                        NeighborEpsilon)
                    {
                        continue;
                    }


                    if ((touchMask & TouchNegZ) == 0 &&
                        (touchMask & TouchPosZ) == 0 &&
                        OverlapLength(
                            nodeMin.z,
                            nodeMax.z,
                            ownerBounds.min.z,
                            ownerBounds.max.z) <=
                        NeighborEpsilon)
                    {
                        continue;
                    }


                    AddNode(
                        sourceChunk,
                        x,
                        y,
                        z,
                        worldMin,
                        step,
                        output,
                        uniqueNodes);
                }
            }
        }
    }


    // ============================================================
    // ADD NODE
    // ============================================================

    private void AddNode(
        GpuChunk sourceChunk,
        int x,
        int y,
        int z,
        Vector3 worldMin,
        float step,
        List<GpuSeamNode> output,
        HashSet<NodeKey> uniqueNodes)
    {
        if (sourceChunk == null ||
            output == null ||
            uniqueNodes == null)
        {
            return;
        }


        NodeKey key =
            new NodeKey(
                sourceChunk,
                x,
                y,
                z);


        if (!uniqueNodes.Add(
                key))
        {
            return;
        }


        Vector3 min =
            worldMin +
            new Vector3(
                x * step,
                y * step,
                z * step);


        Vector3 center =
            min +
            Vector3.one *
            (
                step *
                0.5f
            );


        output.Add(
            new GpuSeamNode(
                sourceChunk,
                x,
                y,
                z,
                center,
                step,
                sourceChunk.Lod));
    }


    // ============================================================
    // NODE SIZE SORT
    // ============================================================

    private static int CompareNodeSize(
        SeamOctreeNode lhs,
        SeamOctreeNode rhs)
    {
        return
            lhs.Size.CompareTo(
                rhs.Size);
    }


    // ============================================================
    // FLOOR TO MULTIPLE
    // ============================================================

    private static int FloorToMultiple(
        int value,
        int multiple)
    {
        if (multiple <= 0)
        {
            return 0;
        }


        if (value >= 0)
        {
            return
                value -
                (
                    value %
                    multiple
                );
        }


        int remainder =
            value %
            multiple;


        if (remainder == 0)
        {
            return value;
        }


        return
            value -
            remainder -
            multiple;
    }


    // ============================================================
    // NEXT POWER OF TWO
    // ============================================================

    private static int NextPowerOfTwo(
        int value)
    {
        if (value <= 1)
        {
            return 1;
        }


        int result =
            1;


        while (result < value)
        {
            if (result >
                int.MaxValue / 2)
            {
                return -1;
            }


            result *= 2;
        }


        return result;
    }


    // ============================================================
    // OVERLAP
    // ============================================================

    private static float OverlapLength(
        float minA,
        float maxA,
        float minB,
        float maxB)
    {
        float min =
            Mathf.Max(
                minA,
                minB);


        float max =
            Mathf.Min(
                maxA,
                maxB);


        return
            max -
            min;
    }


    private static bool HasPositiveOverlap(
        float minA,
        float maxA,
        float minB,
        float maxB)
    {
        return
            OverlapLength(
                minA,
                maxA,
                minB,
                maxB) >
            NeighborEpsilon;
    }


    // ============================================================
    // FINITE
    // ============================================================

    private static bool IsFinite(
        float value)
    {
        return
            !float.IsNaN(value) &&
            !float.IsInfinity(value);
    }


    // ============================================================
    // NODE KEY
    // ============================================================

    private readonly struct NodeKey :
        IEquatable<NodeKey>
    {
        private readonly GpuChunk source;

        private readonly int x;
        private readonly int y;
        private readonly int z;


        public NodeKey(
            GpuChunk source,
            int x,
            int y,
            int z)
        {
            this.source =
                source;

            this.x =
                x;

            this.y =
                y;

            this.z =
                z;
        }


        public bool Equals(
            NodeKey other)
        {
            return
                ReferenceEquals(
                    source,
                    other.source)
                &&
                x == other.x
                &&
                y == other.y
                &&
                z == other.z;
        }


        public override bool Equals(
            object obj)
        {
            return
                obj is NodeKey other &&
                Equals(other);
        }


        public override int GetHashCode()
        {
            unchecked
            {
                int hash =
                    source != null
                        ? source.GetHashCode()
                        : 0;


                hash =
                    hash * 397 +
                    x;


                hash =
                    hash * 397 +
                    y;


                hash =
                    hash * 397 +
                    z;


                return hash;
            }
        }
    }


    // ============================================================
    // DUAL CONTOURING TABLES
    // ============================================================

    private static readonly int[,] CellProcFaceMask =
    {
        { 0, 4, 0 },
        { 1, 5, 0 },
        { 2, 6, 0 },
        { 3, 7, 0 },

        { 0, 2, 1 },
        { 4, 6, 1 },
        { 1, 3, 1 },
        { 5, 7, 1 },

        { 0, 1, 2 },
        { 2, 3, 2 },
        { 4, 5, 2 },
        { 6, 7, 2 }
    };


    private static readonly int[,] CellProcEdgeMask =
    {
        { 0, 1, 2, 3, 0 },
        { 4, 5, 6, 7, 0 },

        { 0, 4, 1, 5, 1 },
        { 2, 6, 3, 7, 1 },

        { 0, 2, 4, 6, 2 },
        { 1, 3, 5, 7, 2 }
    };


    private static readonly int[,,] FaceProcFaceMask =
    {
        {
            { 4, 0, 0 },
            { 5, 1, 0 },
            { 6, 2, 0 },
            { 7, 3, 0 }
        },

        {
            { 2, 0, 1 },
            { 6, 4, 1 },
            { 3, 1, 1 },
            { 7, 5, 1 }
        },

        {
            { 1, 0, 2 },
            { 3, 2, 2 },
            { 5, 4, 2 },
            { 7, 6, 2 }
        }
    };


    private static readonly int[,,] FaceProcEdgeMask =
    {
        {
            { 1, 4, 0, 5, 1, 1 },
            { 1, 6, 2, 7, 3, 1 },
            { 0, 4, 6, 0, 2, 2 },
            { 0, 5, 7, 1, 3, 2 }
        },

        {
            { 0, 2, 3, 0, 1, 0 },
            { 0, 6, 7, 4, 5, 0 },
            { 1, 2, 0, 6, 4, 2 },
            { 1, 3, 1, 7, 5, 2 }
        },

        {
            { 1, 1, 0, 3, 2, 0 },
            { 1, 5, 4, 7, 6, 0 },
            { 0, 1, 5, 0, 4, 1 },
            { 0, 3, 7, 2, 6, 1 }
        }
    };


    private static readonly int[,,] EdgeProcEdgeMask =
    {
        {
            { 3, 2, 1, 0, 0 },
            { 7, 6, 5, 4, 0 }
        },

        {
            { 5, 1, 4, 0, 1 },
            { 7, 3, 6, 2, 1 }
        },

        {
            { 6, 4, 2, 0, 2 },
            { 7, 5, 3, 1, 2 }
        }
    };


    // ============================================================
    // TOUCH FLAGS
    // ============================================================

    private const int TouchNegX =
        1;

    private const int TouchPosX =
        2;

    private const int TouchNegY =
        4;

    private const int TouchPosY =
        8;

    private const int TouchNegZ =
        16;

    private const int TouchPosZ =
        32;


    // ============================================================
    // CONSTANT
    // ============================================================

    private const float NeighborEpsilon =
        0.001f;
}