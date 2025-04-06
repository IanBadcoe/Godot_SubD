using VIdx = SubD.Idx<SubD.Vert>;
using EIdx = SubD.Idx<SubD.Edge>;
using PIdx = SubD.Idx<SubD.Poly>;
using System;
using System.Linq;
using System.Collections.Generic;
using Godot;
using System.Diagnostics;
using SubD;

using EdgeWithSharpness = System.Tuple<SubD.Idx<SubD.Vert>, bool>;

namespace SubD
{
    public class CatmullClarkSubdivider : ISubdivider
    {
        int NextVertIdx;
        int NextEdgeIdx;
        int NextPolyIdx;

        BidirectionalDictionary<VIdx, Vert> NewVerts;
        BidirectionalDictionary<EIdx, Edge> NewEdges;
        BidirectionalDictionary<PIdx, Poly> NewPolys;

        public Surface Subdivide(Surface input)
        {
            NextVertIdx = input.Verts.Keys.Max().Value + 1;     // retain all existing ids and build new ones beyond that range
            NextEdgeIdx = 0;                                    // no edges are carried over
            NextPolyIdx = 0;                                    // no polys are carried over

            // preserve the VIds of existing verts
            // all cloned verts are unfrozen and do not have cached Normals
            NewVerts = CloneVerts(input.Verts);
            NewEdges = new BidirectionalDictionary<EIdx, Edge>();
            NewPolys = new BidirectionalDictionary<PIdx, Poly>();

            Dictionary<PIdx, VIdx> face_centre_map = new Dictionary<PIdx, VIdx>();

            // inject face centre verts
            foreach(var pair in input.Polys)
            {
                VIdx v_idx = new(NextVertIdx++);
                NewVerts[v_idx] = new Vert(
                    input.PolyVerts(pair.Key)
                        .Select(x => x.Position)
                        .Sum()
                      / pair.Value.VIdxs.Length);
                face_centre_map[pair.Key] = v_idx;
            }

            Dictionary<EIdx, VIdx> edge_centre_map = new Dictionary<EIdx, VIdx>();

            // inject edge centre verts
            foreach(var pair in input.Edges)
            {
                VIdx v_idx = new(NextVertIdx++);
                edge_centre_map[pair.Key] = v_idx;

                if (pair.Value.IsSharp)
                {
                    // sharp edges just interpolate their original position
                    NewVerts[v_idx] = new Vert(
                        (
                              input.Verts[pair.Value.Start].Position
                            + input.Verts[pair.Value.End].Position
                        ) / 2);
                }
                else
                {
                    // smooth edges interpolate the original end positions
                    // AND the new face-centres
                    NewVerts[v_idx] = new Vert(
                        (
                               input.Verts[pair.Value.Start].Position
                            +  input.Verts[pair.Value.End].Position
                            +  NewVerts[face_centre_map[pair.Value.Left.Value]].Position
                            +  NewVerts[face_centre_map[pair.Value.Right.Value]].Position
                        ) / 4);
                }
            }

            // move pre-existing verts
            foreach(var pair in input.Verts)
            {
                Vert input_vert = pair.Value;
                VIdx input_v_idx = pair.Key;

                int n_sharp_edges;

                // if the vert is tagged sharp then it is sharp irrespective of the edge settings
                // otherwise we follow:
                // n < 2 : smooth rule
                // n == 2 : crease rule
                // n > 2 : sharp rule
                n_sharp_edges = input.VertEdges(input_v_idx).Count(x => x.IsSharp);

                if (input_vert.IsSharp || n_sharp_edges > 2)
                {
                    //   we use the sharp rule, which means we do not move
                }
                else if (n_sharp_edges < 2)
                {
                    // smooth rule

                    // num EIdxs == num PIdxs...
                    int n = input_vert.EIdxs.Count();

                    Vector3 face_points_avg
                        = input_vert.PIdxs
                            .Select(x => NewVerts[face_centre_map[x]].Position)
                            .Sum() / n;

                    Vector3 edge_mid_points_avg
                         = input_vert.EIdxs
                            .Select(x => input.EdgeMidpoint(x))
                            .Sum() / n;

                    Vector3 new_pos = (face_points_avg + 2 * edge_mid_points_avg + (n - 3) * input_vert.Position) / n;

                    // new vert is unfrozen, VIDxs were preserved from original ones to NewVerts
                    NewVerts[input_v_idx] = new Vert(new_pos);
                }
                else // (n_sharp_edges == 2)
                {
                    // crease rule

                    // the other ends of the two original crease vectors...
                    Vector3 sum_crease_edges_other_ends
                        = input.VertEdges(input_v_idx)
                            .Where(x => x.IsSharp)
                            .Select(x => input.Verts[x.OtherVert(input_v_idx).Value].Position)
                            .Sum();

                    Vector3 new_pos = input_vert.Position * 0.75f
                                    + sum_crease_edges_other_ends * 0.125f;

                    // new vert is unfrozen, VIDxs were preserved from original ones to NewVerts
                    NewVerts[input_v_idx] = new Vert(new_pos);
                }
            }

            foreach(var p_pair in input.Polys)
            {
                EIdx[] input_e_idxs = input.PolyEIdxs(p_pair.Key).ToArray();

                EIdx prev_e_idx = input_e_idxs.Last();
                Edge prev_edge = input.Edges[prev_e_idx];

                foreach(EIdx e_idx in input_e_idxs)
                {
                    Edge edge = input.Edges[e_idx];

                    // if we used this edge backwards, then we need to start at the End
                    // otherwise the Start
                    VIdx start = edge.Right == p_pair.Key ? edge.Start : edge.End;

                    AddPoly(
                        [
                            new EdgeWithSharpness(start, edge.IsSharp),
                            new EdgeWithSharpness(edge_centre_map[e_idx], false),
                            new EdgeWithSharpness(face_centre_map[p_pair.Key], false),
                            new EdgeWithSharpness(edge_centre_map[prev_e_idx], prev_edge.IsSharp)
                        ]
                    );

                    prev_e_idx = e_idx;
                    prev_edge = edge;
                }
            }

            Surface ret = new Surface(NewVerts, NewEdges, NewPolys);

            Reset();

            return ret;
        }

