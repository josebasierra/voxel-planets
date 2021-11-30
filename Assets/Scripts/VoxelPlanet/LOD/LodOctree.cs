using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using static GeometryUtility;

 
public class LodOctree : MonoBehaviour
{
    static readonly float3[] NODE_OFFSETS =
    {
         new float3(-0.5f, -0.5f, -0.5f),
         new float3(-0.5f, -0.5f, 0.5f),
         new float3(-0.5f, 0.5f, -0.5f),
         new float3(-0.5f, 0.5f, 0.5f),
         new float3(0.5f, -0.5f, -0.5f),
         new float3(0.5f, -0.5f, 0.5f),
         new float3(0.5f, 0.5f, -0.5f),
         new float3(0.5f, 0.5f, 0.5f)
    };

    class MeshNode
    {
        public byte depth;
        public float3 position;
        public MeshNode[] childs;

        //TODO: Add another flag for modified chunks by player
        public bool isRecentLeaf;
        public bool isDirty;
        public bool isLeaf;

        public MeshChunk meshChunk; 

        public MeshNode(byte depth, float3 position)
        {
            this.depth = depth;
            this.position = position;
            childs = new MeshNode[8];

            isRecentLeaf = false;
            isDirty = false;     // indicates if node or some child of it needs mesh update
            isLeaf = false;     // indicates if node is final (its mesh needs to be active/rendered)

            meshChunk = null;   // reference to component responsible of gameobject with mesh
        }
    }

    // TODO: Transition between more than 1 LOD difference not supported
    // (right now a SEAM is assumed to consist of voxels of the same size) (that's why LODfactor must be at least 1)
    [SerializeField, Range(1f, 3)] float LODFactor = 1f;  
    [SerializeField] bool createCollisionMeshes = false;
    [SerializeField] MeshChunkPool meshChunkPool;

    MeshNode rootNode;
    float maxWorldSize;
    float voxelSize;

    byte maxDepth; // max depth (in chunks)
    byte chunkDepth; // chunkDepth 5 equals chunkResolution 32*32*32

    // to process on mesh transition:
    List<MeshChunk> meshChunksRequiringDestruction = new List<MeshChunk>();
    List<MeshChunk> meshChunksRequiringActivation = new List<MeshChunk>();
    List<MeshChunk> meshChunksRequiringCollisions = new List<MeshChunk>();

    public void Init(float maxWorldSize, byte maxDepth, byte chunkDepth)
    {
        rootNode = new MeshNode(0, float3.zero);
        this.maxWorldSize = maxWorldSize;

        this.maxDepth = maxDepth;
        this.chunkDepth = chunkDepth;

        voxelSize = maxWorldSize / math.exp2(maxDepth + chunkDepth);
    }

    // 'chunkBox' is the bounding box of the first deepest meshNode in contact with 'intersectingBox' 
    public bool TryGetChunkBox(ABB intersectingBox, out ABB chunkBox)
    {
        chunkBox = new ABB();
        return TryGetChunkBox(rootNode, intersectingBox, ref chunkBox);
    }

    // seamBox will contain the seam between the given chunk and the neighbor indicated by seamLocation (e.g (1,0,0) indicates neighbor at +x coordinates)
    public bool TryGetSeamBox(ABB chunkBox, int3 seamLocation, out ABB seamBox)
    {
        Profiler.BeginSample("TryGetSeamBox");

        float chunkSize = chunkBox.GetSize().x;

        float3 min = chunkBox.min + new float3(chunkSize) * seamLocation;
        float3 max = min + 0.1f;

        if (!TryGetChunkBox(new ABB(min, max), out ABB neighborBox))
        {
            // TODO: Return empty ABB ? (Then adapt GetDrawData and MeshGeneration)
            float voxelSize = chunkSize/ math.exp2(chunkDepth);
            seamBox = new ABB(float3.zero, new float3(voxelSize));

            Profiler.EndSample();

            return false;
        }

        float neighborSize = neighborBox.GetSize().x;
        float neighborVoxelSize = neighborSize / math.exp2(chunkDepth);
        float3 offset = new float3(neighborVoxelSize) * seamLocation + new float3(chunkSize) * (new float3(1, 1, 1) - seamLocation);

        seamBox = new ABB(min, min + offset);

        Profiler.EndSample();

        return true;
    }

    public void SetIntersectedLeafsToDirty(ABB intersectingBox)
    {
        // adjust bounding box to affect neighbors chunks affected by modification
        intersectingBox.min -= voxelSize;

        SetIntersectedLeafsToDirty(rootNode, intersectingBox);
    }

