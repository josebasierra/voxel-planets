using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using static GeometryUtility;


// Voxel octree implementation in such a way burst compiled jobs are compatible
public struct ModOctree : IDisposable
{
    #region Data

    static readonly float3[] NODE_OFFSETS =
    {
         new float3(-0.5f, -0.5f, -0.5f),
         new float3(-0.5f, -0.5f, 0.5f),
         new float3(-0.5f, 0.5f, -0.5f),
         new float3(-0.5f, 0.5f, 0.5f),
         new float3(0.5f, -0.5f, -0.5f),
         new float3(0.5f, -0.5f, 0.5f),
         new float3(0.5f, 0.5f, -0.5f),
         new float3(0.5f, 0.5f, 0.5f)
    };

    static readonly int2[] EDGE_TO_CORNERS =
    {
        new int2(0,1),
        new int2(0,2),
        new int2(2,3),
        new int2(1,3),
        new int2(0,4),
        new int2(2,6), 
        new int2(3,7),
        new int2(1,5),
        new int2(4,5),
        new int2(4,6),
        new int2(6,7),
        new int2(5,7),
    };

    static readonly int3[] CORNER_TO_EDGES =
    {
        new int3(0,1,4),
        new int3(0,3,7),
        new int3(1,2,5),
        new int3(2,3,6),
        new int3(4,8,9),
        new int3(8,7,11),
        new int3(5,9,10),
        new int3(6,10,11),
    };

    float3 center;      // global position of the octree
    float totalSize;    // 1,   2,  4,  8,  16, 32, 64, 128,256,512,1024,2048,4096...
    byte maxDepth;      // 0,   1,  2,  3,  4,  5,  6,  7,  8,  9,  10
    float voxelSize;

    int rootNodeKey;

    NativeList<NodeData> nodes;
    NativeList<ChildrenData> childrenPerNode;
    NativeList<NodeIntersections> intersectionsPerNode;

    NativeQueue<int> unusedNodeKeys;
    NativeQueue<int> unusedChildrenDataKeys;
    NativeQueue<int> unusedIntersectionsDataKeys;

    #endregion

    #region Public methods

    public ModOctree(float size, byte maxDepth)
    {
        nodes = new NativeList<NodeData>(Allocator.Persistent);
        childrenPerNode = new NativeList<ChildrenData>(Allocator.Persistent);
        intersectionsPerNode = new NativeList<NodeIntersections>(Allocator.Persistent);

        unusedNodeKeys = new NativeQueue<int>(Allocator.Persistent);
        unusedChildrenDataKeys = new NativeQueue<int>(Allocator.Persistent);
        unusedIntersectionsDataKeys = new NativeQueue<int>(Allocator.Persistent);

        this.center = float3.zero;
        this.totalSize = size;
        this.maxDepth = maxDepth;

        voxelSize = totalSize / math.exp2(maxDepth);
        rootNodeKey = -1; 

        rootNodeKey = CreateNode(NodeType.Homogeneous, 0);
    }

    public void Dispose()
    {
        nodes.Dispose();
        childrenPerNode.Dispose();
        intersectionsPerNode.Dispose();

        unusedNodeKeys.Dispose();
        unusedChildrenDataKeys.Dispose();
        unusedIntersectionsDataKeys.Dispose();
    }

    public void ApplyModification(in ModificationData modificationData)
    {
        ApplyModification(rootNodeKey, center, modificationData);
    }

    public void GetVoxelGrid(ABB chunkBox, float targetVoxelSize, NativeArray3D<byte> materials, NativeArray3D<CornerIntersections> intersections)
    {
        // init materials to null:
        for (int i = 0; i < materials.GetLength1D(); i++)
        {
            materials[i] = 255;
        }

        GetVoxelGrid(rootNodeKey, center, chunkBox, targetVoxelSize, materials, intersections);
    }

