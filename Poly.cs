using System;
using System.Collections.Generic;
using System.Linq;
using Geom_Util;
using Godot;

using Godot_Util.CSharp_Util;

using Godot_Util;

namespace SubD
{
    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;
    using PIdx = Idx<Poly>;

    public class Poly : ISpatialValue<PIdx>
    {
        public Vert[] Verts
        {
            get;
            set;
        }

        public Edge[] Edges
        {
            get;
            set;
        }

        // a place to cache it when calculated by the Surface
        public Vector3? Normal
        {
            get;
            set;
        }

        public string Tag
        {
            get;
            set;
        }

        public PIdx Key { get; set; }

        public Vector3 Centre => Verts.Select(x => x.Position).Sum() / Verts.Length;

        static public IEnumerable<Vert> StandardiseVIdxOrder(IEnumerable<Vert> verts)
        {
            int dummy;

            return StandardiseVIdxOrder(verts, out dummy);
        }

        static public IEnumerable<Vert> StandardiseVIdxOrder(IEnumerable<Vert> verts, out int where)
        {
            // rotete-permute verts and edges into a standard order so we can compare
            // polys from different sources

            var temp_vs = verts.ToArray();

            VIdx min = temp_vs.Select(x => x.Key).Min();

            where = Array.IndexOf(temp_vs, min);

            return verts.Skip(where).Concat(verts.Take(where));
        }

        public Poly(IEnumerable<Vert> verts, IEnumerable<Edge> edges)
        {
            // rotate-permute verts and edges into a standard order so we can compare
            // polys from different sources
            int where;
            Verts = [.. StandardiseVIdxOrder(verts, out where)];

            var temp_es = edges.ToArray();

            // permute the edges the same, to preserve the relationship
            Edges =  [.. temp_es.Skip(where), .. temp_es.Take(where)];
        }

        // potentially dangerous as *shallow* copy
        public Poly(Poly old_poly)
        {
            Verts = [.. old_poly.Verts];
            Edges = [.. old_poly.Edges];

            SetMetadataFrom(old_poly);
        }

        public void SetMetadataFrom(Poly original_poly)
        {
            Tag = original_poly.Tag;
        }

        public ImBounds GetBounds()
        {
            return Verts.Aggregate(new ImBounds(), (x, y) => x.UnionedWith(y.GetBounds()));
        }
    }
}