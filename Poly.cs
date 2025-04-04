using System;
using System.Collections.Generic;
using System.Linq;

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

        public Poly(IEnumerable<VIdx> v_idxs, IEnumerable<EIdx> e_idxs)
        {
            // rotete-permute verts and edges into a standard order so we can compare
            // polys from different sources

            var temp_vs = v_idxs.ToArray();

            VIdx min = temp_vs.Min();

            int where_lowest = Array.IndexOf(temp_vs, min);

            VIdxs = temp_vs.Skip(where_lowest).Concat(temp_vs.Take(where_lowest)).ToArray();

            var temp_es = e_idxs.ToArray();

            EIdxs =  temp_es.Skip(where_lowest).Concat(temp_es.Take(where_lowest)).ToArray();
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
            return VIdxs.Aggregate(0, (x, y) => HashCode.Combine(x.GetHashCode(), y.GetHashCode()));
        }
    }
}