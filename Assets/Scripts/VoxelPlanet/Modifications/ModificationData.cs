using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static GeometryUtility;

public struct ModificationData
{
    ModificationType type;
    byte material;
    Matrix4x4 inverse;
    ABB aabb;

    public ModificationData(ModificationType type, byte material, Matrix4x4 inverse, ABB aabb)
    {
        this.type = type;
        this.material = material;
        this.inverse = inverse;
        this.aabb = aabb;
    }

    public byte GetMaterial() => material;

    public ABB GetABB() => aabb;

    public float Evaluate(float3 point)
    {
        point = inverse.MultiplyPoint(point);
        if (type == ModificationType.Sphere)
        {
            return math.length(point) - 0.5f;
        }
        else if (type == ModificationType.Cube)
        {
            float3 b = new float3(0.5f);
            float3 q = math.abs(point) - b;
            return math.length(math.max(q, 0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0f);
        }
        else
        {
            return 0;
        }
    }
}
