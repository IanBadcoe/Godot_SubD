using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

using VIdx = SubD.Idx<SubD.Vert>;
using EIdx = SubD.Idx<SubD.Edge>;

namespace SubD
{
    public class Poly
    {
        public VIdx[] VIdxs
        {
            get;
            private set;
        }

        public EIdx[] EIdxs
        {
            get;
            private set;
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

        static public IEnumerable<VIdx> StandardiseVIdxOrder(IEnumerable<VIdx> v_idxs)
        {
            int dummy;

            return StandardiseVIdxOrder(v_idxs, out dummy);
        }

        static public IEnumerable<VIdx> StandardiseVIdxOrder(IEnumerable<VIdx> v_idxs, out int where)
        {
            // rotete-permute verts and edges into a standard order so we can compare
            // polys from different sources

            var temp_vs = v_idxs.ToArray();

            VIdx min = temp_vs.Min();

            where = Array.IndexOf(temp_vs, min);

            return temp_vs.Skip(where).Concat(temp_vs.Take(where));
        }

        public Poly(IEnumerable<VIdx> v_idxs, IEnumerable<EIdx> e_idxs)
        {
            // rotate-permute verts and edges into a standard order so we can compare
            // polys from different sources
            int where;
            VIdxs = [.. StandardiseVIdxOrder(v_idxs, out where)];

            var temp_es = e_idxs.ToArray();

            // permute the edges the same, to preserve the relationship
            EIdxs =  [.. temp_es.Skip(where), .. temp_es.Take(where)];
        }

        public void SetMetadataFrom(Poly original_poly)
        {
            Tag = original_poly.Tag;
        }

        public static bool operator==(Poly lhs, Poly rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (ReferenceEquals(lhs, null) || ReferenceEquals(rhs, null))
            {
                return false;
            }

            return lhs.VIdxs.SequenceEqual(rhs.VIdxs);
        }

        public static bool operator!=(Poly lhs, Poly rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            var poly = obj as Poly;

            return this == poly;
        }

        public override int GetHashCode()
        {
            return VIdxs.Aggregate(0, (x, y) => HashCode.Combine(x, y.GetHashCode()));
        }
    }
}