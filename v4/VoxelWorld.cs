using System.Collections.Generic;
using UnityEngine;

public class VoxelWorld : MonoBehaviour
{
    [Header("World")]
    [SerializeField] private Vector3 planetCenter = Vector3.zero;
    [SerializeField] private float planetRadius = 50f;
    [SerializeField] private Transform player;

    [Header("Chunks")]
    [SerializeField] private float chunkSize = 20f;
    [SerializeField] private int resolution = 16;

    [Header("Noise")]
    [SerializeField] private int noiseSeed = 1234567;
    [SerializeField] private float noiseFrequency = 3f;
    [SerializeField] private float noiseHeight = 3f;

    [Header("Rendering")]
    [SerializeField] private Material material;

    private VoxelGenerator generator;
    private VoxelModificationStorage modificationStorage;
    private DualContouringMesher mesher;

    private OctreeNode octreeRoot;

    [SerializeField] private VoxelLodLevel[] lodLevels;
    [SerializeField] private float lod0NodeSize = 20f;
    [SerializeField] private float lodUpdateDistance = 5f;

    private Vector3 lastLodPlayerPosition;
    private string lastLodSummary;

    private readonly Dictionary<Vector3Int, VoxelChunk> chunks = new();
    private readonly List<OctreeNode> leafNodes = new();
    private readonly List<VoxelChunk> octreeChunks = new();
    private readonly Dictionary<OctreeNode, VoxelChunk> octreeChunkMap = new();

    private readonly Queue<OctreeNode> chunkBuildQueue = new();
    private readonly HashSet<OctreeNode> queuedChunkNodes = new();
    [SerializeField] private int maxChunkBuildsPerFrame = 1;
    private void Awake()
    {
        InitializeLodLevels();

        generator = new VoxelGenerator(
            planetCenter,
            planetRadius,
            noiseSeed,
            noiseFrequency,
            noiseHeight
        );

        modificationStorage =
            new VoxelModificationStorage();

        mesher =
            new DualContouringMesher();

        float planetDiameter =
            generator.SurfaceRadius * 2f;

        int maxLod = 0;

        while (
            lod0NodeSize *
            Mathf.Pow(2f, maxLod)
            < planetDiameter)
        {
            maxLod++;
        }

        float worldSize =
            lod0NodeSize *
            Mathf.Pow(2f, maxLod);

        float rootVoxelSize =
            lodLevels[maxLod].VoxelSize;

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

        lastLodPlayerPosition =
            player.position;
    }

    private void Start()
    {
        //BuildInitialChunks();

        leafNodes.Clear();

        GetLeafNodes(
            octreeRoot,
            leafNodes
        );

        Debug.Log(
            $"Initial Octree Leaves: {leafNodes.Count}"
        );

        for (int i = 0; i < leafNodes.Count; i++)
        {
            OctreeNode node =
                leafNodes[i];

            VoxelChunk chunk =
                CreateChunk(
                    node
                );

            octreeChunks.Add(
                chunk
            );

            octreeChunkMap.Add(
                node,
                chunk
            );
        }
    }

    //private void SyncOctreeChunks()
    //{
    //    leafNodes.Clear();

    //    GetLeafNodes(
    //        octreeRoot,
    //        leafNodes
    //    );

    //    List<OctreeNode> nodesToRemove =
    //        new();

    //    foreach (KeyValuePair<OctreeNode, VoxelChunk> pair
    //        in octreeChunkMap)
    //    {
    //        if (!leafNodes.Contains(pair.Key))
    //        {
    //            nodesToRemove.Add(
    //                pair.Key
    //            );
    //        }
    //    }

    //    for (int i = 0; i < nodesToRemove.Count; i++)
    //    {
    //        OctreeNode node =
    //            nodesToRemove[i];

    //        VoxelChunk chunk =
    //            octreeChunkMap[node];

    //        chunk.Destroy();

    //        octreeChunkMap.Remove(
    //            node
    //        );

    //        octreeChunks.Remove(
    //            chunk
    //        );
    //    }

    //    for (int i = 0; i < leafNodes.Count; i++)
    //    {
    //        OctreeNode node =
    //            leafNodes[i];

    //        if (octreeChunkMap.ContainsKey(node))
    //            continue;

    //        VoxelChunk chunk =
    //            CreateChunk(
    //                node
    //            );

    //        octreeChunkMap.Add(
    //            node,
    //            chunk
    //        );

    //        octreeChunks.Add(
    //            chunk
    //        );
    //    }
    //}

