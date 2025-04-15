using SubD;
using System.Collections.Generic;
using System.Linq;
using System;

using VIdx = SubD.Idx<SubD.Vert>;
using EIdx = SubD.Idx<SubD.Edge>;
using PIdx = SubD.Idx<SubD.Poly>;

namespace SubD
{
    public static class VertUtil
    {
        public static Vert ToVertWithSortedEdgesAndPolys(
            Vert vert, VIdx v_idx,
            BidirectionalDictionary<EIdx, Edge> edges,
            Dictionary<PIdx, Poly> polys)
        {
            List<EIdx> edges_ordered = [];
            List<PIdx> polys_ordered = [];

            // number of polys is the same
            int num_edges = vert.EIdxs.Length;

            if (num_edges == 0)
            {
                return vert;
            }

            EIdx e_idx = vert.EIdxs.First();

            do {
                Edge edge = edges[e_idx];

                // let's check that the edge we found really does involve this vert
                Util.Assert(edge.VIdxs.Contains(v_idx));

                bool are_we_start_of_edge = edge.Start == v_idx;
                Util.Assert(are_we_start_of_edge || edge.End == v_idx);

                // if we are at the start of the edge, then the clockwise poly is the Right one on the edge
                // and vice-versa

                PIdx p_idx = are_we_start_of_edge ? edge.Right.Value : edge. Left.Value;

                edges_ordered.Add(e_idx);
                polys_ordered.Add(p_idx);

                Poly poly = polys[p_idx];

                int e_idx_idx_in_poly = Array.IndexOf(poly.EIdxs, e_idx);

                // since poly edges rotate clockwise, the next edge common to this vert
                // should be one before us in this list
                e_idx = poly.EIdxs[(e_idx_idx_in_poly + poly.EIdxs.Length - 1) % poly.EIdxs.Length];
            } while (edges_ordered.Count < num_edges);

            Vert new_vert = new(vert.Position, edges_ordered, polys_ordered);
            new_vert.SetMetadataFrom(vert);

            return new_vert;
        }
    }
}