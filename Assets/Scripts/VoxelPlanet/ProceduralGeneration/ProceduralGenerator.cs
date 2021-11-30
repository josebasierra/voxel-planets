using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;

public class ProceduralGenerator : MonoBehaviour
{
    [System.Serializable]
    public struct BiomeMaterialData
    {
        public float isovalue;
        public VoxelMat voxelMat;
    }

    struct BiomeMaterialJobData
    {
        public BiomeMaterialJobData(BiomeMaterialData biomeMaterialData)
        {
            isovalue = biomeMaterialData.isovalue;
            materialId = biomeMaterialData.voxelMat.GetId();
        }

        public float isovalue;
        public byte materialId;
    }

    public SignedDistanceField planetSurfaceSDF;
    public SignedDistanceField cavesSDF;
    public SignedDistanceField biomeMaterialsSDF;

    public float planetSurfaceIsovalue;
    public float cavesIsovalue;

    public List<BiomeMaterialData> biomeMaterialsData;

    NativeArray<BiomeMaterialJobData> biomeMaterialsJobData;

    public void Init()
    {
        biomeMaterialsJobData = new NativeArray<BiomeMaterialJobData>(biomeMaterialsData.Count, Allocator.Persistent);
        for (int i = 0; i < biomeMaterialsJobData.Length; i++)
        {
            biomeMaterialsJobData[i] = new BiomeMaterialJobData(biomeMaterialsData[i]);
        }
    }

    // TODO: Cache and reuse results
    public JobHandle GenerateVoxelData(float3 chunkOrigin, NativeArray3D<float3> positions, 
        NativeArray3D<byte> materials, NativeArray3D<CornerIntersections> intersections, JobHandle dependsOn)
    {
        NativeArray3D<float> planetSurfaceCachedSDF = new NativeArray3D<float>(positions.GetLength3D(), Allocator.Persistent);
        NativeArray3D<float> cavesCachedSDF = new NativeArray3D<float>(positions.GetLength3D(), Allocator.Persistent);
        NativeArray3D<float> biomeMaterialsCachedSDF = new NativeArray3D<float>(positions.GetLength3D(), Allocator.Persistent);

        var precomputeDistancesJob = new PrecomputeDistancesJob()
        {
            chunkOrigin = chunkOrigin,
            localPositions = positions,

            planetSurfaceSDF = planetSurfaceSDF,
            cavesSDF = cavesSDF,
            biomeMaterialsSDF = biomeMaterialsSDF,

            planetCachedSDF = planetSurfaceCachedSDF,
            cavesCachedSDF = cavesCachedSDF,
            biomeMaterialsCachedSDF = biomeMaterialsCachedSDF,
        };
        var distHandle = precomputeDistancesJob.Schedule(dependsOn);

        var generateProcDataJob = new GenerateProcDataJob()
        {
            planetSurfaceCachedSDF = planetSurfaceCachedSDF,
            cavesCachedSDF = cavesCachedSDF,
            biomeMaterialsCachedSDF = biomeMaterialsCachedSDF,

            planetSurfaceIsovalue = planetSurfaceIsovalue,
            cavesIsovalue = cavesIsovalue,
            biomeMaterialsData = biomeMaterialsJobData,

            materials = materials,
            intersections = intersections,
        };
        var procHandle = generateProcDataJob.Schedule(distHandle);

        return procHandle;
    }

    [BurstCompile(CompileSynchronously = true)]
    struct PrecomputeDistancesJob : IJob
    {
        public float3 chunkOrigin;
        [ReadOnly] public NativeArray3D<float3> localPositions;

        public SignedDistanceField planetSurfaceSDF;
        public SignedDistanceField cavesSDF;
        public SignedDistanceField biomeMaterialsSDF;

        public NativeArray3D<float> planetCachedSDF;
        public NativeArray3D<float> cavesCachedSDF;
        public NativeArray3D<float> biomeMaterialsCachedSDF;

