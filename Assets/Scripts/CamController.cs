using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamController : MonoBehaviour
{
    [SerializeField] Transform pointOfView;

    void LateUpdate()
    {
        transform.position = pointOfView.position;
        transform.rotation = pointOfView.rotation;
    }
}
