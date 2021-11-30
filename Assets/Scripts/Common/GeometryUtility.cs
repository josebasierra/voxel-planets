using System;
using Unity.Mathematics;

public static class GeometryUtility
{
    // axis-aligned bounding box
    public struct ABB : IEquatable<ABB>
    {
        public float3 min;
        public float3 max;

        public ABB(float3 min, float3 max)
        {
            this.min = min;
            this.max = max;
        }

        public float3 GetSize() 
        {
            return math.abs(max - min);
        }

        public float3 GetCenter()
        {
            return min + (max - min) / 2f;
        }

        public override string ToString()
        {
            return (min.ToString() + " " + max.ToString());
        }

        public bool Equals(ABB other)
        {
            return min.Equals(other.min) && max.Equals(other.max);
        }
    }

    // box1.max > box2.min && box1.min < box2.max
    public static bool Intersects(in ABB box1, in ABB box2)
    {
        return box1.max.x > box2.min.x && box1.max.y > box2.min.y && box1.max.z > box2.min.z &&
            box1.min.x < box2.max.x && box1.min.y < box2.max.y && box1.min.z < box2.max.z;
    }

    // cube center and size -> AABB (min,max)
    public static ABB GetABB(in float3 center,in float size)
    {
        return new ABB(center - size / 2f, center + size / 2f);
    }

    public static float GetDistanceToSurface(float3 point, ABB abb)
    {
        float3 distanceToCenter = math.abs(point - abb.GetCenter());
        float3 boxExtends = abb.GetSize()/2f;

        float3 V = math.pow(math.max(float3.zero, distanceToCenter - boxExtends), 2);
        float distanceToSurface = math.sqrt(V.x + V.y + V.z);

        return distanceToSurface;
    }

    public static float SphereSDF(float3 point, float radius)
    {
        return math.length(point) - radius;
    }

    public static float BoxSDF(float3 point, float3 size)
    {
        float3 b = size / 2f;
        float3 q = math.abs(point) - b;
        return math.length(math.max(q, 0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0f);
    }
}
