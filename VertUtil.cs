using System.Collections.Generic;
using System.Linq;
using System;

using Godot_Util;

namespace SubD
{
    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;
    using PIdx = Idx<Poly>;

    public static class VertUtil
    {
        public static void SortVertEdgesAndPolys(Vert vert)
        {
            List<Edge> edges_ordered = [];
            List<Poly> polys_ordered = [];

            // number of polys is the same
            int num_edges = vert.Edges.Count;

            if (num_edges == 0)
            {
                return;
            }

            Edge edge = vert.Edges.First();

            do {
                // let's check that the edge we found really does involve this vert
                Util.Assert(edge.Verts.Contains(vert));

                bool are_we_start_of_edge = edge.Start == vert;
                Util.Assert(are_we_start_of_edge || edge.End == vert);

                // if we are at the start of the edge, then the clockwise poly is the Right one on the edge
                // and vice-versa

                Poly poly = are_we_start_of_edge ? edge.Right : edge.Left;

                edges_ordered.Add(edge);
                polys_ordered.Add(poly);

                int e_idx_idx_in_poly = Array.IndexOf(poly.Edges, edge);

                // since poly edges rotate clockwise, the next edge common to this vert
                // should be one before us in this list
                edge = poly.Edges[(e_idx_idx_in_poly + poly.Edges.Length - 1) % poly.Edges.Length];
            } while (edges_ordered.Count < num_edges);

            vert.Edges = edges_ordered;
            vert.Polys = polys_ordered;
        }
    }
}