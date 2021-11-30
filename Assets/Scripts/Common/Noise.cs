using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public static class Noise
{
    public enum Type { perlin, simplex, cellular };

    [System.Serializable]
    public struct NoiseSettings
    {
        public Noise.Type type;
        public float frequency;
        public float amplitude;
        public int octaveCount;
    }

    public static float ComputeNoise(in float3 position, in NoiseSettings noiseSettings)
    {
        Type noiseType = noiseSettings.type;
        float freq = noiseSettings.frequency;
        float ampl = noiseSettings.amplitude;
        int octaveCount = noiseSettings.octaveCount;

        float noiseValue;
        if (octaveCount > 1)
        {
            noiseValue = ComputeNoiseWithOctaves(position * freq, noiseType, octaveCount) * ampl; ;
        }
        else
        {
            noiseValue = ComputeNoise(position * freq, noiseType) * ampl;
        }

        return noiseValue;
    }

    public static float ComputeNoise(float3 point, Type type)
    {
        switch (type)
        {
            case Type.perlin:
                return (noise.cnoise(point) + 1) / 2;   //[-1,1] -> [0,1]
            case Type.cellular:
                return noise.cellular(point).x;
            case Type.simplex:
                return (noise.snoise(point) + 1) / 2;    //[-1,1] -> [0,1]
            default: return 0f;
        }
    }

    public static float ComputeNoiseWithOctaves(float3 p, Type noiseType, int octaveCount)
    {
        float persistence = 0.5f;

        float ampl = 1;
        float freq = 1;

        float totalAmpl = 0;
        float result = 0f;
        for (int i = 0; i < octaveCount; i++)
        {
            result += ComputeNoise(freq * p, noiseType) * ampl;
            totalAmpl += ampl;

            ampl *= persistence;
            freq *= 2; //lacunarity
        }

        return result / totalAmpl;
    }
}
