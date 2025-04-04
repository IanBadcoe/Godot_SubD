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
        static int NextVertIdx = 0;
        static int NextEdgeIdx = 0;
        static int NextPolyIdx = 0;

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

        BidirectionalDictionary<VIdx, Vert> Verts = new BidirectionalDictionary<VIdx, Vert>();
        BidirectionalDictionary<EIdx, Edge> Edges = new BidirectionalDictionary<EIdx, Edge>();
        BidirectionalDictionary<PIdx, Poly> Polys = new BidirectionalDictionary<PIdx, Poly>();

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
            List<IdxCube> idx_cubes = new List<IdxCube>();

            foreach(var pair in Cubes)
            {
                idx_cubes.Add(new IdxCube(CubeToIdxVerts(pair.Key), pair.Value));
            }

            foreach(var cube in idx_cubes)
            {
                List<VIdx[]> real_faces = new List<VIdx[]>();

                // if any face is the negative of some other face, then instead of adding this one, we need to remove the other
                // so that the two cubes join
                //
                // and we need to do that first, because edges can only have one Left or Right, and we might add a new
                // face that wants to take an edge from an old one
                foreach(int[] locally_indexed_face in FaceIdxs)
                {
                    VIdx[] globally_indexed_face = locally_indexed_face.Select(x => cube.VertIdxs[x]).ToArray();

                    if (!ResolveOppositeFaces(globally_indexed_face))
                    {
                        real_faces.Add(globally_indexed_face);
                    }
                }

                foreach(VIdx[] globally_indexed_face in real_faces)
                {
                    List<EIdx> face_edges = new List<EIdx>();
                    List<Edge> left_edges = new List<Edge>();
                    List<Edge> right_edges = new List<Edge>();

                    VIdx prev_v_idx = globally_indexed_face.Last();

                    foreach(VIdx v_idx in globally_indexed_face)
                    {
                        Edge edge = new Edge(prev_v_idx, v_idx);
                        Edge r_edge = edge.Reversed();

                        if (Edges.Contains(edge))
                        {
                            // this can only happen, if we had a face using this edge already, in this direction, and subsequently removed it
                            // because otherwise, we see each edge exactly twice, first time forwards (second following clause)
                            // second time backwards (next clause)
                            EIdx e_idx = Edges[edge];
                            face_edges.Add(e_idx);
                            // store the real dictionary member for setting its "Left" later
                            right_edges.Add(Edges[e_idx]);
                        }
                        else if (Edges.Contains(r_edge))
                        {
                            EIdx e_idx = Edges[r_edge];
                            face_edges.Add(e_idx);
                            // store the real dictionary member for setting its "Left" later
                            left_edges.Add(Edges[e_idx]);
                        }
                        else
                        {
                            EIdx e_idx = new EIdx(NextEdgeIdx++);

                            Edges[edge] = e_idx;
                            face_edges.Add(e_idx);
                            // store the real dictionary member for setting its "Right" later
                            right_edges.Add(edge);

                            // it's a new edge, so let the two verts know
                            Verts[edge.Start].AddEIdx(e_idx);
                            Verts[edge.End].AddEIdx(e_idx);
                        }

                        prev_v_idx = v_idx;
                    }

                    Poly poly = new Poly(globally_indexed_face, face_edges);

                    PIdx p_idx = new(NextPolyIdx++);
                    Polys[poly] = p_idx;

                    // it's a new poly, so let all the verts know
                    foreach(Vert vert in poly.VIdxs.Select(x => Verts[x]))
                    {
                        vert.AddPIdx(p_idx);
                    }

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
                }
            }

            return new Surface(Verts, Edges, Polys);
        }

        private bool ResolveOppositeFaces(VIdx[] v_idxs)
        {
            VIdx[] v_r_temp = Poly.StandardiseVIdxOrder(v_idxs.Reverse()).ToArray();

#if DEBUG
            VIdx[] v_temp = Poly.StandardiseVIdxOrder(v_idxs).ToArray();;
#endif

            foreach(var pair in Polys)
            {
#if DEBUG
                // we should *never* find the same face come up twice the same way around
                Debug.Assert(!pair.Value.VIdxs.SequenceEqual(v_temp));
#endif

                // if we see the reverse of an existing face, they cancel...
                if (pair.Value.VIdxs.SequenceEqual(v_r_temp))
                {
                    RemovePoly(pair.Key);

                    return true;
                }
            }

            return false;
        }

        private void RemovePoly(PIdx p_idx)
        {
            Poly poly = Polys.Remove(p_idx);

            List<Tuple<EIdx, Edge>> removed_edges = new List<Tuple<EIdx, Edge>>();

            foreach(Edge edge in poly.EIdxs.Select(x => Edges[x]))
            {
                Debug.Assert(edge.PIdxs.Contains(p_idx));

                edge.RemovePoly(p_idx);

                if (!edge.PIdxs.Any())
                {
                    EIdx e_idx = Edges[edge];

                    Edges.Remove(edge);

                    removed_edges.Add(new Tuple<EIdx, Edge>(e_idx, edge));
                }
            }

            foreach(Vert vert in poly.VIdxs.Select(x => Verts[x]))
            {
                vert.RemovePoly(p_idx);
            }

            foreach(var pair in removed_edges)
            {
                foreach(Vert vert in pair.Item2.VIdxs.Select(x => Verts[x]))
                {
                    Debug.Assert(vert.EIdxs.Contains(pair.Item1));

                    vert.RemoveEdge(pair.Item1);

                    if (!vert.EIdxs.Any())
                    {
                        // vert realy should become empty of edges and polys at the same time...
                        Debug.Assert(!vert.PIdxs.Any());

                        Verts.Remove(vert);
                    }
                }
            }
        }

        IEnumerable<VIdx> CubeToIdxVerts(Vector3I position)
        {
            foreach(var offset in VertOffsets)
            {
                Vector3 pos = (Vector3)position + offset;
                Vert vert = new Vert(pos);

                if (!Verts.Contains(vert))
                {
                    Verts[vert] = new VIdx(NextVertIdx++);
                }

                yield return Verts[vert];
            }
        }
    }
}