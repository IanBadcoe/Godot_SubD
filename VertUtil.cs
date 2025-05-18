using System.Collections.Generic;
using System.Linq;
using System;

using Godot_Util;
using Godot;

namespace SubD
{
    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;
    using FIdx = Idx<Face>;

    public static class VertUtil
    {
        public static void SortVertEdgesAndFaces(Surface surf, Vert vert, bool allow_split)
        {
            // number of faces is the same
            int num_edges = vert.Edges.Count;

            if (num_edges == 0)
            {
                return;
            }

            List<Edge> open_edges = vert.Edges.ToList();
            Vert new_vert = vert;       // at first this is _not_ new, but if we need to inject another vert it will be...

            while(open_edges.Count > 0)
            {
                Edge start_edge = open_edges[0];
                Edge edge = start_edge;

                List<Edge> edges_ordered = [];
                List<Face> faces_ordered = [];

                do
                {
                    Util.Assert(open_edges.Contains(edge));
                    open_edges.Remove(edge);

                    // let's check that the edge we found really does involve this vert
                    Util.Assert(edge.Verts.Contains(new_vert));

                    bool are_we_start_of_edge = edge.Start == new_vert;
                    Util.Assert(are_we_start_of_edge || edge.End == new_vert);

                    // if we are at the start of the edge, then the clockwise face is the Forwards one on the edge
                    // and vice-versa

                    Face face = are_we_start_of_edge ? edge.Forwards : edge.Backwards;

                    edges_ordered.Add(edge);
                    faces_ordered.Add(face);

                    int e_idx_idx_in_face = Array.IndexOf(face.Edges, edge);

                    // since face edges rotate clockwise, the next edge common to this vert
                    // should be one before us in this list
                    edge = face.Edges[(e_idx_idx_in_face + face.Edges.Length - 1) % face.Edges.Length];
                } while (edge != start_edge);

                new_vert.Edges = edges_ordered;
                new_vert.Faces = faces_ordered;

                if (!allow_split)
                {
                    Util.Assert(!open_edges.Any());
                }
                else
                {
                    if (open_edges.Any())
                    {
                        new_vert = new(vert.Position);
                        new_vert.SetMetadataFrom(vert);

                        surf.AddVert(new_vert);

                        foreach(Edge e_temp in open_edges)
                        {
                            // move the remaining edges/faces over to the new vert
                            // (needs doing sometime, and doing it now means we can still assert vert<->edge and vert<->face
                            //  relationships in the innert loop)
                            SwapVertReferences(e_temp, new_vert, vert);
                        }
                    }
                }
            }
        }

        public static void SwapVertReferences(Vert search_from, Vert swap_to, Vert swap_from)
        {
            // should be a nop anyway, but for paranoidly debugging, let's early out
            // if this is a swap of something for itself
            if (swap_from == swap_to)
            {
                return;
            }

            foreach(Edge edge in search_from.Edges)
            {
                if (edge.Start == swap_from)
                {
                    edge.Start = swap_to;
                }
                else
                {
                    Util.Assert(edge.End == swap_from);
                    edge.End = swap_to;
                }
            }

            foreach(Face face in search_from.Faces)
            {
                face.Verts = [.. face.Verts.Select(x => x == swap_from ? swap_to : x)];
            }
        }

        public static void SwapVertReferences(Edge search_from, Vert swap_to, Vert swap_from)
        {
            if (search_from.Start == swap_from)
            {
                search_from.Start = swap_to;
            }
            else
            {
                Util.Assert(search_from.End == swap_from);

                search_from.End = swap_to;
            }

            foreach(Face face in search_from.Faces)
            {
                face.Verts = [.. face.Verts.Select(x => x == swap_from ? swap_to : x)];
            }
        }
    }
}