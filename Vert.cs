using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        public List<EIdx> EIdxsInner = new();
        public EIdx[] EIdxs
        {
            get => EIdxsInner.ToArray();
        }

        public List<PIdx> PIdxsInner = new();
        public PIdx[] PIdxs
        {
            get => PIdxsInner.ToArray();
        }

        // a place to cache it when calculated by the Surface
        public Vector3? Normal
        {
            get;
            set;
        }

        public bool IsSharp {
            get;
            set;
        }

        public void AddEIdx(EIdx e_idx)
        {
            // we need to continue adding these for a while,
            // but once construction is done we shouldn't touch them anymore
            if (Frozen)
            {
                throw new InvalidOperationException();
            }

            EIdxsInner.Add(e_idx);
        }

        public void AddPIdx(PIdx p_idx)
        {
            // we need to continue adding these for a while,
            // but once construction is done we shouldn't touch them anymore
            if (Frozen)
            {
                throw new InvalidOperationException();
            }

            PIdxsInner.Add(p_idx);
        }

        public void Freeze()
        {
            Frozen = true;
        }

        public Vert(Vector3 pos)
        {
            Position = pos;
        }

        public Vert(Vector3 pos, IEnumerable<EIdx> e_idxs, IEnumerable<PIdx> p_idxs) : this(pos)
        {
            EIdxsInner = e_idxs.ToList();
            PIdxsInner = p_idxs.ToList();
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

            PIdxsInner.Remove(p_idx);
        }

        public void RemoveEdge(EIdx e_idx)
        {
            if (Frozen)
            {
                throw new InvalidOperationException();
            }

            EIdxsInner.Remove(e_idx);
        }

        public Vert Clone(bool position_only)
        {
            Vert ret;

            if (position_only)
            {
                ret = new Vert(Position);
            }
            else
            {
                ret = new Vert(Position, EIdxs, PIdxs);
            }

            ret.IsSharp = IsSharp;

            return ret;
        }
    }
}