    public void PrintMemoryUsage()
    {
        int nodeDataSize = UnsafeUtility.SizeOf<NodeData>();
        int childrenDataSize = UnsafeUtility.SizeOf<ChildrenData>();
        int intersectionsDataSize = UnsafeUtility.SizeOf<NodeIntersections>();

        int bytes = nodeDataSize * (nodes.Length - unusedNodeKeys.Count) + childrenDataSize *
            (childrenPerNode.Length - unusedChildrenDataKeys.Count) + intersectionsDataSize * (intersectionsPerNode.Length - unusedIntersectionsDataKeys.Count);

        int reservedBytes = nodeDataSize * nodes.Capacity + childrenDataSize *
            childrenPerNode.Capacity + intersectionsDataSize * intersectionsPerNode.Capacity;

        float kilobytes = bytes / 1024f;
        float reservedKilobytes = reservedBytes / 1024f;

        float megabytes = kilobytes / 1024f;
        float reservedMegabytes = reservedKilobytes / 1024f;

        Debug.Log("\tMinimum Memory Required: " + megabytes.ToString("0.00") + " MB" +
            "\tTotal Memory Reserved: " + reservedMegabytes.ToString("0.00") + " MB\n");
    }

    public int GetNodeCount()
    {
        return nodes.Length - unusedNodeKeys.Count;
    }

    #endregion

    #region Data Creation/Deletion methods

    int CreateNode(NodeType type, byte depth)
    {
        NodeData node = new NodeData(type, depth);

        if (type == NodeType.Interior)
        {
            node.childrenDataKey = CreateChildrenData();
        }

        if (type == NodeType.Interior || type == NodeType.Heterogeneous)
        {
            node.intersectionsDataKey = CreateIntersectionsData();
        }

        if (unusedNodeKeys.TryDequeue(out int nodeKey))
        {
            nodes[nodeKey] = node;
        }
        else
        {
            nodes.Add(node);
            nodeKey = nodes.Length - 1;
        }

        return nodeKey;
    }

    int CreateChildrenData()
    {
        ChildrenData childrenKeys = new ChildrenData();
        for (int i = 0; i < 8; i++)
        {
            childrenKeys[i] = -1;
        }

        if (unusedChildrenDataKeys.TryDequeue(out int childrenDataKey))
        {
            childrenPerNode[childrenDataKey] = childrenKeys;
        }
        else
        {
            childrenPerNode.Add(childrenKeys);
            childrenDataKey = childrenPerNode.Length - 1;
        }

        return childrenDataKey;
    }

    int CreateIntersectionsData()
    {
        NodeIntersections intersections = new NodeIntersections();
        for (int i = 0; i < 12; i++)
        {
            intersections[i] = 0;
        }

        if (unusedIntersectionsDataKeys.TryDequeue(out int intersectionsDataKey))
        {
            intersectionsPerNode[intersectionsDataKey] = intersections;
        }
        else
        {
            intersectionsPerNode.Add(intersections);
            intersectionsDataKey = intersectionsPerNode.Length - 1;
        }

        return intersectionsDataKey;
    }

    // force parameter parentKey ?
    void DeleteNode(int nodeKey)
    {
        if (nodeKey == -1) return;

        var node = nodes[nodeKey];

        DeleteChildrenData(node.childrenDataKey);
        node.childrenDataKey = -1;

        DeleteIntersectionsData(node.intersectionsDataKey);
        node.childrenDataKey = -1;

        nodes[nodeKey] = new NodeData(NodeType.Null, 0);
        unusedNodeKeys.Enqueue(nodeKey);
    }

    void DeleteChildrenData(int childrenDataKey)
    {
        if (childrenDataKey == -1) return;

        // Delete child nodes
        ChildrenData childrenKeys = childrenPerNode[childrenDataKey];
        for (int i = 0; i < 8; i++)
        {
            DeleteNode(childrenKeys[i]);
        }

        unusedChildrenDataKeys.Enqueue(childrenDataKey);
    }

    void DeleteIntersectionsData(int intersectionsDataKey)
    {
        if (intersectionsDataKey == -1) return;

        unusedIntersectionsDataKeys.Enqueue(intersectionsDataKey);
    }

    #endregion

    #region Private methods

