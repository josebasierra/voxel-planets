using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static GeometryUtility;

public enum ModificationType : byte { Sphere, Cube };

public class Modifier : MonoBehaviour
{
    [SerializeField] ModificationType type;
    [SerializeField] VoxelMat voxelMat;

    [SerializeField] GameObject sphereView;
    [SerializeField] GameObject cubeView;

    GameObject currentView;

    void Start()
    {
        sphereView.SetActive(false);
        cubeView.SetActive(false);

        SetModificationType(type);
        SetVoxelMat(voxelMat);
    }

    public ModificationType GetModificationType() => type;

    public void SetModificationType(ModificationType type)
    {
        if(currentView != null) currentView.SetActive(false);

        if (type == ModificationType.Sphere) currentView = sphereView;
        else currentView = cubeView;

        currentView.SetActive(true);
        this.type = type;
    }

    public VoxelMat GetVoxelMat() => voxelMat;

    public void SetVoxelMat(VoxelMat voxelMat)
    {
        this.voxelMat = voxelMat;
    }

    public float3 GetSize() => transform.localScale;

    public void SetSize(float3 size)
    {
        const float MIN_SIZE = 3f;
        const float MAX_SIZE = 30f;

        if (size.x < MIN_SIZE || size.x > MAX_SIZE) return;
        if (size.y < MIN_SIZE || size.y > MAX_SIZE) return;
        if (size.z < MIN_SIZE || size.z > MAX_SIZE) return;

        transform.localScale = size;
    }

    public void ApplyModification()
    {
        VoxelPlanet voxelPlanet = VoxelPlanet.GetNearestPlanet(transform.position);

        var inverse = GetInverse();
        var abb = GetABB(voxelPlanet.transform);

        ModificationData modificationData = new ModificationData(type, voxelMat.GetId(), inverse, abb);
        voxelPlanet.ApplyModification(modificationData);
    }

    Matrix4x4 GetInverse()
    {
        var oldParent = transform.parent;
        transform.parent = VoxelPlanet.GetNearestPlanet(transform.position).transform;
        var inverse = Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale).inverse;
        transform.parent = oldParent;

        return inverse;
    }

    ABB GetABB(Transform origin)
    {
        float3 localModifierPositionToPlanet = origin.InverseTransformPoint(transform.position);

        float3 localScale = transform.localScale;

        bool isDeformed = localScale.x != localScale.y || localScale.x != localScale.z;

        float3 offset;
        if (type == ModificationType.Sphere && !isDeformed)
        {
            offset = localScale.x / 2f;
        }
        else //TODO: compute better AABB
        {
            float maxDiagonal = math.max(localScale.x, math.max(localScale.y, localScale.z)) * 0.5f;
            maxDiagonal = math.sqrt(3*(maxDiagonal*maxDiagonal));
            offset = maxDiagonal;
        }

        return new ABB(localModifierPositionToPlanet - offset, localModifierPositionToPlanet + offset);
    }
}
