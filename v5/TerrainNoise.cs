using UnityEngine;

public class TerrainNoise
{
    private readonly FastNoiseLite noise;

    public TerrainNoise(
        int seed,
        float scale,
        float strength)
    {
        noise =
            new FastNoiseLite(seed);

        noise.SetNoiseType(
            FastNoiseLite.NoiseType.OpenSimplex2
        );

        noise.SetFrequency(
            scale
        );
    }

    public float GetNoise(
        Vector3 worldPosition)
    {
        return Mathf.Clamp(
            noise.GetNoise(
                worldPosition.x,
                worldPosition.y,
                worldPosition.z
            ),
            -1f,
            1f
        );
    }

    public float GetDensityOffset(
        Vector3 worldPosition)
    {
        return -GetNoise(
            worldPosition
        );
    }
}