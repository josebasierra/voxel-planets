using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Atmosphere : MonoBehaviour
{
    public struct AtmosphereData
    {
        public float3 center;
        public float minRadius;
        public float maxRadius;
        public float avgDensityHeightLocation;

        public float3 sunLightDir;
        public float3 sunLightIntensity;
        public float3 scatteringCoeficients;

        public int numInScatteringPoints;
        public int numOpticalDepthPoints;
    };

    [SerializeField] Transform planetTransform;
    [SerializeField] Transform sunLightTransform;

    [Header("Atmosphere settings")]
    [SerializeField] float minRadius;
    [SerializeField] float maxRadius;
    [SerializeField, Range(0, 0.5f)] float avgDensityHeightLocation = 0.04f;
    [SerializeField] float scatteringStrength = 1f;
    [SerializeField] float3 sunLightIntensity = new float3(15, 15, 15);

    [Header("Performance settings")]
    [SerializeField] int numInScatteringPoints = 8;
    [SerializeField] int numOpticalDepthPoints = 8;

    float3 channelWavelengths = new float3(700, 530, 440);

    AtmospherePassController atmosPassController;
    int atmosphereId;

    bool needsUpdate = true;

    void Start()
    {
        atmosPassController = FindObjectOfType<AtmospherePassController>();
        if (atmosPassController == null) Debug.LogError("AtmospherePassController not found");

        atmosphereId = atmosPassController.GetAtmosphereId();
    }

    void Update()
    {
        if (needsUpdate)
        {
            needsUpdate = false;
            SetupMaterial();
        }
    }

    void SetupMaterial()
    {
        float3 scatteringCoeficients = math.pow(400 / channelWavelengths, 4) * scatteringStrength;

        AtmosphereData atmosphereData = new AtmosphereData()
        {
            center = planetTransform.position,
            minRadius = minRadius,
            maxRadius = maxRadius,
            avgDensityHeightLocation = avgDensityHeightLocation,

            sunLightDir = sunLightTransform.forward,
            sunLightIntensity = sunLightIntensity,
            scatteringCoeficients = scatteringCoeficients,

            numInScatteringPoints = numInScatteringPoints,
            numOpticalDepthPoints = numOpticalDepthPoints,
        };

        atmosPassController.SetAtmosphereData(atmosphereId, atmosphereData);
    }

    void OnValidate()
    {
        needsUpdate = true;
    }
}
