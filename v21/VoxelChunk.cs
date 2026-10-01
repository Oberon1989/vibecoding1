using UnityEngine;

public class VoxelChunk
{
    public Vector3Int Coordinate { get; }
    public int Lod { get; }
    public float Size { get; }

    public GameObject GameObject { get; }
    public MeshFilter MeshFilter { get; }
    public MeshRenderer MeshRenderer { get; }
    public MeshCollider MeshCollider { get; }

    public bool IsDirty { get; private set; }

    public Vector3 WorldMin =>
        new Vector3(
            Coordinate.x * Size,
            Coordinate.y * Size,
            Coordinate.z * Size
        );

    public Vector3 WorldMax =>
        WorldMin + Vector3.one * Size;

    public Bounds WorldBounds =>
        new Bounds(
            (WorldMin + WorldMax) * 0.5f,
            Vector3.one * Size
        );

    public VoxelChunk(
        Vector3Int coordinate,
        int lod,
        float size,
        Transform parent,
        Material material)
    {
        Coordinate = coordinate;
        Lod = lod;
        Size = size;

        GameObject = new GameObject(
            $"Chunk_{coordinate.x}_{coordinate.y}_{coordinate.z}_LOD{lod}"
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
        // Удаляем старую геометрию collider.
        MeshCollider.sharedMesh = null;

        // Если mesh пустой, просто очищаем chunk.
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

        // Unity MeshCollider не принимает
        // mesh без хотя бы одного корректного треугольника.
        if (mesh.triangles.Length >= 3)
        {
            MeshCollider.sharedMesh = mesh;
        }

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
            Object.Destroy(GameObject);
    }
}