using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public static class Utility
{
    public static bool IsPowerOfTwo(int x)
    {
        return (x & (x - 1)) == 0;
    }
}