    // Pre: lod center position is local to planet
    public void SetLODCenter(float3 center)
    {
        float k = 0.8f;
        if (math.length(center) >  k*maxWorldSize) // stop reducing detail when at certain distance
        {
            center = math.normalize(center) * k*maxWorldSize;
        }

        SetLODCenter(rootNode, center);
        SetRecentLeafsToDirty(rootNode);
    }

    public void GetDirtyChunksAndCleanDirtyFlag(Queue<ABB> dirtyChunks)
    {
        GetDirtyChunksAndCleanDirtyFlag(rootNode, dirtyChunks);
    }

    public void SetAndDisposeMeshData(ABB chunkBox, Mesh.MeshDataArray meshDataArray, NativeList<byte> voxelMatIdPerSubmesh, NativeReference<ABB> meshBoundsRef)
    {
        // Remove all meshes on the way to node, and child meshes of the node

        Profiler.BeginSample("SetMeshData");

        SetAndDisposeMeshData(rootNode, chunkBox, meshDataArray, voxelMatIdPerSubmesh, meshBoundsRef);

        Profiler.EndSample();
    }

    public void MakeMeshTransition()
    {
        Profiler.BeginSample("MakeMeshTransition");

        foreach (var meshChunk in meshChunksRequiringDestruction)
        {
            meshChunkPool.Destroy(meshChunk);
        }

        foreach (var meshChunk in meshChunksRequiringActivation)
        {
            meshChunk.Enable();
        }

        meshChunksRequiringDestruction.Clear();
        meshChunksRequiringActivation.Clear();

        PrepareCollisionMeshes();

        Profiler.EndSample();
    }

    void SetIntersectedLeafsToDirty(MeshNode meshNode, ABB intersectingBox)
    {
        float meshNodeSize = maxWorldSize / math.exp2(meshNode.depth);

        if (Intersects(intersectingBox, GetABB(meshNode.position, meshNodeSize)))
        {
            if (meshNode.isLeaf)
            {
                meshNode.isDirty = true;
                return;
            }

            foreach (var child in meshNode.childs)
            {
                if (child != null) SetIntersectedLeafsToDirty(child, intersectingBox);
            }
        }
    }

    bool TryGetChunkBox(MeshNode meshNode, ABB intersectingBox, ref ABB chunkBox)
    {
        float meshNodeSize = maxWorldSize / math.exp2(meshNode.depth);

        if (Intersects(intersectingBox, GetABB(meshNode.position, meshNodeSize)))
        {
            if (meshNode.isLeaf)
            {
                chunkBox = GetABB(meshNode.position, meshNodeSize);
                return true;
            }

            foreach (var child in meshNode.childs)
            {
                if (child != null && TryGetChunkBox(child, intersectingBox, ref chunkBox))
                {
                    return true;
                }
            }
        }
        return false;
    }

    //TODO: Do not affect neighbor -xyz
    void SetAffectedNeighborsToDirty(MeshNode meshNode)
    {
        float meshNodeSize = maxWorldSize / math.exp2(meshNode.depth);
        SetIntersectedLeafsToDirty(new ABB(meshNode.position - (meshNodeSize / 2f + 0.1f), meshNode.position + meshNodeSize / 2f));

        //float offset = (meshNodeSize / 2f + 0.1f);
        //float3 offset1 = new float3(-offset, -0.1f, -offset);
        //float3 offset2 = new float3(-0.1f, -offset, -offset);
        //float3 offset3 = new float3(-offset, -offset, -0.1f);

        //SetIntersectedLeafsToDirty(new ABB(meshNode.position + offset1, meshNode.position + meshNodeSize / 2f));
        //SetIntersectedLeafsToDirty(new ABB(meshNode.position + offset2, meshNode.position + meshNodeSize / 2f));
        //SetIntersectedLeafsToDirty(new ABB(meshNode.position + offset3, meshNode.position + meshNodeSize / 2f));

    }

    void SetLODCenter(MeshNode meshNode, float3 lodCenter)
    {
        float meshNodeSize = maxWorldSize / math.exp2(meshNode.depth);

        if (meshNode.depth == maxDepth)
        {
            if (!meshNode.isLeaf)
            {
                meshNode.isLeaf = true;
                meshNode.isRecentLeaf = true;
            }

        }
        else if (!IsWithinReach(lodCenter, meshNode))
        {
            if (!meshNode.isLeaf)
            {
                meshNode.isLeaf = true;
                meshNode.isRecentLeaf = true;
            }
        }
        else
        {
            meshNode.isLeaf = false;

            // expand tree and recurse
            for (int i = 0; i < 8; i++)
            {
                if (meshNode.childs[i] == null)
                {
                    meshNode.childs[i] = new MeshNode((byte)(meshNode.depth + 1), GetChildCenter(meshNode.position, meshNodeSize * 0.5f, i));
                }
                
                SetLODCenter(meshNode.childs[i], lodCenter);
            }
        }
    }

