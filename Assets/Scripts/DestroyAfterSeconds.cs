using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DestroyAfterSeconds : MonoBehaviour
{
    [SerializeField] float secondsToDestroy;

    float currentSeconds = 0f;

    void Update()
    {
        currentSeconds += Time.deltaTime;
        if (currentSeconds >= secondsToDestroy) Destroy(gameObject);
    }
}
