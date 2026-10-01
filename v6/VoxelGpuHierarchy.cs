using System;
using UnityEngine;

[DisallowMultipleComponent]
public class VoxelGpuHierarchy : MonoBehaviour
{
    [Header("Compute")]
    [SerializeField] private ComputeShader voxelDensityShader;

    [Header("Hierarchy")]
    [SerializeField] private int rootLod = 12;
    [SerializeField] private int minLod = 0;
    [SerializeField] private int maxLod = 20;
    [SerializeField] private int maxOctreeNodes = 262144;
    [SerializeField] private int maxActiveLeaves = 65536;

    [Header("LOD Distances")]
    [SerializeField] private float lodSplitDistanceMultiplier = 1.5f;
    [SerializeField] private float lodMergeDistanceMultiplier = 2.5f;

    [Header("Leaf Mesh Arena")]
    [SerializeField] private int meshVertexArenaCapacity = 1048576;
    [SerializeField] private int meshIndexArenaCapacity = 8388608;
    [SerializeField] private int meshVertexHashCapacity = 2097152;

    [Header("Debug")]
    [SerializeField] private bool debugReadback = true;
    [SerializeField] private int debugFrameInterval = 30;

    private const int NODE_STRIDE = 48;
    private const int ACTIVE_LEAF_STRIDE = 24;
    private const int ACTIVE_LEAF_NEIGHBOR_TOPOLOGY_STRIDE = 96;
    private const int ACTIVE_LEAF_TRANSITION_TOPOLOGY_STRIDE = 24;
    private const int ACTIVE_LEAF_MESH_SLOT_STRIDE = 4;
    private const int ACTIVE_LEAF_MESH_INFO_STRIDE = 28;
    private const int UINT_STRIDE = 4;

    private const int LEAF_SLOT_GROUPS_X = 32768;

    private const int THREADS = 64;
    private const int DEFAULT_CHUNK_RESOLUTION = 32;
    private const float BASE_LATTICE_SIZE = 0.5f;

    private const uint TRANSITION_NONE = 0u;
    private const uint TRANSITION_COARSE_TO_FINE = 1u;
    private const uint TRANSITION_FINE_TO_COARSE = 2u;
    private const uint TRANSITION_MIXED = 3u;

    private GraphicsBuffer octreeNodeBuffer;
    private GraphicsBuffer activeLeafBuffer;
    private GraphicsBuffer activeLeafNeighborTopologyBuffer;
    private GraphicsBuffer activeLeafTransitionTopologyBuffer;
    private GraphicsBuffer activeLeafMeshSlotBuffer;

    private GraphicsBuffer activeLeafMeshInfoBuffer;
    private GraphicsBuffer activeLeafMeshVertexCounterBuffer;
    private GraphicsBuffer activeLeafMeshIndexCounterBuffer;
    private GraphicsBuffer activeLeafMeshVertexHashKeyBuffer;
    private GraphicsBuffer activeLeafMeshVertexHashValueBuffer;
    private GraphicsBuffer meshOverflowFlagsBuffer;

    private GraphicsBuffer octreeFreeNodeStack;
    private GraphicsBuffer octreeFreeNodeCountBuffer;
    private GraphicsBuffer activeLeafCountBuffer;
    private GraphicsBuffer hierarchyChangedCountBuffer;
    private GraphicsBuffer activeLeafMinLodBuffer;
    private GraphicsBuffer activeLeafMaxLodBuffer;

    private GraphicsBuffer vertexBuffer;
    private GraphicsBuffer normalBuffer;
    private GraphicsBuffer vertexValidBuffer;
    private GraphicsBuffer vertexCountBuffer;
    private GraphicsBuffer indexBuffer;
    private GraphicsBuffer indexCountBuffer;

    private int initializeOctreeKernel;
    private int selectLodKernel;
    private int balanceOctree2To1Kernel;
    private int applyLodRequestsKernel;
    private int applyMergeRequestsKernel;

    private int buildNeighborLodKernel;
    private int buildNeighborTopologyKernel;
    private int buildTransitionTopologyKernel;

    private int resetActiveLeavesKernel;
    private int rebuildActiveLeavesKernel;

    private int resetLeafMeshKernel;
    private int countLeafMeshVerticesKernel;
    private int scanLeafMeshVertexOffsetsKernel;
    private int buildVerticesKernel;

    private int countLeafMeshIndicesKernel;
    private int scanLeafMeshIndexOffsetsKernel;

    private int buildXFacesKernel;
    private int buildYFacesKernel;
    private int buildZFacesKernel;
    private int buildLeafBoundaryFacesKernel;

    private int finalizeLeafMeshKernel;

    private int resetLodStatsKernel;
    private int calculateLodStatsKernel;

    private bool initialized;
    private int frameCounter;

    private Vector3 lodTargetPosition;
    private Vector3Int rootCanonicalOrigin;

    private readonly uint[] oneUint = new uint[1];
    private readonly uint[] rootReadback = new uint[12];

    // VoxelActiveLeaf = 24 bytes = 6 uint values.
    private readonly uint[] activeLeafReadback =
        new uint[65536 * 6];

    private bool hasLoggedLodState;

