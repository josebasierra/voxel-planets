using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ComputeShaderTest : MonoBehaviour
{
    [Header("Compute")]
    public ComputeShader computeShader;


    void Start()
    {
        uint groupSizeX, groupSizeY, groupSizeZ;
        computeShader.GetKernelThreadGroupSizes(0, out groupSizeX, out groupSizeY, out groupSizeZ);
        Debug.Log(groupSizeX + " " + groupSizeY + " " + groupSizeZ);


        int count = 100;
        float[] floats = new float[count];

        ComputeBuffer buffer = new ComputeBuffer(count, sizeof(float));
        buffer.SetData(floats);

        computeShader.SetBuffer(0, "floats", buffer);


        computeShader.Dispatch(0, (int)(count / groupSizeX), 1, 1);

        buffer.GetData(floats);
        foreach (var i in floats) Debug.Log(i);

        buffer.Release();
    }
}
