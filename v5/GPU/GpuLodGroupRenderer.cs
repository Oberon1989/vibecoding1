using System;
using UnityEngine;
using UnityEngine.Rendering;

public class GpuLodGroupRenderer
{
    private readonly Material material;
    private readonly MaterialPropertyBlock propertyBlock;

    private readonly ComputeShader computeShader;
    private readonly int copyKernel;

    private bool seamRenderDiagnosticLogged;


    // --------------------------------------------------------
    // LEGACY CONSTRUCTOR
    // --------------------------------------------------------

    public GpuLodGroupRenderer(
        Material material)
    {
        this.material =
            material;

        propertyBlock =
            new MaterialPropertyBlock();

        computeShader =
            null;

        copyKernel =
            -1;
    }


    // --------------------------------------------------------
    // COMPACT GPU CONSTRUCTOR
    // --------------------------------------------------------

    public GpuLodGroupRenderer(
        Material material,
        ComputeShader computeShader)
    {
        if (material == null)
            throw new ArgumentNullException(
                nameof(material));

        if (computeShader == null)
            throw new ArgumentNullException(
                nameof(computeShader));


        this.material =
            material;

        propertyBlock =
            new MaterialPropertyBlock();

        this.computeShader =
            computeShader;

        copyKernel =
            computeShader.FindKernel(
                "CSCopyChunk");
    }


    // --------------------------------------------------------
    // UPLOAD ONE COMPLETED CHUNK
    // --------------------------------------------------------

    public void UploadChunk(
        GpuChunk chunk,
        GpuLodGroup group,
        GpuChunkBuildBuffers buildBuffers,
        int vertexCount,
        int indexCount)
    {
        if (chunk == null)
            throw new ArgumentNullException(
                nameof(chunk));

        if (group == null)
            throw new ArgumentNullException(
                nameof(group));

        if (buildBuffers == null)
            throw new ArgumentNullException(
                nameof(buildBuffers));

        if (vertexCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(vertexCount));

        if (indexCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(indexCount));

        if (chunk.Slot < 0)
        {
            throw new InvalidOperationException(
                "Chunk does not have an assigned GPU slot.");
        }

        if (computeShader == null ||
            copyKernel < 0)
        {
            throw new InvalidOperationException(
                "Compact GPU upload requires a ComputeShader " +
                "with the CSCopyChunk kernel.");
        }


        GpuLodGroupBuffers buffers =
            group.Buffers;


        int vertexOffset =
            buffers.GetVertexOffset(
                chunk.Slot);


        int indexOffset =
            buffers.GetIndexOffset(
                chunk.Slot);


        int allocatedVertexCount =
            buffers.GetVertexCount(
                chunk.Slot);


        int allocatedIndexCount =
            buffers.GetIndexCount(
                chunk.Slot);


        if (vertexCount > allocatedVertexCount)
        {
            throw new InvalidOperationException(
                $"Chunk needs {vertexCount} vertices, " +
                $"but slot has only {allocatedVertexCount}.");
        }


        if (indexCount > allocatedIndexCount)
        {
            throw new InvalidOperationException(
                $"Chunk needs {indexCount} indices, " +
                $"but slot has only {allocatedIndexCount}.");
        }


        // ----------------------------------------------------
        // SOURCE
        // ----------------------------------------------------

        computeShader.SetBuffer(
            copyKernel,
            "CopySourceVertexBuffer",
            buildBuffers.VertexBuffer);


        computeShader.SetBuffer(
            copyKernel,
            "CopySourceNormalBuffer",
            buildBuffers.NormalBuffer);


        computeShader.SetBuffer(
            copyKernel,
            "CopySourceIndexBuffer",
            buildBuffers.IndexBuffer);


        // ----------------------------------------------------
        // DESTINATION
        // ----------------------------------------------------

        computeShader.SetBuffer(
            copyKernel,
            "CopyDestinationVertexBuffer",
            buffers.VertexBuffer);


        computeShader.SetBuffer(
            copyKernel,
            "CopyDestinationNormalBuffer",
            buffers.NormalBuffer);


        computeShader.SetBuffer(
            copyKernel,
            "CopyDestinationIndexBuffer",
            buffers.IndexComputeBuffer);


        // ----------------------------------------------------
        // COUNTS
        // ----------------------------------------------------

        computeShader.SetInt(
            "CopyVertexCount",
            vertexCount);


        computeShader.SetInt(
            "CopyIndexCount",
            indexCount);


        // ----------------------------------------------------
        // DESTINATION OFFSETS
        // ----------------------------------------------------

        computeShader.SetInt(
            "CopyVertexDestinationOffset",
            vertexOffset);


        computeShader.SetInt(
            "CopyIndexDestinationOffset",
            indexOffset);


        // ----------------------------------------------------
        // DISPATCH
        // ----------------------------------------------------

        int copyCount =
            Mathf.Max(
                vertexCount,
                indexCount);


        int groups =
            (copyCount + 63) / 64;


        computeShader.Dispatch(
            copyKernel,
            groups,
            1,
            1);


        // ----------------------------------------------------
        // INDEX BUFFER
        //
        // IndexRenderBuffer must remain a real Index buffer.
        // ----------------------------------------------------

        Graphics.CopyBuffer(
            buffers.IndexComputeBuffer,
            buffers.IndexRenderBuffer);
    }


