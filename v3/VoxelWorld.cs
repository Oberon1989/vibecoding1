using System.Collections.Generic;
using UnityEngine;

public class VoxelWorld : MonoBehaviour
{
    [Header("World")]
    [SerializeField] private Vector3 planetCenter = Vector3.zero;
    [SerializeField] private float planetRadius = 50f;

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

    private readonly Dictionary<Vector3Int, VoxelChunk> chunks = new();

    private void Awake()
    {
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
    }

    private void Start()
    {
        BuildInitialChunks();
    }

    //private void BuildInitialChunks()
    //{
    //    int half = chunksPerAxis / 2;

    //    for (int z = -half; z <= half; z++)
    //    {
    //        for (int y = -half; y <= half; y++)
    //        {
    //            for (int x = -half; x <= half; x++)
    //            {
    //                Vector3Int coordinate =
    //                    new Vector3Int(x, y, z);

    //                CreateChunk(coordinate);
    //            }
    //        }
    //    }
    //}

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
                        new Vector3Int(x, y, z);

                    CreateChunk(coordinate);
                }
            }
        }
    }
    private VoxelChunk CreateChunk(Vector3Int coordinate)
    {
        if (chunks.TryGetValue(
            coordinate,
            out VoxelChunk existing))
        {
            return existing;
        }

        VoxelChunk chunk =
            new VoxelChunk(
                coordinate,
                0,
                chunkSize,
                transform,
                material
            );

        chunks.Add(coordinate, chunk);

        RebuildChunk(chunk);

        return chunk;
    }

    private void RebuildChunk(VoxelChunk chunk)
    {
        Mesh mesh =
            mesher.Build(
                chunk,
                generator,
                modificationStorage,
                resolution
            );

        chunk.SetMesh(mesh);
    }

    public void AddModification(
        VoxelModification modification)
    {
        modificationStorage.Add(modification);

        RebuildAffectedChunks(modification);
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
            RebuildChunk(chunk);
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
        foreach (VoxelChunk chunk in chunks.Values)
        {
            chunk.Destroy();
        }

        chunks.Clear();
    }
}