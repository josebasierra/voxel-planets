using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/* 3D wrapper of NativeArray */
public struct NativeArray3D<T> : IDisposable where T : struct
{
    NativeArray<T> internalArray;
    readonly int length1D;
    readonly int3 length3D;

    public NativeArray3D(int lengthX, int lengthY, int lengthZ, Allocator allocator)
    {
        length1D = lengthX * lengthY * lengthZ;
        length3D = new int3(lengthX, lengthY, lengthZ);
        internalArray = new NativeArray<T>(length1D, allocator);
    }

    public NativeArray3D(int3 length3D, Allocator allocator)
    {
        length1D = length3D.x * length3D.y * length3D.z;
        this.length3D = length3D;
        internalArray = new NativeArray<T>(length1D, allocator);
    }

    public T this[int i]
    {
        get => internalArray[i];
        set => internalArray[i] = value;   
    }

    public T this[in int3 index3D]
    {
        get => internalArray[GetIndex1D(index3D)];
        set => internalArray[GetIndex1D(index3D)] = value;
    }

    public T this[int x, int y, int z]
    {
        get => internalArray[GetIndex1D(x, y, z)];
        set => internalArray[GetIndex1D(x, y, z)] = value;
    }

    public int GetIndex1D(int x, int y, int z)
    {
        return x * length3D.z * length3D.y + (y * length3D.z + z);
    }

    public int GetIndex1D(in int3 index3D)
    {
        return index3D.x * length3D.z * length3D.y + (index3D.y * length3D.z + index3D.z);
    }

    public int3 GetIndex3D(int index)
    {
        int x = index / (length3D.y * length3D.z);
        int w = index % (length3D.y * length3D.z);
        int y = w / length3D.z;
        int z = w % length3D.z;

        return new int3(x, y, z);
    }

    public int GetLength1D() => length1D;
    public int GetLengthX() => length3D.x;
    public int GetLengthY() => length3D.y;
    public int GetLengthZ() => length3D.z;
    public int3 GetLength3D() => length3D;

    public void Dispose()
    {
        internalArray.Dispose();
    }

    public void Dispose(JobHandle dependsOn)
    {
        internalArray.Dispose(dependsOn);
    }
}


