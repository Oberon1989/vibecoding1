using UnityEngine;

public class TerrainNoise
{
    private readonly float scale;
    private readonly float strength;

    private readonly int[] permutation =
        new int[512];

    private static readonly Vector3[] Gradients =
    {
        new Vector3(1, 1, 0),
        new Vector3(-1, 1, 0),
        new Vector3(1, -1, 0),
        new Vector3(-1, -1, 0),

        new Vector3(1, 0, 1),
        new Vector3(-1, 0, 1),
        new Vector3(1, 0, -1),
        new Vector3(-1, 0, -1),

        new Vector3(0, 1, 1),
        new Vector3(0, -1, 1),
        new Vector3(0, 1, -1),
        new Vector3(0, -1, -1)
    };

    public TerrainNoise(
        int seed,
        float scale,
        float strength)
    {
        this.scale = scale;
        this.strength = strength;

        InitializePermutation(seed);
    }

    private void InitializePermutation(int seed)
    {
        int[] values =
            new int[256];

        for (int i = 0; i < 256; i++)
            values[i] = i;

        System.Random random =
            new System.Random(seed);

        for (int i = 255; i > 0; i--)
        {
            int j =
                random.Next(i + 1);

            int temp =
                values[i];

            values[i] =
                values[j];

            values[j] =
                temp;
        }

        for (int i = 0; i < 512; i++)
        {
            permutation[i] =
                values[i & 255];
        }
    }

    public float GetDensityOffset(
        Vector3 worldPosition)
    {
        Vector3 point =
            worldPosition * scale;

        float noise =
            Simplex3D(
                point.x,
                point.y,
                point.z
            );

        return -noise * strength;
    }

    private float Simplex3D(
        float x,
        float y,
        float z)
    {
        const float F3 =
            1f / 3f;

        const float G3 =
            1f / 6f;

        float skew =
            (x + y + z) * F3;

        int i =
            Mathf.FloorToInt(
                x + skew
            );

        int j =
            Mathf.FloorToInt(
                y + skew
            );

        int k =
            Mathf.FloorToInt(
                z + skew
            );

        float unskew =
            (i + j + k) * G3;

        float x0 =
            x - (i - unskew);

        float y0 =
            y - (j - unskew);

        float z0 =
            z - (k - unskew);

        int i1;
        int j1;
        int k1;

        int i2;
        int j2;
        int k2;

        if (x0 >= y0)
        {
            if (y0 >= z0)
            {
                i1 = 1;
                j1 = 0;
                k1 = 0;

                i2 = 1;
                j2 = 1;
                k2 = 0;
            }
            else if (x0 >= z0)
            {
                i1 = 1;
                j1 = 0;
                k1 = 0;

                i2 = 1;
                j2 = 0;
                k2 = 1;
            }
            else
            {
                i1 = 0;
                j1 = 0;
                k1 = 1;

                i2 = 1;
                j2 = 0;
                k2 = 1;
            }
        }
        else
        {
            if (y0 < z0)
            {
                i1 = 0;
                j1 = 0;
                k1 = 1;

                i2 = 0;
                j2 = 1;
                k2 = 1;
            }
            else if (x0 < z0)
            {
                i1 = 0;
                j1 = 1;
                k1 = 0;

                i2 = 0;
                j2 = 1;
                k2 = 1;
            }
            else
            {
                i1 = 0;
                j1 = 1;
                k1 = 0;

                i2 = 1;
                j2 = 1;
                k2 = 0;
            }
        }

        float x1 =
            x0 - i1 + G3;

        float y1 =
            y0 - j1 + G3;

        float z1 =
            z0 - k1 + G3;

        float x2 =
            x0 - i2 + 2f * G3;

        float y2 =
            y0 - j2 + 2f * G3;

        float z2 =
            z0 - k2 + 2f * G3;

        float x3 =
            x0 - 1f + 3f * G3;

        float y3 =
            y0 - 1f + 3f * G3;

        float z3 =
            z0 - 1f + 3f * G3;

        int ii =
            i & 255;

        int jj =
            j & 255;

        int kk =
            k & 255;

        int gi0 =
            permutation[
                ii +
                permutation[
                    jj +
                    permutation[kk]
                ]
            ] % Gradients.Length;

        int gi1 =
            permutation[
                ii + i1 +
                permutation[
                    jj + j1 +
                    permutation[
                        kk + k1
                    ]
                ]
            ] % Gradients.Length;

        int gi2 =
            permutation[
                ii + i2 +
                permutation[
                    jj + j2 +
                    permutation[
                        kk + k2
                    ]
                ]
            ] % Gradients.Length;

        int gi3 =
            permutation[
                ii + 1 +
                permutation[
                    jj + 1 +
                    permutation[
                        kk + 1
                    ]
                ]
            ] % Gradients.Length;

        float n0 =
            Contribution(
                x0,
                y0,
                z0,
                Gradients[gi0]
            );

        float n1 =
            Contribution(
                x1,
                y1,
                z1,
                Gradients[gi1]
            );

        float n2 =
            Contribution(
                x2,
                y2,
                z2,
                Gradients[gi2]
            );

        float n3 =
            Contribution(
                x3,
                y3,
                z3,
                Gradients[gi3]
            );

        return 32f *
            (n0 + n1 + n2 + n3);
    }

    private float Contribution(
        float x,
        float y,
        float z,
        Vector3 gradient)
    {
        float t =
            0.6f -
            x * x -
            y * y -
            z * z;

        if (t < 0f)
            return 0f;

        t *= t;

        return
            t *
            t *
            Vector3.Dot(
                gradient,
                new Vector3(
                    x,
                    y,
                    z
                )
            );
    }
}