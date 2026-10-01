using UnityEngine;

public sealed class GpuSeamBuildBuffers
{
    public struct GpuSeamNodeData
    {
        public Vector3 Center;
        public float Size;
        public int Lod;
        public int Padding;
    }

    public struct GpuSeamEdgeData
    {
        public uint Node0;
        public uint Node1;
        public uint Node2;
        public uint Node3;
        public uint Axis;
    }

    public ComputeBuffer NodeBuffer { get; private set; }
    public ComputeBuffer EdgeBuffer { get; private set; }

    public ComputeBuffer VertexBuffer { get; private set; }
    public ComputeBuffer NormalBuffer { get; private set; }
    public ComputeBuffer VertexValidBuffer { get; private set; }
    public ComputeBuffer VertexIndexBuffer { get; private set; }

    public ComputeBuffer IndexBuffer { get; private set; }

    public ComputeBuffer VertexCountBuffer { get; private set; }
    public ComputeBuffer IndexCountBuffer { get; private set; }

    public int NodeCapacity { get; private set; }
    public int EdgeCapacity { get; private set; }
    public int VertexCapacity { get; private set; }
    public int IndexCapacity { get; private set; }

    public void Create(int nodeCapacity, int edgeCapacity)
    {
        Release();

        NodeCapacity = Mathf.Max(1, nodeCapacity);
        EdgeCapacity = Mathf.Max(1, edgeCapacity);

        VertexCapacity = NodeCapacity;
        IndexCapacity = Mathf.Max(6, EdgeCapacity * 6);

        int nodeStride =
            sizeof(float) * 3 +
            sizeof(float) +
            sizeof(int) +
            sizeof(int);

        int edgeStride = sizeof(uint) * 5;

        NodeBuffer = new ComputeBuffer(
            NodeCapacity,
            nodeStride,
            ComputeBufferType.Structured);

        EdgeBuffer = new ComputeBuffer(
            EdgeCapacity,
            edgeStride,
            ComputeBufferType.Structured);

        VertexBuffer = new ComputeBuffer(
            VertexCapacity,
            sizeof(float) * 3,
            ComputeBufferType.Structured);

        NormalBuffer = new ComputeBuffer(
            VertexCapacity,
            sizeof(float) * 3,
            ComputeBufferType.Structured);

        VertexValidBuffer = new ComputeBuffer(
            VertexCapacity,
            sizeof(uint),
            ComputeBufferType.Structured);

        VertexIndexBuffer = new ComputeBuffer(
            VertexCapacity,
            sizeof(uint),
            ComputeBufferType.Structured);

        IndexBuffer = new ComputeBuffer(
            IndexCapacity,
            sizeof(uint),
            ComputeBufferType.Structured);

        VertexCountBuffer = new ComputeBuffer(
            1,
            sizeof(uint),
            ComputeBufferType.Structured);

        IndexCountBuffer = new ComputeBuffer(
            1,
            sizeof(uint),
            ComputeBufferType.Structured);
    }

    public void Release()
    {
        ReleaseNodeBuffer();
        ReleaseEdgeBuffer();

        ReleaseVertexBuffer();
        ReleaseNormalBuffer();
        ReleaseVertexValidBuffer();
        ReleaseVertexIndexBuffer();

        ReleaseIndexBuffer();

        ReleaseVertexCountBuffer();
        ReleaseIndexCountBuffer();

        NodeCapacity = 0;
        EdgeCapacity = 0;
        VertexCapacity = 0;
        IndexCapacity = 0;
    }

    private void ReleaseNodeBuffer()
    {
        if (NodeBuffer == null)
            return;

        NodeBuffer.Release();
        NodeBuffer = null;
    }

    private void ReleaseEdgeBuffer()
    {
        if (EdgeBuffer == null)
            return;

        EdgeBuffer.Release();
        EdgeBuffer = null;
    }

    private void ReleaseVertexBuffer()
    {
        if (VertexBuffer == null)
            return;

        VertexBuffer.Release();
        VertexBuffer = null;
    }

    private void ReleaseNormalBuffer()
    {
        if (NormalBuffer == null)
            return;

        NormalBuffer.Release();
        NormalBuffer = null;
    }

    private void ReleaseVertexValidBuffer()
    {
        if (VertexValidBuffer == null)
            return;

        VertexValidBuffer.Release();
        VertexValidBuffer = null;
    }

    private void ReleaseVertexIndexBuffer()
    {
        if (VertexIndexBuffer == null)
            return;

        VertexIndexBuffer.Release();
        VertexIndexBuffer = null;
    }

    private void ReleaseIndexBuffer()
    {
        if (IndexBuffer == null)
            return;

        IndexBuffer.Release();
        IndexBuffer = null;
    }

    private void ReleaseVertexCountBuffer()
    {
        if (VertexCountBuffer == null)
            return;

        VertexCountBuffer.Release();
        VertexCountBuffer = null;
    }

    private void ReleaseIndexCountBuffer()
    {
        if (IndexCountBuffer == null)
            return;

        IndexCountBuffer.Release();
        IndexCountBuffer = null;
    }
}