    private uint lastActiveLeafCount;
    private uint lastHierarchyChangedCount;
    private uint lastMinLod;
    private uint lastMaxLod;
    private uint lastMeshVertexCount;
    private uint lastMeshIndexCount;
    private uint lastMeshOverflowFlags;

    public bool IsInitialized =>
        initialized;

    public GraphicsBuffer VertexBuffer =>
        vertexBuffer;

    public GraphicsBuffer NormalBuffer =>
        normalBuffer;

    public GraphicsBuffer IndexBuffer =>
        indexBuffer;

    public GraphicsBuffer IndexCountBuffer =>
        indexCountBuffer;

    public GraphicsBuffer VertexCountBuffer =>
        vertexCountBuffer;

    public GraphicsBuffer MeshOverflowFlagsBuffer =>
        meshOverflowFlagsBuffer;

    public int MeshVertexArenaCapacity =>
        meshVertexArenaCapacity;

    public int MeshIndexArenaCapacity =>
        meshIndexArenaCapacity;

    public Vector3Int RootCanonicalOrigin =>
        rootCanonicalOrigin;

    private void OnDisable()
    {
        Shutdown();
    }

    private void OnDestroy()
    {
        Shutdown();
    }

    public bool Initialize(
        Vector3 densityPlanetCenter,
        float densityPlanetRadius,
        int densityNoiseSeed,
        float densityNoiseFrequency,
        float densityNoiseHeight,
        Vector3 initialLodTargetPosition
    )
    {
        if (initialized)
            return true;

        if (voxelDensityShader == null)
        {
            LogError("ComputeShader is not assigned.");
            return false;
        }

        if (densityPlanetRadius < 0.0f)
        {
            LogError(
                $"PlanetRadius={densityPlanetRadius} is invalid."
            );
            return false;
        }

        if (maxOctreeNodes < 9)
        {
            LogError(
                $"MaxOctreeNodes={maxOctreeNodes} is too small."
            );
            return false;
        }

        if (maxActiveLeaves < 1)
        {
            LogError(
                $"MaxActiveLeaves={maxActiveLeaves} is invalid."
            );
            return false;
        }

        if (maxActiveLeaves > 65536)
        {
            LogError(
                $"MaxActiveLeaves={maxActiveLeaves} exceeds the current " +
                "leaf-slot encoding limit of 65536."
            );
            return false;
        }

        if (meshVertexArenaCapacity < 1)
        {
            LogError(
                $"MeshVertexArenaCapacity={meshVertexArenaCapacity} is invalid."
            );
            return false;
        }

        if (meshIndexArenaCapacity < 6)
        {
            LogError(
                $"MeshIndexArenaCapacity={meshIndexArenaCapacity} is invalid."
            );
            return false;
        }

        if (meshVertexHashCapacity < 1024)
        {
            LogError(
                $"MeshVertexHashCapacity={meshVertexHashCapacity} is too small."
            );
            return false;
        }

        lodTargetPosition =
            initialLodTargetPosition;

        CalculateRoot(
            densityPlanetCenter,
            densityPlanetRadius,
            densityNoiseHeight
        );

        FindKernels();
        AllocateBuffers();

        SetShaderParameters(
            densityPlanetCenter,
            densityPlanetRadius,
            densityNoiseSeed,
            densityNoiseFrequency,
            densityNoiseHeight
        );

        voxelDensityShader.SetVector(
            "LodTargetPosition",
            lodTargetPosition
        );

        BindAllBuffers();

        DispatchInitializeOctree();
        DispatchBuildNeighborLod();

        DispatchResetActiveLeaves();
        DispatchRebuildActiveLeaves();

        DispatchBuildNeighborTopology();
        DispatchBuildTransitionTopology();

        DispatchBuildLeafMesh();

        DispatchResetLodStats();
        DispatchCalculateLodStats();

        initialized = true;

        LogDebug(
            $"Initialized " +
            $"PlanetCenter={densityPlanetCenter} " +
            $"PlanetRadius={densityPlanetRadius} " +
            $"RootLod={rootLod} " +
            $"MinLod={minLod} " +
            $"MaxLod={maxLod} " +
            $"MaxNodes={maxOctreeNodes} " +
            $"MaxLeaves={maxActiveLeaves}"
        );

        LogDebug(
            $"Root Origin={rootCanonicalOrigin} " +
            $"WorldSize={GetRootWorldSize():F0}"
        );

        LogDebug(
            $"MeshArena " +
            $"Vertices={meshVertexArenaCapacity} " +
            $"Indices={meshIndexArenaCapacity} " +
            $"Hash={meshVertexHashCapacity}"
        );

        LogDebug(
            $"LogFile={VoxelDebugLogger.GetLogFilePath()}"
        );

        if (debugReadback)
        {
            ReadDebugRoot();
            ReadDebugState();
        }

        return true;
    }

    public void SetLodTargetPosition(
        Vector3 position
    )
    {
        lodTargetPosition = position;

        if (!initialized ||
            voxelDensityShader == null)
        {
            return;
        }

        voxelDensityShader.SetVector(
            "LodTargetPosition",
            lodTargetPosition
        );
    }

