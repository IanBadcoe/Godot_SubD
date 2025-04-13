using VIdx = SubD.Idx<SubD.Vert>;
using EIdx = SubD.Idx<SubD.Edge>;
using PIdx = SubD.Idx<SubD.Poly>;
using System;
using System.Linq;
using System.Collections.Generic;
using Godot;

namespace SubD
{
    struct AnnotatedVert
    {
        public VIdx VIdx;
        public Edge OriginalEdge;       //< this is the edge which (in terms of the poly we are splitting) followed VIdx
                                        //< (the edge may have been used forwards or backwards, so this doesn't mean VIdx == OriginalEdge.Start)

        public AnnotatedVert(VIdx v_idx, Edge original_edge)
        {
            VIdx = v_idx;
            OriginalEdge = original_edge;
        }
    }

    public class CatmullClarkSubdivider : ISubdivider
    {
        int NextVertIdx;
        int NextEdgeIdx;
        int NextPolyIdx;

        BidirectionalDictionary<VIdx, Vert> NewVerts;
        BidirectionalDictionary<EIdx, Edge> NewEdges;
        Dictionary<PIdx, Poly> NewPolys;

        public Surface Subdivide(Surface input)
        {
            NextVertIdx = input.Verts.Keys.Max().Value + 1;     // retain all existing ids and build new ones beyond that range
            NextEdgeIdx = 0;                                    // no edges are carried over
            NextPolyIdx = 0;                                    // no polys are carried over

            // preserve the VIds of existing verts
            // all cloned verts are unfrozen and do not have cached Normals
            NewVerts = CloneVerts(input.Verts);
            NewEdges = new BidirectionalDictionary<EIdx, Edge>();
            NewPolys = [];

            Dictionary<PIdx, VIdx> face_centre_map = [];

            // inject face centre verts
            foreach(var pair in input.Polys)
            {
                VIdx v_idx = new(NextVertIdx++);
                NewVerts[v_idx] = new Vert(input.PolyCentre(pair.Key));
                face_centre_map[pair.Key] = v_idx;
            }

            Dictionary<EIdx, VIdx> edge_centre_map = [];

            // inject edge centre verts
            foreach(var pair in input.Edges)
            {
                VIdx v_idx = new(NextVertIdx++);
                edge_centre_map[pair.Key] = v_idx;

                if (pair.Value.IsSetSharp)
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
                n_sharp_edges = input.VertEdges(input_v_idx).Count(x => x.IsSetSharp);

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
                            .Where(x => x.IsSetSharp)
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
                EIdx[] input_e_idxs = [.. input.PolyEIdxs(p_pair.Key)];

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
                            new AnnotatedVert(start, edge),
                            new AnnotatedVert(edge_centre_map[e_idx], null),
                            new AnnotatedVert(face_centre_map[p_pair.Key], null),
                            new AnnotatedVert(edge_centre_map[prev_e_idx], prev_edge)
                        ]
                    );

                    prev_e_idx = e_idx;
                    prev_edge = edge;
                }
            }

            foreach(VIdx v_idx in NewVerts.Keys)
            {
                Vert old_vert = NewVerts[v_idx];
                // we added the edges and polys to the verts in a fairly arbitraty order, but we need
                // them to both be clockwise, from outside the cube, looking inwards, and...
                //
                // we need the two edges of the poly at position N to be N and N + 1
                NewVerts[v_idx] = VertUtil.ToVertWithSortedEdgesAndPolys(old_vert, v_idx, NewEdges, NewPolys);
            }

            Surface ret = new(NewVerts, NewEdges, NewPolys);

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

        private void AddPoly(AnnotatedVert[] v_idxs)
        {
            List<EIdx> e_idxs = [];
            List<Edge> left_edges = [];
            List<Edge> right_edges = [];

            for(int i = 0; i < v_idxs.Length; i++)
            {
                AnnotatedVert av = v_idxs[i];
                int next_i = (i + 1) % v_idxs.Length;
                AnnotatedVert av_next = v_idxs[next_i];

                bool is_left;
                EIdx e_idx = AddEdge(av, av_next, out is_left);

                e_idxs.Add(e_idx);

                (is_left ? left_edges : right_edges).Add(NewEdges[e_idx]);
            }

            Poly poly = new(v_idxs.Select(x => x.VIdx), e_idxs);
            PIdx p_idx = new(NextPolyIdx++);
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

        private EIdx AddEdge(AnnotatedVert v1, AnnotatedVert v2, out bool is_left)
        {
            Edge edge = new(v1.VIdx, v2.VIdx);
            Edge r_edge = edge.Reversed();

            // we should see each edge twice, once forwards, when it should be new, and once backwards
            Util.Assert(!NewEdges.Contains(edge));

            if (NewEdges.Contains(r_edge))
            {
                is_left = true;

                return NewEdges[r_edge];
            }

            is_left = false;

            EIdx e_idx = new(NextEdgeIdx++);

            NewEdges[e_idx] = edge;

            NewVerts[v1.VIdx].AddEIdx(e_idx);
            NewVerts[v2.VIdx].AddEIdx(e_idx);

            // if there was an original edge, propogate metadata
            if (v1.OriginalEdge != null)
            {
                edge.SetMetaDataFrom(v1.OriginalEdge);
            }

            return e_idx;
        }

        BidirectionalDictionary<VIdx, Vert> CloneVerts(BidirectionalDictionary<VIdx, Vert> verts)
        {
            BidirectionalDictionary<VIdx, Vert> ret = new();

            foreach(var pair in verts)
            {
                ret[pair.Key] = pair.Value.Clone(true);
            }

            return ret;
        }
    }
}