    void ApplyModification(int nodeKey, in float3 nodeCenter, in ModificationData modData)
    {
        if (nodes[nodeKey].depth == maxDepth)
        {
            UpdateLeafData(nodeKey, nodeCenter, modData);
        }
        else
        {
            TryExpandHomogeneousNode(nodeKey);
            
            Debug.Assert(nodes[nodeKey].type == NodeType.Interior);

            NodeData node = nodes[nodeKey];
            float nodeSize = totalSize / math.exp2(node.depth);
            ChildrenData childrenKeys = childrenPerNode[node.childrenDataKey];

            for (int i = 0; i < 8; i++)
            {
                float childSize = nodeSize * 0.5f;
                float3 childCenter = GetChildCenter(nodeCenter, childSize, i);

                if (Intersects(modData.GetABB(), GetABB(childCenter, childSize)))
                {
                    if (childrenKeys[i] == -1 && node.depth == maxDepth - 1)
                    {
                        childrenKeys[i] = CreateNode(NodeType.Heterogeneous, (byte)(node.depth + 1));
                        childrenPerNode[node.childrenDataKey] = childrenKeys;
                    }
                    else if (childrenKeys[i] == -1)
                    {
                        childrenKeys[i] = CreateNode(NodeType.Interior, (byte)(node.depth + 1));
                        childrenPerNode[node.childrenDataKey] = childrenKeys;
                    }
                    ApplyModification(childrenKeys[i], childCenter, modData);
                }
            }
        }
        TryCollapse(nodeKey);
    }

    void GetVoxelGrid(int nodeKey, in float3 nodeCenter, in ABB chunkBox, in float targetNodeSize, NativeArray3D<byte> materials, NativeArray3D<CornerIntersections> intersections)
    {
        NodeData node = nodes[nodeKey];
        float nodeSize = totalSize / math.exp2(node.depth);

        if (!Intersects(GetABB(nodeCenter, nodeSize), chunkBox)) return;

        int3 arrayLength3D = materials.GetLength3D();

        if (nodeSize == targetNodeSize && (node.type == NodeType.Interior || node.type == NodeType.Heterogeneous))
        {
            float3 cornerPosition = nodeCenter + NODE_OFFSETS[0] * nodeSize;
            int3 cornerArrayIndex3D = (int3)((cornerPosition - chunkBox.min) / targetNodeSize);

            // every node is responsible of providing the data of one corner (corner origin, 0), except if node is at the limits of x,y,z
            if ((cornerArrayIndex3D < arrayLength3D - 2 ).Equals(new bool3(true)))
            {
                materials[cornerArrayIndex3D] = node.materials[0];

                // set intersections
                NodeIntersections nodeIntersections = intersectionsPerNode[node.intersectionsDataKey];
                CornerIntersections cornerIntersections = intersections[cornerArrayIndex3D];

                cornerIntersections.x = nodeIntersections[4];
                cornerIntersections.y = nodeIntersections[1];
                cornerIntersections.z = nodeIntersections[0];

                intersections[cornerArrayIndex3D] = cornerIntersections;
            }
            else
            {
                for (int corner = 0; corner < 8; corner++)
                {
                    cornerPosition = nodeCenter + NODE_OFFSETS[corner] * nodeSize;
                    cornerArrayIndex3D = (int3)((cornerPosition - chunkBox.min) / targetNodeSize);

                    materials[cornerArrayIndex3D] = node.materials[corner];

                    // set intersections
                    NodeIntersections nodeIntersections = intersectionsPerNode[node.intersectionsDataKey];
                    CornerIntersections cornerIntersections = intersections[cornerArrayIndex3D];
                    switch (corner)
                    {
                        case 0:
                            cornerIntersections.x = nodeIntersections[4];
                            cornerIntersections.y = nodeIntersections[1];
                            cornerIntersections.z = nodeIntersections[0];
                            break;
                        case 1:
                            cornerIntersections.x = nodeIntersections[7];
                            cornerIntersections.y = nodeIntersections[3];
                            break;
                        case 2:
                            cornerIntersections.x = nodeIntersections[5];
                            cornerIntersections.z = nodeIntersections[2];
                            break;
                        case 3:
                            cornerIntersections.x = nodeIntersections[6];
                            break;
                        case 4:
                            cornerIntersections.y = nodeIntersections[9];
                            cornerIntersections.z = nodeIntersections[8];
                            break;
                        case 5:
                            cornerIntersections.y = nodeIntersections[11];
                            break;
                        case 6:
                            cornerIntersections.z = nodeIntersections[10];
                            break;
                        case 7:
                            break;
                    }
                    intersections[cornerArrayIndex3D] = cornerIntersections;
                }
            } 
        }
        else if (node.type == NodeType.Homogeneous)
        {
            byte nodeMaterial = node.materials[0];

            float3 nodeMinCornerPosition = nodeCenter + NODE_OFFSETS[0] * nodeSize;
            float3 nodeMaxCornerPosition = nodeCenter + NODE_OFFSETS[7] * nodeSize;

            int3 minArrayIndex3D = (int3)((nodeMinCornerPosition - chunkBox.min) / targetNodeSize);
            int3 maxArrayIndex3D = (int3)((nodeMaxCornerPosition - chunkBox.min) / targetNodeSize);
            minArrayIndex3D = math.max(minArrayIndex3D, int3.zero);
            maxArrayIndex3D = math.min(maxArrayIndex3D, arrayLength3D - 1);

            // do not take responsability of the max x,y,z elements, adjacent nodes will be responsible for them
            if ((maxArrayIndex3D < arrayLength3D - 1).Equals(new bool3(true)))
            {
                maxArrayIndex3D -= 1;
            }

            for (int x = minArrayIndex3D.x; x <= maxArrayIndex3D.x; x++)
            {
                for (int y = minArrayIndex3D.y; y <= maxArrayIndex3D.y; y++)
                {
                    for (int z = minArrayIndex3D.z; z <= maxArrayIndex3D.z; z++)
                    {
                        materials[x,y,z] = nodeMaterial;
                    }
                }
            }
        }
        else
        {
            ChildrenData childrenKeys = childrenPerNode[node.childrenDataKey];
            for (int i = 0; i < 8; i++)
            {
                if (childrenKeys[i] != -1)
                {
                    float3 childCenter = GetChildCenter(nodeCenter, nodeSize * 0.5f, i);
                    GetVoxelGrid(childrenKeys[i], childCenter, chunkBox, targetNodeSize, materials, intersections);
                }
            }
        }
    }

