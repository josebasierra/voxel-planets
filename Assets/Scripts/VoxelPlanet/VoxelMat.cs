using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "defaultMat", menuName = "VoxelPlanet/VoxelMat")]
public class VoxelMat : ScriptableObject
{
    static VoxelMat[] voxelMats = new VoxelMat[256]; 

    [SerializeField] Material material;

    int id = -1;

    public static VoxelMat GetVoxelMat(byte id)
    {
        return voxelMats[id];
    }

    public byte GetId()
    {
        return (byte)id;
    }

    public Material GetMaterial()
    {
        Debug.Assert(id != 0 && id != 255);
        return material;
    }

    private bool TryAssignId()
    {
        for (int i = 1; i <= 254; i++) // [1,254] id 0 reserved for empty material, and last id 255 for null material 
        {
            if (voxelMats[i] == null)
            {
                voxelMats[i] = this;
                id = i;
                return true;
            }
        }
        return false;
    }

    private void OnEnable()
    {
        if (name == "_Empty")
        {
            id = 0;
            voxelMats[id] = this;
        }
        else if (name == "_Null")
        {
            id = 255;
            voxelMats[id] = this;
        }
        else if (!TryAssignId())
        {
            Debug.LogError("Maximum number of VoxelMats reached (254)");
            Destroy(this);
        }
    }

    private void OnDisable()
    {
        voxelMats[id] = null;
    }
}
