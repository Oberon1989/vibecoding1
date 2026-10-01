using UnityEngine;

public class GpuChunk
{
    public Vector3 Center { get; }

    public float Size { get; }

    public int Lod { get; }

    // Number of cells along one axis.
    public int Resolution { get; }

    // Slot inside the GPU buffer group.
    public int Slot { get; private set; }


    // ============================================================
    // NEIGHBOR LODS
    // ============================================================

    // -1 means that the neighbor is not known / does not exist.
    public int NeighborLodNegX { get; private set; }
    public int NeighborLodPosX { get; private set; }

    public int NeighborLodNegY { get; private set; }
    public int NeighborLodPosY { get; private set; }

    public int NeighborLodNegZ { get; private set; }
    public int NeighborLodPosZ { get; private set; }


    // ============================================================
    // COARSE NEIGHBOR -> SEAM NEEDED ON COARSE SIDE
    // ============================================================

    // This chunk is coarser than its neighbor.
    public bool HasSeamNegX =>
        NeighborLodNegX >= 0 &&
        NeighborLodNegX < Lod;

    public bool HasSeamPosX =>
        NeighborLodPosX >= 0 &&
        NeighborLodPosX < Lod;

    public bool HasSeamNegY =>
        NeighborLodNegY >= 0 &&
        NeighborLodNegY < Lod;

    public bool HasSeamPosY =>
        NeighborLodPosY >= 0 &&
        NeighborLodPosY < Lod;

    public bool HasSeamNegZ =>
        NeighborLodNegZ >= 0 &&
        NeighborLodNegZ < Lod;

    public bool HasSeamPosZ =>
        NeighborLodPosZ >= 0 &&
        NeighborLodPosZ < Lod;


    // ============================================================
    // FINE SIDE -> BOUNDARY MERGE NEEDED
    // ============================================================

    // This chunk is finer than its neighbor.
    public bool NeedsMergeNegX =>
        NeighborLodNegX > Lod;

    public bool NeedsMergePosX =>
        NeighborLodPosX > Lod;

    public bool NeedsMergeNegY =>
        NeighborLodNegY > Lod;

    public bool NeedsMergePosY =>
        NeighborLodPosY > Lod;

    public bool NeedsMergeNegZ =>
        NeighborLodNegZ > Lod;

    public bool NeedsMergePosZ =>
        NeighborLodPosZ > Lod;


    // ============================================================
    // ANY MERGE
    // ============================================================

    public bool NeedsBoundaryMerge =>
        NeedsMergeNegX ||
        NeedsMergePosX ||
        NeedsMergeNegY ||
        NeedsMergePosY ||
        NeedsMergeNegZ ||
        NeedsMergePosZ;


    // ============================================================
    // SEAM STATE
    // ============================================================

    // True when this chunk currently has a generated seam mesh.
    public bool SeamReady { get; private set; }

    // Number of vertices in the dedicated seam buffer.
    public int SeamVertexCount { get; private set; }

    // Number of indices in the dedicated seam buffer.
    public int SeamIndexCount { get; private set; }

    public bool HasSeamMesh =>
        SeamReady &&
        SeamVertexCount > 0 &&
        SeamIndexCount > 0;


    // ============================================================
    // VOXEL / BOUNDS
    // ============================================================

    // Size of one cell.
    public float VoxelSize =>
        Size / Resolution;

    public Vector3 WorldMin =>
        Center -
        Vector3.one * (Size * 0.5f);

    public Vector3 WorldMax =>
        Center +
        Vector3.one * (Size * 0.5f);

    public Bounds WorldBounds =>
        new Bounds(
            Center,
            Vector3.one * Size
        );


    // ============================================================
    // CONSTRUCTOR
    // ============================================================

    public GpuChunk(
        Vector3 center,
        float size,
        int lod,
        int resolution)
    {
        Center =
            center;

        Size =
            size;

        Lod =
            lod;

        Resolution =
            resolution;

        Slot =
            -1;


        NeighborLodNegX =
            -1;

        NeighborLodPosX =
            -1;

        NeighborLodNegY =
            -1;

        NeighborLodPosY =
            -1;

        NeighborLodNegZ =
            -1;

        NeighborLodPosZ =
            -1;


        SeamReady =
            false;

        SeamVertexCount =
            0;

        SeamIndexCount =
            0;
    }


    // ============================================================
    // GPU SLOT
    // ============================================================

    public void AssignSlot(
        int slot)
    {
        Slot =
            slot;
    }


    // ============================================================
    // NEIGHBORS
    // ============================================================

    public void SetNeighborLods(
        int negX,
        int posX,
        int negY,
        int posY,
        int negZ,
        int posZ)
    {
        NeighborLodNegX =
            negX;

        NeighborLodPosX =
            posX;

        NeighborLodNegY =
            negY;

        NeighborLodPosY =
            posY;

        NeighborLodNegZ =
            negZ;

        NeighborLodPosZ =
            posZ;
    }


    // ============================================================
    // SEAM STATE
    // ============================================================

    public void SetSeamCounts(
        int vertexCount,
        int indexCount)
    {
        SeamVertexCount =
            Mathf.Max(
                0,
                vertexCount
            );

        SeamIndexCount =
            Mathf.Max(
                0,
                indexCount
            );

        SeamReady =
            SeamVertexCount > 0 &&
            SeamIndexCount > 0;
    }


    public void ClearSeam()
    {
        SeamReady =
            false;

        SeamVertexCount =
            0;

        SeamIndexCount =
            0;
    }
}