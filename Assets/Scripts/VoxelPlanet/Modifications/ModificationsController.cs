using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using static GeometryUtility;

public class ModificationsController : MonoBehaviour
{
    ModOctree modOctree;

    public void Init(float maxWorldSize, byte maxDepth)
    {
        modOctree = new ModOctree(maxWorldSize, maxDepth);
    }

    public JobHandle ApplyModification(ModificationData modification, JobHandle dependsOn = default)
    {
        var job = new ApplyModificationJob()
        {
            modOctree = modOctree,
            modification = modification,
        };

        var handle = job.Schedule(dependsOn);
        return handle;
    }

    public JobHandle GetVoxelGrid(ABB drawBox, float targetVoxelSize, NativeArray3D<byte> materials, NativeArray3D<CornerIntersections> intersections, 
        JobHandle dependsOn = default)
    {
        var job = new GetVoxelGridJob()
        {
            modOctree = modOctree,
            drawBox = drawBox,
            targetVoxelSize = targetVoxelSize,

            materials = materials,
            intersections = intersections,
        };

        var handle = job.Schedule(dependsOn);
        return handle;
    }

    void OnDestroy()
    {
        modOctree.Dispose();
    }

    [BurstCompile(CompileSynchronously = true)]
    struct ApplyModificationJob : IJob
    {
        public ModOctree modOctree;
        public ModificationData modification;

        public void Execute()
        {
            modOctree.ApplyModification(modification);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct GetVoxelGridJob : IJob
    {
        [ReadOnly] public ModOctree modOctree;
        public ABB drawBox;
        public float targetVoxelSize;

        public NativeArray3D<byte> materials;
        public NativeArray3D<CornerIntersections> intersections;

        public void Execute()
        {
            modOctree.GetVoxelGrid(drawBox, targetVoxelSize, materials, intersections);
        }
    }

    #region Debug
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        //if (drawOctree)
        //{
        //    voxelModificationsOctree.Draw(drawHt, drawHm, drawInterior);
        //}
    }
    #endregion
}
