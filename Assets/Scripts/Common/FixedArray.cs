using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Fixed size arrays to be used inside burst compiled jobs

struct FixedArray8<T> where T : struct
{
    T e0, e1, e2, e3, e4, e5, e6, e7;

    public T GetValue(int key)
    {
        return key switch
        {
            0 => e0,
            1 => e1,
            2 => e2,
            3 => e3,
            4 => e4,
            5 => e5,
            6 => e6,
            7 => e7,
            _ => default,
        };
    }

    public void SetValue(int key, T value)
    {
        switch (key)
        {
            case 0:
                e0 = value;
                break;
            case 1:
                e1 = value;
                break;
            case 2:
                e2 = value;
                break;
            case 3:
                e3 = value;
                break;
            case 4:
                e4 = value;
                break;
            case 5:
                e5 = value;
                break;
            case 6:
                e6 = value;
                break;
            case 7:
                e7 = value;
                break;
        }
    }

    public T this[int key]
    {
        get => GetValue(key);
        set => SetValue(key, value);
    }
}


struct FixedArray12<T> where T : struct
{
    T e0, e1, e2, e3, e4, e5, e6, e7, e8, e9, e10, e11;

    public T GetValue(int key)
    {
        return key switch
        {
            0 => e0,
            1 => e1,
            2 => e2,
            3 => e3,
            4 => e4,
            5 => e5,
            6 => e6,
            7 => e7,
            8 => e8,
            9 => e9,
            10 => e10,
            11 => e11,
            _ => default,
        };
    }

    public void SetValue(int key, T value)
    {
        switch (key)
        {
            case 0:
                e0 = value;
                break;
            case 1:
                e1 = value;
                break;
            case 2:
                e2 = value;
                break;
            case 3:
                e3 = value;
                break;
            case 4:
                e4 = value;
                break;
            case 5:
                e5 = value;
                break;
            case 6:
                e6 = value;
                break;
            case 7:
                e7 = value;
                break;
            case 8:
                e8 = value;
                break;
            case 9:
                e9 = value;
                break;
            case 10:
                e10 = value;
                break;
            case 11:
                e11 = value;
                break;
        }
    }

    public T this[int key]
    {
        get => GetValue(key);
        set => SetValue(key, value);
    }
}