    void UpdateLeafData(int nodeKey, in float3 nodeCenter, in ModificationData modData)
    {
        NodeData node = nodes[nodeKey];
        if (node.type == NodeType.Homogeneous)
        {
            node.type = NodeType.Heterogeneous;
            node.intersectionsDataKey = CreateIntersectionsData();
        }

        Debug.Assert(node.depth == maxDepth && (node.type == NodeType.Heterogeneous || node.type == NodeType.Homogeneous));

        NodeIntersections nodeIntersections = intersectionsPerNode[node.intersectionsDataKey];

        // save old intersection state
        FixedArray12<bool> isOldIntersectionValid = new FixedArray12<bool>();
        for (int edge = 0; edge < 12; edge++)
        {
            int2 corners = EDGE_TO_CORNERS[edge];
            isOldIntersectionValid[edge] = node.materials[corners[0]] != node.materials[corners[1]];
        }

        for (int edge = 0; edge < 12; edge++)
        {
            int2 corners = EDGE_TO_CORNERS[edge];
            int corner1 = corners[0];
            int corner2 = corners[1];

            float3 corner1Position = nodeCenter + NODE_OFFSETS[corner1] * voxelSize;
            float3 corner2Position = nodeCenter + NODE_OFFSETS[corner2] * voxelSize;

            float value1 = modData.Evaluate(corner1Position);
            float value2 = modData.Evaluate(corner2Position);

            // update corner materials
            if (value1 < 0) node.materials[corner1] = modData.GetMaterial();
            if (value2 < 0) node.materials[corner2] = modData.GetMaterial();

            // update edge intersection values
            if (node.materials[corner1] != node.materials[corner2])
            {
                if (value1 < 0)
                {
                    byte intersectionValue = (byte)(math.abs(value1) / math.abs(value2 - value1) * 255);

                    nodeIntersections[edge] = isOldIntersectionValid[edge] ?
                        (byte)math.max((int)intersectionValue, (int)nodeIntersections[edge]) :  // do not override previous intersection if outside sdf isocontour
                        intersectionValue;
                }
                else if (value2 < 0)
                {
                    byte intersectionValue = (byte)(math.abs(value1) / math.abs(value2 - value1) * 255);

                    nodeIntersections[edge] = isOldIntersectionValid[edge] ?
                        (byte)math.min((int)intersectionValue, (int)nodeIntersections[edge]) :
                        intersectionValue;
                }
            }
        }

        intersectionsPerNode[node.intersectionsDataKey] = nodeIntersections;
        nodes[nodeKey] = node;
    }

