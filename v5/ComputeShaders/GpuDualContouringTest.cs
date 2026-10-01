using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class GpuDualContouringTest : MonoBehaviour
{
    [Header("GPU")]
    [SerializeField]
    private ComputeShader voxelDensityShader;

    [SerializeField]
    private Material gpuMaterial;


    [Header("Grid")]
    [SerializeField]
    [Range(1, 128)]
    private int resolution = 40;


    [Header("Sphere")]
    [SerializeField]
    private Vector3 planetCenter =
        Vector3.zero;

    [SerializeField]
    private float planetRadius = 10f;


    [Header("Noise")]
    [SerializeField]
    private int noiseSeed = 12345;

    [SerializeField]
    private float noiseFrequency = 4f;

    [SerializeField]
    private float noiseHeight = 0f;


    [Header("Build")]
    [SerializeField]
    private bool buildOnStart = true;


    [Header("Debug")]
    [SerializeField]
    private bool logResults = true;


    private const float MinimumGridPadding = 10f;

    private float step;

    private Vector3 origin;


    private int vertexKernel;

    private int xFaceKernel;

    private int yFaceKernel;

    private int zFaceKernel;


    private int cellCount;

    private int cellCountTotal;

    private int maxIndexCount;


    private GraphicsBuffer vertexBuffer;

    private GraphicsBuffer normalBuffer;

    private GraphicsBuffer vertexValidBuffer;

    private GraphicsBuffer vertexCountBuffer;

    private GraphicsBuffer indexComputeBuffer;

    private GraphicsBuffer indexRenderBuffer;

    private GraphicsBuffer indexCountBuffer;


    private MaterialPropertyBlock propertyBlock;

    private Bounds renderBounds;

    private int renderIndexCount;

    private bool readbackPending;

    private bool initialized;


    private float lastBuiltRadius;

    private Vector3 lastBuiltPlanetCenter;

    private int lastBuiltResolution;

    private int lastBuiltNoiseSeed;

    private float lastBuiltNoiseFrequency;

    private float lastBuiltNoiseHeight;


    private Mesh collisionMesh;

    private MeshCollider collisionMeshCollider;


    private void Start()
    {
        if (!Initialize())
        {
            return;
        }

        if (buildOnStart)
        {
            Build();
        }
    }


    private void Update()
    {
        if (!initialized)
        {
            return;
        }


        bool configurationChanged =
            Mathf.Abs(
                planetRadius -
                lastBuiltRadius
            ) > 0.0001f
            ||
            Vector3.Distance(
                planetCenter,
                lastBuiltPlanetCenter
            ) > 0.0001f
            ||
            resolution !=
            lastBuiltResolution
            ||
            noiseSeed !=
            lastBuiltNoiseSeed
            ||
            Mathf.Abs(
                noiseFrequency -
                lastBuiltNoiseFrequency
            ) > 0.000001f
            ||
            Mathf.Abs(
                noiseHeight -
                lastBuiltNoiseHeight
            ) > 0.000001f;


        if (configurationChanged &&
            !readbackPending)
        {
            Build();
        }


        RenderGpuMesh();
    }


    private void OnDestroy()
    {
        if (collisionMeshCollider != null)
        {
            collisionMeshCollider.sharedMesh = null;
        }

        if (collisionMesh != null)
        {
            Destroy(collisionMesh);

            collisionMesh =
                null;
        }

        ReleaseBuffers();

        propertyBlock =
            null;

        collisionMeshCollider =
            null;

        initialized =
            false;
    }


    private bool Initialize()
    {
        if (voxelDensityShader == null)
        {
            Debug.LogError(
                "GpuDualContouringTest: ComputeShader is not assigned."
            );

            return false;
        }


        Debug.Log(
            "GpuDualContouringTest ComputeShader = " +
            voxelDensityShader.name
        );


        if (gpuMaterial == null)
        {
            Debug.LogError(
                "GpuDualContouringTest: GPU material is not assigned."
            );

            return false;
        }


        resolution =
            Mathf.Clamp(
                resolution,
                1,
                128
            );


        if (planetRadius <= 0.0f)
        {
            Debug.LogError(
                "GpuDualContouringTest: PlanetRadius must be > 0."
            );

            return false;
        }


        UpdateAutomaticGrid();


        try
        {
            vertexKernel =
                voxelDensityShader.FindKernel(
                    "CSBuildVertices"
                );


            xFaceKernel =
                voxelDensityShader.FindKernel(
                    "CSBuildXFaces"
                );


            yFaceKernel =
                voxelDensityShader.FindKernel(
                    "CSBuildYFaces"
                );


            zFaceKernel =
                voxelDensityShader.FindKernel(
                    "CSBuildZFaces"
                );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "GpuDualContouringTest: Failed to find compute kernels.\n" +
                exception
            );

            return false;
        }


        if (!RecalculateGridCounts())
        {
            return false;
        }


        propertyBlock =
            new MaterialPropertyBlock();


        CreateBuffers();

        BindBuffers();

        UpdateShaderParameters();

        UpdateRenderBounds();


        initialized =
            true;


        return true;
    }


    private void UpdateAutomaticGrid()
    {
        if (planetRadius <= 0.0f)
        {
            planetRadius = 0.001f;
        }


        float diameter =
            planetRadius *
            2.0f;


        float noisePadding =
            Mathf.Abs(
                noiseHeight
            ) +
            MinimumGridPadding;


        float gridSize =
            diameter +
            noisePadding *
            2.0f;


        step =
            gridSize /
            resolution;


        origin =
            planetCenter -
            Vector3.one *
            (gridSize * 0.5f);
    }


    private bool RecalculateGridCounts()
    {
        cellCount =
            resolution +
            1;


        long totalCellsLong =
            (long)cellCount *
            cellCount *
            cellCount;


        if (totalCellsLong >
            int.MaxValue)
        {
            Debug.LogError(
                "GpuDualContouringTest: Too many cells."
            );

            return false;
        }


        cellCountTotal =
            (int)totalCellsLong;


        long maxIndexCountLong =
            18L *
            resolution *
            resolution *
            resolution;


        if (maxIndexCountLong >
            int.MaxValue)
        {
            Debug.LogError(
                "GpuDualContouringTest: Index buffer is too large."
            );

            return false;
        }


        maxIndexCount =
            (int)maxIndexCountLong;


        return true;
    }


    private void CreateBuffers()
    {
        ReleaseBuffers();


        vertexBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                cellCountTotal,
                sizeof(float) * 3
            );


        normalBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                cellCountTotal,
                sizeof(float) * 3
            );


        vertexValidBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                cellCountTotal,
                sizeof(uint)
            );


        vertexCountBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint)
            );


        indexComputeBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.CopySource,
                maxIndexCount,
                sizeof(uint)
            );


        indexRenderBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Index |
                GraphicsBuffer.Target.CopyDestination,
                maxIndexCount,
                sizeof(uint)
            );


        indexCountBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint)
            );
    }


    private void ReleaseBuffers()
    {
        if (vertexBuffer != null)
        {
            vertexBuffer.Release();

            vertexBuffer =
                null;
        }


        if (normalBuffer != null)
        {
            normalBuffer.Release();

            normalBuffer =
                null;
        }


        if (vertexValidBuffer != null)
        {
            vertexValidBuffer.Release();

            vertexValidBuffer =
                null;
        }


        if (vertexCountBuffer != null)
        {
            vertexCountBuffer.Release();

            vertexCountBuffer =
                null;
        }


        if (indexComputeBuffer != null)
        {
            indexComputeBuffer.Release();

            indexComputeBuffer =
                null;
        }


        if (indexRenderBuffer != null)
        {
            indexRenderBuffer.Release();

            indexRenderBuffer =
                null;
        }


        if (indexCountBuffer != null)
        {
            indexCountBuffer.Release();

            indexCountBuffer =
                null;
        }
    }


    private void BindBuffers()
    {
        voxelDensityShader.SetBuffer(
            vertexKernel,
            "VertexBuffer",
            vertexBuffer
        );


        voxelDensityShader.SetBuffer(
            vertexKernel,
            "NormalBuffer",
            normalBuffer
        );


        voxelDensityShader.SetBuffer(
            vertexKernel,
            "VertexValid",
            vertexValidBuffer
        );


        voxelDensityShader.SetBuffer(
            vertexKernel,
            "VertexCountBuffer",
            vertexCountBuffer
        );


        voxelDensityShader.SetBuffer(
            xFaceKernel,
            "VertexBuffer",
            vertexBuffer
        );


        voxelDensityShader.SetBuffer(
            xFaceKernel,
            "VertexValid",
            vertexValidBuffer
        );


        voxelDensityShader.SetBuffer(
            xFaceKernel,
            "IndexBuffer",
            indexComputeBuffer
        );


        voxelDensityShader.SetBuffer(
            xFaceKernel,
            "IndexCountBuffer",
            indexCountBuffer
        );


        voxelDensityShader.SetBuffer(
            yFaceKernel,
            "VertexBuffer",
            vertexBuffer
        );


        voxelDensityShader.SetBuffer(
            yFaceKernel,
            "VertexValid",
            vertexValidBuffer
        );


        voxelDensityShader.SetBuffer(
            yFaceKernel,
            "IndexBuffer",
            indexComputeBuffer
        );


        voxelDensityShader.SetBuffer(
            yFaceKernel,
            "IndexCountBuffer",
            indexCountBuffer
        );


        voxelDensityShader.SetBuffer(
            zFaceKernel,
            "VertexBuffer",
            vertexBuffer
        );


        voxelDensityShader.SetBuffer(
            zFaceKernel,
            "VertexValid",
            vertexValidBuffer
        );


        voxelDensityShader.SetBuffer(
            zFaceKernel,
            "IndexBuffer",
            indexComputeBuffer
        );


        voxelDensityShader.SetBuffer(
            zFaceKernel,
            "IndexCountBuffer",
            indexCountBuffer
        );
    }


    private void UpdateShaderParameters()
    {
        voxelDensityShader.SetInt(
            "CellCount",
            cellCount
        );


        voxelDensityShader.SetVector(
            "Origin",
            origin
        );


        voxelDensityShader.SetFloat(
            "Step",
            step
        );


        voxelDensityShader.SetVector(
            "PlanetCenter",
            planetCenter
        );


        voxelDensityShader.SetFloat(
            "PlanetRadius",
            planetRadius
        );


        voxelDensityShader.SetInt(
            "NoiseSeed",
            noiseSeed
        );


        voxelDensityShader.SetFloat(
            "NoiseFrequency",
            noiseFrequency
        );


        voxelDensityShader.SetFloat(
            "NoiseHeight",
            noiseHeight
        );
    }


    private void ClearBuffers()
    {
        uint[] zero =
        {
            0u
        };


        vertexCountBuffer.SetData(
            zero
        );


        indexCountBuffer.SetData(
            zero
        );


        uint[] validFlags =
            new uint[
                cellCountTotal
            ];


        vertexValidBuffer.SetData(
            validFlags
        );
    }


    public void Build()
    {
        if (!initialized)
        {
            return;
        }


        UpdateAutomaticGrid();


        if (!RecalculateGridCounts())
        {
            return;
        }


        bool bufferSizeMismatch =
            vertexBuffer == null ||
            normalBuffer == null ||
            vertexValidBuffer == null ||
            vertexCountBuffer == null ||
            indexComputeBuffer == null ||
            indexRenderBuffer == null ||
            indexCountBuffer == null;


        if (bufferSizeMismatch ||
            resolution != lastBuiltResolution)
        {
            CreateBuffers();

            BindBuffers();
        }


        ClearBuffers();


        UpdateShaderParameters();


        UpdateRenderBounds();


        int vertexGroups =
            CeilDiv(
                cellCount,
                8
            );


        voxelDensityShader.Dispatch(
            vertexKernel,
            vertexGroups,
            vertexGroups,
            vertexGroups
        );


        int faceGroups =
            CeilDiv(
                resolution,
                8
            );


        voxelDensityShader.Dispatch(
            xFaceKernel,
            faceGroups,
            faceGroups,
            faceGroups
        );


        voxelDensityShader.Dispatch(
            yFaceKernel,
            faceGroups,
            faceGroups,
            faceGroups
        );


        voxelDensityShader.Dispatch(
            zFaceKernel,
            faceGroups,
            faceGroups,
            faceGroups
        );


        Graphics.CopyBuffer(
            indexComputeBuffer,
            indexRenderBuffer
        );


        if (!readbackPending)
        {
            readbackPending =
                true;


            AsyncGPUReadback.Request(
                indexCountBuffer,
                OnIndexCountReadback
            );
        }


        lastBuiltRadius =
            planetRadius;


        lastBuiltPlanetCenter =
            planetCenter;


        lastBuiltResolution =
            resolution;


        lastBuiltNoiseSeed =
            noiseSeed;


        lastBuiltNoiseFrequency =
            noiseFrequency;


        lastBuiltNoiseHeight =
            noiseHeight;


        if (logResults)
        {
            float gridSize =
                resolution *
                step;


            Debug.Log(
                "GPU voxel build dispatched. " +
                $"Radius={planetRadius:F2}, " +
                $"Resolution={resolution}, " +
                $"CellCount={cellCount}, " +
                $"Step={step:F3}, " +
                $"GridSize={gridSize:F2}, " +
                $"Origin={origin}, " +
                $"NoiseSeed={noiseSeed}, " +
                $"NoiseFrequency={noiseFrequency:F3}, " +
                $"NoiseHeight={noiseHeight:F3}"
            );
        }
    }


    private void OnIndexCountReadback(
        AsyncGPUReadbackRequest request)
    {
        readbackPending =
            false;


        if (request.hasError)
        {
            Debug.LogError(
                "GpuDualContouringTest: " +
                "Index count readback failed."
            );

            return;
        }


        NativeArray<uint> data =
            request.GetData<uint>();


        if (data.Length == 0)
        {
            renderIndexCount =
                0;

            return;
        }


        uint indexCount =
            data[0];


        if (indexCount >
            (uint)maxIndexCount)
        {
            indexCount =
                (uint)maxIndexCount;
        }


        indexCount -=
            indexCount % 3u;


        renderIndexCount =
            (int)indexCount;


        if (logResults)
        {
            Debug.Log(
                "GPU Mesh: " +
                $"IndexCount={renderIndexCount}, " +
                $"TriangleCount={renderIndexCount / 3}"
            );
        }
    }


    private void RenderGpuMesh()
    {
        if (renderIndexCount < 3)
        {
            return;
        }


        if (vertexBuffer == null ||
            normalBuffer == null ||
            indexRenderBuffer == null)
        {
            return;
        }


        propertyBlock.Clear();


        propertyBlock.SetBuffer(
            "VertexBuffer",
            vertexBuffer
        );


        propertyBlock.SetBuffer(
            "NormalBuffer",
            normalBuffer
        );


        RenderParams renderParams =
            new RenderParams(
                gpuMaterial
            );


        renderParams.worldBounds =
            renderBounds;


        renderParams.matProps =
            propertyBlock;


        renderParams.shadowCastingMode =
            ShadowCastingMode.Off;


        renderParams.receiveShadows =
            false;


        renderParams.camera =
            null;


        Graphics.RenderPrimitivesIndexed(
            renderParams,
            MeshTopology.Triangles,
            indexRenderBuffer,
            renderIndexCount,
            0,
            1
        );
    }


    private void UpdateRenderBounds()
    {
        float gridSize =
            resolution *
            step;


        float sphereDiameter =
            planetRadius *
            2.0f;


        float noisePadding =
            Mathf.Abs(
                noiseHeight
            ) +
            MinimumGridPadding;


        float boundsSize =
            Mathf.Max(
                gridSize +
                step *
                2.0f,

                sphereDiameter +
                noisePadding *
                2.0f
            );


        renderBounds =
            new Bounds(
                planetCenter,
                Vector3.one *
                boundsSize
            );
    }


    public void ReadbackCurrentMesh()
    {
        if (!initialized)
        {
            Debug.LogWarning(
                "GPU test is not initialized."
            );

            return;
        }


        if (readbackPending)
        {
            Debug.LogWarning(
                "GPU readback is already pending."
            );

            return;
        }


        if (renderIndexCount <= 0)
        {
            Debug.LogWarning(
                "No rendered indices yet."
            );

            return;
        }


        readbackPending =
            true;


        AsyncGPUReadback.Request(
            vertexBuffer,
            vertexRequest =>
            {
                if (vertexRequest.hasError)
                {
                    readbackPending =
                        false;

                    Debug.LogError(
                        "VertexBuffer readback failed."
                    );

                    return;
                }


                NativeArray<Vector3> vertexData =
                    vertexRequest.GetData<Vector3>();


                Vector3[] gpuVertices =
                    vertexData.ToArray();


                int indexByteSize =
                    renderIndexCount *
                    sizeof(uint);


                AsyncGPUReadback.Request(
                    indexRenderBuffer,
                    indexByteSize,
                    0,
                    indexRequest =>
                    {
                        readbackPending =
                            false;


                        if (indexRequest.hasError)
                        {
                            Debug.LogError(
                                "IndexBuffer readback failed."
                            );

                            return;
                        }


                        NativeArray<uint> indexData =
                            indexRequest.GetData<uint>();


                        uint[] gpuIndices =
                            indexData.ToArray();


                        if (gpuIndices.Length < 3)
                        {
                            Debug.LogWarning(
                                "GPU mesh contains no triangles."
                            );

                            return;
                        }


                        if (gpuIndices.Length % 3 != 0)
                        {
                            Debug.LogError(
                                "GPU index count is not divisible by 3."
                            );

                            return;
                        }


                        for (int i = 0;
                             i < gpuIndices.Length;
                             i++)
                        {
                            if (gpuIndices[i] >=
                                (uint)gpuVertices.Length)
                            {
                                Debug.LogError(
                                    $"GPU index {gpuIndices[i]} " +
                                    $"is outside VertexBuffer " +
                                    $"range {gpuVertices.Length}."
                                );

                                return;
                            }
                        }


                        int[] remap =
                            new int[
                                gpuVertices.Length
                            ];


                        for (int i = 0;
                             i < remap.Length;
                             i++)
                        {
                            remap[i] =
                                -1;
                        }


                        int usedVertexCount =
                            0;


                        for (int i = 0;
                             i < gpuIndices.Length;
                             i++)
                        {
                            int oldIndex =
                                (int)gpuIndices[i];


                            if (remap[oldIndex] >= 0)
                            {
                                continue;
                            }


                            remap[oldIndex] =
                                usedVertexCount;


                            usedVertexCount++;
                        }


                        Vector3[] meshVertices =
                            new Vector3[
                                usedVertexCount
                            ];


                        for (int oldIndex = 0;
                             oldIndex < gpuVertices.Length;
                             oldIndex++)
                        {
                            int newIndex =
                                remap[oldIndex];


                            if (newIndex < 0)
                            {
                                continue;
                            }


                            meshVertices[newIndex] =
                                gpuVertices[oldIndex];
                        }


                        int[] meshTriangles =
                            new int[
                                gpuIndices.Length
                            ];


                        for (int i = 0;
                             i < gpuIndices.Length;
                             i++)
                        {
                            int oldIndex =
                                (int)gpuIndices[i];


                            meshTriangles[i] =
                                remap[oldIndex];
                        }


                        if (collisionMeshCollider != null)
                        {
                            collisionMeshCollider.sharedMesh =
                                null;
                        }


                        if (collisionMesh != null)
                        {
                            Destroy(collisionMesh);

                            collisionMesh =
                                null;
                        }


                        collisionMesh =
                            new Mesh();


                        collisionMesh.name =
                            "GPU_Collision_Mesh";


                        collisionMesh.indexFormat =
                            IndexFormat.UInt32;


                        collisionMesh.SetVertices(
                            meshVertices
                        );


                        collisionMesh.SetTriangles(
                            meshTriangles,
                            0,
                            false
                        );


                        collisionMesh.RecalculateBounds();


                        collisionMeshCollider =
                            GetComponent<MeshCollider>();


                        if (collisionMeshCollider == null)
                        {
                            collisionMeshCollider =
                                gameObject.AddComponent<MeshCollider>();
                        }


                        collisionMeshCollider.enabled =
                            true;


                        collisionMeshCollider.convex =
                            false;


                        collisionMeshCollider.sharedMesh =
                            collisionMesh;


                        Debug.Log(
                            $"GPU Mesh Readback: " +
                            $"Vertices = {gpuVertices.Length}, " +
                            $"Indices = {gpuIndices.Length}, " +
                            $"Triangles = {gpuIndices.Length / 3}"
                        );


                        Debug.Log(
                            $"GPU Collision Mesh Created: " +
                            $"Vertices = {meshVertices.Length}, " +
                            $"Indices = {meshTriangles.Length}, " +
                            $"Triangles = {meshTriangles.Length / 3}"
                        );


                        Debug.Log(
                            $"MeshCollider assigned. " +
                            $"enabled={collisionMeshCollider.enabled}, " +
                            $"sharedMesh=" +
                            $"{collisionMeshCollider.sharedMesh != null}, " +
                            $"convex={collisionMeshCollider.convex}"
                        );
                    }
                );
            }
        );
    }


    [ContextMenu("Readback Current Mesh")]
    private void DebugReadbackCurrentMesh()
    {
        ReadbackCurrentMesh();
    }


    private static int CeilDiv(
        int value,
        int divisor)
    {
        return
            (value + divisor - 1) /
            divisor;
    }
}