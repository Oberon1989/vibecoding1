using System.Collections.Generic;
using UnityEngine;

public class QefSolver
{
    private readonly List<Vector3> points = new();
    private readonly List<Vector3> normals = new();

    public void Clear()
    {
        points.Clear();
        normals.Clear();
    }

    public void Add(Vector3 point, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.000001f)
            return;

        points.Add(point);
        normals.Add(normal.normalized);
    }

    public Vector3 Solve(Vector3 fallback)
    {
        if (points.Count == 0)
            return fallback;

        Vector3 result = Vector3.zero;

        float[,] ata = new float[3, 3];
        Vector3 atb = Vector3.zero;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 n = normals[i];
            Vector3 p = points[i];

            ata[0, 0] += n.x * n.x;
            ata[0, 1] += n.x * n.y;
            ata[0, 2] += n.x * n.z;

            ata[1, 0] += n.y * n.x;
            ata[1, 1] += n.y * n.y;
            ata[1, 2] += n.y * n.z;

            ata[2, 0] += n.z * n.x;
            ata[2, 1] += n.z * n.y;
            ata[2, 2] += n.z * n.z;

            float d = Vector3.Dot(n, p);

            atb.x += n.x * d;
            atb.y += n.y * d;
            atb.z += n.z * d;
        }

        if (!SolveLinearSystem(ata, atb, out result))
            return AveragePoint();

        return result;
    }

    private Vector3 AveragePoint()
    {
        Vector3 sum = Vector3.zero;

        for (int i = 0; i < points.Count; i++)
            sum += points[i];

        return sum / points.Count;
    }

    private bool SolveLinearSystem(
        float[,] matrix,
        Vector3 b,
        out Vector3 result)
    {
        float[,] a = new float[3, 4];

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
                a[row, col] = matrix[row, col];
        }

        a[0, 3] = b.x;
        a[1, 3] = b.y;
        a[2, 3] = b.z;

        for (int col = 0; col < 3; col++)
        {
            int pivot = col;

            for (int row = col + 1; row < 3; row++)
            {
                if (Mathf.Abs(a[row, col]) > Mathf.Abs(a[pivot, col]))
                    pivot = row;
            }

            if (Mathf.Abs(a[pivot, col]) < 0.000001f)
            {
                result = Vector3.zero;
                return false;
            }

            if (pivot != col)
            {
                for (int j = col; j < 4; j++)
                {
                    float temp = a[col, j];
                    a[col, j] = a[pivot, j];
                    a[pivot, j] = temp;
                }
            }

            float divisor = a[col, col];

            for (int j = col; j < 4; j++)
                a[col, j] /= divisor;

            for (int row = 0; row < 3; row++)
            {
                if (row == col)
                    continue;

                float factor = a[row, col];

                for (int j = col; j < 4; j++)
                    a[row, j] -= factor * a[col, j];
            }
        }

        result = new Vector3(
            a[0, 3],
            a[1, 3],
            a[2, 3]
        );

        return true;
    }
}