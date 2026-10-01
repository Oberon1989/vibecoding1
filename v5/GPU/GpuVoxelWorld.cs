using System.Collections.Generic;
using UnityEngine;

public partial class GpuVoxelWorld : MonoBehaviour
{
    [Header("GPU")]

    [SerializeField]
    private ComputeShader computeShader;


    [SerializeField]
    private Material gpuMaterial;


    [Header("World")]

    [SerializeField]
    private Vector3 planetCenter =
        Vector3.zero;


    [SerializeField]
    private float planetRadius =
        50f;


    [SerializeField]
    private Transform player;


    [Header("Player Spawn")]

    [SerializeField]
    private bool spawnPlayerAtStart =
        true;


    [SerializeField]
    private GameObject playerPrefab;


    [SerializeField]
    private float playerSpawnHeight =
        100f;


    [Header("LOD")]

    [SerializeField]
    private float lod0NodeSize =
        20f;


    [SerializeField]
    private float lodUpdateDistance =
        5f;


    [Header("GPU Buffers")]

    [SerializeField]
    [Min(1)]
    private int maxLod0To3Chunks =
        256;


    [SerializeField]
    [Min(1)]
    private int maxLod4To9Chunks =
        128;


    [SerializeField]
    [Min(1)]
    private int maxLod10PlusChunks =
        32;


    [Header("GPU Resolution")]

    [SerializeField]
    [Min(1)]
    private int gpuResolution =
        40;


    [Header("GPU Build")]

    [SerializeField]
    [Min(1)]
    private int maxGpuChunkBuildsPerFrame =
        1;


    [Header("Planet Noise")]

    [SerializeField]
    private int noiseSeed =
        1234567;


    [SerializeField]
    private float noiseFrequency =
        3f;


    [SerializeField]
    private float noiseHeight =
        3f;


    // ============================================================
    // WORLD STATE
    // ============================================================

    private VoxelLodLevel[] lodLevels;

    private OctreeNode octreeRoot;


    // ============================================================
    // CHUNKS
    // ============================================================

    private readonly List<OctreeNode> leafNodes =
        new();


    private readonly List<GpuChunk> chunks =
        new();


    private readonly List<GpuChunk> pendingChunks =
        new();


    private readonly List<GpuChunk> pendingRemovedChunks =
        new();


    private readonly Dictionary<OctreeNode, GpuChunk>
        activeChunkMap =
            new();


    private readonly Dictionary<GpuChunk, uint>
        pendingRenderIndexCounts =
            new();


    // ============================================================
    // GPU OBJECTS
    // ============================================================

    private GpuChunkBuilder builder;

    private GpuChunkBuildBuffers buildBuffers;

    private GpuLodGroups lodGroups;

    private GpuLodGroupReadback readback;

    private GpuLodGroupRenderer renderer;

    private GpuSeamBuilder seamBuilder;

    private GpuSeamBuildBuffers seamBuildBuffers;


    // ============================================================
    // INDEX COUNTS
    // ============================================================

    private uint[] lod0To3IndexCounts;

    private uint[] lod4To9IndexCounts;

    private uint[] lod10PlusIndexCounts;


    private uint[] pendingLod0To3IndexCounts;

    private uint[] pendingLod4To9IndexCounts;

    private uint[] pendingLod10PlusIndexCounts;


    // ============================================================
    // LOD STATE
    // ============================================================

    private Vector3 lastLodPlayerPosition;

    private string lastLodSummary;


    // ============================================================
    // BUILD STATE
    // ============================================================

    private int buildVersion;

    private bool buildInProgress;

    private bool waitingForChunkReadback;

    private bool pendingBuildFailed;

    private int pendingBuildIndex;

    private bool shuttingDown;


    // ============================================================
    // PLAYER SPAWN STATE
    // ============================================================

    private GameObject playerLodProxy;

    private GameObject spawnedPlayer;

    private bool playerSpawned;