        public void Execute()
        {
            for (int i = 0; i < localPositions.GetLength1D(); i++)
            {
                float3 position = chunkOrigin + localPositions[i];
                planetCachedSDF[i] = planetSurfaceSDF.GetDistance(position);
                //HALF PLANET: if (position.x > 0) planetCachedSDF[i] = 0; 
                cavesCachedSDF[i] = cavesSDF.GetDistance(position);
                biomeMaterialsCachedSDF[i] = biomeMaterialsSDF.GetDistance(position);
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct GenerateProcDataJob : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray3D<float> planetSurfaceCachedSDF;
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray3D<float> cavesCachedSDF;
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray3D<float> biomeMaterialsCachedSDF;
       
        public float planetSurfaceIsovalue;
        public float cavesIsovalue;
        [ReadOnly] public NativeArray<BiomeMaterialJobData> biomeMaterialsData;

        public NativeArray3D<byte> materials;
        public NativeArray3D<CornerIntersections> intersections;

        public void Execute()
        {
            // set materials
            for (int i = 0; i < materials.GetLength1D(); i++)
            {
                materials[i] = GetMaterial(i);
            }

            // set intersections
            int3 length3D = intersections.GetLength3D();
            for (int x = 0; x < length3D.x; x++)
            {
                for (int y = 0; y < length3D.y; y++)
                {
                    for (int z = 0; z < length3D.z; z++)
                    {
                        CornerIntersections cornerIntersections = new CornerIntersections();

                        if (x + 1 < length3D.x && IsActiveEdge(materials[x,y,z], materials[x+1, y, z]))
                        {
                            cornerIntersections.x = GetIntersectionValue(new int3(x,y,z), new int3(x+1, y, z));
                        }
                        if (y + 1 < length3D.y && IsActiveEdge(materials[x, y, z], materials[x, y+1, z]))
                        {
                            cornerIntersections.y = GetIntersectionValue(new int3(x, y, z), new int3(x, y+1, z));
                        }
                        if (z + 1 < length3D.z && IsActiveEdge(materials[x, y, z], materials[x, y, z+1]))
                        {
                            cornerIntersections.z = GetIntersectionValue(new int3(x, y, z), new int3(x, y, z+1));
                        }

                        intersections[x, y, z] = cornerIntersections;
                    }
                }
            }

            // materialId 255->0
            for (int i = 0; i < materials.GetLength1D(); i++)
            {
                if (materials[i] == 255) materials[i] = 0;
            }
        }

        public byte GetMaterial(int cornerId)
        {
            if (cavesCachedSDF[cornerId] < cavesIsovalue)
            {
                return 0; // cave empty material
            }
            else if (planetSurfaceCachedSDF[cornerId] < planetSurfaceIsovalue)
            {
                for (int i = biomeMaterialsData.Length - 1; i >= 0; i--)
                {
                    if (biomeMaterialsCachedSDF[cornerId] < biomeMaterialsData[i].isovalue)
                    {
                        return biomeMaterialsData[i].materialId; // solid materialId
                    }
                }
                return biomeMaterialsData[0].materialId;
            }
            else
            {
                return 255; // TODO: use other id temporarily to distinguish with cave empty
            }
        }

        // Pre: materials[cornerId1] != materials[cornerId2] && cornerId1 < cornerId2
        public byte GetIntersectionValue(int3 cornerId1, int3 cornerId2)
        {
            byte materialId1 = materials[cornerId1];
            byte materialId2 = materials[cornerId2];

            float d1, d2;
            if (materialId1 == 0 || materialId2 == 0) // use cavesSDF
            {
                d1 = cavesCachedSDF[cornerId1] - cavesIsovalue;
                d2 = cavesCachedSDF[cornerId2] - cavesIsovalue;
            }
            else if (materialId1 == 255 || materialId2 == 255)   //use planetSurfaceSDF
            {
                d1 = planetSurfaceCachedSDF[cornerId1] - planetSurfaceIsovalue;
                d2 = planetSurfaceCachedSDF[cornerId2] - planetSurfaceIsovalue;
            }
            else  //use biomeSDF
            {
                float isovalue = biomeMaterialsData[GetBiomeId(materialId1)].isovalue;
                d1 = biomeMaterialsCachedSDF[cornerId1] - isovalue;
                d2 = biomeMaterialsCachedSDF[cornerId2] - isovalue;
            }

            byte intersectionValue = (byte)(math.abs(d1) / math.abs(d2 - d1) * 255);
            return intersectionValue;
        }

        public bool IsActiveEdge(byte materialId1, byte materialId2)
        {
            if (materialId1 == 255) materialId1 = 0;
            if (materialId2 == 255) materialId2 = 0;

            return ((materialId1 == 0 && materialId2 != 0) || (materialId1 != 0 && materialId2 == 0));
        }

        public int GetBiomeId(byte materialId) 
        { 
            for (int i = 0; i < biomeMaterialsData.Length; i++)
            {
                if (biomeMaterialsData[i].materialId == materialId) return i;
            }
            return -1;
        }
    }

    void OnDestroy()
    {
        biomeMaterialsJobData.Dispose();
    }
}
