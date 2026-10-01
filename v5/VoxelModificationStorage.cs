using System.Collections.Generic;
using UnityEngine;

public class VoxelModificationStorage
{
    private readonly List<VoxelModification> modifications = new();

    public void Add(VoxelModification modification)
    {
        modifications.Add(modification);
    }

    public IReadOnlyList<VoxelModification> GetAll()
    {
        return modifications;
    }

    public void GetInBounds(
        Bounds bounds,
        List<VoxelModification> result)
    {
        result.Clear();

        for (int i = 0; i < modifications.Count; i++)
        {
            VoxelModification modification = modifications[i];

            float radius = modification.Radius;

            Bounds modificationBounds = new Bounds(
                modification.Position,
                Vector3.one * radius * 2f
            );

            if (bounds.Intersects(modificationBounds))
            {
                result.Add(modification);
            }
        }
    }

    public void Clear()
    {
        modifications.Clear();
    }
}