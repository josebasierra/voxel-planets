using System.Collections;
using System.Collections.Generic;
using UnityEngine;

enum NodeType : byte { Interior, Homogeneous, Heterogeneous, Null }

struct NodeData     //20 bytes (18 + 2 padding)
{
    public NodeType type;
    public byte depth;
    public FixedArray8<byte> materials;

    public int childrenDataKey;           // for interior nodes
    public int intersectionsDataKey;    // for heterogeneous & interior nodes

    public NodeData(NodeType type, byte depth)
    {
        this.type = type;
        this.depth = depth;

        materials = new FixedArray8<byte>();
        for (int i = 0; i < 8; i++)
        {
            materials[i] = 255;
        }

        childrenDataKey = -1;
        intersectionsDataKey = -1;
    }
}

struct ChildrenData     // 32 bytes
{
    FixedArray8<int> childrenKeys;

    public int this[int key]
    {
        get => childrenKeys[key];
        set => childrenKeys[key] = value;
    }
}

struct NodeIntersections        // 12 bytes
{
    FixedArray12<byte> intersectionValues;  // 12 edges, one intersection value per edge

    public byte this[int key]
    {
        get => intersectionValues[key];
        set => intersectionValues[key] = value;
    }
}

// every corner saves the intersections in the +x,+y,+z directions
public struct CornerIntersections
{
    public byte x, y, z;
}
