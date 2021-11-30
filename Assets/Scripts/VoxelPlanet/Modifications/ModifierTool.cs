using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ModifierTool : MonoBehaviour
{
    [SerializeField] float maxDistance = 64f;
    [SerializeField] LayerMask layerMask;

    [SerializeField] Transform rayOrigin;
    [SerializeField] Transform rayDestination;

    [SerializeField] Modifier modifier;

    [SerializeField] GameObject displayObject;

    [SerializeField] List<VoxelMat> toolVoxelMats;
    [SerializeField] VoxelMat emptyVoxelMat;

    VoxelMat selectedVoxelMat;

    [SerializeField] GameObject spawnObject;

    void Start()
    {
        selectedVoxelMat = toolVoxelMats[0];
        displayObject.GetComponent<MeshRenderer>().material = selectedVoxelMat.GetMaterial();
        modifier.SetVoxelMat(selectedVoxelMat);
    }

    void Update()
    {
        UpdateModifierPosition();
        
        if (Input.GetKeyDown(KeyCode.Tab) && Input.GetKey(KeyCode.LeftControl)) //change modifier form(sphere, cube...)
        {
            if (modifier.GetModificationType() == ModificationType.Sphere)
                modifier.SetModificationType(ModificationType.Cube);
            else
                modifier.SetModificationType(ModificationType.Sphere);
        }
        else if (Input.GetKeyDown(KeyCode.Tab))  // Change tool material
        {
            int selectedListId = toolVoxelMats.FindIndex(voxelMat => voxelMat == selectedVoxelMat);
            selectedVoxelMat = toolVoxelMats[(selectedListId + 1) % toolVoxelMats.Count];

            // change material of display object
            displayObject.GetComponent<MeshRenderer>().material = selectedVoxelMat.GetMaterial();

            modifier.SetVoxelMat(selectedVoxelMat);
        }
        else if (Input.GetKeyDown(KeyCode.Mouse0))   // Apply modification
        {
            modifier.ApplyModification();
        }
        else if (Input.GetKeyDown(KeyCode.Mouse1))  // Apply remove/delete modification
        {
            modifier.SetVoxelMat(emptyVoxelMat);  // set empty material
            modifier.ApplyModification();
            modifier.SetVoxelMat(selectedVoxelMat);
        }
        else if (Input.mouseScrollDelta.y != 0)
        {
            modifier.SetSize(modifier.GetSize() + Input.mouseScrollDelta.y);
        }

        if (Input.GetKeyDown(KeyCode.Alpha1)) // shoot balls to test collisions
        {
            if (spawnObject != null)
            {
                var gObject = Instantiate(spawnObject);
                gObject.transform.position = transform.position + transform.forward * 1f;
                gObject.GetComponent<Rigidbody>().AddForce(transform.forward * 10, ForceMode.Impulse);
            }
        }
    }

    void UpdateModifierPosition()
    {
        Vector3 direction = (rayDestination.position - rayOrigin.position).normalized;
        Ray ray = new Ray(rayOrigin.position, direction);

        if (Input.GetKey(KeyCode.LeftAlt) && Physics.Raycast(ray, out RaycastHit hit, maxDistance, layerMask))
        {
            modifier.transform.position = hit.point;
        }
        else if (Physics.SphereCast(ray, (modifier.GetSize()/2f).x, out hit, maxDistance, layerMask)) //only works as intended for perfect spheres modifications
        {
            modifier.transform.position = hit.point;
        }
        else
        {
            modifier.transform.position = rayOrigin.position + direction * maxDistance;
        }
    }
}
