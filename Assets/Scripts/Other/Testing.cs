using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using System;
using Unity.Burst;
using UnityEngine.Profiling;
using System.Threading;

struct HashMap<TKey, TValue> : IDisposable
    where TKey : unmanaged, IEquatable<TKey>
    where TValue : unmanaged
    
{
    struct HashElement
    {
        public TKey key;
        public TValue value;

        public HashElement(in TKey key, in TValue value)
        {
            this.key = key;
            this.value = value;
        }
    }

    [NativeDisableContainerSafetyRestriction] NativeArray<UnsafeList<HashElement>> hashMap;
    [NativeDisableContainerSafetyRestriction] NativeArray<int> locks;

    public HashMap(int length, Allocator allocator)
    {
        hashMap = new NativeArray<UnsafeList<HashElement>>(length, allocator);
        for (int i = 0; i < hashMap.Length; i++)
        {
            hashMap[i] = new UnsafeList<HashElement>(0, allocator);
        }

        locks = new NativeArray<int>(length, allocator);
        for (int i = 0; i < locks.Length; i++)
        {
            locks[i] = 0;
        } 
    }

    public void Add(in TKey key, in TValue value)
    {
        int hashCode = GetHashCode(key);

        unsafe
        {
            var ptr = UnsafeUtility.ArrayElementAsRef<int>(locks.GetUnsafePtr(), hashCode);
            while (0 != Interlocked.Exchange(ref ptr, 1))
            {
                
            }

            var hashList = hashMap[hashCode];

            int i = GetHashElementIndex(hashList, key);

            if (i != -1)
            {
                hashList[i] = new HashElement(key, value);
            }
            else
            {
                hashList.Add(new HashElement(key, value));
            }

            hashMap[hashCode] = hashList; // assign list value types (f.i length)

            Interlocked.Exchange(ref ptr, 0);
        }
    }

    public void Remove(in TKey key)
    {
        int hashCode = GetHashCode(key);
        var hashList = hashMap[hashCode];

        int i = GetHashElementIndex(hashList, key);
        if (i != -1)
        {
            hashList.RemoveAtSwapBack(i);
        }

        hashMap[hashCode] = hashList;
    }

    public bool TryGetValue(in TKey key, out TValue value)
    {
        int hashCode = GetHashCode(key);
        var hashList = hashMap[hashCode];

        int i = GetHashElementIndex(hashList, key);
        if (i != -1)
        {
            value = hashList[i].value;
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    public int DebugData()
    {
        int maxSize = 0;
        for (int i = 0; i < hashMap.Length; i++)
        {
            if (hashMap[i].Length > maxSize) maxSize = hashMap[i].Length;
        }

        return maxSize;
    }

    int GetHashElementIndex(UnsafeList<HashElement> hashList, in TKey key)
    {
        for (int i = 0; i < hashList.Length; i++)
        {
            if (key.Equals(hashList[i].key))
            {
                return i;
            }
        }
        return -1;
    }

    int GetHashCode(in TKey key)
    {
        return key.GetHashCode() % hashMap.Length;
    }

    public void Dispose()
    {
        hashMap.Dispose();
        locks.Dispose();
    }
}

struct NodeKey : IEquatable<NodeKey>
{
    public int3 position;
    public byte depth;

    public NodeKey(int3 position, byte depth)
    {
        this.position = position;
        this.depth = depth;
    }

    public bool Equals(NodeKey other)
    {
        return position.Equals(other.position) && depth == other.depth;
    }

    public override int GetHashCode()
    {
        return ArrayUtility.From3DTo1D(position, new int3(256, 256, 256));
    }
}

struct NodeValue
{
    public int3 position;
    public byte depth;

    public FixedArray8<byte> materials;
    public FixedArray12<byte> intersections;

    public NodeValue(int3 position, byte depth)
    {
        this.position = position;
        this.depth = depth;

        materials = new FixedArray8<byte>();
        intersections = new FixedArray12<byte>();
    }

    public NodeKey GetNodeKey()
    {
        return new NodeKey(position, depth);
    }

    public override string ToString()
    {
        return position.ToString() + " " + depth.ToString();
    }
}

public class Testing : MonoBehaviour
{
    void Update()
    {
        Test();
        Debug.Break();
    }

    void Test()
    {
        HashMap<NodeKey, NodeValue> hashMap = new HashMap<NodeKey, NodeValue>(16000, Allocator.Persistent);

        NativeList<NodeValue> inputNodes = new NativeList<NodeValue>(Allocator.Persistent);
        var randGen = new Unity.Mathematics.Random(1);

        for (int i = 0; i < 32000; i++)
        {
            NodeValue nodeValue = new NodeValue(randGen.NextInt3(0, 100), 10);
            inputNodes.Add(nodeValue);
        }

        for (int i = 32000; i < 64000; i++)
        {
            NodeValue nodeValue = new NodeValue(randGen.NextInt3(100, 200), 10);
            inputNodes.Add(nodeValue);
        }

        for (int i = 64000; i < 96000; i++)
        {
            NodeValue nodeValue = new NodeValue(randGen.NextInt3(200, 256), 10);
            inputNodes.Add(nodeValue);
        }


        var jobA = new WriteJob()
        {
            start = 0,
            end = 32000,
            nodes = inputNodes,
            hashMap = hashMap,
        };
        var handleA = jobA.Schedule();

        var jobB = new WriteJob()
        {
            start = 32000,
            end = 64000,
            nodes = inputNodes,
            hashMap = hashMap,
        };
        var handleB = jobB.Schedule();

        var jobC= new WriteJob()
        {
            start = 64000,
            end = inputNodes.Length,
            nodes = inputNodes,
            hashMap = hashMap,
        };
        var handleC = jobC.Schedule();

        NativeList<NodeValue> outputNodes = new NativeList<NodeValue>(Allocator.Persistent);

        var jobD = new ReadJob()
        {
            inputNodes = inputNodes,
            hashMap = hashMap,
            outputNodes = outputNodes,
        };
        jobD.Schedule(JobHandle.CombineDependencies(handleA, handleB, handleC)).Complete();

        // check correct results
        Debug.Assert(inputNodes.Length == outputNodes.Length);
        for (int i = 0; i < inputNodes.Length; i++)
        {
            Debug.Assert(inputNodes[i].GetNodeKey().Equals(outputNodes[i].GetNodeKey() ));
        }

        Debug.Log(hashMap.DebugData());

        hashMap.Dispose();
        inputNodes.Dispose();
        outputNodes.Dispose();
    }

    [BurstCompile(CompileSynchronously = true)]
    struct WriteJob : IJob
    {
        public int start, end;
        [ReadOnly] public NativeList<NodeValue> nodes;
        public HashMap<NodeKey, NodeValue> hashMap;

        public void Execute()
        {
            for (int i = start; i < end; i++)
            {
                hashMap.Add(nodes[i].GetNodeKey(), nodes[i]);
            }
        }
    }

        [BurstCompile(CompileSynchronously = true)]
    struct ReadJob : IJob
    {
        [ReadOnly] public NativeList<NodeValue> inputNodes;
        [ReadOnly] public HashMap<NodeKey, NodeValue> hashMap;

        public NativeList<NodeValue> outputNodes;

        public void Execute()
        {
            for (int i = 0; i < inputNodes.Length; i++)
            {
                if (hashMap.TryGetValue(inputNodes[i].GetNodeKey(), out NodeValue nodeValue))
                {
                    outputNodes.Add(nodeValue);
                }
            }
        }
    }
}
