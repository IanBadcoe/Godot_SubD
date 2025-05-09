using System;
using System.Linq;
using System.Collections.Generic;

using Godot;

using Godot_Util;
using Godot_Util.CSharp_Util;

using Geom_Util;

namespace SubD
{
    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;
    using PIdx = Idx<Poly>;

    struct AnnotatedVert
    {
        public Vert Vert;
        public Edge OriginalEdge;       //< this is the edge which (in terms of the poly we are splitting) followed VIdx
                                        //< (the edge may have been used forwards or backwards, so this doesn't mean VIdx == OriginalEdge.Start)

        public AnnotatedVert(Vert vert, Edge original_edge)
        {
            Vert = vert;
            OriginalEdge = original_edge;
        }
    }

    public class CatmullClarkSubdivider : ISubdivider
    {
        int NextVertIdx;
        int NextEdgeIdx;
        int NextPolyIdx;

        SpatialDictionary<VIdx, Vert> NewVerts;
        SpatialDictionary<EIdx, Edge> NewEdges;
        SpatialDictionary<PIdx, Poly> NewPolys;

        public Surface Subdivide(Surface input)
        {
            if (input.Verts.Count == 0)
            {
                return null;
            }

            NextVertIdx = input.Verts.Keys.Max().Value + 1;     // retain all existing ids and build new ones beyond that range
            NextEdgeIdx = 0;                                    // no edges are carried over
            NextPolyIdx = 0;                                    // no polys are carried over

            // preserve the VIds of existing verts
            // all cloned verts are unfrozen and do not have cached Normals
            NewVerts = CloneVerts(input.Verts);
            NewEdges = [];
            NewPolys = [];

            Dictionary<Poly, Vert> face_centre_map = [];

            // inject face centre verts
            foreach(Poly poly in input.Polys.Values)
            {
                VIdx v_idx = new(NextVertIdx++);
                Vert vert = new(poly.Centre);
                NewVerts[v_idx] = vert;

                face_centre_map[poly] = vert;
            }

            Dictionary<Edge, Vert> edge_centre_map = [];

            // inject edge centre verts
            foreach(var edge in input.Edges.Values)
            {
                VIdx v_idx = new(NextVertIdx++);
                Vert vert = null;

                if (edge.IsSetSharp)
                {
                    // sharp edges just interpolate their original position
                    vert = new Vert(
                        (
                              edge.Start.Position
                            + edge.End.Position
                        ) / 2);
                }
                else
                {
                    // smooth edges interpolate the original end positions
                    // AND the new face-centres
                    vert = new Vert(
                        (
                               edge.Start.Position
                            +  edge.End.Position
                            +  face_centre_map[edge.Left].Position
                            +  face_centre_map[edge.Right].Position
                        ) / 4);
                }

                NewVerts[v_idx] = vert;

                edge_centre_map[edge] = vert;
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
                n_sharp_edges = input_vert.Edges.Count(x => x.IsSetSharp);

                if (input_vert.IsSharp || n_sharp_edges > 2)
                {
                    //   we use the sharp rule, which means we do not move
                }
                else if (n_sharp_edges < 2)
                {
                    // smooth rule

                    // num EIdxs == num PIdxs...
                    int n = input_vert.Edges.Count;

                    Vector3 face_points_avg
                        = input_vert.Polys
                            .Select(x => face_centre_map[x].Position)
                            .Sum() / n;

                    Vector3 edge_mid_points_avg
                         = input_vert.Edges
                            .Select(x => x.MidPoint)
                            .Sum() / n;

                    Vector3 new_pos = (face_points_avg + 2 * edge_mid_points_avg + (n - 3) * input_vert.Position) / n;

                    // new vert is unfrozen, VIDxs were preserved from original ones to NewVerts
                    NewVerts[input_v_idx].Position = new_pos;
                }
                else // (n_sharp_edges == 2)
                {
                    // crease rule

                    // the other ends of the two original crease vectors...
                    Vector3 sum_crease_edges_other_ends
                        = input_vert.Edges
                            .Where(x => x.IsSetSharp)
                            .Select(x => x.OtherVert(input_vert).Position)
                            .Sum();

                    Vector3 new_pos = input_vert.Position * 0.75f
                                    + sum_crease_edges_other_ends * 0.125f;

                    // new vert is unfrozen, VIDxs were preserved from original ones to NewVerts
                    NewVerts[input_v_idx].Position = new_pos;
                }
            }

            Dictionary<(Vert, Vert), Edge> made_edges = [];

            foreach(var p_pair in input.Polys)
            {
                Poly poly = p_pair.Value;
                Edge prev_edge = poly.Edges.Last();

                foreach(Edge edge in poly.Edges)
                {
                    // if we used this edge backwards, then we need to start at the End
                    // otherwise the Start
                    VIdx start_v_idx = edge.Right == poly ? edge.Start.Key : edge.End.Key;
                    Vert start = NewVerts[start_v_idx];

                    AddPoly(
                        [
                            new AnnotatedVert(start, edge),
                            new AnnotatedVert(edge_centre_map[edge], null),
                            new AnnotatedVert(face_centre_map[poly], null),
                            new AnnotatedVert(edge_centre_map[prev_edge], prev_edge)
                        ],
                        poly,
                        made_edges
                    );

                    prev_edge = edge;
                }
            }

            foreach(Vert vert in NewVerts.Values)
            {
                // we added the edges and polys to the verts in a fairly arbitraty order, but we need
                // them to both be clockwise, from outside the cube, looking inwards, and...
                //
                // we need the two edges of the poly at position N to be N and N + 1

                VertUtil.SortVertEdgesAndPolys(vert);
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

        private void AddPoly(AnnotatedVert[] v_idxs, Poly orig_poly, Dictionary<(Vert, Vert), Edge> made_edges)
        {
            List<Edge> edges = [];
            List<Edge> left_edges = [];
            List<Edge> right_edges = [];

            for(int i = 0; i < v_idxs.Length; i++)
            {
                AnnotatedVert av = v_idxs[i];
                int next_i = (i + 1) % v_idxs.Length;
                AnnotatedVert av_next = v_idxs[next_i];

                bool is_left;
                Edge edge = AddEdge(av, av_next, made_edges, out is_left);

                edges.Add(edge);

                (is_left ? left_edges : right_edges).Add(edge);
            }

            Poly poly = new(v_idxs.Select(x => x.Vert), edges);
            PIdx p_idx = new(NextPolyIdx++);
            NewPolys[p_idx] = poly;
            poly.SetMetadataFrom(orig_poly);

            // it's a new poly, so let all the verts know
            foreach(Vert vert in poly.Verts)
            {
                vert.Polys.Add(poly);
            }

            foreach(Edge edge in left_edges)
            {
                edge.Left = poly;
            }

            foreach(Edge edge in right_edges)
            {
                edge.Right = poly;
            }
        }

        private Edge AddEdge(AnnotatedVert start, AnnotatedVert end, Dictionary<(Vert, Vert), Edge> made_edges, out bool is_left)
        {
            if (made_edges.TryGetValue((end.Vert, start.Vert), out Edge edge))
            {
                is_left = true;

                return edge;
            }

            is_left = false;

            EIdx e_idx = new(NextEdgeIdx++);

            edge = new(start.Vert, end.Vert);

            NewEdges[e_idx] = edge;

            made_edges[(start.Vert, end.Vert)] = edge;

            start.Vert.Edges.Add(edge);
            end.Vert.Edges.Add(edge);

            // if there was an original edge, propogate metadata
            if (start.OriginalEdge != null)
            {
                edge.SetMetaDataFrom(start.OriginalEdge);
            }

            return edge;
        }

        private ImBounds GetEdgeBounds(VIdx v_idx1, VIdx v_idx2)
        {
            Vert v1 = NewVerts[v_idx1];
            Vert v2 = NewVerts[v_idx2];

            return v1.GetBounds().UnionedWith(v2.GetBounds());
        }

        SpatialDictionary<VIdx, Vert> CloneVerts(SpatialDictionary<VIdx, Vert> verts)
        {
            SpatialDictionary<VIdx, Vert> ret = [];

            foreach(var pair in verts)
            {
                ret[pair.Key] = pair.Value.Clone(true);
            }

            return ret;
        }
    }
}