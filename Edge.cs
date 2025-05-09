using System;
using System.Collections.Generic;
using System.Diagnostics;

using Godot;

using Godot_Util;

using Geom_Util;
using Geom_Util.Interfaces;
using Godot_Util.CSharp_Util;

namespace SubD
{
    using PIdx = Idx<Poly>;
    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;

    [DebuggerDisplay("{Start}->{End} Left: {SubD.Idx<SubD.Poly>.Idx2String(Left)} Right: {SubD.Idx<SubD.Poly>.Idx2String(Right)} Sharp: {IsSharp}")]
    public class Edge : ISpatialValue<EIdx>
    {
        public Vert Start
        {
            get;
            set;
        }

        public Vert End
        {
            get;
            set;
        }

#region cached_data
        // whether our angle measures as "sharp" and our normal
        // are just things we cache here
        // we could store them elsewhere (Surface, and CatmullClarkSubdivider, resp.)
        public bool IsObservedSharp
        {
            get;
            set;
        }

        public Vector3? Normal
        {
            get;
            set;
        }
#endregion

#region metadata
        // meta data is used by algorithms which act on us
        // and needs propogating when the edge is split
        public bool IsSetSharp {
            get;
            set;
        }

        public string Tag {
            get;
            set;
        }
#endregion

        // left and right adjoining polys, if there are any, in the Start->End direction viewed from "outside"
        // (this means that the "right" poly uses this edge in the "forwards" direction)

        Poly LeftInner;
        public Poly Left
        {
            get
            {
                return LeftInner;
            }
            set
            {
                LeftInner = value;
            }
        }

        Poly RightInner;
        public Poly Right
        {
            get
            {
                return RightInner;
            }
            set
            {
                RightInner = value;
            }
        }

        public IEnumerable<Vert> Verts
        {
            get
            {
                yield return Start;
                yield return End;
            }
        }

        public IEnumerable<Poly> Polys
        {
            get
            {
                if (Left != null)
                {
                    yield return Left;
                }

                if (Right != null)
                {
                    yield return Right;
                }
            }
        }

        public EIdx Key { get; set; }

        public Vector3 MidPoint => (Start.Position + End.Position) / 2;

        // potentially dangerous as *shallow* copy
        public Edge(Vert start, Vert end, Poly left = null, Poly right = null)
        {
            Start = start;
            End = end;

            Left = left;
            Right = right;
        }

        // potentially dangerous as *shallow* copy
        public Edge(Edge old_edge) : this(old_edge.Start, old_edge.End, old_edge.Left, old_edge.Right)
        {
            SetMetaDataFrom(old_edge);
        }

        public Vert OtherVert(Vert vert)
        {
            if (vert == Start)
            {
                return End;
            }
            else if (vert == End)
            {
                return Start;
            }

            return null;
        }

        public Poly OtherPoly(Poly poly)
        {
            if (poly == Left)
            {
                return Right;
            }

            Util.Assert(poly == Right);

            return Left;
        }

        public void RemovePoly(Poly poly)
        {
            if (poly == Left)
            {
                LeftInner = null;
            }
            else if (poly == Right)
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

        public void SetMetaDataFrom(Edge original_edge)
        {
            IsSetSharp = original_edge.IsSetSharp;
            Tag = original_edge.Tag;
        }

        public ImBounds GetBounds()
        {
            return Start.GetBounds().UnionedWith(End.GetBounds());
        }
    }
}