    // --------------------------------------------------------
    // UPLOAD SEAM
    // --------------------------------------------------------
    //
    // Seam scratch buffers are copied into exact-size dedicated
    // buffers belonging to this chunk slot.
    //
    // Seam indices already reference the seam vertex buffer
    // from zero, therefore both destination offsets are zero.
    //
    // --------------------------------------------------------

    public void UploadSeam(
        GpuChunk chunk,
        GpuLodGroup group,
        GpuSeamBuildBuffers seamBuffers,
        int vertexCount,
        int indexCount)
    {
        if (chunk == null)
            throw new ArgumentNullException(
                nameof(chunk));

        if (group == null)
            throw new ArgumentNullException(
                nameof(group));

        if (seamBuffers == null)
            throw new ArgumentNullException(
                nameof(seamBuffers));

        if (vertexCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(vertexCount));

        if (indexCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(indexCount));

        if (chunk.Slot < 0)
        {
            throw new InvalidOperationException(
                "Chunk does not have an assigned GPU slot.");
        }

        if (computeShader == null ||
            copyKernel < 0)
        {
            throw new InvalidOperationException(
                "GPU seam upload requires a ComputeShader " +
                "with the CSCopyChunk kernel.");
        }


        GpuLodGroupBuffers buffers =
            group.Buffers;


        if (!buffers.HasSeamSlot(
                chunk.Slot))
        {
            throw new InvalidOperationException(
                "Chunk does not have an allocated seam slot.");
        }


        GraphicsBuffer destinationVertexBuffer =
            buffers.GetSeamVertexBuffer(
                chunk.Slot);


        GraphicsBuffer destinationNormalBuffer =
            buffers.GetSeamNormalBuffer(
                chunk.Slot);


        GraphicsBuffer destinationIndexBuffer =
            buffers.GetSeamIndexComputeBuffer(
                chunk.Slot);


        if (destinationVertexBuffer == null ||
            destinationNormalBuffer == null ||
            destinationIndexBuffer == null)
        {
            throw new InvalidOperationException(
                "GPU seam destination buffers are not created.");
        }


        if (vertexCount >
            destinationVertexBuffer.count)
        {
            throw new InvalidOperationException(
                $"Seam needs {vertexCount} vertices, " +
                $"but destination has " +
                $"{destinationVertexBuffer.count}.");
        }


        if (indexCount >
            destinationIndexBuffer.count)
        {
            throw new InvalidOperationException(
                $"Seam needs {indexCount} indices, " +
                $"but destination has " +
                $"{destinationIndexBuffer.count}.");
        }


        // ----------------------------------------------------
        // SOURCE
        // ----------------------------------------------------

        computeShader.SetBuffer(
            copyKernel,
            "CopySourceVertexBuffer",
            seamBuffers.VertexBuffer);


        computeShader.SetBuffer(
            copyKernel,
            "CopySourceNormalBuffer",
            seamBuffers.NormalBuffer);


        computeShader.SetBuffer(
            copyKernel,
            "CopySourceIndexBuffer",
            seamBuffers.IndexBuffer);


        // ----------------------------------------------------
        // DESTINATION
        // ----------------------------------------------------

        computeShader.SetBuffer(
            copyKernel,
            "CopyDestinationVertexBuffer",
            destinationVertexBuffer);


        computeShader.SetBuffer(
            copyKernel,
            "CopyDestinationNormalBuffer",
            destinationNormalBuffer);


        computeShader.SetBuffer(
            copyKernel,
            "CopyDestinationIndexBuffer",
            destinationIndexBuffer);


        // ----------------------------------------------------
        // COUNTS
        // ----------------------------------------------------

        computeShader.SetInt(
            "CopyVertexCount",
            vertexCount);


        computeShader.SetInt(
            "CopyIndexCount",
            indexCount);


        // ----------------------------------------------------
        // DESTINATION OFFSETS
        // ----------------------------------------------------

        computeShader.SetInt(
            "CopyVertexDestinationOffset",
            0);


        computeShader.SetInt(
            "CopyIndexDestinationOffset",
            0);


        // ----------------------------------------------------
        // DISPATCH
        // ----------------------------------------------------

        int copyCount =
            Mathf.Max(
                vertexCount,
                indexCount);


        int groups =
            (copyCount + 63) / 64;


        computeShader.Dispatch(
            copyKernel,
            groups,
            1,
            1);


        // ----------------------------------------------------
        // SEAM RENDER INDEX BUFFER
        // ----------------------------------------------------

        Graphics.CopyBuffer(
            destinationIndexBuffer,
            buffers.GetSeamIndexRenderBuffer(
                chunk.Slot));
    }


