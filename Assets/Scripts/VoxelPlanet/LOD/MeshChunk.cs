using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using static GeometryUtility;
using static UnityEngine.Mesh;

public class MeshChunk : MonoBehaviour
{
    MeshRenderer meshRenderer;
    MeshCollider meshCollider;
    Mesh mesh;

    float3 chunkSize;

    public void Init(Transform parent, float3 localPosition, float3 chunkSize)
    {
        transform.parent = parent;
        transform.localPosition = localPosition;
        this.chunkSize = chunkSize;

        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();

        mesh = GetComponent<MeshFilter>().mesh;

        gameObject.SetActive(false);
    }

    public void Enable()
    {
        gameObject.SetActive(true);
    }

    public void SetAndDisposeMeshData(MeshDataArray meshDataArray, NativeList<byte> voxelMatIdPerSubmesh, NativeReference<ABB> meshBoundsRef)
    {
        Profiler.BeginSample("Apply&Dispose MeshData");
        ApplyAndDisposeWritableMeshData(meshDataArray, mesh);
        Profiler.EndSample();

        // set submesh materials
        var materials = new Material[voxelMatIdPerSubmesh.Length];
        for (int i = 0; i < voxelMatIdPerSubmesh.Length; i++)
        {
            materials[i] = VoxelMat.GetVoxelMat(voxelMatIdPerSubmesh[i]).GetMaterial();
        }
        meshRenderer.sharedMaterials = materials;

        mesh.bounds = new Bounds(meshBoundsRef.Value.GetCenter(), meshBoundsRef.Value.GetSize());
        //mesh.RecalculateBounds();
        mesh.RecalculateNormals();  // TODO: Compute them in parallel during meshGeneration
    }

    public JobHandle BakeMesh()
    {
        var bakeJob = new BakeMeshJob
        {
            meshId = mesh.GetInstanceID()
        };
        return bakeJob.Schedule();
    }

    public void EnableCollisionMesh()
    {
        meshCollider.enabled = true;
        meshCollider.sharedMesh = mesh;
    }

    public void Clear()
    {
        //mesh.Clear();
        //transform.parent = null;
        meshCollider.sharedMesh = null;
        meshCollider.enabled = false;

        gameObject.SetActive(false);
    }

    public void Dispose()
    {
        Destroy(mesh);
        Destroy(this);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(this.transform.position, chunkSize);
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct BakeMeshJob : IJob
    {
        public int meshId;

        public void Execute()
        {
            Physics.BakeMesh(meshId, false);
        }
    }
}
