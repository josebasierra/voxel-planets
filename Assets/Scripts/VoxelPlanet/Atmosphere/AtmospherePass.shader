Shader "FullScreen/AtmospherePass"
{
    Properties
    {
        // This property is necessary to make the CommandBuffer.Blit bind the source texture to _MainTex
        //_ExampleName("Example vector", Vector) = (.25, .5, .5, 1)
    }

    HLSLINCLUDE

#pragma vertex Vert

#pragma target 4.5
#pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    // The PositionInputs struct allow you to retrieve a lot of useful information for your fullScreenShader:
    // struct PositionInputs
    // {
    //     float3 positionWS;  // World space position (could be camera-relative)
    //     float2 positionNDC; // Normalized screen coordinates within the viewport    : [0, 1) (with the half-pixel offset)
    //     uint2  positionSS;  // Screen space pixel coordinates                       : [0, NumPixels)
    //     uint2  tileCoord;   // Screen tile coordinates                              : [0, NumTiles)
    //     float  deviceDepth; // Depth from the depth buffer                          : [0, 1] (typically reversed)
    //     float  linearDepth; // View space Z coordinate                              : [Near, Far]
    // };

    // To sample custom buffers, you have access to these functions:
    // But be careful, on most platforms you can't sample to the bound color buffer. It means that you
    // can't use the SampleCustomColor when the pass color buffer is set to custom (and same for camera the buffer).
    // float4 SampleCustomColor(float2 uv);
    // float4 LoadCustomColor(uint2 pixelCoords);
    // float LoadCustomDepth(uint2 pixelCoords);
    // float SampleCustomDepth(float2 uv);

    // There are also a lot of utility function you can use inside Common.hlsl and Color.hlsl,
    // you can check them out in the source code of the core SRP package.

    //-------------------------------------------------------------------------------------------

    // main references:
    // GPU GEMS 2 Chapter 16: https://developer.nvidia.com/gpugems/gpugems2/part-ii-shading-lighting-and-shadows/chapter-16-accurate-atmospheric-scattering
    // sebastian video: https://www.youtube.com/watch?v=DxfEbulyFcY&ab_channel=SebastianLague

    struct Atmosphere {
        float3 center;
        float minRadius;
        float maxRadius;
        float avgDensityHeightLocation;

        float3 sunLightDir;
        float3 sunLightIntensity;
        float3 scatteringCoeficients;

        int numInScatteringPoints;
        int numOpticalDepthPoints;
    };

    StructuredBuffer<Atmosphere> atmospheres;
    int atmospheresCount;

    // https://viclw17.github.io/2018/07/16/raytracing-ray-sphere-intersection/
    // returns (distanceToSphere, distanceThroughSphere)
    float2 raySphere(float3 sphereCentre, float sphereRadius, float3 rayOrigin, float3 rayDir) {
        float3 offset = rayOrigin - sphereCentre;
        float a = 1; // dot(rayDir, rayDir) if rayDir not normalized
        float b = 2 * dot(offset, rayDir);
        float c = dot(offset, offset) - sphereRadius * sphereRadius;
        float discriminant = b * b - 4 * a * c; // Discriminant from quadratic formula

        // 2 intersection case
        if (discriminant > 0) {
            float rootValue = sqrt(discriminant);
            float distanceToPoint1 = max(0, (-b - rootValue) / (2 * a));
            float distanceToPoint2 = (-b + rootValue) / (2 * a);

            // Ignore intersections that occur behind the ray
            if (distanceToPoint2 >= 0) {
                return float2(distanceToPoint1, distanceToPoint2 - distanceToPoint1);
            }
        }
        
        // No intersection with sphere (or just 1 point, ray 'touching' sphere)
        return float2(3.402823466e+38, 0);
    }

    //returns height of point above surface, scaled between 0-1
    float scaledHeight(float3 p, in Atmosphere atmo) {
        return (length(p - atmo.center) - atmo.minRadius) / (atmo.maxRadius - atmo.minRadius);  
    }

    float density(float3 p, in Atmosphere atmo) {
        float h = scaledHeight(p, atmo);
        return exp(-h / atmo.avgDensityHeightLocation);
    }

    // average atmospheric density across the ray
    float opticalDepth(float3 rayOrigin, float3 rayDir, float rayLength, in Atmosphere atmo) {
        float3 currentPoint = rayOrigin;
        float stepSize = rayLength / (atmo.numOpticalDepthPoints - 1);
        float opticalDepth = 0;

        for (int i = 0; i < atmo.numOpticalDepthPoints; i++) {
            opticalDepth += density(currentPoint, atmo) * stepSize;
            currentPoint += rayDir * stepSize;
        }
        return opticalDepth;
    }

    float3 outScattering(float3 rayOrig, float3 rayDir, float rayLength, in Atmosphere atmo) {
        return  4*PI * atmo.scatteringCoeficients * opticalDepth(rayOrig, rayDir, rayLength, atmo);
    }

    // rayleigh approximation by setting 'g' to 0
    float rayleighPhase(float cos) {
        return 0.75 * (1 + cos * cos);
    }

    float3 inScattering(float3 rayOrigin, float3 rayDir, float rayLength, in Atmosphere atmo) {
        float3 currentPoint = rayOrigin;
        float stepSize = rayLength / (atmo.numInScatteringPoints - 1);
        float3 inScatteredLight = float3(0,0,0);
 
        for (int i = 0; i < atmo.numInScatteringPoints; i++) {
            float sunRayLength = raySphere(atmo.center, atmo.maxRadius, currentPoint, -atmo.sunLightDir).y;

            float3 transmittance = exp(-(outScattering(currentPoint, -atmo.sunLightDir, sunRayLength, atmo) + outScattering(currentPoint, -rayDir, stepSize * i, atmo)));
            inScatteredLight += density(currentPoint, atmo) * transmittance * stepSize;
            currentPoint += rayDir * stepSize;
        }
        float cos = dot(rayDir, atmo.sunLightDir);
        return  atmo.sunLightIntensity *rayleighPhase(cos)* atmo.scatteringCoeficients * inScatteredLight;
    }


    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float3 viewDirection = GetWorldSpaceNormalizeViewDir(posInput.positionWS);
        float4 color = float4(0.0, 0.0, 0.0, 0.0);

        // Load the camera color buffer at the mip 0 if we're not at the before rendering injection point
        if (_CustomPassInjectionPoint != CUSTOMPASSINJECTIONPOINT_BEFORE_RENDERING)
            color = float4(CustomPassLoadCameraColor(varyings.positionCS.xy, 0), 1);

        // Add your custom pass code here

        for (int i = 0; i < atmospheresCount; i++) {
            float3 rayOrigin = _WorldSpaceCameraPos;
            float3 rayDir = normalize(posInput.positionWS);

            float2 hitInfo = raySphere(atmospheres[i].center, atmospheres[i].maxRadius, rayOrigin, rayDir);

            float dstToAtmosphere = hitInfo.x;
            float dstToSurface = length(posInput.positionWS);
            float dstThroughAtmosphere = min(hitInfo.y, dstToSurface - dstToAtmosphere);

            if (dstThroughAtmosphere > 0) {
                float3 pointInAtmosphere = rayOrigin + rayDir * dstToAtmosphere;
                float3 atmosphereContribution = inScattering(pointInAtmosphere, rayDir, dstThroughAtmosphere, atmospheres[i]);
                color = color * (1 - float4(atmosphereContribution, 1)) + float4(atmosphereContribution, 1);
            }
        }
        return color;
    }

    ENDHLSL

    SubShader
    {
        Pass
        {
            Name "Custom Pass 0"

            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
                #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}
