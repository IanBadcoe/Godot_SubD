using VIdx = SubD.Idx<SubD.Vert>;
using EIdx = SubD.Idx<SubD.Edge>;
using PIdx = SubD.Idx<SubD.Poly>;
using System;
using System.Linq;
using System.Collections.Generic;
using Godot;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Threading;

namespace SubD
{
    public class CatmullClarkSubdivider : ISubdivider
    {
        int NextVertIdx;
        int NextEdgeIdx;
        int NextPolyIdx;

        BidirectionalDictionary<VIdx, Vert> Verts;
        BidirectionalDictionary<EIdx, Edge> Edges;
        BidirectionalDictionary<PIdx, Poly> Polys;

        public Surface Subdivide(Surface input)
        {
            NextVertIdx = input.Verts.Keys.Max().Value + 1;
            NextEdgeIdx = 0;    // no edges are carried over
            NextPolyIdx = 0;    // no polys are carried over

            // preserve the VIds of existing verts
            Verts = CloneVerts(input.Verts);
            Edges = new BidirectionalDictionary<EIdx, Edge>();
            Polys = new BidirectionalDictionary<PIdx, Poly>();

            Dictionary<PIdx, VIdx> face_centre_map = new Dictionary<PIdx, VIdx>();

            // inject face centre verts
            foreach(var pair in input.Polys)
            {
                VIdx v_idx = new(NextVertIdx++);
                Verts[v_idx] = new Vert(input.PolyVerts(pair.Key).Aggregate(Vector3.Zero, (x, y) => x + y.Position) / pair.Value.VIdxs.Count());
                face_centre_map[pair.Key] = v_idx;
            }

            Dictionary<EIdx, VIdx> edge_centre_map = new Dictionary<EIdx, VIdx>();

            // inject edge centre verts
            foreach(var pair in input.Edges)
            {
                VIdx v_idx = new(NextVertIdx++);
                Verts[v_idx]
                    = new Vert((input.Verts[pair.Value.Start].Position
                    +  input.Verts[pair.Value.End].Position
                    +  Verts[face_centre_map[pair.Value.Left.Value]].Position
                    +  Verts[face_centre_map[pair.Value.Right.Value]].Position) / 4);
                edge_centre_map[pair.Key] = v_idx;
            }

            // move pre-existing verts
            foreach(var pair in input.Verts)
            {
                Vert vert = pair.Value;
                VIdx v_idx = pair.Key;

                Vector3 face_points_avg = vert.PIdxs.Select(x => Verts[face_centre_map[x]].Position).Aggregate(Vector3.Zero, (x, y) => x + y) / vert.PIdxs.Count();
                Vector3 edge_mid_points_avg = vert.EIdxs.Select(x => input.EdgeMidpoint(x)).Aggregate(Vector3.Zero, (x, y) => x + y) / vert.EIdxs.Count();

                int n = vert.EIdxs.Count();

                Vector3 new_pos = (face_points_avg + 2 * edge_mid_points_avg + (n - 3) * vert.Position) / n;

                // new vert is unfrozen
                Verts[v_idx] = new Vert(new_pos);
            }

            foreach(var p_pair in input.Polys)
            {
                EIdx[] input_e_idxs = input.PolyEIdxs(p_pair.Key).ToArray();

                EIdx prev_e_idx = input_e_idxs.Last();

                foreach(EIdx e_idx in input_e_idxs)
                {
                    Edge edge = input.Edges[e_idx];

                    // if we used this edge backwards, then we need to start at the End
                    // otherwise the Start
                    VIdx start = edge.Right == p_pair.Key ? edge.Start : edge.End;

                    AddPoly([start, edge_centre_map[e_idx], face_centre_map[p_pair.Key], edge_centre_map[prev_e_idx]]);

                    prev_e_idx = e_idx;
                }
            }

            Surface ret = new Surface(Verts, Edges, Polys);

            Reset();

            return ret;
        }

        private void Reset()
        {
            NextEdgeIdx = NextPolyIdx = NextVertIdx = 0;

            Verts = null;
            Edges = null;
            Polys = null;
        }

        private void AddPoly(VIdx[] v_idxs)
        {
            List<EIdx> e_idxs = new List<EIdx>();
            List<Edge> left_edges = new List<Edge>();
            List<Edge> right_edges = new List<Edge>();

            VIdx prev_v_idx = v_idxs.Last();

            foreach(VIdx v_idx in v_idxs)
            {
                bool is_left;
                EIdx e_idx = AddEdge(prev_v_idx, v_idx, out is_left);

                e_idxs.Add(e_idx);

                (is_left ? left_edges : right_edges).Add(Edges[e_idx]);

                prev_v_idx = v_idx;
            }

            Poly poly = new Poly(v_idxs, e_idxs);
            PIdx p_idx = new PIdx(NextPolyIdx++);
            Polys[p_idx] = poly;

            // it's a new poly, so let all the verts know
            foreach(Vert vert in poly.VIdxs.Select(x => Verts[x]))
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

        private EIdx AddEdge(VIdx v1, VIdx v2, out bool is_left)
        {
            Edge edge = new Edge(v1, v2);
            Edge r_edge = edge.Reversed();

            // we should see each edge twice, once forwards, when it should be new, and once backwards
            Debug.Assert(!Edges.Contains(edge));

            if (Edges.Contains(r_edge))
            {
                is_left = true;
                return Edges[r_edge];
            }

            is_left = false;
            EIdx e_idx = new(NextEdgeIdx++);
            Edges[e_idx] = edge;
            Verts[v1].AddEIdx(e_idx);
            Verts[v2].AddEIdx(e_idx);

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