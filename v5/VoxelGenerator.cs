using UnityEngine;

public class VoxelGenerator
{
    public Vector3 Center { get; }
    public float Radius { get; }
    public float SurfaceRadius { get; }

    private readonly TerrainNoise noise;
    private readonly float noiseHeight;

    public VoxelGenerator(
        Vector3 center,
        float radius,
        int noiseSeed,
        float noiseFrequency,
        float noiseHeight)
    {
        Center = center;
        Radius = radius;

        this.noiseHeight =
            noiseHeight;

        SurfaceRadius =
            radius +
            noiseHeight;

        noise =
            new TerrainNoise(
                noiseSeed,
                noiseFrequency,
                1f
            );
    }

    public float SampleDensity(
        Vector3 worldPosition)
    {
        float sphereDensity =
            Vector3.Distance(
                worldPosition,
                Center
            ) - Radius;

        Vector3 direction =
            (
                worldPosition - Center
            ).normalized;

        float noiseValue =
            noise.GetNoise(
                direction
            );

        return sphereDensity -
               noiseValue * noiseHeight;
    }
}