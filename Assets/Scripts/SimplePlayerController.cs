using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class SimplePlayerController : MonoBehaviour
{
    [SerializeField] Vector3 rotationSpeed;
    [SerializeField] float speed;
    [SerializeField] float boostSpeed;
    [SerializeField] float fastTravelSpeed;

    [SerializeField] GameObject speedUpText;

    GameObject modifierToolObject;

    void Start()
    {
        modifierToolObject = GetComponentInChildren<ModifierTool>().gameObject;
    }

    void Update()
    {
        float3 moveDirection = float3.zero;

        if (Input.GetKey(KeyCode.W))
        {
            moveDirection.z += 1;
        }
        if (Input.GetKey(KeyCode.S))
        {
            moveDirection.z -= 1;
        }
        if (Input.GetKey(KeyCode.A))
        {
            moveDirection.x -= 1;
        }
        if (Input.GetKey(KeyCode.D))
        {
            moveDirection.x += 1;
        }
        if (Input.GetKey(KeyCode.Space))
        {
            moveDirection.y += 1;
        }
        if (Input.GetKey(KeyCode.LeftControl))
        {
            moveDirection.y -= 1;
        }

        modifierToolObject.SetActive(true);
        float3 finalSpeed = speed;

        if (Input.GetKey(KeyCode.LeftShift))
        {
            modifierToolObject.SetActive(false);
            finalSpeed = boostSpeed;
        }

        VoxelPlanet nearestPlanet = VoxelPlanet.GetNearestPlanet(transform.position);
        float distanceToPlanet = Vector3.Distance(transform.position, nearestPlanet.transform.position);
        bool outsidePlanet = distanceToPlanet > nearestPlanet.GetMaxWorldSize() * 0.5f;

        if(speedUpText != null) speedUpText.SetActive(outsidePlanet);

        // increase speed if player far away from any planet
        if (Input.GetKey(KeyCode.F) && outsidePlanet)
        {
            modifierToolObject.SetActive(false);
            finalSpeed = fastTravelSpeed;
        }

        float3 globalDirection = transform.TransformDirection(moveDirection);
        GetComponent<Rigidbody>().velocity = globalDirection * finalSpeed;

        //transform.Translate(moveDirection * finalSpeed * Time.deltaTime);

        float rotationInputX = 0;
        if (Input.GetKey(KeyCode.Q)) rotationInputX += 1;
        if (Input.GetKey(KeyCode.E)) rotationInputX -= 1;
        rotationInputX *= Time.deltaTime;

        float3 rotationInput = new float3(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), rotationInputX);  // Mouse Y/X is frame independent

        transform.Rotate(rotationInput * rotationSpeed);
    }
}
