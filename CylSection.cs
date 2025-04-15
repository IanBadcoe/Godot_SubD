using Godot;

namespace SubD
{
    using CylTypes;

    using SectionIdx = Idx<CylSection>;

    using VertPropsFunc = System.Func<CylSection, int, CylTypes.Topology, CylTypes.VertProps>;
    using EdgePropsFunc = System.Func<CylSection, int, CylTypes.Topology, CylTypes.EdgeType, CylTypes.EdgeProps>;
    using PolyPropsFunc = System.Func<CylSection, int, CylTypes.Topology, CylTypes.PolyProps>;
    using SectorPropsFunc = System.Func<CylSection, int, CylTypes.SectorProps>;

    // one section will (theoretically) be a 2-sided disk (Solid).  Side view:
    //
    //           centre line
    //         /
    //        |
    // +======.======+
    //        <------>
    //         radius
    //
    // or a two-sided annulus
    //
    //           centre line
    //         /
    //        |
    // +===+  .  +===+
    // <------>  <--->
    //    \         \
    //      radius    thickness
    //
    // HOWEVER ***we cannot generate single section structures*** because they end up
    // with the inside->outside edges and the outside->inside edges being the same edges
    // and thus needing 2x Left and 2x Right polys, which we cannot store in the edge structure
    // (could be made to work with special casing, but who would want that anyway???)
    //
    // longer constructs are made by piling those up and connecting lengthways:
    //
    // ++   .   ++   Section#4, Hollow, Radius=6, Thickness=2
    // | \     / |   (Height of Section#3)
    // +  +-.-+  +   Section#3, Hollow, Radius=6, Thickness=4
    // |         |   (Height of Section#2)
    // |         |
    // +    .    +   Section#2, Solid, Radius=6, Thickness ignored
    //  \       /    (height of Section#1)
    //   +==.==+     Section#1, Solid, Radius=4, thickness ignored
    //
    // A hollow tube with one "solid" ring, somewhere in it, will have the tube blocked at the position
    // of the solid ring by two geometrically-coincident disks, topologically separate and facing opposite directions
    // (subdivision may pull them apart)
    //
    // -ve height can be used to step downwards from the current level
    public class CylSection
    {
        // Hollow sections have an inner surface Thickness inside the outer.
        // Contiguous hollow sections connect their inner surfaces and continue until they either
        // reach the end, or else hit a solid section.
        // Chained hollow sections use their thickness

        public float Radius
        {
            get;
            private set;
        }

        public int Sectors
        {
            get;
            private set;
        }

        public SectionSolidity Solidity
        {
            get;
            private set;
        }

        public float Thickness
        {
            get;
            private set;
        }

        public Transform3D Transform
        {
            get;
            private set;
        }

        public EdgePropsFunc EdgeCallback
        {
            get;
            private set;
        }

        public VertPropsFunc VertCallback
        {
            get;
            private set;
        }

        public PolyPropsFunc PolyCallback
        {
            get;
            private set;
        }

        public SectorPropsFunc SectorCallback
        {
            get;
            private set;
        }

        public SectionIdx SectionIdx
        {
            get;
            set;
        } = SectionIdx.Empty;

        public CylSection(
            float radius = 3,
            float length = 1,
            int sectors = 6,
            SectionSolidity solidity = SectionSolidity.Solid,
            float thickness = 1,
            float rot_x_degrees = 0, float rot_y_degrees = 0, float rot_z_degrees = 0,
            EdgePropsFunc edge_callback = null,
            VertPropsFunc vert_callback = null,
            PolyPropsFunc poly_callback = null,
            SectorPropsFunc sector_callback = null)
            : this(
                radius, sectors, solidity, thickness,
                Transform3D.Identity
                    .RotatedLocal(new Vector3(0, 1, 0), rot_y_degrees * Mathf.Pi / 180)         //<
                    .RotatedLocal(new Vector3(1, 0, 0), rot_x_degrees * Mathf.Pi / 180)         //< just a guess for the best order to apply these in
                    .RotatedLocal(new Vector3(0, 0, 1), rot_z_degrees * Mathf.Pi / 180)         //< (could offer some clever quat thing as well)
                    .Translated(new Vector3(0, length, 0)),
                edge_callback, vert_callback, poly_callback, sector_callback)
        {
        }

        public CylSection(
            float radius = 3,
            int sectors = 6,
            SectionSolidity solidity = SectionSolidity.Solid,
            float thickness = 1,
            Transform3D? transform = null,
            EdgePropsFunc edge_callback = null,
            VertPropsFunc vert_callback = null,
            PolyPropsFunc poly_callback = null,
            SectorPropsFunc sector_callback = null)
        {
            Radius = radius;

            Sectors = sectors;

            Solidity = solidity;
            Thickness = thickness;

            Transform = transform ?? Transform3D.Identity;

            EdgeCallback = edge_callback;
            VertCallback = vert_callback;
            PolyCallback = poly_callback;
            SectorCallback = sector_callback;
       }
    }
}