    private void SyncOctreeChunks()
    {
        leafNodes.Clear();

        GetLeafNodes(
            octreeRoot,
            leafNodes
        );

        List<OctreeNode> nodesToRemove =
            new();

        foreach (
            KeyValuePair<OctreeNode, VoxelChunk> pair
            in octreeChunkMap)
        {
            if (!leafNodes.Contains(pair.Key))
            {
                nodesToRemove.Add(
                    pair.Key
                );
            }
        }

        for (int i = 0; i < nodesToRemove.Count; i++)
        {
            OctreeNode node =
                nodesToRemove[i];

            VoxelChunk chunk =
                octreeChunkMap[node];

            chunk.Destroy();

            octreeChunkMap.Remove(
                node
            );

            octreeChunks.Remove(
                chunk
            );
        }

        for (int i = 0; i < leafNodes.Count; i++)
        {
            OctreeNode node =
                leafNodes[i];

            if (octreeChunkMap.ContainsKey(node))
                continue;

            if (queuedChunkNodes.Contains(node))
                continue;

            chunkBuildQueue.Enqueue(
                node
            );

            queuedChunkNodes.Add(
                node
            );
        }
    }

    private void ProcessChunkBuildQueue()
    {
        int builds =
            0;

        while (
            builds <
            maxChunkBuildsPerFrame &&
            chunkBuildQueue.Count > 0)
        {
            OctreeNode node =
                chunkBuildQueue.Dequeue();

            queuedChunkNodes.Remove(
                node
            );

            if (!node.IsLeaf)
                continue;

            if (octreeChunkMap.ContainsKey(node))
                continue;

            VoxelChunk chunk =
                CreateChunk(
                    node
                );

            octreeChunkMap.Add(
                node,
                chunk
            );

            octreeChunks.Add(
                chunk
            );

            builds++;
        }
    }

    private OctreeNode GetClosestLeafNode()
    {
        leafNodes.Clear();

        GetLeafNodes(
            octreeRoot,
            leafNodes
        );

        OctreeNode closest =
            null;

        float closestDistance =
            float.MaxValue;

        for (int i = 0; i < leafNodes.Count; i++)
        {
            OctreeNode node =
                leafNodes[i];

            float distance =
                node.Bounds.SqrDistance(
                    player.position
                );

            if (distance < closestDistance)
            {
                closestDistance =
                    distance;

                closest =
                    node;
            }
        }

        return closest;
    }

    private void Update()
    {
        if (player == null)
            return;

        float distanceSqr =
            (
                player.position -
                lastLodPlayerPosition
            ).sqrMagnitude;

        float updateDistanceSqr =
            lodUpdateDistance *
            lodUpdateDistance;

        if (distanceSqr < updateDistanceSqr)
            return;

        lastLodPlayerPosition =
            player.position;

        UpdateOctreeNode(
            octreeRoot
        );

        SyncOctreeChunks();
        ProcessChunkBuildQueue();
        leafNodes.Clear();

        GetLeafNodes(
            octreeRoot,
            leafNodes
        );

        string summary = "";

        for (int i = 0; i < lodLevels.Length; i++)
        {
            int count = 0;

            for (int j = 0; j < leafNodes.Count; j++)
            {
                if (leafNodes[j].Lod ==
                    lodLevels[i].Lod)
                {
                    count++;
                }
            }

            if (count > 0)
            {
                summary +=
                    $"LOD {lodLevels[i].Lod}: {count}  ";
            }
        }

        if (summary != lastLodSummary)
        {
            lastLodSummary =
                summary;

            Debug.Log(
                $"Octree: {summary}"
            );
        }
    }

    private void BuildInitialChunks()
    {
        float worldRadius =
            generator.SurfaceRadius;

        int minCoordinate =
            Mathf.FloorToInt(
                -worldRadius / chunkSize
            );

        int maxCoordinate =
            Mathf.CeilToInt(
                worldRadius / chunkSize
            ) - 1;

        for (int z = minCoordinate; z <= maxCoordinate; z++)
        {
            for (int y = minCoordinate; y <= maxCoordinate; y++)
            {
                for (int x = minCoordinate; x <= maxCoordinate; x++)
                {
                    Vector3Int coordinate =
                        new Vector3Int(
                            x,
                            y,
                            z
                        );

                    CreateChunk(
                        coordinate
                    );
                }
            }
        }
    }

    private VoxelChunk CreateChunk(
    Vector3Int coordinate)
    {
        if (chunks.TryGetValue(
            coordinate,
            out VoxelChunk existing))
        {
            return existing;
        }

        Vector3 center =
            new Vector3(
                coordinate.x * chunkSize,
                coordinate.y * chunkSize,
                coordinate.z * chunkSize
            ) +
            Vector3.one * (chunkSize * 0.5f);

        VoxelChunk chunk =
            new VoxelChunk(
                center,
                chunkSize,
                0,
                chunkSize / resolution,
                transform,
                material
            );

        chunks.Add(
            coordinate,
            chunk
        );

        RebuildChunk(
            chunk
        );

        return chunk;
    }

