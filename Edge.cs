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
    using FIdx = Idx<Face>;
    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;

    [DebuggerDisplay("{Key.Value} : {Start.Key}->{End.Key} Forwards:{Forwards.Key} Backwards:{Backwards.Key} SetSharp: {IsSetSharp}")]
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

        // if there currently are any, the meaning of "Forwards" is that face uses this edge's verts
        // in the forwards (Start -> End) direction, where the backwards face uses them the other way around
        // every edge will be used once forwards, and once backwards, because faces all need to rotate the same way
        // so along the contact edge, one is going one way, and one the other...
        //
        // (this is true, even if they meet at an acute angle, because "rotation" has to be measured looking along
        //  the face normal)

        public Face Backwards { get; set; }

        public Face Forwards { get; set; }

        public IEnumerable<Vert> Verts
        {
            get
            {
                yield return Start;
                yield return End;
            }
        }

        public IEnumerable<Face> Faces
        {
            get
            {
                if (Backwards != null)
                {
                    yield return Backwards;
                }

                if (Forwards != null)
                {
                    yield return Forwards;
                }
            }
        }

        public EIdx Key { get; set; }

        public Vector3 MidPoint => (Start.Position + End.Position) / 2;

        // potentially dangerous as *shallow* copy
        public Edge(Vert start, Vert end, Face backwards = null, Face forwards = null)
        {
            Start = start;
            End = end;

            Backwards = backwards;
            Forwards = forwards;
        }

        // potentially dangerous as *shallow* copy
        public Edge(Edge old_edge) : this(old_edge.Start, old_edge.End, old_edge.Backwards, old_edge.Forwards)
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

        public Face OtherFace(Face face)
        {
            if (face == Backwards)
            {
                return Forwards;
            }

            Util.Assert(face == Forwards);

            return Backwards;
        }

        public void RemoveFace(Face face)
        {
            if (face == Backwards)
            {
                Backwards = null;
            }
            else if (face == Forwards)
            {
                Forwards = null;
            }
            else
            {
                Util.Assert(false);
            }
        }

        public Edge Reversed()
        {
            return new Edge(End, Start, Forwards, Backwards);
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

        internal void RemoveVert(Vert vert)
        {
            // an edge with a null vert is an incredibly disfunctional thing, should only do this as a step on the way
            // to removing the edge, or other highly transient state
            if (vert == Start)
            {
                Start = null;
            }
            else
            {
                Util.Assert(vert == End);

                End = null;
            }
        }
    }
}
