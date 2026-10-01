using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class DualContouringMesher
{
    private static readonly Vector3Int[] CornerOffsets =
    {
        new Vector3Int(0, 0, 0),
        new Vector3Int(1, 0, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(1, 1, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(1, 0, 1),
        new Vector3Int(0, 1, 1),
        new Vector3Int(1, 1, 1)
    };

    private static readonly int[,] EdgeCorners =
    {
        { 0, 1 },
        { 0, 2 },
        { 0, 4 },
        { 1, 3 },
        { 1, 5 },
        { 2, 3 },
        { 2, 6 },
        { 3, 7 },
        { 4, 5 },
        { 4, 6 },
        { 5, 7 },
        { 6, 7 }
    };

    private readonly QefSolver qefSolver = new();

    private readonly List<Vector3> vertices = new();
    private readonly List<int> triangles = new();

    private readonly List<VoxelModification> modifications = new();

    public Mesh Build(
        VoxelChunk chunk,
        VoxelGenerator generator,
        VoxelModificationStorage modificationStorage,
        int resolution)
    {
        vertices.Clear();
        triangles.Clear();
        modifications.Clear();

        float step =
            chunk.Size /
            resolution;

        Vector3 origin =
            chunk.WorldMin;

        int sampleCount =
            resolution + 2;

        int cellCount =
            resolution + 1;

        Bounds queryBounds =
            chunk.WorldBounds;

        queryBounds.Expand(
            step * 3f
        );

        modificationStorage.GetInBounds(
            queryBounds,
            modifications
        );

        float[,,] density =
            new float[
                sampleCount,
                sampleCount,
                sampleCount
            ];

        for (int z = 0;
             z < sampleCount;
             z++)
        {
            for (int y = 0;
                 y < sampleCount;
                 y++)
            {
                for (int x = 0;
                     x < sampleCount;
                     x++)
                {
                    Vector3 position =
                        origin +
                        new Vector3(
                            x * step,
                            y * step,
                            z * step
                        );

                    density[x, y, z] =
                        SampleDensity(
                            position,
                            generator
                        );
                }
            }
        }

        int[,,] vertexIndices =
            new int[
                cellCount,
                cellCount,
                cellCount
            ];

        for (int z = 0;
             z < cellCount;
             z++)
        {
            for (int y = 0;
                 y < cellCount;
                 y++)
            {
                for (int x = 0;
                     x < cellCount;
                     x++)
                {
                    vertexIndices[
                        x,
                        y,
                        z
                    ] = -1;
                }
            }
        }

        for (int z = 0;
             z < cellCount;
             z++)
        {
            for (int y = 0;
                 y < cellCount;
                 y++)
            {
                for (int x = 0;
                     x < cellCount;
                     x++)
                {
                    BuildCell(
                        x,
                        y,
                        z,
                        origin,
                        step,
                        density,
                        vertexIndices,
                        generator
                    );
                }
            }
        }

        BuildXFaces(
            resolution,
            density,
            vertexIndices
        );

        BuildYFaces(
            resolution,
            density,
            vertexIndices
        );

        BuildZFaces(
            resolution,
            density,
            vertexIndices
        );

        Mesh mesh =
            new Mesh();

        mesh.indexFormat =
            IndexFormat.UInt32;

        mesh.SetVertices(
            vertices
        );

        mesh.SetTriangles(
            triangles,
            0
        );

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private float SampleDensity(
        Vector3 position,
        VoxelGenerator generator)
    {
        float density =
            generator.SampleDensity(
                position
            );

        for (int i = 0;
             i < modifications.Count;
             i++)
        {
            VoxelModification modification =
                modifications[i];

            if (modification.Radius <= 0f)
            {
                continue;
            }

            float distance =
                Vector3.Distance(
                    position,
                    modification.Position
                );

            if (distance >
                modification.Radius)
            {
                continue;
            }

            float falloff =
                1f -
                distance /
                modification.Radius;

            float influence =
                falloff *
                modification.Strength;

            switch (modification.Operation)
            {
                case VoxelModificationOperation.Subtract:
                    density -= influence;
                    break;

                case VoxelModificationOperation.Add:
                    density += influence;
                    break;
            }
        }

        return density;
    }

    private void BuildCell(
        int x,
        int y,
        int z,
        Vector3 origin,
        float step,
        float[,,] density,
        int[,,] vertexIndices,
        VoxelGenerator generator)
    {
        Vector3[] positions =
            new Vector3[8];

        float[] values =
            new float[8];

        bool hasInside =
            false;

        bool hasOutside =
            false;

        for (int i = 0;
             i < 8;
             i++)
        {
            Vector3Int cornerOffset =
                CornerOffsets[i];

            int sx =
                x +
                cornerOffset.x;

            int sy =
                y +
                cornerOffset.y;

            int sz =
                z +
                cornerOffset.z;

            positions[i] =
                origin +
                new Vector3(
                    sx * step,
                    sy * step,
                    sz * step
                );

            values[i] =
                density[
                    sx,
                    sy,
                    sz
                ];

            if (values[i] < 0f)
            {
                hasInside = true;
            }
            else
            {
                hasOutside = true;
            }
        }

        if (!hasInside ||
            !hasOutside)
        {
            return;
        }

        Vector3 center =
            origin +
            new Vector3(
                (x + 0.5f) * step,
                (y + 0.5f) * step,
                (z + 0.5f) * step
            );

        qefSolver.Clear();

        List<Vector3> intersectionPoints =
            new();

        for (int edge = 0;
             edge < 12;
             edge++)
        {
            int a =
                EdgeCorners[
                    edge,
                    0
                ];

            int b =
                EdgeCorners[
                    edge,
                    1
                ];

            float da =
                values[a];

            float db =
                values[b];

            if (!IsSignChange(
                da,
                db))
            {
                continue;
            }

            float denominator =
                da -
                db;

            if (Mathf.Abs(
                denominator) <
                0.000001f)
            {
                continue;
            }

            float t =
                da /
                denominator;

            t =
                Mathf.Clamp01(
                    t
                );

            Vector3 point =
                Vector3.Lerp(
                    positions[a],
                    positions[b],
                    t
                );

            intersectionPoints.Add(
                point
            );

            Vector3 normal =
                EstimateNormal(
                    point,
                    generator,
                    step
                );

            qefSolver.Add(
                point - center,
                normal
            );
        }

        if (intersectionPoints.Count == 0)
        {
            return;
        }

        Vector3 vertex =
            center +
            qefSolver.Solve(
                Vector3.zero
            );

        Vector3 min =
            origin +
            new Vector3(
                x * step,
                y * step,
                z * step
            );

        Vector3 max =
            min +
            Vector3.one *
            step;

        if (vertex.x < min.x ||
            vertex.x > max.x ||
            vertex.y < min.y ||
            vertex.y > max.y ||
            vertex.z < min.z ||
            vertex.z > max.z)
        {
            Vector3 massPoint =
                Vector3.zero;

            for (int i = 0;
                 i < intersectionPoints.Count;
                 i++)
            {
                massPoint +=
                    intersectionPoints[i];
            }

            massPoint /=
                intersectionPoints.Count;

            vertex =
                massPoint;
        }

        int index =
            vertices.Count;

        vertices.Add(
            vertex
        );

        vertexIndices[
            x,
            y,
            z
        ] = index;
    }

    private void BuildXFaces(
        int resolution,
        float[,,] density,
        int[,,] vertexIndices)
    {
        for (int x = 0;
             x < resolution;
             x++)
        {
            for (int y = 1;
                 y <= resolution;
                 y++)
            {
                for (int z = 1;
                     z <= resolution;
                     z++)
                {
                    float da =
                        density[
                            x,
                            y,
                            z
                        ];

                    float db =
                        density[
                            x + 1,
                            y,
                            z
                        ];

                    if (!IsSignChange(
                        da,
                        db))
                    {
                        continue;
                    }

                    int a =
                        vertexIndices[
                            x,
                            y - 1,
                            z - 1
                        ];

                    int b =
                        vertexIndices[
                            x,
                            y,
                            z - 1
                        ];

                    int c =
                        vertexIndices[
                            x,
                            y,
                            z
                        ];

                    int d =
                        vertexIndices[
                            x,
                            y - 1,
                            z
                        ];

                    AddQuadFromEdge(
                        a,
                        b,
                        c,
                        d,
                        da,
                        db,
                        Vector3.right
                    );
                }
            }
        }
    }

    private void BuildYFaces(
        int resolution,
        float[,,] density,
        int[,,] vertexIndices)
    {
        for (int x = 1;
             x <= resolution;
             x++)
        {
            for (int y = 0;
                 y < resolution;
                 y++)
            {
                for (int z = 1;
                     z <= resolution;
                     z++)
                {
                    float da =
                        density[
                            x,
                            y,
                            z
                        ];

                    float db =
                        density[
                            x,
                            y + 1,
                            z
                        ];

                    if (!IsSignChange(
                        da,
                        db))
                    {
                        continue;
                    }

                    int a =
                        vertexIndices[
                            x - 1,
                            y,
                            z - 1
                        ];

                    int b =
                        vertexIndices[
                            x,
                            y,
                            z - 1
                        ];

                    int c =
                        vertexIndices[
                            x,
                            y,
                            z
                        ];

                    int d =
                        vertexIndices[
                            x - 1,
                            y,
                            z
                        ];

                    AddQuadFromEdge(
                        a,
                        b,
                        c,
                        d,
                        da,
                        db,
                        Vector3.up
                    );
                }
            }
        }
    }

    private void BuildZFaces(
        int resolution,
        float[,,] density,
        int[,,] vertexIndices)
    {
        for (int x = 1;
             x <= resolution;
             x++)
        {
            for (int y = 1;
                 y <= resolution;
                 y++)
            {
                for (int z = 0;
                     z < resolution;
                     z++)
                {
                    float da =
                        density[
                            x,
                            y,
                            z
                        ];

                    float db =
                        density[
                            x,
                            y,
                            z + 1
                        ];

                    if (!IsSignChange(
                        da,
                        db))
                    {
                        continue;
                    }

                    int a =
                        vertexIndices[
                            x - 1,
                            y - 1,
                            z
                        ];

                    int b =
                        vertexIndices[
                            x,
                            y - 1,
                            z
                        ];

                    int c =
                        vertexIndices[
                            x,
                            y,
                            z
                        ];

                    int d =
                        vertexIndices[
                            x - 1,
                            y,
                            z
                        ];

                    AddQuadFromEdge(
                        a,
                        b,
                        c,
                        d,
                        da,
                        db,
                        Vector3.forward
                    );
                }
            }
        }
    }

    private void AddQuadFromEdge(
        int a,
        int b,
        int c,
        int d,
        float da,
        float db,
        Vector3 axis)
    {
        if (a < 0 ||
            b < 0 ||
            c < 0 ||
            d < 0)
        {
            return;
        }

        bool insideToOutside =
            da < 0f &&
            db >= 0f;

        bool baseNormalMatchesPositiveAxis =
            axis != Vector3.up;

        bool reverse =
            insideToOutside !=
            baseNormalMatchesPositiveAxis;

        if (reverse)
        {
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);

            triangles.Add(a);
            triangles.Add(d);
            triangles.Add(c);
        }
        else
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);

            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }
    }

    private Vector3 EstimateNormal(
        Vector3 position,
        VoxelGenerator generator,
        float step)
    {
        float h =
            Mathf.Max(
                step * 0.25f,
                0.001f
            );

        float dx =
            SampleDensity(
                position + Vector3.right * h,
                generator
            )
            -
            SampleDensity(
                position - Vector3.right * h,
                generator
            );

        float dy =
            SampleDensity(
                position + Vector3.up * h,
                generator
            )
            -
            SampleDensity(
                position - Vector3.up * h,
                generator
            );

        float dz =
            SampleDensity(
                position + Vector3.forward * h,
                generator
            )
            -
            SampleDensity(
                position - Vector3.forward * h,
                generator
            );

        Vector3 normal =
            new Vector3(
                dx,
                dy,
                dz
            );

        if (normal.sqrMagnitude <
            0.000001f)
        {
            return Vector3.up;
        }

        return normal.normalized;
    }

    private bool IsSignChange(
        float a,
        float b)
    {
        return
            (a < 0f && b >= 0f) ||
            (a >= 0f && b < 0f);
    }
}