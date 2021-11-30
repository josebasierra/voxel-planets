using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Atmosphere;

public class AtmospherePassController : MonoBehaviour
{
    [SerializeField] Material atmospherePassMat;

    AtmosphereData[] atmospheres;
    ComputeBuffer buffer;

    bool sendNewData = false;

    int nextAtmosphereId = 0;

    void Start()
    {
        int atmosphereCount = FindObjectsOfType<Atmosphere>().Length;
        atmospheres = new AtmosphereData[atmosphereCount];

        buffer = new ComputeBuffer(atmosphereCount, System.Runtime.InteropServices.Marshal.SizeOf(typeof(AtmosphereData)), ComputeBufferType.Default);
    }

    public int GetAtmosphereId()
    {
        int id = nextAtmosphereId;
        nextAtmosphereId++;
        return id;
    }

    public void SetAtmosphereData(int atmosphereId, AtmosphereData atmosphereData)
    {
        atmospheres[atmosphereId] = atmosphereData;
        sendNewData = true;
    }

    void LateUpdate()
    {
        if (sendNewData)
        {
            sendNewData = false;
            buffer.SetData(atmospheres);
            atmospherePassMat.SetBuffer("atmospheres", buffer);
            atmospherePassMat.SetInt("atmospheresCount", atmospheres.Length);
        }
    }

    void OnDestroy()
    {
        buffer.Release();
    }
}
