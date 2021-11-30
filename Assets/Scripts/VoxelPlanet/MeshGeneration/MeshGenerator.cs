using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using static GeometryUtility;
using static UnityEngine.Mesh;

public class MeshGenerator : MonoBehaviour
{
    public JobHandle GenerateSeamlessMesh(VoxelGrid mainChunk, VoxelGrid seamX, VoxelGrid seamY, VoxelGrid seamZ, 
        VoxelGrid seamXY, VoxelGrid seamYZ, VoxelGrid seamXZ,
        MeshData meshData, NativeList<byte> matIdPerSubmesh, out NativeReference<ABB> meshBoundsRef, JobHandle dependsOn = default)
    {
        meshBoundsRef = new NativeReference<ABB>(new ABB(), Allocator.Persistent);

        var job = new GenerateSeamlessMeshJob()
        {
            mainChunk = mainChunk,
            seamX = seamX,
            seamY = seamY,
            seamZ = seamZ,
            seamXY = seamXY,
            seamYZ = seamYZ,
            seamXZ = seamXZ,

            meshData = meshData,
            meshBoundsRef = meshBoundsRef,
            matIdPerSubmesh = matIdPerSubmesh,
        };

        return job.Schedule(dependsOn);
    }

    [BurstCompile(CompileSynchronously = true)]
    struct GenerateSeamlessMeshJob : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion] public VoxelGrid mainChunk;
        [ReadOnly, DeallocateOnJobCompletion] public VoxelGrid seamX;
        [ReadOnly, DeallocateOnJobCompletion] public VoxelGrid seamY;
        [ReadOnly, DeallocateOnJobCompletion] public VoxelGrid seamZ;
        [ReadOnly, DeallocateOnJobCompletion] public VoxelGrid seamXY;
        [ReadOnly, DeallocateOnJobCompletion] public VoxelGrid seamYZ;
        [ReadOnly, DeallocateOnJobCompletion] public VoxelGrid seamXZ;

        public MeshData meshData;
        public NativeReference<ABB> meshBoundsRef;
        public NativeList<byte> matIdPerSubmesh;

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

        enum Location { main, seamX, seamY, seamZ, seamXY, seamYZ, seamXZ };

        static readonly int3[] SEAM_OFFSETS =
        {
            new int3(0,0,0),
            new int3(1,0,0),
            new int3(0,1,0),
            new int3(0,0,1),
            new int3(1,1,0),
            new int3(0,1,1),
            new int3(1,0,1),
        };

        struct SeamEdge
        {
            public int3 start;
            public int3 end;

            public Location location;

            public SeamEdge(int3 start, int3 end, Location location)
            {
                this.start = start;
                this.end = end;
                this.location = location;
            }
        }

        struct CellToVertexIdMaps : IDisposable
        {
            NativeHashMap<int, ushort> cellToVertexId;
            NativeHashMap<int, ushort> cellToVertexId_seamX;
            NativeHashMap<int, ushort> cellToVertexId_seamY;
            NativeHashMap<int, ushort> cellToVertexId_seamZ;
            NativeHashMap<int, ushort> cellToVertexId_seamXY;
            NativeHashMap<int, ushort> cellToVertexId_seamYZ;
            NativeHashMap<int, ushort> cellToVertexId_seamXZ;

            public CellToVertexIdMaps(int capacity, Allocator allocator)
            {
                cellToVertexId = new NativeHashMap<int, ushort>(capacity, allocator);
                cellToVertexId_seamX = new NativeHashMap<int, ushort>(capacity/2, allocator);
                cellToVertexId_seamY = new NativeHashMap<int, ushort>(capacity/2, allocator);
                cellToVertexId_seamZ = new NativeHashMap<int, ushort>(capacity/2, allocator);
                cellToVertexId_seamXY = new NativeHashMap<int, ushort>(capacity/2, allocator);
                cellToVertexId_seamYZ = new NativeHashMap<int, ushort>(capacity/2, allocator);
                cellToVertexId_seamXZ = new NativeHashMap<int, ushort>(capacity/2, allocator);
            }

            public NativeHashMap<int, ushort> GetMap(Location location)
            {
                return location switch
                {
                    Location.main => cellToVertexId,
                    Location.seamX => cellToVertexId_seamX,
                    Location.seamY => cellToVertexId_seamY,
                    Location.seamZ => cellToVertexId_seamZ,
                    Location.seamXY => cellToVertexId_seamXY,
                    Location.seamYZ => cellToVertexId_seamYZ,
                    Location.seamXZ => cellToVertexId_seamXZ,
                    _ => default,
                };
            }

            public void Dispose()
            {
                cellToVertexId.Dispose();
                cellToVertexId_seamX.Dispose();
                cellToVertexId_seamY.Dispose();
                cellToVertexId_seamZ.Dispose();
                cellToVertexId_seamXY.Dispose();
                cellToVertexId_seamYZ.Dispose();
                cellToVertexId_seamXZ.Dispose();
            }
        }

        public void Execute()
        {
            // TODO: Allocate in main thread, before scheduling job (this is because its possible that there's not enough space for Temp allocations, slowing down the program)

            CellToVertexIdMaps cellToVertexIdMaps = new CellToVertexIdMaps(64, Allocator.Temp);

            NativeList<Vertex> vertices = new NativeList<Vertex>(64, Allocator.Temp);
            NativeMultiHashMap<byte, Triangle> triangles = new NativeMultiHashMap<byte, Triangle>(64, Allocator.Temp);

            ProcessMainChunk(cellToVertexIdMaps, vertices, triangles);

            ProcessSeamX(cellToVertexIdMaps, vertices, triangles);
            ProcessSeamY(cellToVertexIdMaps, vertices, triangles);
            ProcessSeamZ(cellToVertexIdMaps, vertices, triangles);

            ProcessSeamXY(cellToVertexIdMaps, vertices, triangles);
            ProcessSeamYZ(cellToVertexIdMaps, vertices, triangles);
            ProcessSeamXZ(cellToVertexIdMaps, vertices, triangles);

            cellToVertexIdMaps.Dispose();

            // Fill meshData
            FillMeshData(vertices, triangles, meshData, matIdPerSubmesh);
            vertices.Dispose();
            triangles.Dispose();
        }

        void ProcessMainChunk(CellToVertexIdMaps cellToVertexIdMaps, NativeList<Vertex> vertices, NativeMultiHashMap<byte, Triangle> triangles)
        {
            var cellToVertexId = cellToVertexIdMaps.GetMap(Location.main);
            int3 length3D = mainChunk.materials.GetLength3D();

            for (int x = 0; x < length3D.x - 1; x++)
            {
                for (int y = 0; y < length3D.y - 1; y++)
                {
                    for (int z = 0; z < length3D.z - 1; z++)
                    {
                        int3 edgeStart = new int3(x, y, z);
                        int3 edgeEndX = edgeStart + new int3(1, 0, 0);
                        int3 edgeEndY = edgeStart + new int3(0, 1, 0);
                        int3 edgeEndZ = edgeStart + new int3(0, 0, 1);

                        if (IsActiveEdge(edgeStart, edgeEndX, mainChunk.materials) && y > 0 && z > 0)
                        {
                            CreateFace(edgeStart, edgeEndX, cellToVertexId, vertices, triangles);
                        }
                        if (IsActiveEdge(edgeStart, edgeEndY, mainChunk.materials) && x > 0 && z > 0)
                        {
                            CreateFace(edgeStart, edgeEndY, cellToVertexId, vertices, triangles);
                        }
                        if (IsActiveEdge(edgeStart, edgeEndZ, mainChunk.materials) && x > 0 && y > 0)
                        {
                            CreateFace(edgeStart, edgeEndZ, cellToVertexId, vertices, triangles);
                        }
                    }
                }
            }
        }

        // TODO: One reusable/generic method for processing seams
        void ProcessSeamX(CellToVertexIdMaps cellToVertexIdMaps, NativeList<Vertex> vertices, NativeMultiHashMap<byte, Triangle> triangles)
        {
            int3 main_length3D = mainChunk.materials.GetLength3D();
            int3 seamX_length3D = seamX.materials.GetLength3D();

            int3 length3D;
            int x;
            Location edgeLocation;
            VoxelGrid edgeVoxelGrid;

            if (GetCellResolution(mainChunk) < GetCellResolution(seamX))
            {
                length3D = seamX_length3D;
                x = 0;
                edgeLocation = Location.seamX;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
            }
            else
            {
                length3D = main_length3D;
                x = length3D.x - 1;
                edgeLocation = Location.main;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
            }

            for (int y = 0; y < length3D.y - 1; y++)
            {
                for (int z = 0; z < length3D.z - 1; z++)
                {
                    int3 edgeStart = new int3(x, y, z);
                    int3 edgeEndY = edgeStart + new int3(0, 1, 0);
                    int3 edgeEndZ = edgeStart + new int3(0, 0, 1);

                    if (IsActiveEdge(edgeStart, edgeEndY, edgeVoxelGrid.materials) &&  z > 0)
                    {
                        SeamEdge seamEdge = new SeamEdge(edgeStart, edgeEndY, edgeLocation);
                        CreateSeamFace(seamEdge, cellToVertexIdMaps, vertices, triangles);
                    }

                    if (IsActiveEdge(edgeStart, edgeEndZ, edgeVoxelGrid.materials) && y > 0)
                    {
                        SeamEdge seamEdge = new SeamEdge(edgeStart, edgeEndZ, edgeLocation);
                        CreateSeamFace(seamEdge, cellToVertexIdMaps, vertices, triangles);
                    }
                }   
            }  
        }

        void ProcessSeamY(CellToVertexIdMaps cellToVertexIdMaps, NativeList<Vertex> vertices, NativeMultiHashMap<byte, Triangle> triangles)
        {
            int3 main_length3D = mainChunk.materials.GetLength3D();
            int3 seamY_length3D = seamY.materials.GetLength3D();

            int3 length3D;
            int y;
            Location edgeLocation;
            VoxelGrid edgeVoxelGrid;

            if (GetCellResolution(mainChunk) < GetCellResolution(seamY))
            {
                length3D = seamY_length3D;
                y = 0;
                edgeLocation = Location.seamY;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
            }
            else
            {
                length3D = main_length3D;
                y = length3D.y - 1;
                edgeLocation = Location.main;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
            }

            for (int x = 0; x < length3D.x - 1; x++)
            {
                for (int z = 0; z < length3D.z - 1; z++)
                {
                    int3 edgeStart = new int3(x, y, z);
                    int3 edgeEndX = edgeStart + new int3(1, 0, 0);
                    int3 edgeEndZ = edgeStart + new int3(0, 0, 1);

                    if (IsActiveEdge(edgeStart, edgeEndX, edgeVoxelGrid.materials) && z > 0)
                    {
                        SeamEdge seamEdge = new SeamEdge(edgeStart, edgeEndX, edgeLocation);
                        CreateSeamFace(seamEdge, cellToVertexIdMaps, vertices, triangles);
                    }

                    if (IsActiveEdge(edgeStart, edgeEndZ, edgeVoxelGrid.materials) && x > 0)
                    {
                        SeamEdge seamEdge = new SeamEdge(edgeStart, edgeEndZ, edgeLocation);
                        CreateSeamFace(seamEdge, cellToVertexIdMaps, vertices, triangles);
                    }
                }
            }
        }

        void ProcessSeamZ(CellToVertexIdMaps cellToVertexIdMaps, NativeList<Vertex> vertices, NativeMultiHashMap<byte, Triangle> triangles)
        {
            int3 main_length3D = mainChunk.materials.GetLength3D();
            int3 seamZ_length3D = seamZ.materials.GetLength3D();

            int3 length3D;
            int z;
            Location edgeLocation;
            VoxelGrid edgeVoxelGrid;

            if (GetCellResolution(mainChunk) < GetCellResolution(seamZ))
            {
                length3D = seamZ_length3D;
                z = 0;
                edgeLocation = Location.seamZ;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
            }
            else
            {
                length3D = main_length3D;
                z = length3D.z - 1;
                edgeLocation = Location.main;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
            }

            for (int x = 0; x < length3D.x - 1; x++)
            {
                for (int y = 0; y < length3D.y - 1; y++)
                {
                    int3 edgeStart = new int3(x, y, z);
                    int3 edgeEndX = edgeStart + new int3(1, 0, 0);
                    int3 edgeEndY = edgeStart + new int3(0, 1, 0);

                    if (IsActiveEdge(edgeStart, edgeEndX, edgeVoxelGrid.materials) && y > 0)
                    {
                        SeamEdge seamEdge = new SeamEdge(edgeStart, edgeEndX, edgeLocation);
                        CreateSeamFace(seamEdge, cellToVertexIdMaps, vertices, triangles);
                    }

                    if (IsActiveEdge(edgeStart, edgeEndY, edgeVoxelGrid.materials) && x > 0)
                    {
                        SeamEdge seamEdge = new SeamEdge(edgeStart, edgeEndY, edgeLocation);
                        CreateSeamFace(seamEdge, cellToVertexIdMaps, vertices, triangles);
                    }
                }
            }
        }
        
        void ProcessSeamXY(CellToVertexIdMaps cellToVertexIdMaps, NativeList<Vertex> vertices, NativeMultiHashMap<byte, Triangle> triangles)
        {
            Location edgeLocation;
            VoxelGrid edgeVoxelGrid;
            int3 length3D;
            int x, y;

            int main_cellResolution = GetCellResolution(mainChunk);
            int seamX_cellResolution = GetCellResolution(seamX);
            int seamY_cellResolution = GetCellResolution(seamY);
            int seamXY_cellResolution = GetCellResolution(seamXY);

            if (main_cellResolution >= math.max(seamX_cellResolution, math.max(seamY_cellResolution, seamXY_cellResolution)) )
            {
                edgeLocation = Location.main;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                x = length3D.x - 1;
                y = length3D.y - 1;

            }
            else if (seamXY_cellResolution >= math.max(seamX_cellResolution, seamY_cellResolution))
            {
                edgeLocation = Location.seamXY;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                x = 0;
                y = 0;

            }
            else if (seamX_cellResolution >= seamY_cellResolution)
            {
                edgeLocation = Location.seamX;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                x = 0;
                y = length3D.y - 1;
            }
            else
            {
                edgeLocation = Location.seamY;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                x = length3D.x - 1;
                y = 0;
            }

            for (int z = 0; z < length3D.z - 1; z++)
            {
                int3 edgeStart = new int3(x, y, z);
                int3 edgeEndZ = edgeStart + new int3(0, 0, 1);

                if (IsActiveEdge(edgeStart, edgeEndZ, edgeVoxelGrid.materials))
                {
                    SeamEdge seamEdge = new SeamEdge(edgeStart, edgeEndZ, edgeLocation);
                    CreateSeamFace(seamEdge, cellToVertexIdMaps, vertices, triangles);
                }
            }
        }

        void ProcessSeamYZ(CellToVertexIdMaps cellToVertexIdMaps, NativeList<Vertex> vertices, NativeMultiHashMap<byte, Triangle> triangles)
        {
            Location edgeLocation;
            VoxelGrid edgeVoxelGrid;
            int3 length3D;
            int y, z;

            int main_cellResolution = GetCellResolution(mainChunk);
            int seamY_cellResolution = GetCellResolution(seamY);
            int seamZ_cellResolution = GetCellResolution(seamZ);
            int seamYZ_cellResolution = GetCellResolution(seamYZ);

            if (main_cellResolution >= math.max(seamY_cellResolution, math.max(seamZ_cellResolution, seamYZ_cellResolution)))
            {
                edgeLocation = Location.main;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                y = length3D.y - 1;
                z = length3D.z - 1;
            }
            else if (seamYZ_cellResolution >= math.max(seamY_cellResolution, seamZ_cellResolution))
            {
                edgeLocation = Location.seamYZ;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                y = 0;
                z = 0;
            }
            else if (seamY_cellResolution >= seamZ_cellResolution)
            {
                edgeLocation = Location.seamY;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                y = 0;
                z = length3D.z - 1;
            }
            else
            {
                edgeLocation = Location.seamZ;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                y = length3D.y - 1;
                z = 0;
            }

            for (int x = 0; x < length3D.x - 1; x++)
            {
                int3 edgeStart = new int3(x, y, z);
                int3 edgeEndX = edgeStart + new int3(1, 0, 0);

                if (IsActiveEdge(edgeStart, edgeEndX, edgeVoxelGrid.materials))
                {
                    SeamEdge seamEdge = new SeamEdge(edgeStart, edgeEndX, edgeLocation);
                    CreateSeamFace(seamEdge, cellToVertexIdMaps, vertices, triangles);
                }
            }
        }

        void ProcessSeamXZ(CellToVertexIdMaps cellToVertexIdMaps, NativeList<Vertex> vertices, NativeMultiHashMap<byte, Triangle> triangles)
        {
            Location edgeLocation;
            VoxelGrid edgeVoxelGrid;
            int3 length3D;
            int x, z;

            int main_cellResolution = GetCellResolution(mainChunk);
            int seamX_cellResolution = GetCellResolution(seamX);
            int seamZ_cellResolution = GetCellResolution(seamZ);
            int seamXZ_cellResolution = GetCellResolution(seamXZ);

            if (main_cellResolution >= math.max(seamX_cellResolution, math.max(seamZ_cellResolution, seamXZ_cellResolution)))
            {
                edgeLocation = Location.main;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                x = length3D.x - 1;
                z = length3D.z - 1;
            }
            else if (seamXZ_cellResolution >= math.max(seamX_cellResolution, seamZ_cellResolution))
            {
                edgeLocation = Location.seamXZ;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                x = 0;
                z = 0;
            }
            else if (seamX_cellResolution >= seamZ_cellResolution)
            {
                edgeLocation = Location.seamX;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                x = 0;
                z = length3D.z - 1;
            }
            else
            {
                edgeLocation = Location.seamZ;
                edgeVoxelGrid = GetVoxelGrid(edgeLocation);
                length3D = edgeVoxelGrid.materials.GetLength3D();

                x = length3D.x - 1;
                z = 0;
            }

            for (int y = 0; y < length3D.y - 1; y++)
            {
                int3 edgeStart = new int3(x, y, z);
                int3 edgeEndY = edgeStart + new int3(0, 1, 0);

                if (IsActiveEdge(edgeStart, edgeEndY, edgeVoxelGrid.materials))
                {
                    SeamEdge seamEdge = new SeamEdge(edgeStart, edgeEndY, edgeLocation);
                    CreateSeamFace(seamEdge, cellToVertexIdMaps, vertices, triangles);
                }
            }
        }

        bool IsActiveEdge(int3 edgeStart, int3 edgeEnd, NativeArray3D<byte> materials)
        {
            return (materials[edgeStart] == 0 && materials[edgeEnd] != 0) || (materials[edgeStart] != 0 && materials[edgeEnd] == 0);
        }

        void CreateFace(int3 edgeStart, int3 edgeEnd, NativeHashMap<int, ushort> cellToVertexId, NativeList<Vertex> vertices, NativeMultiHashMap<byte, Triangle> triangles)
        {
            VoxelGrid voxelGrid = mainChunk;
            byte edgeMaterial = (byte)(voxelGrid.materials[edgeStart] + voxelGrid.materials[edgeEnd]);

            GetEdgeAdjacentCellsOffsets(edgeStart, edgeEnd, voxelGrid,
                out int3 offset1, out int3 offset2, out int3 offset3, out int3 offset4);

            int3 adjacentCell1 = edgeStart + offset1;
            int3 adjacentCell2 = edgeStart + offset2;
            int3 adjacentCell3 = edgeStart + offset3;
            int3 adjacentCell4 = edgeStart + offset4;

            ushort vertexId1 = TryCreateAndGetVertexId(adjacentCell1, voxelGrid, cellToVertexId, vertices);
            ushort vertexId2 = TryCreateAndGetVertexId(adjacentCell2, voxelGrid, cellToVertexId, vertices);
            ushort vertexId3 = TryCreateAndGetVertexId(adjacentCell3, voxelGrid, cellToVertexId, vertices);
            ushort vertexId4 = TryCreateAndGetVertexId(adjacentCell4, voxelGrid, cellToVertexId, vertices);

            Triangle triangle1 = new Triangle(vertexId1, vertexId2, vertexId3);
            Triangle triangle2 = new Triangle(vertexId1, vertexId3, vertexId4);

            triangles.Add(edgeMaterial, triangle1);
            triangles.Add(edgeMaterial, triangle2);
        }

        void CreateSeamFace(SeamEdge seamEdge, CellToVertexIdMaps cellToVertexIdMaps, NativeList<Vertex> vertices, NativeMultiHashMap<byte, Triangle> triangles)
        {
            VoxelGrid edgeVoxelGrid = GetVoxelGrid(seamEdge.location);
            byte edgeMaterial = (byte)(edgeVoxelGrid.materials[seamEdge.start] + edgeVoxelGrid.materials[seamEdge.end]);

            GetEdgeAdjacentCellsOffsets(seamEdge.start, seamEdge.end, edgeVoxelGrid,
                out int3 offset1, out int3 offset2, out int3 offset3, out int3 offset4);

            GetLocalAdjacentCell(seamEdge, offset1, out int3 adjacentCell1, out Location adjacentCell1Location);
            GetLocalAdjacentCell(seamEdge, offset2, out int3 adjacentCell2, out Location adjacentCell2Location);
            GetLocalAdjacentCell(seamEdge, offset3, out int3 adjacentCell3, out Location adjacentCell3Location);
            GetLocalAdjacentCell(seamEdge, offset4, out int3 adjacentCell4, out Location adjacentCell4Location);

            ushort vertexId1 = TryCreateAndGetVertexId(adjacentCell1, GetVoxelGrid(adjacentCell1Location), cellToVertexIdMaps.GetMap(adjacentCell1Location), vertices);
            ushort vertexId2 = TryCreateAndGetVertexId(adjacentCell2, GetVoxelGrid(adjacentCell2Location), cellToVertexIdMaps.GetMap(adjacentCell2Location), vertices);
            ushort vertexId3 = TryCreateAndGetVertexId(adjacentCell3, GetVoxelGrid(adjacentCell3Location), cellToVertexIdMaps.GetMap(adjacentCell3Location), vertices);
            ushort vertexId4 = TryCreateAndGetVertexId(adjacentCell4, GetVoxelGrid(adjacentCell4Location), cellToVertexIdMaps.GetMap(adjacentCell4Location), vertices);

            // Check repeated vertices (triangle case when joinning differents LODs)
            if (vertexId1 == vertexId2 || vertexId1 == vertexId3 || vertexId1 == vertexId4)
            {
                Triangle triangle = new Triangle(vertexId2, vertexId3, vertexId4);
                triangles.Add(edgeMaterial, triangle);
            }
            else if (vertexId2 == vertexId3 || vertexId2 == vertexId4)
            {
                Triangle triangle = new Triangle(vertexId1, vertexId3, vertexId4);
                triangles.Add(edgeMaterial, triangle);
            }
            else if (vertexId3 == vertexId4)
            {
                Triangle triangle = new Triangle(vertexId1, vertexId2, vertexId4);
                triangles.Add(edgeMaterial, triangle);
            }
            else
            {
                Triangle triangle1 = new Triangle(vertexId1, vertexId2, vertexId3);
                Triangle triangle2 = new Triangle(vertexId1, vertexId3, vertexId4);

                triangles.Add(edgeMaterial, triangle1);
                triangles.Add(edgeMaterial, triangle2);
            }
        }

        // Get adjacent cell positions in clokwise order if edge (solid -> empty), counterclockwise otherwise
        void GetEdgeAdjacentCellsOffsets(int3 edgeStart, int3 edgeEnd, VoxelGrid voxelGrid, out int3 offset1, out int3 offset2, out int3 offset3, out int3 offset4)
        {
            int3 edgeAxis = math.abs(edgeEnd - edgeStart);

            var materials = voxelGrid.materials;

            offset1 = int3.zero;
            if (edgeAxis.x == 1)
            {
                offset2 = new int3(0, -1, 0);
                offset3 = new int3(0, -1, -1);
                offset4 = new int3(0, 0, -1);
            }
            else if (edgeAxis.y == 1)
            {
                offset2 = new int3(0, 0, -1);
                offset3 = new int3(-1, 0, -1);
                offset4 = new int3(-1, 0, 0);
            }
            else
            {
                offset2 = new int3(-1, 0, 0);
                offset3 = new int3(-1, -1, 0);
                offset4 = new int3(0, -1, 0);
            }

            // if edge in inverse direction (empty -> solid)
            if (materials[edgeEnd] > 0)
            {
                // change order adjacent cells (clockwise -> counterclockwise)
                int3 aux = offset2;
                offset2 = offset4;
                offset4 = aux;
            }
        }

        void GetLocalAdjacentCell(SeamEdge seamEdge, int3 offset, out int3 adjacentCell, out Location adjacentCellLocation)
        {
            VoxelGrid edgeVoxelGrid = GetVoxelGrid(seamEdge.location);
            int edgeLocationResolution = GetCellResolution(edgeVoxelGrid);

            // position relative to the mainChunk origin and the using resolution of the chunk/seam where the seam edge belongs (the one with more detail/more length)
            int3 adjacentCellGlobalPosition = seamEdge.start + offset + SEAM_OFFSETS[(int)seamEdge.location] * new int3(edgeLocationResolution);

            int3 cellLocationOffset = adjacentCellGlobalPosition - new int3(edgeLocationResolution - 1);
            cellLocationOffset = math.clamp(cellLocationOffset, int3.zero, new int3(1, 1, 1));

            adjacentCellLocation = GetLocation(cellLocationOffset);
            int cellLocationResolution = GetCellResolution(GetVoxelGrid(adjacentCellLocation));

            Debug.Assert(edgeLocationResolution >= cellLocationResolution);

            int LODfactor = (edgeLocationResolution / cellLocationResolution); // always >= 1 (seamEdge belongs to the most detailed location)
            adjacentCell = (adjacentCellGlobalPosition - SEAM_OFFSETS[(int)adjacentCellLocation] * new int3(edgeLocationResolution)) / LODfactor;
        }

        ushort TryCreateAndGetVertexId(int3 cellOrigin, VoxelGrid voxelGrid, NativeHashMap<int, ushort> cellToVertexId, NativeList<Vertex> vertices) 
        {
            int key = voxelGrid.materials.GetIndex1D(cellOrigin);

            if (!cellToVertexId.TryGetValue(key, out ushort vertexId))
            {
                float3 vertexPosition = GetVertexPosition(cellOrigin, voxelGrid);
                vertices.Add(new Vertex(vertexPosition));

                vertexId = (ushort)(vertices.Length - 1);
                cellToVertexId.Add(key, vertexId);

                UpdateMeshBounds(vertexPosition);
            }

            return vertexId;
        }

        float3 GetVertexPosition(int3 cellOrigin, VoxelGrid voxelGrid)
        {
            int x = cellOrigin.x;
            int y = cellOrigin.y;
            int z = cellOrigin.z;

            var materials = voxelGrid.materials;
            var intersections = voxelGrid.intersections;
            var positions = voxelGrid.positions;

            // load cell data per corner

            FixedArray8<byte> cellMaterials = new FixedArray8<byte>();
            FixedArray8<float3> cellPositions = new FixedArray8<float3>();
            FixedArray8<CornerIntersections> cellCornerIntersections = new FixedArray8<CornerIntersections>();

            int corner0ChunkId = materials.GetIndex1D(x, y, z);
            int corner1ChunkId = materials.GetIndex1D(x, y, z + 1);
            int corner2ChunkId = materials.GetIndex1D(x, y + 1, z);
            int corner3ChunkId = materials.GetIndex1D(x, y + 1, z + 1);
            int corner4ChunkId = materials.GetIndex1D(x + 1, y, z);
            int corner5ChunkId = materials.GetIndex1D(x + 1, y, z + 1);
            int corner6ChunkId = materials.GetIndex1D(x + 1, y + 1, z);
            int corner7ChunkId = materials.GetIndex1D(x + 1, y + 1, z + 1);

            float3 center = (positions[corner0ChunkId] + positions[corner7ChunkId]) / 2f;
            // minecraft mode (easy debug view, vertex at center of each cell)
            //return center;

            cellMaterials[0] = materials[corner0ChunkId];
            cellMaterials[1] = materials[corner1ChunkId];
            cellMaterials[2] = materials[corner2ChunkId];
            cellMaterials[3] = materials[corner3ChunkId];
            cellMaterials[4] = materials[corner4ChunkId];
            cellMaterials[5] = materials[corner5ChunkId];
            cellMaterials[6] = materials[corner6ChunkId];
            cellMaterials[7] = materials[corner7ChunkId];

            cellPositions[0] = positions[corner0ChunkId];
            cellPositions[1] = positions[corner1ChunkId];
            cellPositions[2] = positions[corner2ChunkId];
            cellPositions[3] = positions[corner3ChunkId];
            cellPositions[4] = positions[corner4ChunkId];
            cellPositions[5] = positions[corner5ChunkId];
            cellPositions[6] = positions[corner6ChunkId];
            cellPositions[7] = positions[corner7ChunkId];

            cellCornerIntersections[0] = intersections[corner0ChunkId];
            cellCornerIntersections[1] = intersections[corner1ChunkId];
            cellCornerIntersections[2] = intersections[corner2ChunkId];
            cellCornerIntersections[3] = intersections[corner3ChunkId];
            cellCornerIntersections[4] = intersections[corner4ChunkId];
            cellCornerIntersections[5] = intersections[corner5ChunkId];
            cellCornerIntersections[6] = intersections[corner6ChunkId];
            cellCornerIntersections[7] = intersections[corner7ChunkId];

            // use centroid of intersection points as vertex position

            float3 position = float3.zero;
            int count = 0;
            for (int edge = 0; edge < 12; edge++)
            {
                int2 corners = EDGE_TO_CORNERS[edge];
                int corner1 = corners.x;
                int corner2 = corners.y;

                if (cellMaterials[corner1] != cellMaterials[corner2] )//&& (cellMaterials[corner1] == 0 || cellMaterials[corner2] == 0))
                {
                    float3 position1 = cellPositions[corner1];
                    float3 position2 = cellPositions[corner2];

                    float3 direction = position2 - position1;
                    float3 intersectionPoint;
                    if (direction.x > 0.1f)
                    {
                        intersectionPoint = position1 + (cellCornerIntersections[corner1].x / 255f) * math.abs(position2 - position1);
                    }
                    else if (direction.y > 0.1f)
                    {
                        intersectionPoint = position1 + (cellCornerIntersections[corner1].y / 255f) * math.abs(position2 - position1);
                    }
                    else
                    {
                        intersectionPoint = position1 + (cellCornerIntersections[corner1].z / 255f) * math.abs(position2 - position1);
                    }

                    position += intersectionPoint;
                    count++;
                }
            }

            if (count == 0) //unactive cell, just return middle point
            {
                return (cellPositions[0] + cellPositions[7]) / 2f;
            }
            return position / count;
        }

        void UpdateMeshBounds(float3 newPoint)
        {
            ABB meshBounds = meshBoundsRef.Value;

            if (newPoint.x < meshBounds.min.x) meshBounds.min.x = newPoint.x;
            if (newPoint.y < meshBounds.min.y) meshBounds.min.y = newPoint.y;
            if (newPoint.z < meshBounds.min.z) meshBounds.min.z = newPoint.z;

            if (newPoint.x > meshBounds.max.x) meshBounds.max.x = newPoint.x;
            if (newPoint.y > meshBounds.max.y) meshBounds.max.y = newPoint.y;
            if (newPoint.z > meshBounds.max.z) meshBounds.max.z = newPoint.z;

            meshBoundsRef.Value = meshBounds;
        }

        VoxelGrid GetVoxelGrid(Location location)
        {
            return location switch
            {
                Location.main => mainChunk,
                Location.seamX => seamX,
                Location.seamY => seamY,
                Location.seamZ => seamZ,
                Location.seamXY => seamXY,
                Location.seamYZ => seamYZ,
                Location.seamXZ => seamXZ,
                _ => default,
            };
        }

        Location GetLocation(int3 locationOffset)
        {
            if (locationOffset.Equals(new int3(0, 0, 0)))
            {
                return Location.main;
            }
            else if (locationOffset.Equals(new int3(1, 0, 0)))
            {
                return Location.seamX;
            }
            else if (locationOffset.Equals(new int3(0, 1, 0)))
            {
                return Location.seamY;
            }
            else if (locationOffset.Equals(new int3(0, 0, 1)))
            {
                return Location.seamZ;
            }
            else if (locationOffset.Equals(new int3(1, 1, 0)))
            {
                return Location.seamXY;
            }
            else if (locationOffset.Equals(new int3(0, 1, 1)))
            {
                return Location.seamYZ;
            }
            else if (locationOffset.Equals(new int3(1, 0, 1)))
            {
                return Location.seamXZ;
            }
            else
            {
                return default;
            }
        }

        int GetCellResolution(VoxelGrid voxelGrid)
        {
            int3 resolution = voxelGrid.materials.GetLength3D();
            return math.max(resolution.x, math.max(resolution.y, resolution.z)) - 1;
        }
    }

    static void FillMeshData(in NativeList<Vertex> vertices, in NativeMultiHashMap<byte, Triangle> triangles, MeshData meshData, NativeList<byte> matIdPerSubmesh)
    {
        //VERTEX BUFFER DATA
        NativeArray<VertexAttributeDescriptor> nativeLayout = new NativeArray<VertexAttributeDescriptor>(1, Allocator.Temp);
        nativeLayout[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);

        meshData.SetVertexBufferParams(vertices.Length, nativeLayout);
        var meshVertices = meshData.GetVertexData<Vertex>();
        for (int i = 0; i < vertices.Length; i++)
            meshVertices[i] = vertices[i];

        //INDICES
        //var usedMaterialsIds = new NativeList<byte>(Allocator.Temp);
        int triangleCount = 0;
        for (byte m = 1; m < 255; m++)
        {
            if (triangles.ContainsKey(m))
            {
                matIdPerSubmesh.Add(m);
                triangleCount += triangles.CountValuesForKey(m);
            }
        }

        meshData.SetIndexBufferParams(triangleCount * 3, IndexFormat.UInt16);
        meshData.subMeshCount = matIdPerSubmesh.Length;

        var ib = meshData.GetIndexData<Triangle>();
        int k = 0;
        for (int j = 0; j < matIdPerSubmesh.Length; j++)
        {
            int submeshTriangleStart = k;
            int submeshTriangleCount = 0;

            var enumerator = triangles.GetValuesForKey(matIdPerSubmesh[j]);
            while (enumerator.MoveNext())
            {
                Triangle currentTriangle = enumerator.Current;
                ib[k] = currentTriangle; k++; submeshTriangleCount++;
            }

            meshData.SetSubMesh(j, new SubMeshDescriptor(submeshTriangleStart * 3, submeshTriangleCount * 3));
        }
    }

    public struct Vertex
    {
        public float3 pos;

        public Vertex(float3 pos)
        {
            this.pos = pos;
        }
    }

    public struct Triangle
    {
        //indices pointing to vertices
        public ushort i1;
        public ushort i2;
        public ushort i3;

        public Triangle(ushort i1, ushort i2, ushort i3)
        {
            this.i1 = i1;
            this.i2 = i2;
            this.i3 = i3;
        }
    }
}

