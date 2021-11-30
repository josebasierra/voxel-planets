using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using static GeometryUtility;

public class VoxelPlanet : MonoBehaviour
{
    public static List<VoxelPlanet> voxelPlanets = new List<VoxelPlanet>();

    [SerializeField] Transform playerTransform;

    [SerializeField] protected int maxWorldSize = 4096;
    [SerializeField] protected int chunkResolution = 32;
    [SerializeField, Range(0,15)] protected byte maxDepth = 12;

    ModificationsController modController; // just a wrapper for the ModOctree
    ProceduralGenerator procGenerator;
    MeshGenerator meshGenerator;
    LodOctree lodOctree;

    Queue<ModificationData> modifications;
    Queue<ABB> chunksToUpdate;
    Queue<MeshTask> meshTasks;

    int taskManagementState = 0;

    protected virtual void Start()
    {
        voxelPlanets.Add(this);

        Debug.Assert(Utility.IsPowerOfTwo(chunkResolution));
        Debug.Assert(Utility.IsPowerOfTwo(maxWorldSize));

        lodOctree = GetComponent<LodOctree>();
        int chunkDepth = (int) math.log2(chunkResolution);
        int maxMeshOctreeDepth = maxDepth - chunkDepth;
        lodOctree.Init(maxWorldSize, (byte)maxMeshOctreeDepth, (byte)chunkDepth);

        modController = GetComponent<ModificationsController>();
        modController.Init(maxWorldSize, maxDepth);

        procGenerator = GetComponent<ProceduralGenerator>();
        procGenerator.Init();

        meshGenerator = GetComponent<MeshGenerator>();

        modifications = new Queue<ModificationData>(5);
        chunksToUpdate = new Queue<ABB>(1000);
        meshTasks = new Queue<MeshTask>(1000);
    }

    struct MeshTask
    {
        public JobHandle jobHandle;
        public ABB chunkBox;

        // mesh data output from task:
        public Mesh.MeshDataArray meshDataArray;
        public NativeList<byte> voxelMatIdPerSubmesh;
        public NativeReference<ABB> meshBoundsRef;
    }

    protected virtual void Update()
    {
        Profiler.BeginSample("Mesh Generation Tasks");

        if (taskManagementState == 0)   // OBTAIN CHUNKS REQUIRING MESH GENERATION
        {
            Vector3 playerPositionLocalToPlanet = transform.InverseTransformPoint(playerTransform.position);
            lodOctree.SetLODCenter(playerPositionLocalToPlanet);

            while (modifications.Count > 0)
            {
                var modification = modifications.Dequeue();
                modController.ApplyModification(modification).Complete();
                lodOctree.SetIntersectedLeafsToDirty(modification.GetABB());
            }

            lodOctree.GetDirtyChunksAndCleanDirtyFlag(chunksToUpdate);

            if (chunksToUpdate.Count > 0)
            {
                Debug.Log(gameObject.name + " mesh jobs: " + chunksToUpdate.Count);
                Debug.Assert(chunksToUpdate.Count < 3000);

                taskManagementState = 1;
            }
        }
        else if (taskManagementState == 1)   // CREATE MESH TASKS
        {
            const int taskCountPerFrame = 12;  // number set by trial and error, observing performance in profiler
            int taskCount = 0;

            while (chunksToUpdate.Count > 0 && taskCount < taskCountPerFrame)
            {
                // create task
                MeshTask meshTask = new MeshTask();
                meshTask.chunkBox = chunksToUpdate.Dequeue();

                meshTask.jobHandle = GenerateMesh(meshTask.chunkBox, out meshTask.meshDataArray, out meshTask.voxelMatIdPerSubmesh, out meshTask.meshBoundsRef);

                meshTasks.Enqueue(meshTask);

                taskCount++;
            }
            JobHandle.ScheduleBatchedJobs();

            if (chunksToUpdate.Count == 0)
            {
                taskManagementState = 2;
            }
        }
        
        else if (taskManagementState == 2)  // FINISH MESH TASKS
        {
            const int taskCountPerFrame = 50;
            int taskCount = 0;

            while (meshTasks.Count > 0 && taskCount < taskCountPerFrame && meshTasks.Peek().jobHandle.IsCompleted)
            {
                MeshTask meshTask = meshTasks.Dequeue();

                meshTask.jobHandle.Complete();

                // SET MESH AND DISPOSE MeshDataArray
                lodOctree.SetAndDisposeMeshData(meshTask.chunkBox, meshTask.meshDataArray, meshTask.voxelMatIdPerSubmesh, meshTask.meshBoundsRef);

                meshTask.voxelMatIdPerSubmesh.Dispose();
                meshTask.meshBoundsRef.Dispose();

                taskCount++;
            }

            if (meshTasks.Count == 0)
            {
                taskManagementState = 3;
            }
        }
        else if (taskManagementState == 3)
        {
            lodOctree.MakeMeshTransition();
            taskManagementState = 0;
        }

        Profiler.EndSample();
    }