    void TryExpandHomogeneousNode(int nodeKey)
    {
        NodeData node = nodes[nodeKey];
        if (node.type != NodeType.Homogeneous || node.depth >= maxDepth) return;

        node.type = NodeType.Interior;
        node.childrenDataKey = CreateChildrenData();
        node.intersectionsDataKey = CreateIntersectionsData();

        ChildrenData childrenKeys = childrenPerNode[node.childrenDataKey];

        byte material = node.materials[0];

        for (int i = 0; i < 8; i++)
        {
            childrenKeys[i] = CreateNode(NodeType.Homogeneous, (byte)(node.depth + 1));
            NodeData childNode = nodes[childrenKeys[i]];

            for (int j = 0; j < 8; j++)
            {
                childNode.materials[j] = material;
            }

            nodes[childrenKeys[i]] = childNode;
        }
        childrenPerNode[node.childrenDataKey] = childrenKeys;
        nodes[nodeKey] = node; 
    }

    /*Removes unnecessary child nodes and set values of interior nodes*/
    void TryCollapse(int nodeKey)
    {
        TryCollapseNodeIntoHomogeneous(nodeKey);
        TryCollapseChildsIntoNull(nodeKey);
        TryCollapseInteriorNodeChildValues(nodeKey);
    }

    void TryCollapseNodeIntoHomogeneous(int nodeKey)
    {
        NodeData node = nodes[nodeKey];

        if (node.type == NodeType.Interior)
        {
            ChildrenData childrenKeys = childrenPerNode[node.childrenDataKey];
            for (int i = 0; i < 8; i++)
            {
                int childKey = childrenKeys[i];
                if (childKey == -1 || !(nodes[childKey].type == NodeType.Homogeneous)) return;
            }

            node.type = NodeType.Homogeneous;

            byte material = nodes[childrenKeys[0]].materials[0];
            for (int i = 0; i < 8; i++)
            {
                node.materials[i] = material;
            }

            DeleteChildrenData(node.childrenDataKey);
            node.childrenDataKey = -1;

            DeleteIntersectionsData(node.intersectionsDataKey);
            node.intersectionsDataKey = -1;

            nodes[nodeKey] = node;
        }
        else if (node.type == NodeType.Heterogeneous)
        {
            byte material = node.materials[0];
            for (int i = 1; i < 8; i++)
            {
                if (node.materials[i] != material) return;
            }

            node.type = NodeType.Homogeneous;

            DeleteIntersectionsData(node.intersectionsDataKey);
            node.intersectionsDataKey = -1;

            nodes[nodeKey] = node;
        }
    }

    /*Tries to remove childs from node (one node cannot remove itself, the 
 * parent would have an incorrect reference to the removed child)*/
    void TryCollapseChildsIntoNull(int nodeKey)
    {
        NodeData node = nodes[nodeKey];
        if (node.type != NodeType.Interior) return;

        ChildrenData childrenKeys = childrenPerNode[node.childrenDataKey];

        for (int i = 0; i < 8; i++)
        {
            if (CanBeDeleted(childrenKeys[i]))
            {
                DeleteNode(childrenKeys[i]);
                childrenKeys[i] = -1;
            }
        }
        childrenPerNode[node.childrenDataKey] = childrenKeys;
    }

