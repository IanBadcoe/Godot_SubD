using System;
using System.Collections.Generic;
using System.Linq;
using Geom_Util;
using Godot;

using Godot_Util.CSharp_Util;

using Godot_Util;
using SubD.Builders;
using System.Diagnostics;

namespace SubD
{
    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;
    using FIdx = Idx<Face>;

    [DebuggerDisplay("{Key} Edges:{Edges.Length} Verts:{Verts.Length}")]
    public class Face : ISpatialValue<FIdx>, IHasGeneratiorIdentities
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

        public FIdx Key { get; set; }

        public HashSet<IGeneratorIdentity> GIs { get; private set; }

        public Vector3 Centre => Verts.Select(x => x.Position).Sum() / Verts.Length;

        static public IEnumerable<Vert> StandardiseVIdxOrder(IEnumerable<Vert> verts)
        {
            int dummy;

            return StandardiseVIdxOrder(verts, out dummy);
        }

        static public IEnumerable<Vert> StandardiseVIdxOrder(IEnumerable<Vert> verts, out int where)
        {
            // rotete-permute verts and edges into a standard order so we can compare
            // faces from different sources

            var temp_vs = verts.ToArray();

            VIdx min = temp_vs.Select(x => x.Key).Min();

            where = Array.IndexOf(temp_vs, min);

            return verts.Skip(where).Concat(verts.Take(where));
        }

        public Face(IEnumerable<Vert> verts, IEnumerable<Edge> edges, IGeneratorIdentity gi = null)
            : this(verts, edges, new HashSet<IGeneratorIdentity>{ gi })
        {
        }

        public Face(IEnumerable<Vert> verts, IEnumerable<Edge> edges, HashSet<IGeneratorIdentity> gis)
        {
            // rotate-permute verts and edges into a standard order so we can compare
            // faces from different sources
            int where;
            Verts = [.. StandardiseVIdxOrder(verts, out where)];

            var temp_es = edges.ToArray();

            // permute the edges the same, to preserve the relationship
            Edges =  [.. temp_es.Skip(where), .. temp_es.Take(where)];

            // not sure whether to call "GI" metadata or not...
            GIs = gis;
        }

        // potentially dangerous as *shallow* copy
        public Face(Face old_face)
        {
            Verts = [.. old_face.Verts];
            Edges = [.. old_face.Edges];

            // not sure whether to call "GIs" metadata or not...
            // I will!

            SetMetadataFrom(old_face);
        }

        public void SetMetadataFrom(Face original_face)
        {
            Tag = original_face.Tag;
            GIs = original_face.GIs;

        }

        public ImBounds GetBounds()
        {
            return Verts.Aggregate(new ImBounds(), (x, y) => x.UnionedWith(y.GetBounds()));
        }
    }
}