using System.Reflection;

namespace SubD.CylTypes
{
    enum DiskFacing
    {
        Up,
        Down,
    }

    // not a brilliant name, only used so far in the vert/edge callbacks, but could comwe up elsewhere
    public enum Topology
    {
        Inside,
        Outside,
        Crossing
    }

    public enum SectionSolidity
    {
        Hollow,
        Solid
    }

    public struct VertProps
    {
        public bool IsSharp;
        public string Tag;         //< for finding/filtering later, not inherted by any verts added during split

        public VertProps()
            : this(false, null)
        {

        }

        public VertProps(bool is_sharp = false, string tag = null)
        {
            IsSharp = is_sharp;
            Tag = tag;
        }
    }

    public struct EdgeProps
    {
        public bool IsSharp;
        public string Tag;         //< for finding/filtering later, inherted by all edges this edge is split into

        public EdgeProps()
            : this(false, null)
        {
        }

        public EdgeProps(bool is_sharp = false, string tag = null)
        {
            IsSharp = is_sharp;
            Tag = tag;
        }
    }

    public struct PolyProps
    {
        public string Tag;

        public PolyProps()
            : this(null)
        {
        }

        public PolyProps(string tag = null)
        {
            Tag = tag;
        }
    }

    // public struct SectorProperties
    // {
    //     public bool IsConnector;
    //     public bool IsHole;
    //     public HoleProperties? HoleProperties;
    // }

    // public struct HoleProperties
    // {
    //     public float Radius;
    //     public int Sectors;
    // }

    public enum EdgeType
    {
        Circumferential,        //< running round the section
        Coaxial                   //< running forwards/backwards between sections
    }
}