        private void Reset()
        {
            NextEdgeIdx = NextPolyIdx = NextVertIdx = 0;

            NewVerts = null;
            NewEdges = null;
            NewPolys = null;
        }

        private void AddPoly(EdgeWithSharpness[] v_idxs)
        {
            List<EIdx> e_idxs = new List<EIdx>();
            List<Edge> left_edges = new List<Edge>();
            List<Edge> right_edges = new List<Edge>();

            EdgeWithSharpness prev_pair = v_idxs.Last();

            int sh_idx = -1;

            foreach(EdgeWithSharpness pair in v_idxs)
            {
                bool is_left;
                EIdx e_idx = AddEdge(prev_pair, pair, out is_left);
                sh_idx++;

                e_idxs.Add(e_idx);

                (is_left ? left_edges : right_edges).Add(NewEdges[e_idx]);

                prev_pair = pair;
            }

            Poly poly = new Poly(v_idxs.Select(x => x.Item1), e_idxs);
            PIdx p_idx = new PIdx(NextPolyIdx++);
            NewPolys[p_idx] = poly;

            // it's a new poly, so let all the verts know
            foreach(Vert vert in poly.VIdxs.Select(x => NewVerts[x]))
            {
                vert.AddPIdx(p_idx);
            }

            foreach(Edge edge in left_edges)
            {
                edge.Left = p_idx;
            }

            foreach(Edge edge in right_edges)
            {
                edge.Right = p_idx;
            }
        }

        private EIdx AddEdge(EdgeWithSharpness v1, EdgeWithSharpness v2, out bool is_left)
        {
            Edge edge = new Edge(v1.Item1, v2.Item1);
            Edge r_edge = edge.Reversed();

            // we should see each edge twice, once forwards, when it should be new, and once backwards
            Debug.Assert(!NewEdges.Contains(edge));

            if (NewEdges.Contains(r_edge))
            {
                is_left = true;

                return NewEdges[r_edge];
            }

            is_left = false;

            EIdx e_idx = new(NextEdgeIdx++);

            NewEdges[e_idx] = edge;

            NewVerts[v1.Item1].AddEIdx(e_idx);
            NewVerts[v2.Item1].AddEIdx(e_idx);

            // we recorded the sharpness on the start vert
            edge.IsSharp = v1.Item2;

            return e_idx;
        }

        BidirectionalDictionary<VIdx, Vert> CloneVerts(BidirectionalDictionary<VIdx, Vert> verts)
        {
            BidirectionalDictionary<VIdx, Vert> ret = new BidirectionalDictionary<VIdx, Vert>();

            foreach(var pair in verts)
            {
                ret[pair.Key] = pair.Value.Clone(true);
            }

            return ret;
        }
    }
}