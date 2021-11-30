using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class MeshChunkPool : MonoBehaviour
{
    [SerializeField] GameObject meshChunkPrefab;
    [SerializeField, Range(0,3000) ] int maxCapacity = 0;

    public List<MeshChunk> meshChunkPool = new List<MeshChunk>();

    public void Destroy(MeshChunk meshChunk)
    {
        if (meshChunkPool.Count > maxCapacity)
        {
            meshChunk.Dispose();
            return;
        }

        meshChunk.Clear();
        meshChunkPool.Add(meshChunk);
    }

    public MeshChunk Instantiate()
    {
        if (meshChunkPool.Count == 0)
        {
            var meshChunkObject = Instantiate(meshChunkPrefab);
            return meshChunkObject.GetComponent<MeshChunk>();
        }

        MeshChunk meshChunk = meshChunkPool[0];
        meshChunkPool.RemoveAtSwapBack(0);

        return meshChunk;
    }

    
}
