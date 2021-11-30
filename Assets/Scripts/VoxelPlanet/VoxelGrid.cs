using Unity.Mathematics;

public struct VoxelGrid
{
    public NativeArray3D<float3> positions;
    public NativeArray3D<byte> materials;
    public NativeArray3D<CornerIntersections> intersections;
}