    public static VoxelPlanet GetNearestPlanet(Vector3 point)
    {
        VoxelPlanet nearestPlanet = null;
        float nearestDistance = float.MaxValue;

        foreach (var voxelPlanet in voxelPlanets)
        {
            float distance = Vector3.Distance(voxelPlanet.transform.position, point);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPlanet = voxelPlanet;
            }
        }

        return nearestPlanet;
    }

    public int GetMaxWorldSize() => maxWorldSize;

    public bool IsDoingTasks() => taskManagementState != 0;

    public virtual void ApplyModification(ModificationData modification)
    {
        modifications.Enqueue(modification);
    }

    JobHandle GenerateMesh(ABB chunkBox, out Mesh.MeshDataArray meshDataArray, out NativeList<byte> voxelMatIdPerSubmesh, out NativeReference<ABB> meshBoundsRef)
    {
        // MAIN CHUNK
        float chunkSize = chunkBox.GetSize().x;
        float voxelSize = chunkSize / chunkResolution;

        JobHandle mainChunkHandle = ComputeVoxelGrid(chunkBox, voxelSize, float3.zero, out VoxelGrid mainChunkVoxelGrid);

        Profiler.BeginSample("Get seams");

        // SEAMS
        int3 seamLocation;

        // SEAM X
        seamLocation = new int3(1, 0, 0);
        JobHandle seamXHandle = ComputeSeamVoxelGrid(chunkBox, seamLocation, out VoxelGrid seamX_voxelGrid);

        // SEAM Y
        seamLocation = new int3(0, 1, 0);
        JobHandle seamYHandle = ComputeSeamVoxelGrid(chunkBox, seamLocation, out VoxelGrid seamY_voxelGrid);

        // SEAM Z
        seamLocation = new int3(0, 0, 1);
        JobHandle seamZHandle = ComputeSeamVoxelGrid(chunkBox, seamLocation, out VoxelGrid seamZ_voxelGrid);

        // SEAM XY
        seamLocation = new int3(1, 1, 0);
        JobHandle seamXYHandle = ComputeSeamVoxelGrid(chunkBox, seamLocation, out VoxelGrid seamXY_voxelGrid);

        // SEAM YZ
        seamLocation = new int3(0, 1, 1);
        JobHandle seamYZHandle = ComputeSeamVoxelGrid(chunkBox, seamLocation, out VoxelGrid seamYZ_voxelGrid);

        // SEAM XZ
        seamLocation = new int3(1, 0, 1);
        JobHandle seamXZHandle = ComputeSeamVoxelGrid(chunkBox, seamLocation, out VoxelGrid seamXZ_voxelGrid);

        Profiler.EndSample();

        // GENERATE MESH FROM DATA
        Profiler.BeginSample("Mesh allocations");
        meshDataArray = Mesh.AllocateWritableMeshData(1);
        Profiler.EndSample();

        voxelMatIdPerSubmesh = new NativeList<byte>(Allocator.Persistent);

        var handle1 = JobHandle.CombineDependencies(seamXHandle, seamYHandle, seamZHandle);
        var handle2 = JobHandle.CombineDependencies(seamXYHandle, seamYZHandle, seamXZHandle);
        var voxelGridHandle = JobHandle.CombineDependencies(handle1, handle2, mainChunkHandle);

        Profiler.BeginSample("Mesh job schedule");
        JobHandle meshGenHandle = meshGenerator.GenerateSeamlessMesh(mainChunkVoxelGrid, seamX_voxelGrid, seamY_voxelGrid, seamZ_voxelGrid,
            seamXY_voxelGrid, seamYZ_voxelGrid, seamXZ_voxelGrid, meshDataArray[0], voxelMatIdPerSubmesh, out meshBoundsRef, voxelGridHandle);
        Profiler.EndSample();

        return meshGenHandle;
    }

    JobHandle ComputeSeamVoxelGrid(ABB chunkBox, int3 seamLocation, out VoxelGrid voxelGrid, JobHandle dependsOn = default)
    {
        float chunkSize = chunkBox.GetSize().x;

        lodOctree.TryGetSeamBox(chunkBox, seamLocation, out ABB seamBox);  //TODO: Unnecessary work if there's no seam

        float3 seamSize = seamBox.GetSize();
        float voxelSize = math.min(seamSize.x, math.min(seamSize.y, seamSize.z));
        float3 offset = seamLocation * new float3(chunkSize / 2f) + seamLocation * new float3(voxelSize / 2f);

        return ComputeVoxelGrid(seamBox, voxelSize, offset, out voxelGrid, dependsOn);
    }

    protected virtual JobHandle ComputeVoxelGrid(ABB drawBox, float targetVoxelSize, float3 offsetFromMainChunk, out VoxelGrid voxelGrid, 
        JobHandle dependsOn = default)
    {
        Profiler.BeginSample("ComputeVoxelGrid");

        int3 arrayLength3D = (int3)(math.ceil(drawBox.max - drawBox.min) / targetVoxelSize + 1);

        voxelGrid.positions = new NativeArray3D<float3>(arrayLength3D, Allocator.Persistent);
        voxelGrid.materials = new NativeArray3D<byte>(arrayLength3D, Allocator.Persistent);
        voxelGrid.intersections = new NativeArray3D<CornerIntersections>(arrayLength3D, Allocator.Persistent);

        // COMPUTE VOXEL LOCAL CORNER POSITIONS (local to chunk center)
        JobHandle posHandle = ComputeVoxelLocalPositions(offsetFromMainChunk, targetVoxelSize, voxelGrid.positions, dependsOn);

        // OBTAIN PROCEDURAL DATA
        NativeArray3D<byte> procMaterials = new NativeArray3D<byte>(voxelGrid.positions.GetLength3D(), Allocator.Persistent);
        NativeArray3D<CornerIntersections> procIntersections = new NativeArray3D<CornerIntersections>(voxelGrid.positions.GetLength3D(), Allocator.Persistent);

        var procHandle = procGenerator.GenerateVoxelData(drawBox.GetCenter() - offsetFromMainChunk, voxelGrid.positions, procMaterials, procIntersections, posHandle);

        // OBTAIN MODIFICATION DATA FROM OCTREE
        var modHandle = modController.GetVoxelGrid(drawBox, targetVoxelSize, voxelGrid.materials, voxelGrid.intersections, dependsOn);

        // MERGE PLAYER & PROCEDURAL DATA
        JobHandle mergeDependency = JobHandle.CombineDependencies(procHandle, modHandle);

        JobHandle mergeHandle = MergeProceduralAndModificationData(procMaterials, procIntersections, voxelGrid.materials, voxelGrid.intersections, mergeDependency);

        Profiler.EndSample();

        return mergeHandle;
    }

    protected virtual JobHandle ComputeVoxelLocalPositions(float3 offset, float voxelSize, NativeArray3D<float3> positions, JobHandle dependsOn = default)
    {
        var job = new ComputeVoxelLocalPositionsJob()
        {
            offset = offset,
            voxelSize = voxelSize,
            positions = positions,
        };
        return job.Schedule(dependsOn);
    }

    JobHandle MergeProceduralAndModificationData(NativeArray3D<byte> procMaterials, NativeArray3D<CornerIntersections> procIntersections, 
        NativeArray3D<byte> modMaterials, NativeArray3D<CornerIntersections> modIntersections, JobHandle dependsOn = default)
    {
        var job = new MergeProceduralAndModificationDataJob()
        {
            procMaterials = procMaterials,
            procIntersections = procIntersections,
            materials = modMaterials,
            intersections = modIntersections,
        };
        return job.Schedule(dependsOn);
    }

    [BurstCompile(CompileSynchronously = true)]
    struct ComputeVoxelLocalPositionsJob : IJob
    {
        public float3 offset;
        public float voxelSize;
       
        [WriteOnly] public NativeArray3D<float3> positions;

        public void Execute()
        {
            float3 chunkSize = (float3)(positions.GetLength3D() ) * voxelSize;

            for (int x = 0; x < positions.GetLengthX() ; x++)
            {
                for (int y = 0; y < positions.GetLengthY(); y++)
                {
                    for (int z = 0; z < positions.GetLengthZ(); z++)
                    {
                        float3 voxelPosition = offset + new float3(x,y,z) * voxelSize - chunkSize / 2f + voxelSize / 2f;
                        positions[x,y,z] = voxelPosition;
                    }
                }
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct MergeProceduralAndModificationDataJob : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray3D<byte> procMaterials;
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray3D<CornerIntersections> procIntersections;
        public NativeArray3D<byte> materials;
        public NativeArray3D<CornerIntersections> intersections;

        public void Execute2()
        {
            for (int i = 0; i < materials.GetLength1D(); i++)
            {
                if (materials[i] == 255) materials[i] = 0;
            }
        }

        public void Execute()
        {
            int3 length3D = intersections.GetLength3D();
            for (int x = 0; x < length3D.x; x++)
            {
                for (int y = 0; y < length3D.y; y++)
                {
                    for (int z = 0; z < length3D.z; z++)
                    {
                        CornerIntersections cornerIntersections = intersections[x, y, z];

                        if (materials[x,y,z] == 255) 
                        {
                            materials[x, y, z] = procMaterials[x, y, z];

                            if (x+1 < length3D.x && materials[x+1,y,z] == 255)
                            {
                                cornerIntersections.x = procIntersections[x, y, z].x;
                            }

                            if (y+1 < length3D.y && materials[x, y+1, z] == 255)
                            {
                                cornerIntersections.y = procIntersections[x, y, z].y;
                            }

                            if (z+1 < length3D.z && materials[x, y, z+1] == 255)
                            {
                                cornerIntersections.z = procIntersections[x, y, z].z;
                            }
                        }

                        intersections[x, y, z] = cornerIntersections;
                    }
                }
            }
        }
    }
}