    void SetRecentLeafsToDirty(MeshNode meshNode)
    {
        if (meshNode.isRecentLeaf)
        {
            meshNode.isRecentLeaf = false;
            meshNode.isDirty = true;
            SetAffectedNeighborsToDirty(meshNode);
            return;
        }

        for (int i = 0; i < 8; i++)
        {
            if (meshNode.childs[i] != null)
            {
                SetRecentLeafsToDirty(meshNode.childs[i]);
            }
        }
    }

    void GetDirtyChunksAndCleanDirtyFlag(MeshNode meshNode, Queue<ABB> dirtyChunks)
    {
        if (meshNode.isLeaf && meshNode.isDirty)
        {
            meshNode.isDirty = false;
            float meshNodeSize = maxWorldSize / math.exp2(meshNode.depth);
            dirtyChunks.Enqueue(GetABB(meshNode.position, meshNodeSize));
        }
        else
        {
            foreach (var child in meshNode.childs)
            {
                if (child != null)
                {
                    GetDirtyChunksAndCleanDirtyFlag(child, dirtyChunks);
                }
            }
        }
    }

    void SetAndDisposeMeshData(MeshNode meshNode, ABB chunkBox, Mesh.MeshDataArray meshDataArray, NativeList<byte> voxelMatIdPerSubmesh, NativeReference<ABB> meshBoundsRef)
    {
        float meshNodeSize = maxWorldSize / math.exp2(meshNode.depth);
        ABB nodeBox = GetABB(meshNode.position, meshNodeSize);
        if (Intersects(chunkBox, nodeBox))
        {
            // register all meshes on the way to target to destroy on transition
            if (meshNode.meshChunk != null)
            {
                DeleteMeshChunk(meshNode.meshChunk);
                meshNode.meshChunk = null;
            }

            if (chunkBox.Equals(nodeBox))
            {
                // remove childs
                for (int i = 0; i < 8; i++)
                {
                    if (meshNode.childs[i] != null)
                    {
                        DeleteNode(ref meshNode.childs[i]);
                    }
                }

                meshNode.meshChunk = CreateMeshChunk(meshNode.position, meshNodeSize);

                meshNode.meshChunk.SetAndDisposeMeshData(meshDataArray, voxelMatIdPerSubmesh, meshBoundsRef);

                if (meshNode.depth == maxDepth && createCollisionMeshes)
                {
                    meshChunksRequiringCollisions.Add(meshNode.meshChunk);
                }
            }
            else
            {
                foreach (var child in meshNode.childs)
                {
                    if (child != null) SetAndDisposeMeshData(child, chunkBox, meshDataArray, voxelMatIdPerSubmesh, meshBoundsRef);
                }
            }
        }
    }

    void PrepareCollisionMeshes()
    {
        List<JobHandle> handles = new List<JobHandle>();
 
        // create bake jobs
        foreach (var meshChunk in meshChunksRequiringCollisions)
        {
            handles.Add(meshChunk.BakeMesh());
        }
        JobHandle.ScheduleBatchedJobs();

        // complete bake jobs
        foreach (var handle in handles)
        {
            handle.Complete();
        }

        // enable collision meshes
        foreach (var meshChunk in meshChunksRequiringCollisions)
        {
            meshChunk.EnableCollisionMesh();
        }

        meshChunksRequiringCollisions.Clear();
    }

    MeshChunk CreateMeshChunk(float3 localPosition, float size)
    {
        MeshChunk meshChunk = meshChunkPool.Instantiate();
        meshChunk.Init(transform, localPosition, size);

        meshChunksRequiringActivation.Add(meshChunk);

        return meshChunk;
    }

    void DeleteMeshChunk(MeshChunk meshChunk)
    {
        meshChunksRequiringDestruction.Add(meshChunk);
    }

    void DeleteNode(ref MeshNode meshNode)
    {
        if (meshNode.meshChunk != null)
        {
            DeleteMeshChunk(meshNode.meshChunk);
            meshNode.meshChunk = null;
        }
  
        for (int i = 0; i < 8; i++)
        {
            if (meshNode.childs[i] != null)
            {
                DeleteNode(ref meshNode.childs[i]);
            }
        }
        meshNode = null;
    }

    bool IsWithinReach(float3 targetPosition, MeshNode meshNode)
    {
        float meshNodeSize = maxWorldSize / math.exp2(meshNode.depth);

        float distanceToNode = GetDistanceToSurface(targetPosition, GetABB(meshNode.position, meshNodeSize));

        return distanceToNode < LODFactor*meshNodeSize;
    }

    float3 GetChildCenter(float3 parentCenter, float childSize, int childIndex)
    {
        return parentCenter + NODE_OFFSETS[childIndex] * childSize;
    }
}
