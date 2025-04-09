using System;
using System.Collections.Generic;
using System.Diagnostics;
using PIdx = SubD.Idx<SubD.Poly>;
using VIdx = SubD.Idx<SubD.Vert>;

namespace SubD
{
    [DebuggerDisplay("{Start}->{End} Left: {SubD.Idx<SubD.Poly>.Idx2String(Left)} Right: {SubD.Idx<SubD.Poly>.Idx2String(Right)} Sharp: {IsSharp}")]
    public class Edge
    {
        public VIdx Start
        {
            get;
            private set;
        }

        public VIdx End
        {
            get;
            private set;
        }

        public bool IsSharp {
            get;
            set;
        }

        // ideally we would be a const object, but construction is quite spread out in time and having a separate "builder" version of this
        // (and vert, and maybe poly) would be a pain, so let's instead have a "Freeze" operation at the end of construction
        bool Frozen = false;

        public void Freeze()
        {
            Frozen = true;
        }

        // left and right adjoining polys, if there are any, in the Start->End direction viewed from "outside"
        // (this means that the "right" poly uses this edge in the "forwards" direction)

        PIdx? LeftInner;
        public PIdx? Left
        {
            get
            {
                return LeftInner;
            }
            set
            {
                if (Frozen)
                {
                    throw new InvalidOperationException();
                }

                // we do not expect to always have this, but if we do, we expect to set it once and not change it
                // (could add ability to null it here, if that becomes an issue)
                Util.Assert(LeftInner == null);

                LeftInner = value;
            }
        }

        PIdx? RightInner;
        public PIdx? Right
        {
            get
            {
                return RightInner;
            }
            set
            {
                if (Frozen)
                {
                    throw new InvalidOperationException();
                }

                // we do not expect to always have this, but if we do, we expect to set it once and not change it
                // (could add ability to null it here, if that becomes an issue)
                Util.Assert(RightInner == null);

                RightInner = value;
            }
        }

        public IEnumerable<VIdx> VIdxs
        {
            get
            {
                yield return Start;
                yield return End;
            }
        }

        public IEnumerable<PIdx> PIdxs
        {
            get
            {
                if (Left.HasValue)
                {
                    yield return Left.Value;
                }

                if (Right.HasValue)
                {
                    yield return Right.Value;
                }
            }
        }

        public Edge(VIdx start, VIdx end, PIdx? left = null, PIdx? right = null)
        {
            Start = start;
            End = end;

            Left = left;
            Right = right;
        }

        public VIdx? OtherVert(VIdx vert)
        {
            if (vert == Start)
            {
                return End;
            }

            Util.Assert(vert == End);

            return Start;
        }

        public PIdx? OtherPoly(PIdx poly)
        {
            if (poly == Left)
            {
                return Right;
            }

            Util.Assert(poly == Right);

            return Left;
        }

        public void RemovePoly(PIdx p_idx)
        {
            if (Frozen)
            {
                throw new InvalidOperationException();
            }

            if (p_idx == Left)
            {
                LeftInner = null;
            }
            else if (p_idx == Right)
            {
                RightInner = null;
            }
            else
            {
                Util.Assert(false);
            }
        }

        public Edge Reversed()
        {
            return new Edge(End, Start, Right, Left);
        }

        public static bool operator==(Edge lhs, Edge rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (ReferenceEquals(lhs, null) || ReferenceEquals(rhs, null))
            {
                return false;
            }

            return lhs.Start == rhs.Start && lhs.End == rhs.End;
        }

        public static bool operator!=(Edge lhs, Edge rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            var edge = obj as Edge;

            return this == edge;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Start.GetHashCode(), End.GetHashCode());
        }
    }
}
