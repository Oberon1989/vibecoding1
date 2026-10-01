using UnityEngine;

public class GpuSeamNode
{
    public Vector3 Center { get; }
    public float Size { get; }
    public int Lod { get; }

    // Источник узла и его координаты в исходной chunk-сетке.
    // Нужны только для построения topology seam на CPU.
    public GpuChunk SourceChunk { get; }
    public int CellX { get; }
    public int CellY { get; }
    public int CellZ { get; }

    public bool HasTopologyCoordinates =>
        SourceChunk != null &&
        CellX >= 0 &&
        CellY >= 0 &&
        CellZ >= 0;

    public Vector3 WorldMin =>
        Center - Vector3.one * (Size * 0.5f);

    public Vector3 WorldMax =>
        Center + Vector3.one * (Size * 0.5f);

    public Bounds WorldBounds =>
        new Bounds(Center, Vector3.one * Size);

    // Старый конструктор сохраняем для совместимости.
    public GpuSeamNode(
        Vector3 center,
        float size,
        int lod)
    {
        Center = center;
        Size = size;
        Lod = lod;

        SourceChunk = null;
        CellX = -1;
        CellY = -1;
        CellZ = -1;
    }

    // Топологический конструктор.
    public GpuSeamNode(
        GpuChunk sourceChunk,
        int cellX,
        int cellY,
        int cellZ,
        Vector3 center,
        float size,
        int lod)
    {
        SourceChunk = sourceChunk;
        CellX = cellX;
        CellY = cellY;
        CellZ = cellZ;

        Center = center;
        Size = size;
        Lod = lod;
    }
}