using Unity.Mathematics;

public static class ArrayUtility
{
    public static int From3DTo1D(int3 id, int3 dim)
    {
        return id.x * dim.z * dim.y + (id.y * dim.z + id.z);
    }

    public static int3 From1DTo3D(int id, int3 dim)
    {
        int x = id / (dim.y * dim.z);
        int w = id % (dim.y * dim.z);
        int y = w / dim.z;
        int z = w % dim.z;

        return new int3(x, y, z);
    }

    // returns 1D offset in every axis given dimensions of a 3D array.
    public static int3 GetOffset(int3 dim)
    {
        int3 offset = new int3(dim.z * dim.y, dim.z, 1);
        return offset;
    }

    public static int3 GetGlobalElementId(int3 blockElementId, int3 blockId, int3 blockDim)
    {
        return blockElementId + blockDim * blockId;
    }

    public static int3 GetBlockElementId(int3 globalElementId, int3 blockId, int3 blockDim)
    {
        return globalElementId % (blockDim * blockId);
    }
}
