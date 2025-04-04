using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

using EIdx = SubD.Idx<SubD.Edge>;
using PIdx = SubD.Idx<SubD.Poly>;

namespace SubD
{
    [DebuggerDisplay("Position = {Position}")]
    public class Vert
    {
        public Vector3 Position
        {
            get;
            private set;
        }

        // ideally we would be a const object, but construction is quite spread out in time and having a separate "builder" version of this
        // (and vert, and maybe poly) would be a pain, so let's instead have a "Freeze" operation at the end of construction
        bool Frozen = false;

        public List<EIdx> EIdsxInner = new List<EIdx>();
        public IEnumerable<EIdx> EIdxs
        {
            get => EIdsxInner;
        }

        public List<PIdx> PIdsxInner = new List<PIdx>();
        public IEnumerable<PIdx> PIdxs
        {
            get => PIdsxInner;
        }

        public void AddEIdx(EIdx e_idx)
        {
            // we need to continue adding these for a while,
            // but once construction is done we shouldn't touch them anymore
            if (Frozen)
            {
                throw new InvalidOperationException();
            }

            EIdsxInner.Add(e_idx);
        }

        public void AddPIdx(PIdx p_idx)
        {
            // we need to continue adding these for a while,
            // but once construction is done we shouldn't touch them anymore
            if (Frozen)
            {
                throw new InvalidOperationException();
            }

            PIdsxInner.Add(p_idx);
        }

        public void Freeze()
        {
            Frozen = true;
        }

        public Vert(Vector3 pos)
        {
            Position = pos;
        }

        public static bool operator==(Vert lhs, Vert rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (ReferenceEquals(lhs, null) || ReferenceEquals(rhs, null))
            {
                return false;
            }

            return lhs.Position == rhs.Position;
        }

        public static bool operator!=(Vert lhs, Vert rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            Vert vert = obj as Vert;

            return vert == this;
        }

        public override int GetHashCode()
        {
            return Position.GetHashCode();
        }

        public void RemovePoly(PIdx p_idx)
        {
            if (Frozen)
            {
                throw new InvalidOperationException();
            }

            PIdsxInner.Remove(p_idx);
        }

        public void RemoveEdge(EIdx e_idx)
        {
            if (Frozen)
            {
                throw new InvalidOperationException();
            }

            EIdsxInner.Remove(e_idx);
        }
    }
}