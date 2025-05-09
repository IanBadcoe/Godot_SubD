using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

using Geom_Util;
using Geom_Util.Interfaces;

using Godot_Util.CSharp_Util;



namespace SubD
{
    using VIdx = SubD.Idx<SubD.Vert>;
    using EIdx = SubD.Idx<SubD.Edge>;
    using PIdx = SubD.Idx<SubD.Poly>;

    [DebuggerDisplay("Position = {Position}")]
    public class Vert : ISpatialValue<VIdx>
    {
        public Vector3 Position
        {
            get;
            set;
        }

        public List<Edge> Edges { get; set; } = [];

        public List<Poly> Polys { get; set; } = [];

        // normal is cached on here when calculated by the Surface
        // could just cache that inside the Surface
        public Vector3? Normal
        {
            get;
            set;
        }

#region metadata
        // metadata, used by other algorithms, needs propogating when verts are copied to a new Surface
        public bool IsSharp {
            get;
            set;
        }

        public string Tag {
            get;
            set;
        }
#endregion

        public VIdx Key { get; set; }

        public Vert(Vector3 pos)
        {
            Position = pos;
        }

        // potentially dangerous as *shallow* copy
        public Vert(Vector3 pos, IEnumerable<Edge> edges, IEnumerable<Poly> polys) : this(pos)
        {
            Edges = [.. edges];
            Polys = [.. polys];
        }

        // potentially dangerous as *shallow* copy
        public Vert(Vert old_vert) : this(old_vert.Position, old_vert.Edges, old_vert.Polys)
        {
            SetMetadataFrom(old_vert);
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
                ret = new Vert(Position, Edges, Polys);
            }

            ret.SetMetadataFrom(this);

            return ret;
        }

        public void SetMetadataFrom(Vert original_vert)
        {
            IsSharp = original_vert.IsSharp;
            Tag = original_vert.Tag;
        }

        public ImBounds GetBounds()
        {
            return new ImBounds(new ImVec3(Position));
        }
    }
}