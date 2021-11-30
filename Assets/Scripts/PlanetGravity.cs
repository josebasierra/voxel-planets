using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlanetGravity : MonoBehaviour
{
    [SerializeField] float gravityForce = 5f;

    void FixedUpdate()
    {
        Vector3 planetCenter = VoxelPlanet.GetNearestPlanet(transform.position).transform.position;
        Vector3 forceDirection = (planetCenter - transform.position).normalized;
        GetComponent<Rigidbody>().AddForce(gravityForce * forceDirection);
    }
}
