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
        Inside,         //< on the inside of the structure (if there are any "hollow" sections)
        Outside,        //< on the outside of the structure
        Crossing        //< crossing from the outside to the inside, end-caps, or the sides of holes...
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

    public struct FaceProps
    {
        public string Tag;

        public FaceProps()
            : this(null)
        {
        }

        public FaceProps(string tag = null)
        {
            Tag = tag;
        }
    }

    public struct SectorProps
    {
        public HoleProps? HoleProps;

        public SectorProps(HoleProps? hole_props)
        {
            HoleProps = hole_props;
        }
    }

    public struct HoleProps
    {
        // X in this case is the direction around the cylinder
        // Y is the direction along the cylinder
        // (makes sense if the cylinder is upright)
        public float Radius;        //< if Clearance not set, available space on the face targetted must be 5% larger than this
        public float? Clearance;    //< if Clearance set, then hole size is calculated this much in from the existing face corners

        // if there is not room for any of the above cases, then the hole is skipped

        public HoleProps(float radius = 0, float? clearance = null)
        {
            Radius = radius;
            Clearance = clearance;
        }
    }

    // picture worth 1000 words:
    // prev        this              next
    // section     section           section
    //
    //            |                 |
    //            |                 |
    //            |                 |
    // -----------+-------(a)-------+--------------
    //            |\__(hd)         /|
    //            | \             / | a -> axial
    //            |  +--(he)-----+  | c -> circumferential
    //            |  |           |  | hd -> hole-diagonal
    //           (c) |           |  | he -> hole-edge
    //            | (he)         |  |
    //            |  |           |  | h -> hole, not shown, as extend down beneath the inner 4 '+' marks
    //            |  +-----------+  |
    //            | /             \ |
    //            |/               \|
    // -----------+-----------------+--------------
    //            |                 |
    //            |                 |
    //            |                 |

    public enum EdgeType
    {
        Circumferential,            //< running round the section
        Axial,                      //< running forwards<->>backwards between sections
        HoleEdge,                   //< running around the edge of a hole
        HoleDiagonal,               //< from the corners of the face the hole is in, to the corners of the hole
        Hole                        //< running from the outside to the inside, along the length of the hole
    }
}