using UnityEngine;

public class OctreeNode
{
    public Vector3 Center { get; }
    public float Size { get; }
    public int Lod { get; }
    public float VoxelSize { get; }

    public OctreeNode Parent { get; }

    public OctreeNode[] Children { get; private set; }

    public bool IsLeaf =>
        Children == null;

    public Bounds Bounds =>
        new Bounds(
            Center,
            Vector3.one * Size
        );

    public OctreeNode(
        Vector3 center,
        float size,
        int lod,
        float voxelSize,
        OctreeNode parent = null)
    {
        Center = center;
        Size = size;
        Lod = lod;
        VoxelSize = voxelSize;
        Parent = parent;
    }

    public void Subdivide()
    {
        if (!IsLeaf)
            return;

        Children =
            new OctreeNode[8];

        float childSize =
            Size * 0.5f;

        float childVoxelSize =
            VoxelSize * 0.5f;

        float offset =
            childSize * 0.5f;

        int index = 0;

        for (int z = -1; z <= 1; z += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    Vector3 childCenter =
                        Center +
                        new Vector3(
                            x * offset,
                            y * offset,
                            z * offset
                        );

                    Children[index++] =
                    new OctreeNode(
                        childCenter,
                        childSize,
                        Lod - 1,
                        childVoxelSize,
                        this
                    );
                }
            }
        }
    }

    public void Merge()
    {
        if (IsLeaf)
            return;

        for (int i = 0; i < Children.Length; i++)
        {
            Children[i].Merge();
        }

        Children = null;
    }
}