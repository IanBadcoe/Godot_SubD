using System.IO;
using Godot;

namespace SubD
{
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

        public enum SectionSolidity
        {
            Hollow,
            Solid
        }

        public float Radius
        {
            get;
            private set;
        }

        public int Sections
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

        public CylSection(
            float radius = 3,
            float length = 1,
            int sections = 6,
            SectionSolidity solidity = SectionSolidity.Solid,
            float thickness = 1,
            float offset_angle_degrees = 0)
        {
            Radius = radius;

            Sections = sections;

            Solidity = solidity;
            Thickness = thickness;

            Transform =
                Transform3D.Identity
                .RotatedLocal(new Vector3(0, 1, 0), offset_angle_degrees)
                .Translated(new Vector3(0, length, 0));
        }

        public CylSection(
            float radius = 3,
            int sections = 6,
            SectionSolidity solidity = SectionSolidity.Solid,
            float thickness = 1,
            Transform3D? transform = null)
        {
            Radius = radius;

            Sections = sections;

            Solidity = solidity;
            Thickness = thickness;

            Transform = transform ?? Transform3D.Identity;
        }
    }
}