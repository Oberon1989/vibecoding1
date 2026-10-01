using UnityEngine;

public class VoxelChunk
{
    public Vector3 Center { get; }
    public float Size { get; }
    public int Lod { get; }
    public float VoxelSize { get; }

    public GameObject GameObject { get; }
    public MeshFilter MeshFilter { get; }
    public MeshRenderer MeshRenderer { get; }
    public MeshCollider MeshCollider { get; }

    public bool IsDirty { get; private set; }

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

    public VoxelChunk(
        Vector3 center,
        float size,
        int lod,
        float voxelSize,
        Transform parent,
        Material material)
    {
        Center = center;
        Size = size;
        Lod = lod;
        VoxelSize = voxelSize;

        GameObject = new GameObject(
            $"Chunk_{center}_{lod}"
        );

        GameObject.transform.SetParent(
            parent,
            false
        );

        MeshFilter =
            GameObject.AddComponent<MeshFilter>();

        MeshRenderer =
            GameObject.AddComponent<MeshRenderer>();

        MeshCollider =
            GameObject.AddComponent<MeshCollider>();

        MeshRenderer.sharedMaterial =
            material;

        IsDirty = true;
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void SetMesh(Mesh mesh)
    {
        MeshCollider.sharedMesh = null;

        if (mesh == null ||
            mesh.vertexCount < 3 ||
            mesh.triangles == null ||
            mesh.triangles.Length < 3)
        {
            MeshFilter.sharedMesh = null;

            IsDirty = false;

            return;
        }

        MeshFilter.sharedMesh = mesh;

        MeshCollider.sharedMesh = mesh;

        IsDirty = false;
    }

    public void ClearMesh()
    {
        MeshCollider.sharedMesh = null;
        MeshFilter.sharedMesh = null;

        IsDirty = true;
    }

    public void Destroy()
    {
        if (GameObject != null)
        {
            Object.Destroy(
                GameObject
            );
        }
    }
}