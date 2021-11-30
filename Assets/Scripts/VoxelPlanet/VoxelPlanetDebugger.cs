using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static GeometryUtility;

public class VoxelPlanetDebugger : VoxelPlanet
{
    [Header("Debug settings")]

    [SerializeField] bool drawSeamVoxels = false;
    [SerializeField] int3 seamAxis = new int3(1, 0, 0);
    [SerializeField] bool pauseOnModification = false;
    [SerializeField] bool pauseOnUpdate = false;

    List<DrawTask> drawTasks;

    struct DrawTask
    {
        public ABB bbox;
        public Color color;

        public DrawTask(ABB bbox, Color color)
        {
            this.bbox = bbox;
            this.color = color;
        }
    }

    protected override void Start()
    {
        base.Start();

        drawTasks = new List<DrawTask>();
    }

    protected override void Update()
    {
        drawTasks.Clear();
        base.Update();

        if (pauseOnUpdate) Debug.Break();
    }

    public override void ApplyModification(ModificationData modification)
    {
        base.ApplyModification(modification);
        
        if (pauseOnModification) Debug.Break();
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        for (int i = 0; i < drawTasks.Count; i++)
        {
            var drawTask = drawTasks[i];
            Gizmos.color = drawTask.color;
            Gizmos.DrawCube(drawTask.bbox.min + (drawTask.bbox.max - drawTask.bbox.min) / 2f, drawTask.bbox.GetSize());

            if (i > 2046) break;
        }
    }

    protected override JobHandle ComputeVoxelGrid(ABB chunkBox, float voxelSize, float3 offsetFromMainChunk, out VoxelGrid voxelGrid, JobHandle dependsOn = default)
    {
        JobHandle handle = base.ComputeVoxelGrid(chunkBox, voxelSize, offsetFromMainChunk, out voxelGrid, dependsOn);
        if (!drawSeamVoxels) return handle;

        handle.Complete();

        ABB drawArea = chunkBox;

        float3 mainChunkGlobalCenter = drawArea.GetCenter() - offsetFromMainChunk;

        int3 length3D = voxelGrid.positions.GetLength3D();
        if (drawSeamVoxels && (length3D.x == 2 || length3D.y == 2 || length3D.z == 2) && (length3D * (new float3(1,1,1) - this.seamAxis) + this.seamAxis*2).Equals(length3D))
        {
            for (int i = 0; i < voxelGrid.positions.GetLength1D(); i++)
            {
                drawTasks.Add(
                    new DrawTask(
                        new ABB(
                            mainChunkGlobalCenter + voxelGrid.positions[i] - voxelSize/2f, 
                            mainChunkGlobalCenter + voxelGrid.positions[i] + voxelSize/2f), 
                        new Color(0, 0, 1, 0.4f)));
            }
        }

        return default;
    }

    protected override JobHandle ComputeVoxelLocalPositions(float3 offset, float voxelSize, NativeArray3D<float3> positions, JobHandle dependsOn = default)
    {
        return base.ComputeVoxelLocalPositions(offset, voxelSize, positions, dependsOn);
    }
}
