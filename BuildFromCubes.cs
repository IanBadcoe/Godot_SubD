using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SubD;

using VIdx = SubD.Idx<SubD.Vert>;
using EIdx = SubD.Idx<SubD.Edge>;
using PIdx = SubD.Idx<SubD.Poly>;
using System.Diagnostics;

namespace SubD
{
    [DebuggerDisplay("Count = {Cubes.Count}")]
    public class BuildFromCubes
    {
        public struct IdxCube
        {
            public VIdx[] VertIdxs
            {
                get;
                private set;
            }

            public int Group
            {
                get;
                private set;
            }

            public IdxCube(IEnumerable<VIdx> verts, int group)
            {
                VertIdxs = verts.ToArray();
                Group = group;
            }
        }

        Vector3[] VertOffsets = [
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f,  0.5f, -0.5f),

            new Vector3(-0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f,  0.5f,  0.5f),
            new Vector3(-0.5f,  0.5f,  0.5f),
        ];

        int[][] FaceIdxs = new int[][]
        {
            new int[] {
                0, 1, 2, 3
            },
            new int[] {
                1, 0, 4, 5
            },
            new int[] {
                4, 0, 3, 7
            },
            new int[] {
                4, 7, 6, 5
            },
            new int[] {
                6, 7, 3, 2
            },
            new int[] {
                6, 2, 1, 5
            },
        };

        Dictionary<Vector3I, int> Cubes = new Dictionary<Vector3I, int>();

        public void SetCube(Vector3I position, int group = 0)
        {
            Cubes[position] = group;
        }

        public void RemoveCube(Vector3I position)
        {
            Cubes.Remove(position);
        }

        public Surface ToSurface()
        {
            BidirectionalDictionary<VIdx, Vert> verts = new BidirectionalDictionary<VIdx, Vert>();

            List<IdxCube> idx_cubes = new List<IdxCube>();

            foreach(var pair in Cubes)
            {
                idx_cubes.Add(new IdxCube(CubeToIdxVerts(verts, pair.Key), pair.Value));
            }

            BidirectionalDictionary<EIdx, Edge> edges = new BidirectionalDictionary<EIdx, Edge>();

            BidirectionalDictionary<PIdx, Poly> polys = new BidirectionalDictionary<PIdx, Poly>();

            foreach(var cube in idx_cubes)
            {
                foreach(int[] locally_indexed_face in FaceIdxs)
                {
                    List<EIdx> face_edges = new List<EIdx>();
                    List<Edge> left_edges = new List<Edge>();
                    List<Edge> right_edges = new List<Edge>();

                    VIdx[] globally_indexed_face = locally_indexed_face.Select(x => cube.VertIdxs[x]).ToArray();

                    VIdx prev_v_idx = globally_indexed_face.Last();

                    foreach(VIdx v_idx in globally_indexed_face)
                    {
                        Edge edge = new Edge(prev_v_idx, v_idx);
                        Edge r_edge = edge.Reversed();

                        if (edges.Contains(edge))
                        {
                            // shouldn't ever happen, because the first time we see the edge we should add it forwards
                            // (second clause following) and the second time we see it we should want to use it backwards
                            // (next clause)
                            Debug.Assert(false);
                        }
                        else if (edges.Contains(r_edge))
                        {
                            EIdx e_idx = edges[r_edge];
                            face_edges.Add(e_idx);
                            // store the real dictionary member for setting its "Left" later
                            left_edges.Add(edges[e_idx]);
                        }
                        else
                        {
                            EIdx e_idx = new EIdx(edges.Count);

                            edges[edge] = e_idx;
                            edges[e_idx] = edge;
                            face_edges.Add(e_idx);
                            // store the real dictionary member for setting its "Right" later
                            right_edges.Add(edge);
                        }

                        prev_v_idx = v_idx;
                    }

                    Poly poly = new Poly(globally_indexed_face, face_edges);

                    PIdx p_idx = new(polys.Count);
                    polys[poly] = p_idx;


                    // forward edges will have the new face on their right
                    // backward ones on the left...
                    foreach(Edge edge in left_edges)
                    {
                        edge.Left = p_idx;
                    }

                    foreach(Edge edge in right_edges)
                    {
                        edge.Right = p_idx;
                    }

                    foreach(Edge edge in right_edges.Concat(left_edges))
                    {

                    }
                }
            }

            return new Surface(verts, edges, polys);
        }

        IEnumerable<VIdx> CubeToIdxVerts(BidirectionalDictionary<VIdx, Vert> known_verts, Vector3I position)
        {
            foreach(var offset in VertOffsets)
            {
                Vector3 pos = (Vector3)position + offset;
                Vert vert = new Vert(pos);

                if (!known_verts.Contains(vert))
                {
                    known_verts[vert] = new VIdx(known_verts.Count);
                }

                yield return known_verts[vert];
            }
        }
    }
}