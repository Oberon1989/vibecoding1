using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct GpuBoundaryCellData
{
    public Vector3 Position;
    public Vector3 Normal;

    public uint LocalVertexIndex;
    public uint CompactVertexIndex;
}


public class GpuLodGroupReadback
{
    // ============================================================
    // INDEX COUNTS
    // ============================================================

    public void RequestIndexCounts(
        GpuLodGroup group,
        Action<uint[]> onComplete)
    {
        if (group == null)
            throw new ArgumentNullException(
                nameof(group));


        if (group.Buffers == null ||
            group.Buffers.IndexCountBuffer == null)
        {
            throw new InvalidOperationException(
                "GPU LOD group buffers are not created.");
        }


        RequestIndexCounts(
            group.Buffers.IndexCountBuffer,
            onComplete);
    }


    public void RequestIndexCounts(
        GraphicsBuffer indexCountBuffer,
        Action<uint[]> onComplete)
    {
        if (indexCountBuffer == null)
        {
            onComplete?.Invoke(null);

            return;
        }


        try
        {
            AsyncGPUReadback.Request(
                indexCountBuffer,
                request =>
                {
                    if (request.hasError)
                    {
                        onComplete?.Invoke(null);

                        return;
                    }


                    NativeArray<uint> data =
                        request.GetData<uint>();


                    uint[] counts =
                        data.ToArray();


                    onComplete?.Invoke(
                        counts
                    );
                });
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "GpuLodGroupReadback: index count readback " +
                "was not started: " +
                exception.Message
            );


            onComplete?.Invoke(
                null
            );
        }
    }


    // ============================================================
    // REGULAR CHUNK COUNTS
    // ============================================================

    public void RequestCounts(
        GraphicsBuffer vertexCountBuffer,
        GraphicsBuffer indexCountBuffer,
        Action<uint, uint> onComplete)
    {
        if (vertexCountBuffer == null ||
            indexCountBuffer == null)
        {
            onComplete?.Invoke(
                0u,
                0u
            );

            return;
        }


        try
        {
            AsyncGPUReadback.Request(
                vertexCountBuffer,
                vertexRequest =>
                {
                    if (vertexRequest.hasError)
                    {
                        onComplete?.Invoke(
                            0u,
                            0u
                        );

                        return;
                    }


                    NativeArray<uint> vertexData =
                        vertexRequest.GetData<uint>();


                    if (vertexData.Length == 0)
                    {
                        onComplete?.Invoke(
                            0u,
                            0u
                        );

                        return;
                    }


                    uint vertexCount =
                        vertexData[0];


                    // ------------------------------------------------
                    // The world may have been destroyed between the
                    // two asynchronous readbacks.
                    // ------------------------------------------------

                    if (indexCountBuffer == null)
                    {
                        onComplete?.Invoke(
                            vertexCount,
                            0u
                        );

                        return;
                    }


                    try
                    {
                        AsyncGPUReadback.Request(
                            indexCountBuffer,
                            indexRequest =>
                            {
                                if (indexRequest.hasError)
                                {
                                    onComplete?.Invoke(
                                        vertexCount,
                                        0u
                                    );

                                    return;
                                }


                                NativeArray<uint> indexData =
                                    indexRequest.GetData<uint>();


                                if (indexData.Length == 0)
                                {
                                    onComplete?.Invoke(
                                        vertexCount,
                                        0u
                                    );

                                    return;
                                }


                                uint indexCount =
                                    indexData[0];


                                onComplete?.Invoke(
                                    vertexCount,
                                    indexCount
                                );
                            }
                        );
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "GpuLodGroupReadback: index readback " +
                            "could not be started: " +
                            exception.Message
                        );


                        onComplete?.Invoke(
                            vertexCount,
                            0u
                        );
                    }
                });
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "GpuLodGroupReadback: vertex readback " +
                "could not be started: " +
                exception.Message
            );


            onComplete?.Invoke(
                0u,
                0u
            );
        }
    }


    // ============================================================
    // SEAM COUNTS - COMPUTE BUFFER
    // ============================================================
    //
    // GpuSeamBuildBuffers uses ComputeBuffer, therefore this
    // overload is intentionally separate from the GraphicsBuffer
    // based regular chunk readback.
    //
    // ============================================================

    public void RequestSeamCounts(
        ComputeBuffer seamVertexCountBuffer,
        ComputeBuffer seamIndexCountBuffer,
        Action<uint, uint> onComplete)
    {
        if (seamVertexCountBuffer == null ||
            seamIndexCountBuffer == null)
        {
            onComplete?.Invoke(
                0u,
                0u
            );

            return;
        }


        try
        {
            AsyncGPUReadback.Request(
                seamVertexCountBuffer,
                vertexRequest =>
                {
                    if (vertexRequest.hasError)
                    {
                        onComplete?.Invoke(
                            0u,
                            0u
                        );

                        return;
                    }


                    NativeArray<uint> vertexData =
                        vertexRequest.GetData<uint>();


                    if (vertexData.Length == 0)
                    {
                        onComplete?.Invoke(
                            0u,
                            0u
                        );

                        return;
                    }


                    uint vertexCount =
                        vertexData[0];


                    if (seamIndexCountBuffer == null)
                    {
                        onComplete?.Invoke(
                            vertexCount,
                            0u
                        );

                        return;
                    }


                    try
                    {
                        AsyncGPUReadback.Request(
                            seamIndexCountBuffer,
                            indexRequest =>
                            {
                                if (indexRequest.hasError)
                                {
                                    onComplete?.Invoke(
                                        vertexCount,
                                        0u
                                    );

                                    return;
                                }


                                NativeArray<uint> indexData =
                                    indexRequest.GetData<uint>();


                                if (indexData.Length == 0)
                                {
                                    onComplete?.Invoke(
                                        vertexCount,
                                        0u
                                    );

                                    return;
                                }


                                uint indexCount =
                                    indexData[0];


                                onComplete?.Invoke(
                                    vertexCount,
                                    indexCount
                                );
                            }
                        );
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "GpuLodGroupReadback: seam index " +
                            "readback could not be started: " +
                            exception.Message
                        );


                        onComplete?.Invoke(
                            vertexCount,
                            0u
                        );
                    }
                });
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "GpuLodGroupReadback: seam vertex readback " +
                "could not be started: " +
                exception.Message
            );


            onComplete?.Invoke(
                0u,
                0u
            );
        }
    }


    // ============================================================
    // SEAM COUNTS - GRAPHICS BUFFER
    // ============================================================
    //
    // Kept as an overload for future use and compatibility.
    //
    // ============================================================

    public void RequestSeamCounts(
        GraphicsBuffer seamVertexCountBuffer,
        GraphicsBuffer seamIndexCountBuffer,
        Action<uint, uint> onComplete)
    {
        if (seamVertexCountBuffer == null ||
            seamIndexCountBuffer == null)
        {
            onComplete?.Invoke(
                0u,
                0u
            );

            return;
        }


        try
        {
            AsyncGPUReadback.Request(
                seamVertexCountBuffer,
                vertexRequest =>
                {
                    if (vertexRequest.hasError)
                    {
                        onComplete?.Invoke(
                            0u,
                            0u
                        );

                        return;
                    }


                    NativeArray<uint> vertexData =
                        vertexRequest.GetData<uint>();


                    if (vertexData.Length == 0)
                    {
                        onComplete?.Invoke(
                            0u,
                            0u
                        );

                        return;
                    }


                    uint vertexCount =
                        vertexData[0];


                    try
                    {
                        AsyncGPUReadback.Request(
                            seamIndexCountBuffer,
                            indexRequest =>
                            {
                                if (indexRequest.hasError)
                                {
                                    onComplete?.Invoke(
                                        vertexCount,
                                        0u
                                    );

                                    return;
                                }


                                NativeArray<uint> indexData =
                                    indexRequest.GetData<uint>();


                                if (indexData.Length == 0)
                                {
                                    onComplete?.Invoke(
                                        vertexCount,
                                        0u
                                    );

                                    return;
                                }


                                onComplete?.Invoke(
                                    vertexCount,
                                    indexData[0]
                                );
                            }
                        );
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "GpuLodGroupReadback: seam index " +
                            "readback could not be started: " +
                            exception.Message
                        );


                        onComplete?.Invoke(
                            vertexCount,
                            0u
                        );
                    }
                });
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "GpuLodGroupReadback: seam vertex readback " +
                "could not be started: " +
                exception.Message
            );


            onComplete?.Invoke(
                0u,
                0u
            );
        }
    }


    // ============================================================
    // BOUNDARY CELLS
    // ============================================================

    public void RequestBoundaryCells(
        GraphicsBuffer boundaryCellBuffer,
        GraphicsBuffer boundaryCellCountBuffer,
        Action<GpuBoundaryCellData[]> onComplete)
    {
        if (boundaryCellBuffer == null ||
            boundaryCellCountBuffer == null)
        {
            onComplete?.Invoke(null);

            return;
        }


        try
        {
            AsyncGPUReadback.Request(
                boundaryCellCountBuffer,
                countRequest =>
                {
                    if (countRequest.hasError)
                    {
                        onComplete?.Invoke(null);

                        return;
                    }


                    NativeArray<uint> countData =
                        countRequest.GetData<uint>();


                    if (countData.Length == 0)
                    {
                        onComplete?.Invoke(null);

                        return;
                    }


                    uint boundaryCount =
                        countData[0];


                    if (boundaryCount == 0u)
                    {
                        onComplete?.Invoke(
                            Array.Empty<GpuBoundaryCellData>()
                        );

                        return;
                    }


                    if (boundaryCount >
                        (uint)boundaryCellBuffer.count)
                    {
                        Debug.LogWarning(
                            "GpuLodGroupReadback: boundary count " +
                            $"{boundaryCount} exceeds boundary buffer " +
                            $"capacity {boundaryCellBuffer.count}."
                        );


                        onComplete?.Invoke(null);

                        return;
                    }


                    try
                    {
                        AsyncGPUReadback.Request(
                            boundaryCellBuffer,
                            dataRequest =>
                            {
                                if (dataRequest.hasError)
                                {
                                    onComplete?.Invoke(null);

                                    return;
                                }


                                NativeArray<GpuBoundaryCellData> data =
                                    dataRequest.GetData<
                                        GpuBoundaryCellData
                                    >();


                                int count =
                                    Mathf.Min(
                                        (int)boundaryCount,
                                        data.Length
                                    );


                                GpuBoundaryCellData[] result =
                                    new GpuBoundaryCellData[count];


                                for (int i = 0;
                                     i < count;
                                     i++)
                                {
                                    result[i] =
                                        data[i];
                                }


                                onComplete?.Invoke(
                                    result
                                );
                            }
                        );
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "GpuLodGroupReadback: boundary data " +
                            "readback could not be started: " +
                            exception.Message
                        );


                        onComplete?.Invoke(null);
                    }
                }
            );
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "GpuLodGroupReadback: boundary count readback " +
                "could not be started: " +
                exception.Message
            );


            onComplete?.Invoke(null);
        }
    }
}