    // ============================================================
    // AWAKE
    // ============================================================

    private void Awake()
    {
        if (computeShader == null)
        {
            Debug.LogError(
                "GpuVoxelWorld: ComputeShader is not assigned."
            );

            return;
        }


        if (gpuMaterial == null)
        {
            Debug.LogError(
                "GpuVoxelWorld: Material is not assigned."
            );

            return;
        }


        // --------------------------------------------------------
        // PLAYER REFERENCE
        //
        // Before the real player exists we create a lightweight
        // proxy only for LOD calculations.
        // --------------------------------------------------------

        if (spawnPlayerAtStart)
        {
            if (playerPrefab == null)
            {
                Debug.LogError(
                    "GpuVoxelWorld: Player Prefab is not assigned."
                );

                return;
            }


            if (!CreatePlayerLodProxy())
            {
                return;
            }
        }
        else
        {
            if (player == null)
            {
                Debug.LogError(
                    "GpuVoxelWorld: Player is not assigned."
                );

                return;
            }
        }


        InitializeLodLevels();


        float planetDiameter =
            planetRadius * 2f;


        int maxLod =
            0;


        while (
            lod0NodeSize *
            Mathf.Pow(
                2f,
                maxLod
            )
            < planetDiameter)
        {
            maxLod++;
        }


        if (maxLod >= lodLevels.Length)
        {
            maxLod =
                lodLevels.Length - 1;
        }


        float worldSize =
            lod0NodeSize *
            Mathf.Pow(
                2f,
                maxLod
            );


        float rootVoxelSize =
            lodLevels[
                maxLod
            ].VoxelSize;


        octreeRoot =
            new OctreeNode(
                planetCenter,
                worldSize,
                maxLod,
                rootVoxelSize
            );


        UpdateOctreeNode(
            octreeRoot
        );


        RebuildPendingChunkList();


        // ========================================================
        // BUILD BUFFERS
        // ========================================================

        buildBuffers =
            new GpuChunkBuildBuffers();


        buildBuffers.Create(
            gpuResolution
        );


        // ========================================================
        // SEAM BUFFERS
        // ========================================================

        int seamBoundaryNodeCount =
            3 * gpuResolution * gpuResolution -
            3 * gpuResolution +
            1;


        int seamNodeCapacity =
            Mathf.Max(
                1,
                seamBoundaryNodeCount * 8
            );


        int seamEdgeCapacity =
            Mathf.Max(
                1,
                seamNodeCapacity * 3
            );


        seamBuildBuffers =
            new GpuSeamBuildBuffers();


        seamBuildBuffers.Create(
            seamNodeCapacity,
            seamEdgeCapacity
        );


        // ========================================================
        // LOD GROUPS
        // ========================================================

        lodGroups =
            new GpuLodGroups(
                gpuResolution,
                maxLod0To3Chunks,
                maxLod4To9Chunks,
                maxLod10PlusChunks
            );


        lod0To3IndexCounts =
            new uint[
                maxLod0To3Chunks
            ];


        lod4To9IndexCounts =
            new uint[
                maxLod4To9Chunks
            ];


        lod10PlusIndexCounts =
            new uint[
                maxLod10PlusChunks
            ];


        // ========================================================
        // GPU SERVICES
        // ========================================================

        builder =
            new GpuChunkBuilder(
                computeShader
            );


        seamBuilder =
            new GpuSeamBuilder();


        readback =
            new GpuLodGroupReadback();


        renderer =
            new GpuLodGroupRenderer(
                gpuMaterial,
                computeShader
            );


        lastLodPlayerPosition =
            player.position;
    }


    // ============================================================
    // START
    // ============================================================

    private void Start()
    {
        if (octreeRoot == null ||
            lodGroups == null ||
            buildBuffers == null)
        {
            return;
        }


        StartBuild();


        LogLodSummary();
    }


    // ============================================================
    // UPDATE
    // ============================================================

