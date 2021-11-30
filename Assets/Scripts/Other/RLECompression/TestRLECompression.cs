using UnityEngine;
using System.Collections;
using UnityEngine.Profiling;
using Unity.Collections;
using static RLECompression;
using Unity.Jobs;

public class TestRLECompression : MonoBehaviour
{

    // Use this for initialization
    void Start()
    {
        // First time Unity Editor compile Burst jobs, incrementing the profiled time
        TestPerformance();

        Profiler.BeginSample("RLECompressionTest");
        TestPerformance();
        Profiler.EndSample();
        Debug.Break();
    }

    void TestPerformance()
    {
        int chunkSize = 64*64*64;

        Profiler.BeginSample("Initial Allocation");
        NativeArray<byte> data = new NativeArray<byte>(chunkSize, Allocator.Persistent);
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)UnityEngine.Random.Range(0, 4);
        }
        NativeArray<byte> expectedData = new NativeArray<byte>(chunkSize, Allocator.Persistent);
        data.CopyTo(expectedData);
        Profiler.EndSample();


        Profiler.BeginSample("ComputeRunCount");
        var runCount = new NativeArray<int>(1, Allocator.TempJob);
        var job = new ComputeRunCountJob()
        {
            data = data,
            runCount = runCount,
        };
        job.Schedule().Complete();
        Profiler.EndSample();


        Profiler.BeginSample("Allocate Compressed Data");
        NativeArray<Run> compressedData = new NativeArray<Run>(runCount[0], Allocator.Persistent);
        runCount.Dispose();
        Profiler.EndSample();


        Profiler.BeginSample("Compress");
        var compressJob = new CompressJob()
        {
            data = data,
            compressedData = compressedData,
        };
        compressJob.Schedule().Complete();
        Profiler.EndSample();

        Profiler.BeginSample("Changing data");
        // change data
        for (int i = 0; i < data.Length; i++) data[i] = 255;
        Profiler.EndSample();

        Profiler.BeginSample("Decompress");
        var decompressJob = new DecompressJob()
        {
            compressedData = compressedData,
            data = data,
        };
        decompressJob.Schedule().Complete();

        Profiler.EndSample();

        CheckCorrectState(data, expectedData);

        Debug.Log(data.Length);
        Debug.Log(compressedData.Length);

        data.Dispose();
        compressedData.Dispose();
        expectedData.Dispose();
    }

    void CheckCorrectState(NativeArray<byte> data, NativeArray<byte> expectedData)
    {
        bool areEqual = true;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != expectedData[i])
            {
                areEqual = false;
            }
        }

        Debug.Assert(areEqual);
    }
}
