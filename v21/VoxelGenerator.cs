using UnityEngine;

public class VoxelGenerator
{
    public Vector3 Center { get; }
    public float Radius { get; }

    public VoxelGenerator(Vector3 center, float radius)
    {
        Center = center;
        Radius = radius;
    }

    public float SampleDensity(Vector3 worldPosition)
    {
        return Vector3.Distance(worldPosition, Center) - Radius;
    }
}