    // --------------------------------------------------------
    // LEGACY FULL INDEX COPY
    // --------------------------------------------------------

    public void CopyIndexBuffer(
        GpuLodGroup group)
    {
        if (group == null)
            throw new ArgumentNullException(
                nameof(group));


        Graphics.CopyBuffer(
            group.Buffers.IndexComputeBuffer,
            group.Buffers.IndexRenderBuffer);
    }


    // --------------------------------------------------------
    // RENDER REGULAR CHUNK
    // --------------------------------------------------------

    public void Render(
        GpuChunk chunk,
        GpuLodGroup group,
        int renderIndexCount)
    {
        if (chunk == null ||
            group == null)
        {
            return;
        }


        if (renderIndexCount < 3)
            return;


        if (chunk.Slot < 0)
            return;


        GpuLodGroupBuffers buffers =
            group.Buffers;


        int indexStart =
            buffers.GetIndexOffset(
                chunk.Slot);


        int vertexBaseOffset =
            buffers.GetVertexOffset(
                chunk.Slot);


        propertyBlock.Clear();


        propertyBlock.SetBuffer(
            "VertexBuffer",
            buffers.VertexBuffer);


        propertyBlock.SetBuffer(
            "NormalBuffer",
            buffers.NormalBuffer);


        propertyBlock.SetInt(
            "_VertexBaseOffset",
            vertexBaseOffset);


        RenderParams renderParams =
            new RenderParams(
                material);


        renderParams.worldBounds =
            chunk.WorldBounds;


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
            buffers.IndexRenderBuffer,
            renderIndexCount,
            indexStart,
            1);
    }


    // --------------------------------------------------------
    // RENDER SEAM
    // --------------------------------------------------------

   public void RenderSeam(
    GpuChunk chunk,
    GpuLodGroup group)
{
    if (chunk == null ||
        group == null)
    {
        return;
    }


    if (chunk.Slot < 0)
    {
        return;
    }


    GpuLodGroupBuffers buffers =
        group.Buffers;


    if (!buffers.HasSeamSlot(
            chunk.Slot))
    {
        return;
    }


    int seamIndexCount =
        buffers.GetSeamIndexCount(
            chunk.Slot);


    if (seamIndexCount < 3)
    {
        return;
    }


    GraphicsBuffer seamVertexBuffer =
        buffers.GetSeamVertexBuffer(
            chunk.Slot);


    GraphicsBuffer seamNormalBuffer =
        buffers.GetSeamNormalBuffer(
            chunk.Slot);


    GraphicsBuffer seamIndexBuffer =
        buffers.GetSeamIndexRenderBuffer(
            chunk.Slot);


    if (seamVertexBuffer == null ||
        seamNormalBuffer == null ||
        seamIndexBuffer == null)
    {
        return;
    }


    // --------------------------------------------------------
    // ONE-TIME DIAGNOSTIC
    // --------------------------------------------------------

    if (!seamRenderDiagnosticLogged)
    {
        seamRenderDiagnosticLogged = true;

        Debug.Log(
            $"GPU SEAM RENDER FIRST: " +
            $"chunk={chunk.Center} " +
            $"lod={chunk.Lod} " +
            $"slot={chunk.Slot} " +
            $"indices={seamIndexCount}");
    }


    propertyBlock.Clear();


    // --------------------------------------------------------
    // SEAM DEBUG COLOR
    // --------------------------------------------------------

    propertyBlock.SetColor(
        "_Color",
        Color.red);

    propertyBlock.SetFloat(
        "_Brightness",
        3.0f);

    propertyBlock.SetFloat(
        "_Ambient",
        1.0f);

    propertyBlock.SetFloat(
        "_LightStrength",
        0.0f);

    propertyBlock.SetFloat(
        "_SpecularStrength",
        0.0f);


    // --------------------------------------------------------
    // BUFFERS
    // --------------------------------------------------------

    propertyBlock.SetBuffer(
        "VertexBuffer",
        seamVertexBuffer);


    propertyBlock.SetBuffer(
        "NormalBuffer",
        seamNormalBuffer);


    propertyBlock.SetInt(
        "_VertexBaseOffset",
        0);


    // --------------------------------------------------------
    // BOUNDS
    // --------------------------------------------------------

    Bounds seamBounds =
        chunk.WorldBounds;


    seamBounds.Expand(
        seamBounds.size * 6.0f);


    // --------------------------------------------------------
    // RENDER
    // --------------------------------------------------------

    RenderParams renderParams =
        new RenderParams(
            material);


    renderParams.worldBounds =
        seamBounds;


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
        seamIndexBuffer,
        seamIndexCount,
        0,
        1);
}
}