    bool CanBeDeleted(int nodeKey)
    {
        if (nodeKey == -1) return false;

        NodeData node = nodes[nodeKey];

        if (node.type == NodeType.Homogeneous)
        {
            // if exists material different to null(255), don't destroy
            for (int i = 0; i < 8; i++)
            {
                if (node.materials[i] != 255) return false;
            }
        }
        else if (node.type == NodeType.Interior)
        {
            // if exists child, don't destroy
            ChildrenData childrenKeys = childrenPerNode[node.childrenDataKey];
            for (int i = 0; i < 8; i++)
            {
                if (childrenKeys[i] != -1) return false;
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    void TryCollapseInteriorNodeChildValues(int nodeKey)
    {
        if (nodes[nodeKey].type != NodeType.Interior) return;

        NodeData node = nodes[nodeKey];
        ChildrenData childrenKeys = childrenPerNode[node.childrenDataKey];

        // compute corner materials
        for (int corner = 0; corner < 8; corner++)
        {
            if (childrenKeys[corner] != -1)
                node.materials[corner] = nodes[childrenKeys[corner]].materials[corner];
            else
                node.materials[corner] = 255; //null material
        }

        // compute edge intersection values
        NodeIntersections nodeIntersections = intersectionsPerNode[node.intersectionsDataKey];
        for (int edge = 0; edge < 12; edge++)
        {
            int2 corners = EDGE_TO_CORNERS[edge];
            int corner1 = corners[0];
            int corner2 = corners[1];

            if (node.materials[corner1] != node.materials[corner2])
            {
                bool isIntersection1Valid = TryGetIntersectionValue(childrenKeys[corner1], edge, out byte intersectionValue1);
                bool isIntersection2Valid = TryGetIntersectionValue(childrenKeys[corner2], edge, out byte intersectionValue2);

                if (!isIntersection2Valid)
                {
                    nodeIntersections[edge] = (byte)(intersectionValue1 / 2f);
                }
                else if (!isIntersection1Valid)
                {
                    nodeIntersections[edge] = (byte)((256 + intersectionValue2) / 2f);
                }
                else
                {
                    //TODO: Average point instead?
                    nodeIntersections[edge] = 128;
                }
            }
        }
        intersectionsPerNode[node.intersectionsDataKey] = nodeIntersections;
        nodes[nodeKey] = node;
    }

    /* Obtain intersection value on the edge of the given heterogenous node if possible*/
    bool TryGetIntersectionValue(int nodeKey, int edge, out byte intersectionValue)
    {
        intersectionValue = 0;

        if (nodeKey == -1) return false;

        NodeData node = nodes[nodeKey];
        if (node.type == NodeType.Homogeneous || node.type == NodeType.Null) return false;

        int2 corners = EDGE_TO_CORNERS[edge];
        if (node.materials[corners[0]] == node.materials[corners[1]]) return false;

        NodeIntersections intersections = intersectionsPerNode[node.intersectionsDataKey];
        intersectionValue = intersections[edge];
        return true;
    }

    float3 GetChildCenter(float3 parentCenter, float childSize, int childIndex)
    {
        return parentCenter + NODE_OFFSETS[childIndex] * childSize;
    }

    static float GetNodeSize(float totalSize, byte depth)
    {
        return totalSize / math.exp2(depth);
    }

    #endregion

    #region Debug

    struct DebugSettings
    {
        public bool drawHt;
        public bool drawHm;
        public bool drawInterior;

        public Color interiorNodeColor;
        public Color htLeafColor;
        public Color hmLeafColor;

        public DebugSettings(bool drawHt, bool drawHm, bool drawInterior)
        {
            this.drawHt = drawHt;
            this.drawHm = drawHm;
            this.drawInterior = drawInterior;

            interiorNodeColor = new Color(1, 1, 1, 0.5f);
            htLeafColor = new Color(0, 0, 1, 0.5f);
            hmLeafColor = new Color(0, 1, 0, 0.5f);
        }
    }

    public void Draw(bool drawHt, bool drawHm, bool drawInterior)
    {
        DebugSettings settings = new DebugSettings(drawHt, drawHm, drawInterior);
        Draw(rootNodeKey, center, ref settings);
    }

    void Draw(int nodeKey, float3 nodeCenter, ref DebugSettings settings)
    {
        NodeData node = nodes[nodeKey];
        float nodeSize = GetNodeSize(totalSize, node.depth);

        if (node.type == NodeType.Homogeneous && settings.drawHm)
        {
            Gizmos.color = settings.hmLeafColor;
            Gizmos.DrawWireCube(nodeCenter, new float3(nodeSize));
        }
        else if (node.type == NodeType.Heterogeneous && settings.drawHt)
        {
            Gizmos.color = settings.htLeafColor;
            Gizmos.DrawWireCube(nodeCenter, new float3(nodeSize));
        }
        else if (node.type == NodeType.Interior)
        {
            Gizmos.color = settings.interiorNodeColor;
            if (settings.drawInterior) Gizmos.DrawWireCube(nodeCenter, new float3(nodeSize));

            if (node.childrenDataKey != -1)
            {
                ChildrenData childrenKeys = childrenPerNode[node.childrenDataKey];
                for (int i = 0; i < 8; i++)
                {
                    int childKey = childrenKeys[i];
                    if (childKey != -1)
                    {
                        float3 childCenter = nodeCenter + NODE_OFFSETS[i] * nodeSize / 2f;
                        Draw(childKey, childCenter, ref settings);
                    }
                }
            } 
        }
    }

    public void DebugNodeValues()
    {
        var intersectionsDict = new Dictionary<float3x2, List<byte>>();
        var materialsDict = new Dictionary<float3, List<byte>>();
        DebugNodeValues(rootNodeKey, float3.zero, intersectionsDict, materialsDict);

        foreach (var item in intersectionsDict)
        {
            if (item.Value.Exists(o => o != item.Value[0]))
                Debug.Log(String.Join(", ", item.Value.ToArray()));

            //Debug.Log(item.Key + "-------------------------------");
            //Debug.Log(String.Join(", ", item.Value.ToArray()));
        }

        Debug.Break();
    }

    void DebugNodeValues(int nodeKey, float3 nodeCenter, Dictionary<float3x2, List<byte>> intersectionsDict, Dictionary<float3, List<byte>> materialsDict)
    {
        var node = nodes[nodeKey];
        float nodeSize = GetNodeSize(totalSize, node.depth);
        
        if (node.depth == maxDepth - 1)
        {
            if (node.type == NodeType.Interior || node.type == NodeType.Heterogeneous)
            {
                for (int edge = 0; edge < 12; edge++)
                {
                    int2 corners = EDGE_TO_CORNERS[edge];
                    int corner1 = corners[0];
                    int corner2 = corners[1];

                    if (node.materials[corner1] != node.materials[corner2])
                    {
                        float3 corner1Position = nodeCenter + NODE_OFFSETS[corner1] * nodeSize;
                        float3 corner2Position = nodeCenter + NODE_OFFSETS[corner2] * nodeSize;

                        var key = new float3x2(corner1Position, corner2Position);
                        if (!intersectionsDict.ContainsKey(key)) intersectionsDict.Add(key, new List<byte>());

                        intersectionsDict[key].Add(intersectionsPerNode[node.intersectionsDataKey][edge]);
                    }
                }
            }

            for (int corner = 0; corner < 8; corner++)
            {
                float3 cornerPosition = nodeCenter + NODE_OFFSETS[corner] * nodeSize;

                var key = cornerPosition;
                if (!materialsDict.ContainsKey(key)) materialsDict.Add(key, new List<byte>());

                materialsDict[key].Add(node.materials[corner]);
            }
        }
        
        if (node.type == NodeType.Interior)
        {
            ChildrenData childrenKeys = childrenPerNode[node.childrenDataKey];
            for (int i = 0; i < 8; i++)
            {
                if (childrenKeys[i] != -1)
                {
                    DebugNodeValues(childrenKeys[i], GetChildCenter(nodeCenter, nodeSize * 0.5f, i), intersectionsDict, materialsDict);
                }
            }
        }
    }
    #endregion
}