    public void Tick()
    {
        if (!initialized)
            return;

        voxelDensityShader.SetVector(
            "LodTargetPosition",
            lodTargetPosition
        );

        DispatchLodSelection();
        DispatchBalance2To1();
        DispatchLodRequests();
        DispatchMergeRequests();
        DispatchBuildNeighborLod();

        DispatchResetActiveLeaves();
        DispatchRebuildActiveLeaves();

        DispatchBuildNeighborTopology();
        DispatchBuildTransitionTopology();

        DispatchBuildLeafMesh();

        DispatchResetLodStats();
        DispatchCalculateLodStats();

        frameCounter++;

        if (debugReadback &&
            debugFrameInterval > 0 &&
            frameCounter % debugFrameInterval == 0)
        {
            ReadDebugState();
        }
    }

    public void Shutdown()
    {
        ReleaseBuffers();

        initialized = false;
        frameCounter = 0;
        hasLoggedLodState = false;
    }

    private void FindKernels()
    {
        initializeOctreeKernel =
            voxelDensityShader.FindKernel(
                "CSInitializeOctree"
            );

        selectLodKernel =
            voxelDensityShader.FindKernel(
                "CSSelectLOD"
            );

        balanceOctree2To1Kernel =
            voxelDensityShader.FindKernel(
                "CSBalanceOctree2To1"
            );

        applyLodRequestsKernel =
            voxelDensityShader.FindKernel(
                "CSApplyLODRequests"
            );

        applyMergeRequestsKernel =
            voxelDensityShader.FindKernel(
                "CSApplyMergeRequests"
            );

        buildNeighborLodKernel =
            voxelDensityShader.FindKernel(
                "CSBuildNeighborLOD"
            );

        buildNeighborTopologyKernel =
            voxelDensityShader.FindKernel(
                "CSBuildNeighborTopology"
            );

        buildTransitionTopologyKernel =
            voxelDensityShader.FindKernel(
                "CSBuildTransitionTopology"
            );

        resetActiveLeavesKernel =
            voxelDensityShader.FindKernel(
                "CSResetActiveLeaves"
            );

        rebuildActiveLeavesKernel =
            voxelDensityShader.FindKernel(
                "CSRebuildActiveLeaves"
            );

        resetLeafMeshKernel =
            voxelDensityShader.FindKernel(
                "CSResetLeafMesh"
            );

        countLeafMeshVerticesKernel =
            voxelDensityShader.FindKernel(
                "CSCountLeafMeshVertices"
            );

        scanLeafMeshVertexOffsetsKernel =
            voxelDensityShader.FindKernel(
                "CSScanLeafMeshVertexOffsets"
            );

        buildVerticesKernel =
            voxelDensityShader.FindKernel(
                "CSBuildVertices"
            );

        countLeafMeshIndicesKernel =
            voxelDensityShader.FindKernel(
                "CSCountLeafMeshIndices"
            );

        scanLeafMeshIndexOffsetsKernel =
            voxelDensityShader.FindKernel(
                "CSScanLeafMeshIndexOffsets"
            );

        buildXFacesKernel =
            voxelDensityShader.FindKernel(
                "CSBuildXFaces"
            );

        buildYFacesKernel =
            voxelDensityShader.FindKernel(
                "CSBuildYFaces"
            );

        buildZFacesKernel =
            voxelDensityShader.FindKernel(
                "CSBuildZFaces"
            );

        buildLeafBoundaryFacesKernel =
            voxelDensityShader.FindKernel(
                "CSBuildLeafBoundaryFaces"
            );

        finalizeLeafMeshKernel =
            voxelDensityShader.FindKernel(
                "CSFinalizeLeafMesh"
            );

        resetLodStatsKernel =
            voxelDensityShader.FindKernel(
                "CSResetLodStats"
            );

        calculateLodStatsKernel =
            voxelDensityShader.FindKernel(
                "CSCalculateLodStats"
            );
    }

    private void AllocateBuffers()
    {
        ReleaseBuffers();

        octreeNodeBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxOctreeNodes,
            NODE_STRIDE
        );

        activeLeafBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxActiveLeaves,
            ACTIVE_LEAF_STRIDE
        );

        activeLeafNeighborTopologyBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxOctreeNodes,
            ACTIVE_LEAF_NEIGHBOR_TOPOLOGY_STRIDE
        );

        activeLeafTransitionTopologyBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxOctreeNodes,
            ACTIVE_LEAF_TRANSITION_TOPOLOGY_STRIDE
        );

        activeLeafMeshSlotBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxOctreeNodes,
            ACTIVE_LEAF_MESH_SLOT_STRIDE
        );

        activeLeafMeshInfoBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxActiveLeaves,
            ACTIVE_LEAF_MESH_INFO_STRIDE
        );

        activeLeafMeshVertexCounterBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxActiveLeaves,
            UINT_STRIDE
        );

        activeLeafMeshIndexCounterBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxActiveLeaves,
            UINT_STRIDE
        );

        activeLeafMeshVertexHashKeyBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            meshVertexHashCapacity,
            UINT_STRIDE
        );

        activeLeafMeshVertexHashValueBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            meshVertexHashCapacity,
            UINT_STRIDE
        );

        meshOverflowFlagsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            1,
            UINT_STRIDE
        );

        octreeFreeNodeStack = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxOctreeNodes,
            UINT_STRIDE
        );

        octreeFreeNodeCountBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            1,
            UINT_STRIDE
        );

        activeLeafCountBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            1,
            UINT_STRIDE
        );

        hierarchyChangedCountBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            1,
            UINT_STRIDE
        );

        activeLeafMinLodBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            1,
            UINT_STRIDE
        );

        activeLeafMaxLodBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            1,
            UINT_STRIDE
        );

        vertexBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            meshVertexArenaCapacity,
            sizeof(float) * 3
        );

        normalBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            meshVertexArenaCapacity,
            sizeof(float) * 3
        );

        vertexValidBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            meshVertexArenaCapacity,
            UINT_STRIDE
        );

        vertexCountBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            1,
            UINT_STRIDE
        );

        indexBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            meshIndexArenaCapacity,
            UINT_STRIDE
        );

        indexCountBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            1,
            UINT_STRIDE
        );
    }

    private void SetShaderParameters(
        Vector3 densityPlanetCenter,
        float densityPlanetRadius,
        int densityNoiseSeed,
        float densityNoiseFrequency,
        float densityNoiseHeight
    )
    {
        voxelDensityShader.SetInt(
            "CellCount",
            DEFAULT_CHUNK_RESOLUTION
        );

        voxelDensityShader.SetVector(
            "Origin",
            densityPlanetCenter
        );

        voxelDensityShader.SetFloat(
            "Step",
            BASE_LATTICE_SIZE
        );

        voxelDensityShader.SetVector(
            "PlanetCenter",
            densityPlanetCenter
        );

        voxelDensityShader.SetFloat(
            "PlanetRadius",
            densityPlanetRadius
        );

        voxelDensityShader.SetInt(
            "NoiseSeed",
            densityNoiseSeed
        );

        voxelDensityShader.SetFloat(
            "NoiseFrequency",
            densityNoiseFrequency
        );

        voxelDensityShader.SetFloat(
            "NoiseHeight",
            densityNoiseHeight
        );

        voxelDensityShader.SetInt(
            "RootLod",
            rootLod
        );

        voxelDensityShader.SetInt(
            "MinLod",
            minLod
        );

        voxelDensityShader.SetInt(
            "MaxLod",
            maxLod
        );

        voxelDensityShader.SetInt(
            "MaxOctreeNodes",
            maxOctreeNodes
        );

        voxelDensityShader.SetInt(
            "MaxActiveLeaves",
            maxActiveLeaves
        );

        voxelDensityShader.SetInts(
            "RootCanonicalOrigin",
            rootCanonicalOrigin.x,
            rootCanonicalOrigin.y,
            rootCanonicalOrigin.z
        );

        voxelDensityShader.SetVector(
            "LodTargetPosition",
            lodTargetPosition
        );

        voxelDensityShader.SetFloat(
            "LodSplitDistanceMultiplier",
            lodSplitDistanceMultiplier
        );

        voxelDensityShader.SetFloat(
            "LodMergeDistanceMultiplier",
            lodMergeDistanceMultiplier
        );

        voxelDensityShader.SetInt(
            "MeshVertexArenaCapacity",
            meshVertexArenaCapacity
        );

        voxelDensityShader.SetInt(
            "MeshIndexArenaCapacity",
            meshIndexArenaCapacity
        );

        voxelDensityShader.SetInt(
            "MeshVertexHashCapacity",
            meshVertexHashCapacity
        );
    }

    private void BindAllBuffers()
    {
        BindCommonBuffers(
            initializeOctreeKernel
        );

        BindCommonBuffers(
            selectLodKernel
        );

        BindCommonBuffers(
            balanceOctree2To1Kernel
        );

        BindCommonBuffers(
            applyLodRequestsKernel
        );

        BindCommonBuffers(
            applyMergeRequestsKernel
        );

        BindCommonBuffers(
            buildNeighborLodKernel
        );

        BindCommonBuffers(
            buildNeighborTopologyKernel
        );

        BindCommonBuffers(
            buildTransitionTopologyKernel
        );

        BindCommonBuffers(
            resetActiveLeavesKernel
        );

        BindCommonBuffers(
            rebuildActiveLeavesKernel
        );

        BindCommonBuffers(
            resetLeafMeshKernel
        );

        BindCommonBuffers(
            countLeafMeshVerticesKernel
        );

        BindCommonBuffers(
            scanLeafMeshVertexOffsetsKernel
        );

        BindCommonBuffers(
            buildVerticesKernel
        );

        BindCommonBuffers(
            countLeafMeshIndicesKernel
        );

        BindCommonBuffers(
            scanLeafMeshIndexOffsetsKernel
        );

        BindCommonBuffers(
            buildXFacesKernel
        );

        BindCommonBuffers(
            buildYFacesKernel
        );

        BindCommonBuffers(
            buildZFacesKernel
        );

        // IMPORTANT:
        // Do NOT call BindCommonBuffers(buildLeafBoundaryFacesKernel).
        // That kernel must use a reduced UAV set.

        BindCommonBuffers(
            finalizeLeafMeshKernel
        );

        BindCommonBuffers(
            resetLodStatsKernel
        );

        BindCommonBuffers(
            calculateLodStatsKernel
        );

        BindLeafMeshBuffers(
            resetLeafMeshKernel
        );

        BindLeafMeshBuffers(
            countLeafMeshVerticesKernel
        );

        BindLeafMeshBuffers(
            scanLeafMeshVertexOffsetsKernel
        );

        BindLeafMeshBuffers(
            buildVerticesKernel
        );

        BindLeafMeshBuffers(
            countLeafMeshIndicesKernel
        );

        BindLeafMeshBuffers(
            scanLeafMeshIndexOffsetsKernel
        );

        BindLeafMeshBuffers(
            buildXFacesKernel
        );

        BindLeafMeshBuffers(
            buildYFacesKernel
        );

        BindLeafMeshBuffers(
            buildZFacesKernel
        );

        // IMPORTANT:
        // Do NOT call BindLeafMeshBuffers(buildLeafBoundaryFacesKernel).
        // It binds extra UAVs that the boundary kernel does not need.

        BindLeafBoundaryFaceBuffers();

        BindLeafMeshBuffers(
            finalizeLeafMeshKernel
        );

        voxelDensityShader.SetBuffer(
            buildNeighborTopologyKernel,
            "ActiveLeafNeighborTopologyBuffer",
            activeLeafNeighborTopologyBuffer
        );

        voxelDensityShader.SetBuffer(
            buildTransitionTopologyKernel,
            "ActiveLeafNeighborTopologyBuffer",
            activeLeafNeighborTopologyBuffer
        );

        voxelDensityShader.SetBuffer(
            buildTransitionTopologyKernel,
            "ActiveLeafTransitionTopologyBuffer",
            activeLeafTransitionTopologyBuffer
        );

        voxelDensityShader.SetBuffer(
            rebuildActiveLeavesKernel,
            "ActiveLeafMeshSlotBuffer",
            activeLeafMeshSlotBuffer
        );

        voxelDensityShader.SetBuffer(
            countLeafMeshIndicesKernel,
            "ActiveLeafMeshSlotBuffer",
            activeLeafMeshSlotBuffer
        );

        voxelDensityShader.SetBuffer(
            countLeafMeshIndicesKernel,
            "ActiveLeafNeighborTopologyBuffer",
            activeLeafNeighborTopologyBuffer
        );
    }

    private void BindLeafBoundaryFaceBuffers()
    {
        voxelDensityShader.SetBuffer(
            buildLeafBoundaryFacesKernel,
            "ActiveLeafBuffer",
            activeLeafBuffer
        );

        voxelDensityShader.SetBuffer(
            buildLeafBoundaryFacesKernel,
            "ActiveLeafMeshSlotBuffer",
            activeLeafMeshSlotBuffer
        );

        voxelDensityShader.SetBuffer(
            buildLeafBoundaryFacesKernel,
            "ActiveLeafMeshInfoBuffer",
            activeLeafMeshInfoBuffer
        );

        voxelDensityShader.SetBuffer(
            buildLeafBoundaryFacesKernel,
            "ActiveLeafMeshIndexCounterBuffer",
            activeLeafMeshIndexCounterBuffer
        );

        voxelDensityShader.SetBuffer(
            buildLeafBoundaryFacesKernel,
            "IndexBuffer",
            indexBuffer
        );

        voxelDensityShader.SetBuffer(
            buildLeafBoundaryFacesKernel,
            "ActiveLeafNeighborTopologyBuffer",
            activeLeafNeighborTopologyBuffer
        );

        voxelDensityShader.SetBuffer(
            buildLeafBoundaryFacesKernel,
            "ActiveLeafMeshVertexHashKeyBuffer",
            activeLeafMeshVertexHashKeyBuffer
        );

        voxelDensityShader.SetBuffer(
            buildLeafBoundaryFacesKernel,
            "ActiveLeafMeshVertexHashValueBuffer",
            activeLeafMeshVertexHashValueBuffer
        );
    }

    private void BindLeafMeshBuffers(
        int kernel
    )
    {
        voxelDensityShader.SetBuffer(
            kernel,
            "ActiveLeafMeshInfoBuffer",
            activeLeafMeshInfoBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "ActiveLeafMeshVertexCounterBuffer",
            activeLeafMeshVertexCounterBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "ActiveLeafMeshIndexCounterBuffer",
            activeLeafMeshIndexCounterBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "ActiveLeafMeshVertexHashKeyBuffer",
            activeLeafMeshVertexHashKeyBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "ActiveLeafMeshVertexHashValueBuffer",
            activeLeafMeshVertexHashValueBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "MeshOverflowFlagsBuffer",
            meshOverflowFlagsBuffer
        );
    }

    private void BindCommonBuffers(
        int kernel
    )
    {
        voxelDensityShader.SetBuffer(
            kernel,
            "OctreeNodeBuffer",
            octreeNodeBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "ActiveLeafBuffer",
            activeLeafBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "OctreeFreeNodeStack",
            octreeFreeNodeStack
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "OctreeFreeNodeCountBuffer",
            octreeFreeNodeCountBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "ActiveLeafCountBuffer",
            activeLeafCountBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "HierarchyChangedCountBuffer",
            hierarchyChangedCountBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "ActiveLeafMinLodBuffer",
            activeLeafMinLodBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "ActiveLeafMaxLodBuffer",
            activeLeafMaxLodBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "VertexBuffer",
            vertexBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "NormalBuffer",
            normalBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "VertexValid",
            vertexValidBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "VertexCountBuffer",
            vertexCountBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "IndexBuffer",
            indexBuffer
        );

        voxelDensityShader.SetBuffer(
            kernel,
            "IndexCountBuffer",
            indexCountBuffer
        );
    }

    private void DispatchInitializeOctree()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxOctreeNodes /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            initializeOctreeKernel,
            groups,
            1,
            1
        );
    }

    private void DispatchLodSelection()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxOctreeNodes /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            selectLodKernel,
            groups,
            1,
            1
        );
    }

    private void DispatchBalance2To1()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxOctreeNodes /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            balanceOctree2To1Kernel,
            groups,
            1,
            1
        );
    }

    private void DispatchLodRequests()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxOctreeNodes /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            applyLodRequestsKernel,
            groups,
            1,
            1
        );
    }

    private void DispatchMergeRequests()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxOctreeNodes /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            applyMergeRequestsKernel,
            groups,
            1,
            1
        );
    }

    private void DispatchBuildNeighborLod()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxOctreeNodes /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            buildNeighborLodKernel,
            groups,
            1,
            1
        );
    }

    private void DispatchBuildNeighborTopology()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxOctreeNodes /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            buildNeighborTopologyKernel,
            groups,
            1,
            1
        );
    }

    private void DispatchBuildTransitionTopology()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxOctreeNodes /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            buildTransitionTopologyKernel,
            groups,
            1,
            1
        );
    }

    private void DispatchResetActiveLeaves()
    {
        voxelDensityShader.Dispatch(
            resetActiveLeavesKernel,
            1,
            1,
            1
        );
    }

    private void DispatchRebuildActiveLeaves()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxOctreeNodes /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            rebuildActiveLeavesKernel,
            groups,
            1,
            1
        );
    }

    private void DispatchBuildLeafMesh()
    {
        DispatchResetLeafMesh();

        DispatchCountLeafMeshVertices();

        DispatchScanLeafMeshVertexOffsets();

        DispatchBuildVertices();

        DispatchCountLeafMeshIndices();

        DispatchScanLeafMeshIndexOffsets();

        DispatchBuildXFaces();

        DispatchBuildYFaces();

        DispatchBuildZFaces();

        DispatchBuildLeafBoundaryFaces();

        DispatchFinalizeLeafMesh();
    }

    private void DispatchResetLeafMesh()
    {
        int resetItemCount =
            Mathf.Max(
                maxActiveLeaves,
                meshVertexHashCapacity
            );

        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    resetItemCount /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            resetLeafMeshKernel,
            groups,
            1,
            1
        );
    }

    private void DispatchLeafMeshPerLeaf(
        int kernel
    )
    {
        int groupsX =
            Mathf.Min(
                maxActiveLeaves,
                LEAF_SLOT_GROUPS_X
            );

        int groupsY =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxActiveLeaves /
                    (float)LEAF_SLOT_GROUPS_X
                )
            );

        voxelDensityShader.Dispatch(
            kernel,
            groupsX,
            groupsY,
            1
        );
    }

    private void DispatchCountLeafMeshVertices()
    {
        DispatchLeafMeshPerLeaf(
            countLeafMeshVerticesKernel
        );
    }

    private void DispatchScanLeafMeshVertexOffsets()
    {
        voxelDensityShader.Dispatch(
            scanLeafMeshVertexOffsetsKernel,
            1,
            1,
            1
        );
    }

    private void DispatchBuildVertices()
    {
        DispatchLeafMeshPerLeaf(
            buildVerticesKernel
        );
    }

    private void DispatchCountLeafMeshIndices()
    {
        DispatchLeafMeshPerLeaf(
            countLeafMeshIndicesKernel
        );
    }

    private void DispatchScanLeafMeshIndexOffsets()
    {
        voxelDensityShader.Dispatch(
            scanLeafMeshIndexOffsetsKernel,
            1,
            1,
            1
        );
    }

    private void DispatchBuildXFaces()
    {
        DispatchLeafMeshPerLeaf(
            buildXFacesKernel
        );
    }

    private void DispatchBuildYFaces()
    {
        DispatchLeafMeshPerLeaf(
            buildYFacesKernel
        );
    }

    private void DispatchBuildZFaces()
    {
        DispatchLeafMeshPerLeaf(
            buildZFacesKernel
        );
    }

    private void DispatchBuildLeafBoundaryFaces()
    {
        DispatchLeafMeshPerLeaf(
            buildLeafBoundaryFacesKernel
        );
    }

    private void DispatchFinalizeLeafMesh()
    {
        voxelDensityShader.Dispatch(
            finalizeLeafMeshKernel,
            1,
            1,
            1
        );
    }

    private void DispatchResetLodStats()
    {
        voxelDensityShader.Dispatch(
            resetLodStatsKernel,
            1,
            1,
            1
        );
    }

    private void DispatchCalculateLodStats()
    {
        int groups =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maxActiveLeaves /
                    (float)THREADS
                )
            );

        voxelDensityShader.Dispatch(
            calculateLodStatsKernel,
            groups,
            1,
            1
        );
    }

    private void ReadDebugState()
    {
        uint activeLeafCount =
            ReadSingleUInt(
                activeLeafCountBuffer
            );

        uint hierarchyChangedCount =
            ReadSingleUInt(
                hierarchyChangedCountBuffer
            );

        uint minLod =
            ReadSingleUInt(
                activeLeafMinLodBuffer
            );

        uint maxLod =
            ReadSingleUInt(
                activeLeafMaxLodBuffer
            );

        uint meshVertexCount =
            ReadSingleUInt(
                vertexCountBuffer
            );

        uint meshIndexCount =
            ReadSingleUInt(
                indexCountBuffer
            );

        uint meshOverflowFlags =
            ReadSingleUInt(
                meshOverflowFlagsBuffer
            );

        ReadDebugActiveLeafFields(
            activeLeafCount
        );

        bool changed =
            !hasLoggedLodState ||
            activeLeafCount != lastActiveLeafCount ||
            hierarchyChangedCount != lastHierarchyChangedCount ||
            minLod != lastMinLod ||
            maxLod != lastMaxLod ||
            meshVertexCount != lastMeshVertexCount ||
            meshIndexCount != lastMeshIndexCount ||
            meshOverflowFlags != lastMeshOverflowFlags;

        if (!changed)
            return;

        hasLoggedLodState = true;

        lastActiveLeafCount =
            activeLeafCount;

        lastHierarchyChangedCount =
            hierarchyChangedCount;

        lastMinLod =
            minLod;

        lastMaxLod =
            maxLod;

        lastMeshVertexCount =
            meshVertexCount;

        lastMeshIndexCount =
            meshIndexCount;

        lastMeshOverflowFlags =
            meshOverflowFlags;

        float targetDistance =
            Vector3.Distance(
                lodTargetPosition,
                Vector3.zero
            );

        LogDebug(
            $"TargetPos={lodTargetPosition} " +
            $"TargetDist={targetDistance:F0} " +
            $"Active={activeLeafCount} " +
            $"LOD=[{minLod}..{maxLod}] " +
            $"Changed={hierarchyChangedCount} " +
            $"MeshV={meshVertexCount} " +
            $"MeshI={meshIndexCount} " +
            $"MeshOverflow=0x{meshOverflowFlags:X8}"
        );
    }

    private void ReadDebugActiveLeafFields(
        uint activeLeafCount
    )
    {
        if (!debugReadback)
            return;

        if (activeLeafBuffer == null)
            return;

        if (activeLeafCount == 0)
            return;

        int count =
            Mathf.Min(
                (int)activeLeafCount,
                maxActiveLeaves
            );

        int uintCount =
            count * 6;

        activeLeafBuffer.GetData(
            activeLeafReadback,
            0,
            0,
            uintCount
        );

        ulong[] sums =
            new ulong[6];

        ulong[] sums2 =
            new ulong[6];

        unchecked
        {
            for (int leafIndex = 0;
                 leafIndex < count;
                 leafIndex++)
            {
                int baseIndex =
                    leafIndex * 6;

                for (int word = 0;
                     word < 6;
                     word++)
                {
                    ulong value =
                        activeLeafReadback[
                            baseIndex + word
                        ];

                    sums[word] +=
                        value;

                    sums2[word] +=
                        value * value;
                }
            }
        }

        LogDebug(
            $"ActiveLeafFields " +
            $"Count={count} " +
            $"W0=0x{sums[0]:X16}/0x{sums2[0]:X16} " +
            $"W1=0x{sums[1]:X16}/0x{sums2[1]:X16} " +
            $"W2=0x{sums[2]:X16}/0x{sums2[2]:X16} " +
            $"W3=0x{sums[3]:X16}/0x{sums2[3]:X16} " +
            $"W4=0x{sums[4]:X16}/0x{sums2[4]:X16} " +
            $"W5=0x{sums[5]:X16}/0x{sums2[5]:X16}"
        );
    }

    private void ReadDebugRoot()
    {
        if (octreeNodeBuffer == null)
            return;

        Array.Clear(
            rootReadback,
            0,
            rootReadback.Length
        );

        octreeNodeBuffer.GetData(
            rootReadback,
            0,
            0,
            12
        );

        int rootLodRead =
            unchecked(
                (int)rootReadback[3]
            );

        Vector3Int canonicalOrigin =
            new Vector3Int(
                unchecked(
                    (int)rootReadback[4]
                ),
                unchecked(
                    (int)rootReadback[5]
                ),
                unchecked(
                    (int)rootReadback[6]
                )
            );

        int parentIndex =
            unchecked(
                (int)rootReadback[7]
            );

        int firstChildIndex =
            unchecked(
                (int)rootReadback[8]
            );

        uint state =
            rootReadback[9];

        uint flags =
            rootReadback[10];

        uint neighborMask =
            rootReadback[11];

        LogDebug(
            $"Root LOD={rootLodRead} " +
            $"Origin={canonicalOrigin} " +
            $"Parent={parentIndex} " +
            $"FirstChild={firstChildIndex} " +
            $"State={state} " +
            $"Flags={flags} " +
            $"NeighborMask={neighborMask}"
        );
    }

    private uint ReadSingleUInt(
        GraphicsBuffer buffer
    )
    {
        if (buffer == null)
            return 0u;

        oneUint[0] = 0u;

        buffer.GetData(
            oneUint,
            0,
            0,
            1
        );

        return oneUint[0];
    }

    private void CalculateRoot(
        Vector3 densityPlanetCenter,
        float densityPlanetRadius,
        float densityNoiseHeight
    )
    {
        float chunkWorldSize =
            DEFAULT_CHUNK_RESOLUTION *
            BASE_LATTICE_SIZE;

        float rootWorldSize =
            chunkWorldSize *
            Mathf.Pow(
                2.0f,
                rootLod
            );

        float requiredDiameter =
            densityPlanetRadius * 2.0f +
            Mathf.Abs(densityNoiseHeight) * 2.0f;

        if (rootWorldSize < requiredDiameter)
        {
            LogWarning(
                $"RootWorldSize={rootWorldSize:F0} " +
                $"Required={requiredDiameter:F0}"
            );
        }

        Vector3 rootMinWorld =
            CalculateRootMinWorld(
                densityPlanetCenter,
                rootWorldSize
            );

        rootCanonicalOrigin =
            new Vector3Int(
                Mathf.FloorToInt(
                    rootMinWorld.x /
                    BASE_LATTICE_SIZE
                ),
                Mathf.FloorToInt(
                    rootMinWorld.y /
                    BASE_LATTICE_SIZE
                ),
                Mathf.FloorToInt(
                    rootMinWorld.z /
                    BASE_LATTICE_SIZE
                )
            );
    }

    private Vector3 CalculateRootMinWorld(
        Vector3 center,
        float rootWorldSize
    )
    {
        float x =
            Mathf.Floor(
                center.x /
                rootWorldSize +
                0.5f
            ) *
            rootWorldSize -
            rootWorldSize * 0.5f;

        float y =
            Mathf.Floor(
                center.y /
                rootWorldSize +
                0.5f
            ) *
            rootWorldSize -
            rootWorldSize * 0.5f;

        float z =
            Mathf.Floor(
                center.z /
                rootWorldSize +
                0.5f
            ) *
            rootWorldSize -
            rootWorldSize * 0.5f;

        return new Vector3(
            x,
            y,
            z
        );
    }

    private float GetRootWorldSize()
    {
        float chunkWorldSize =
            DEFAULT_CHUNK_RESOLUTION *
            BASE_LATTICE_SIZE;

        return chunkWorldSize *
               Mathf.Pow(
                   2.0f,
                   rootLod
               );
    }

    private void ReleaseBuffers()
    {
        ReleaseBuffer(
            ref octreeNodeBuffer
        );

        ReleaseBuffer(
            ref activeLeafBuffer
        );

        ReleaseBuffer(
            ref activeLeafNeighborTopologyBuffer
        );

        ReleaseBuffer(
            ref activeLeafTransitionTopologyBuffer
        );

        ReleaseBuffer(
            ref activeLeafMeshSlotBuffer
        );

        ReleaseBuffer(
            ref activeLeafMeshInfoBuffer
        );

        ReleaseBuffer(
            ref activeLeafMeshVertexCounterBuffer
        );

        ReleaseBuffer(
            ref activeLeafMeshIndexCounterBuffer
        );

        ReleaseBuffer(
            ref activeLeafMeshVertexHashKeyBuffer
        );

        ReleaseBuffer(
            ref activeLeafMeshVertexHashValueBuffer
        );

        ReleaseBuffer(
            ref meshOverflowFlagsBuffer
        );

        ReleaseBuffer(
            ref octreeFreeNodeStack
        );

        ReleaseBuffer(
            ref octreeFreeNodeCountBuffer
        );

        ReleaseBuffer(
            ref activeLeafCountBuffer
        );

        ReleaseBuffer(
            ref hierarchyChangedCountBuffer
        );

        ReleaseBuffer(
            ref activeLeafMinLodBuffer
        );

        ReleaseBuffer(
            ref activeLeafMaxLodBuffer
        );

        ReleaseBuffer(
            ref vertexBuffer
        );

        ReleaseBuffer(
            ref normalBuffer
        );

        ReleaseBuffer(
            ref vertexValidBuffer
        );

        ReleaseBuffer(
            ref vertexCountBuffer
        );

        ReleaseBuffer(
            ref indexBuffer
        );

        ReleaseBuffer(
            ref indexCountBuffer
        );
    }

    private void ReleaseBuffer(
        ref GraphicsBuffer buffer
    )
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

    private void LogDebug(
        string message
    )
    {
        VoxelDebugLogger.Log(
            $"[VoxelGpuHierarchy] {message}"
        );
    }

    private void LogWarning(
        string message
    )
    {
        VoxelDebugLogger.Warning(
            $"[VoxelGpuHierarchy] {message}"
        );
    }

    private void LogError(
        string message
    )
    {
        VoxelDebugLogger.Error(
            $"[VoxelGpuHierarchy] {message}"
        );
    }
}