    private VoxelChunk CreateChunk(
    OctreeNode node)
    {
        VoxelChunk chunk =
            new VoxelChunk(
                node.Center,
                node.Size,
                node.Lod,
                node.VoxelSize,
                transform,
                material
            );

        RebuildChunk(
            chunk
        );

        return chunk;
    }

    private void RebuildChunk(
    VoxelChunk chunk)
    {
        int chunkResolution =
            Mathf.RoundToInt(
                chunk.Size /
                chunk.VoxelSize
            );

        float buildStart =
            Time.realtimeSinceStartup;

        Mesh mesh =
            mesher.Build(
                chunk,
                generator,
                modificationStorage,
                chunkResolution
            );

        float buildTime =
            (
                Time.realtimeSinceStartup -
                buildStart
            ) * 1000f;

        float meshStart =
            Time.realtimeSinceStartup;

        chunk.SetMesh(
            mesh
        );

        float meshTime =
            (
                Time.realtimeSinceStartup -
                meshStart
            ) * 1000f;

        Debug.Log(
            $"Chunk Build: " +
            $"LOD={chunk.Lod}, " +
            $"Size={chunk.Size:F2}, " +
            $"VoxelSize={chunk.VoxelSize:F2}, " +
            $"Resolution={chunkResolution}, " +
            $"Vertices={mesh.vertexCount}, " +
            $"Build={buildTime:F2} ms, " +
            $"SetMesh={meshTime:F2} ms"
        );
    }

    public void AddModification(
        VoxelModification modification)
    {
        modificationStorage.Add(
            modification
        );

        RebuildAffectedChunks(
            modification
        );
    }

    private void RebuildAffectedChunks(
        VoxelModification modification)
    {
        Bounds modificationBounds =
            new Bounds(
                modification.Position,
                Vector3.one *
                modification.Radius *
                2f
            );

        foreach (VoxelChunk chunk in chunks.Values)
        {
            if (!chunk.WorldBounds.Intersects(
                modificationBounds))
            {
                continue;
            }

            chunk.MarkDirty();

            RebuildChunk(
                chunk
            );
        }
    }

    public VoxelModificationStorage
        GetModificationStorage()
    {
        return modificationStorage;
    }

    public VoxelGenerator GetGenerator()
    {
        return generator;
    }

    public IReadOnlyDictionary<Vector3Int, VoxelChunk>
        GetChunks()
    {
        return chunks;
    }

    private void OnDestroy()
    {
        //foreach (VoxelChunk chunk in chunks.Values)
        //{
        //    chunk.Destroy();
        //}

        //chunks.Clear();

        foreach (VoxelChunk chunk in chunks.Values)
        {
            chunk.Destroy();
        }

        chunks.Clear();

        for (int i = 0; i < octreeChunks.Count; i++)
        {
            octreeChunks[i].Destroy();
        }

        octreeChunks.Clear();
    }

    private void InitializeLodLevels()
    {
        int levelCount = 19;

        lodLevels =
            new VoxelLodLevel[levelCount];

        float voxelSize = 0.5f;

        for (int i = 0; i < levelCount; i++)
        {
            lodLevels[i] =
                new VoxelLodLevel
                {
                    Lod = i,
                    VoxelSize = voxelSize
                };

            voxelSize *= 2f;
        }
    }

    private int GetLodForDistance(
        float distance)
    {
        for (int i = 0; i < lodLevels.Length; i++)
        {
            float maxDistance =
                lodLevels[i].VoxelSize * 40f;

            if (distance < maxDistance)
            {
                return lodLevels[i].Lod;
            }
        }

        return lodLevels[
            lodLevels.Length - 1
        ].Lod;
    }

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

        if (requiredLod < node.Lod)
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
            for (int i = 0; i < node.Children.Length; i++)
            {
                UpdateOctreeNode(
                    node.Children[i]
                );
            }
        }
    }

    private int CountOctreeNodes(
        OctreeNode node)
    {
        int count = 1;

        if (!node.IsLeaf)
        {
            for (int i = 0; i < node.Children.Length; i++)
            {
                count += CountOctreeNodes(
                    node.Children[i]
                );
            }
        }

        return count;
    }

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

        for (int i = 0; i < node.Children.Length; i++)
        {
            GetLeafNodes(
                node.Children[i],
                result
            );
        }
    }
}