using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;


public static class RLECompression 
{ 
    public struct Run
    {
        public uint count;
        public byte value;

        public Run(byte value)
        {
            count = 1;
            this.value = value;
        }
    }

    public static int ComputeRunCount(NativeArray<byte> data)
    {
        int runCount = 1;
        for (int i = 1; i < data.Length; i++)
        {
            if (data[i] != data[i - 1])
            {
                runCount++;
            }
        }
        return runCount;
    }

    public static void Compress(NativeArray<byte> data, NativeArray<Run> compressedData)
    {
        Run run = new Run(data[0]);
        int runIndex = 0;
        for (int i = 1; i < data.Length; i++)
        {
            if (data[i] != data[i - 1])
            {
                compressedData[runIndex] = run;
                runIndex++;
                run = new Run(data[i]);
            }
            else
            {
                run.count++;
            }
        }
        compressedData[runIndex] = run;
    }

    public static void Decompress(NativeArray<Run> compressedData, NativeArray<byte> data)
    {
        int i = 0;
        for (int j = 0; j < compressedData.Length; j++)
        {
            Run run = compressedData[j];
            while (run.count > 0)
            {
                data[i] = run.value;
                run.count--;
                i++;
            }
        }
    }

    #region ParallelJobs

    [BurstCompile(CompileSynchronously = true)]
    public struct ComputeRunCountJob : IJob
    {
        [ReadOnly] public NativeArray<byte> data;
        [WriteOnly] public NativeArray<int> runCount;

        public void Execute()
        {
            runCount[0] = ComputeRunCount(data);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct CompressJob : IJob
    {
        [ReadOnly] public NativeArray<byte> data;
        [WriteOnly] public NativeArray<Run> compressedData;
        
        public void Execute()
        {
            Compress(data, compressedData);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct DecompressJob : IJob
    {
        [ReadOnly] public NativeArray<Run> compressedData;
        [WriteOnly] public NativeArray<byte> data;

        public void Execute()
        {
            Decompress(compressedData, data);
        }
    }

    #endregion
}
