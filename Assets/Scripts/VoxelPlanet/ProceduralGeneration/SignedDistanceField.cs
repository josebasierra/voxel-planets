using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using static Noise;

[System.Serializable]
public struct SignedDistanceField
{
    public enum FieldType { noise, sphereDistortion, sphereElevationDistortion};

    [System.Serializable]
    public struct SphereSettings
    {
        public float radius;
        public float minRadius;
        public float3 center;
    }

    [SerializeField] FieldType fieldType;
    [SerializeField] NoiseSettings noiseSettings;
    [SerializeField] SphereSettings sphereSettings;

    public SignedDistanceField(FieldType fieldType, NoiseSettings noiseSettings, SphereSettings sphereSettings)
    {
        this.fieldType = fieldType;
        this.noiseSettings = noiseSettings;
        this.sphereSettings = sphereSettings;
    }

    public float GetDistance(float3 position)
    {
        if (fieldType == FieldType.sphereDistortion)
        {
            return GeometryUtility.SphereSDF(position - sphereSettings.center, sphereSettings.radius) + ComputeNoise(position, noiseSettings);
        }
        else if (fieldType == FieldType.sphereElevationDistortion)
        {
            float3 centerToPosition = position - sphereSettings.center;

            float3 direction = math.normalize(centerToPosition);
            float3 projectedPointToSphere = sphereSettings.center + direction * sphereSettings.radius;

            float noiseValue = ComputeNoise(projectedPointToSphere, noiseSettings) - noiseSettings.amplitude/2f;
            float perturbedRadius = sphereSettings.radius + noiseValue;
            perturbedRadius = math.max(sphereSettings.minRadius, perturbedRadius);

            float elevationDistance = math.length(centerToPosition) - perturbedRadius;
            return elevationDistance;
        }
        else
        {
            return ComputeNoise(position, noiseSettings);
        }
    }
}