    private void Update()
    {
        if (shuttingDown)
        {
            return;
        }


        if (player == null ||
            octreeRoot == null ||
            lodGroups == null)
        {
            return;
        }


        RenderAllChunks();


        if (buildInProgress)
        {
            ProcessPendingBuilds();

            return;
        }


        TryUpdateLod();
    }


    // ============================================================
    // PLAYER LOD PROXY
    // ============================================================

    private bool CreatePlayerLodProxy()
    {
        Vector3 spawnPosition =
            GetPlayerSpawnPosition();


        playerLodProxy =
            new GameObject(
                "Player_LodProxy"
            );


        playerLodProxy.transform.position =
            spawnPosition;


        player =
            playerLodProxy.transform;


        return true;
    }


    private Vector3 GetPlayerSpawnPosition()
    {
        return
            planetCenter +
            Vector3.up *
            (
                planetRadius +
                playerSpawnHeight
            );
    }


    // ============================================================
    // PLAYER SPAWN
    // ============================================================
    //
    // Called after the first surface chunk has been successfully
    // built and uploaded to the GPU.
    //

    private void TrySpawnPlayerAfterSurface()
    {
        if (!spawnPlayerAtStart)
        {
            return;
        }


        if (playerSpawned)
        {
            return;
        }


        if (playerPrefab == null)
        {
            return;
        }


        Vector3 spawnPosition =
            GetPlayerSpawnPosition();


        spawnedPlayer =
            Instantiate(
                playerPrefab,
                spawnPosition,
                Quaternion.identity
            );


        spawnedPlayer.name =
            "Player_Runtime";


        // ========================================================
        // PLANET REFERENCE
        // ========================================================

        SimplePlayerMovement movement =
            spawnedPlayer.GetComponent<
                SimplePlayerMovement
            >();


        if (movement == null)
        {
            Debug.LogWarning(
                "GpuVoxelWorld: Spawned Player has no " +
                "SimplePlayerMovement component."
            );
        }
        else
        {
            movement.planetCenter =
                transform;
        }


        // ========================================================
        // PLAYER REFERENCE
        // ========================================================

        player =
            spawnedPlayer.transform;

        player.GetComponent<SimplePlayerMovement>().useFly = true;


        playerSpawned =
            true;


        lastLodPlayerPosition =
            player.position;


        // ========================================================
        // REMOVE LOD PROXY
        // ========================================================

        if (playerLodProxy != null)
        {
            Destroy(
                playerLodProxy
            );


            playerLodProxy =
                null;
        }
    }


    // ============================================================
    // DESTROY
    // ============================================================

    private void OnDestroy()
    {
        shuttingDown =
            true;


        buildVersion++;


        buildInProgress =
            false;


        waitingForChunkReadback =
            false;


        pendingRenderIndexCounts.Clear();


        if (playerLodProxy != null)
        {
            Destroy(
                playerLodProxy
            );


            playerLodProxy =
                null;
        }


        if (lodGroups != null)
        {
            lodGroups.Release();

            lodGroups =
                null;
        }


        if (buildBuffers != null)
        {
            buildBuffers.Release();

            buildBuffers =
                null;
        }


        if (seamBuildBuffers != null)
        {
            seamBuildBuffers.Release();

            seamBuildBuffers =
                null;
        }


        builder =
            null;


        seamBuilder =
            null;


        readback =
            null;


        renderer =
            null;


        if (octreeRoot != null)
        {
            octreeRoot.Merge();

            octreeRoot =
                null;
        }


        leafNodes.Clear();

        chunks.Clear();

        pendingChunks.Clear();

        pendingRemovedChunks.Clear();

        activeChunkMap.Clear();
    }


    // ============================================================
    // PUBLIC ACCESS
    // ============================================================

    public IReadOnlyList<GpuChunk> GetChunks()
    {
        return chunks;
    }


    public IReadOnlyList<OctreeNode> GetLeafNodes()
    {
        return leafNodes;
    }
}