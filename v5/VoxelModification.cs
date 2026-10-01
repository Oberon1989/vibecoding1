using UnityEngine;

public struct VoxelModification
{
    public Vector3 Position;
    public float Radius;
    public float Strength;
    public VoxelModificationOperation Operation;

    public VoxelModification(
        Vector3 position,
        float radius,
        float strength,
        VoxelModificationOperation operation)
    {
        Position = position;
        Radius = radius;
        Strength = strength;
        Operation = operation;
    }
}

public enum VoxelModificationOperation
{
    Subtract,
    Add
}