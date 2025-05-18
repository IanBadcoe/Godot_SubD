using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Collections.ObjectModel;

using Godot;

using Godot_Util;
using Godot_Util.CSharp_Util;

namespace SubD.Builders
{
    using CylTypes;

    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;
    using FIdx = Idx<Face>;
    using SectionIdx = Idx<CylSection>;

    using VertPropsFunc = Func<CylSection, int, CylTypes.Topology, CylTypes.VertProps>;
    using EdgePropsFunc = Func<CylSection, int, CylTypes.Topology, CylTypes.EdgeType, CylTypes.EdgeProps>;
    using FacePropsFunc = Func<CylSection, int, CylTypes.Topology, CylTypes.FaceProps>;
    using Geom_Util;

    public class BuildFromCylinders
    {
        Dictionary<SectionIdx, CylSection> SectionsInner = [];

        public ReadOnlyCollection<CylSection> Sections
        {
            get => new ReadOnlyCollection<CylSection>(SectionsInner.Values.ToList());
        }

        // Surface building
        int NextVIdx = 0;
        int NextEIdx = 0;
        int NextFIdx = 0;
        int NextSectionIdx = 0;

        SpatialDictionary<VIdx, Vert> NewVerts = [];
        SpatialDictionary<EIdx, Edge> NewEdges = new();
        SpatialDictionary<FIdx, Face> NewFaces = [];

        Dictionary<(SectionIdx section, int sector, int sub_sector, Topology Topology), FIdx> FaceMap = [];

        void Reset()
        {
            NextVIdx = 0;
            NextEIdx = 0;
            NextFIdx = 0;

            NewVerts = [];
            NewEdges = new();
            NewFaces = [];

            FaceMap = [];
        }

        public void AddSection(CylSection section)
        {
            SectionIdx sect_idx = new(NextSectionIdx++);
            SectionsInner[sect_idx] = section;
            section.SectionIdx = sect_idx;
        }

        public Surface ToSurface()
        {
            int num_sections = SectionsInner.Count;

            // ***we cannot generate single section structures*** because they end up
            // with the inside->outside edges and the outside->inside edges being the same edges
            // and thus needing 2x Backwards and 2x Forwards faces, which we cannot store in the edge structure
            // (could be made to work with special casing, but who would want that anyway???)
            if (num_sections < 2)
            {
                return null;
            }

            CylSection prev_sect = null;

            bool was_prev_section_hollow = false;

            VertLoop prev_inner_loop = null;
            VertLoop prev_outer_loop = null;

            Transform3D transform = Transform3D.Identity;

            SectionIdx prev_sect_idx;

            Dictionary<(Vert, Vert), Edge> made_edges = [];

            for(int i = 0; i < num_sections; i++)
            {
                SectionIdx sect_idx = new(i);

                CylSection sect = SectionsInner[sect_idx];
                prev_sect_idx = prev_sect != null ? prev_sect.SectionIdx : SectionIdx.Empty;

                bool is_first_section = i == 0;
                bool is_last_section = i == num_sections - 1;

                bool is_section_hollow = sect.Solidity == SectionSolidity.Hollow;

                // because we only build loops once, and hang onto them for as long as they are needed
                // we only ever make any Vert once, meaning it is unique to this usage, even if a subsequent
                // Vert is placed at the same location, which in turn makes all Edges/EIdxs context dependent as well

                transform *= sect.Transform;

                // literally everything has an outer loop
                VertLoop outer_loop = MakeOuterLoop(sect, transform);
                VertLoop inner_loop = is_section_hollow ? MakeInnerLoop(sect, transform) : null;

                if (!is_first_section)
                {
                    // always need this, except on the very first section where there is no prev_section_outer
                    JoinLoops(prev_outer_loop, outer_loop, prev_sect, sect, made_edges);
                }

                if (is_first_section)
                {
                    if (is_section_hollow)
                    {
                        JoinLoops(inner_loop, outer_loop, sect, sect, made_edges);
                    }
                    else
                    {
                        FillLoop(outer_loop, sect, DiskFacing.Down, made_edges);
                    }
                }
                else if (is_last_section)
                {
                    // if that last section is hollow, but the previous was not, then there is no prev_inner_loop and we
                    // have to ignore the hollowness
                    if (is_section_hollow)
                    {
                        JoinLoops(outer_loop, inner_loop, sect, sect, made_edges);

                        if (prev_outer_loop != null)
                        {
                            JoinLoops(inner_loop, prev_inner_loop, sect, prev_sect, made_edges);
                        }
                    }
                    else
                    {
                        FillLoop(outer_loop, sect, DiskFacing.Up, made_edges);
                    }
                }
                else
                {
                    if (is_section_hollow && was_prev_section_hollow)
                    {
                        JoinLoops(inner_loop, prev_inner_loop, sect, prev_sect, made_edges);
                    }
                    else if (is_section_hollow && !was_prev_section_hollow)
                    {
                        FillLoop(inner_loop, sect, DiskFacing.Up, made_edges);
                    }
                    else if (!is_section_hollow && was_prev_section_hollow)
                    {
                        FillLoop(prev_inner_loop, prev_sect, DiskFacing.Down, made_edges);
                    }
                    else
                    {
                        // solid->solid only needs the one loop join we always do
                    }
                }

                prev_inner_loop = inner_loop;
                prev_outer_loop = outer_loop;

                was_prev_section_hollow = is_section_hollow;

                prev_sect = sect;
            }

            for(int section = 0; section < SectionsInner.Count; section++)
            {
                SectionIdx sect_idx = new(section);

                bool is_last_section = section == SectionsInner.Count - 1;

                CylSection sect = SectionsInner[sect_idx];

                if (sect.SectorCallback != null)
                {
                    for(int sector = 0; sector < sect.Sectors; sector++)
                    {
                        SectorProps props = sect.SectorCallback(sect, sector);

                        // can only make holes through into hollow cores ATM, could, maybe, theoretically, go right through
                        // a solid to the opposite sector/face, although it depends on there being an even number of sectors
                        // for "opposite" to be unambiguous...
                        //
                        // also the last section is not a valid candidate, because it has no cylindrical section
                        // (which would have to extend onto the section *after* the last one)
                        if (props.HoleProps != null && sect.Solidity == SectionSolidity.Hollow && !is_last_section)
                        {
                            AddHole(sect, sect_idx, sector, props.HoleProps.Value, made_edges);
                        }
                    }
                }
            }

            foreach(VIdx v_idx in NewVerts.Keys)
            {
                VertUtil.SortVertEdgesAndFaces(null, NewVerts[v_idx], false);
            }

            Surface ret = new(NewVerts, NewEdges, NewFaces);

            Reset();

            return ret;
        }

        void AddHole(
            CylSection sect, SectionIdx sect_idx, int sector,
            HoleProps hole_props,
            Dictionary<(Vert, Vert), Edge> made_edges)
        {
            // we are looking for unmodified faces from the Create phase
            // so there are no sub_sectors (== -1) as yet
            FIdx outer_f_idx = FaceMap[(sect_idx, sector, -1, Topology.Outside)];
            FIdx inner_f_idx = FaceMap[(sect_idx, sector, -1, Topology.Inside)];

            Face outer_face = NewFaces[outer_f_idx];
            Face inner_face = NewFaces[inner_f_idx];

            Vector3 outer_centre = FaceCentre(outer_face);
            Vector3 inner_centre = FaceCentre(inner_face);

            float outer_radius = FaceRadiusForHole(outer_face, outer_centre);
            float inner_radius = FaceRadiusForHole(inner_face, inner_centre);

            VertLoop outer_hole_loop;
            VertLoop inner_hole_loop;

            if (hole_props.Clearance.HasValue)
            {
                outer_hole_loop = MakeHoleLoopFromFace(outer_face, hole_props.Clearance.Value, Topology.Outside);
                inner_hole_loop = MakeHoleLoopFromFace(inner_face, hole_props.Clearance.Value, Topology.Inside);

                if (outer_hole_loop == null || inner_hole_loop == null)
                {
                    // // THIS is a PAIN, but no feedback that we failed is also a PAIN
                    // // need maybe to accumumlate a collection of things which failed
                    // // and return that from the ToSurface call
                    // throw new InvalidOperationException();
                    return;
                }

                inner_hole_loop = inner_hole_loop.Reversed();
            }
            else
            {
                // we must have room for the desired hole on the inside and outside
                // and allow a little space
                if (outer_radius < hole_props.Radius * 1.05f)
                {
                    // // THIS is a PAIN, but no feedback that we failed is also a PAIN
                    // // need maybe to accumumlate a collection of things which failed
                    // // and return that from the ToSurface call
                    // throw new InvalidOperationException();
                    return;
                }

                if (inner_radius < hole_props.Radius * 1.05f)
                {
                    // // THIS is a PAIN, but no feedback that we failed is also a PAIN
                    // // need maybe to accumumlate a collection of things which failed
                    // // and return that from the ToSurface call
                    // throw new InvalidOperationException();
                    return;
                }

                Vector3[] outer_face_verts = [..outer_face.Verts.Select(x => x.Position)];
                Vector3 coords_y = FaceUtil.FaceNormal(outer_face_verts);
                Vector3 coords_x = (outer_face_verts[0] - outer_face_verts[1]).Normalized();
                Vector3 coords_z = coords_y.Cross(coords_x);

                outer_hole_loop = MakeOuterHoleLoop(hole_props.Radius, coords_x, coords_y, coords_z, outer_centre, sect);
                inner_hole_loop = MakeInnerHoleLoop(hole_props.Radius, coords_x, coords_y, coords_z, inner_centre, sect).Reversed();
            }

            RemoveFace(outer_f_idx);
            RemoveFace(inner_f_idx);

            VertLoop outer_face_loop = MakeOuterFaceLoop(outer_face);
            VertLoop inner_face_loop = MakeInnerFaceLoop(inner_face).Reversed();

            JoinLoops(outer_face_loop, outer_hole_loop, sect, sect, made_edges, JoinLoopMode.HoleEdge, sector);
            JoinLoops(inner_hole_loop, inner_face_loop, sect, sect, made_edges, JoinLoopMode.HoleEdge, sector);
            // by doing this one last, the edges of the interior faces have already had their callbacks called
            // with the correct EdgeType, meaning the "HoleInterior" logic can ignore them
            JoinLoops(outer_hole_loop, inner_hole_loop, sect, sect, made_edges, JoinLoopMode.HoleInterior, sector);
        }

        void RemoveFace(FIdx f_idx)
        {
            Face face = NewFaces[f_idx];

            foreach(Vert vert in face.Verts)
            {
                vert.Faces = [.. vert.Faces.Where(x => x != face)];
            }

            foreach(Edge edge in face.Edges)
            {
                edge.RemoveFace(face);
            }

            NewFaces.Remove(f_idx);
            // not efficient :-o
            FaceMap.Remove(FaceMap.Where(x => x.Value == f_idx).First().Key);
        }

        float FaceRadiusForHole(Face face, Vector3 centre)
        {
            return face.Edges
                .Select(x => EdgeUtils.EdgeVertDistance(x.Start.Position, x.End.Position, centre))
                .Min();
        }

        Vector3 FaceCentre(Face face)
        {
            return FaceUtil.FaceCentre([.. face.Verts.Select(x => x.Position)]);
        }

        Vector3 FaceNormal(Face face)
        {
            return FaceUtil.FaceNormal([..face.Verts.Select(x => x.Position)]);
        }

        // sometimes we know the two loops are in register, sometimes we need to search for which
        // pairs of verts go together.  the assumption is that a) both loops rotate the same way, and
        // b) finding the closest vert in second loop to the first vert of the first loop is sufficient
        // to find the best cyclic permutation
        enum JoinLoopConnectMode
        {
            AssumeCorrect,
            SearchClosest
        }
        // "first" and "second" here are in the sense of if we imagine a flow up the outside of the structure
        // and down the inside...  this will always start at a down-facing end-cap, and end at an up-facing one
        // EXCEPT for the special case of a totally hollow structure, when there are no end-caps:
        //
        // Simple solid:        Part-hollow         Fully-hollow
        //
        // +---->.<----+        +->+     +<-+       +->+     +<-+
        // ^           ^        ^  |     |  ^       ^  |     |  ^
        // |           |        |  v     v  |       |  |     |  |
        // |           |        +  +->.<-+  +       |  |     |  |
        // |           |        ^           ^       |  |     |  |
        // |           |        +  +<-.->+  +       |  |     |  |
        // |           |        ^  |     |  ^       |  |     |  |
        // +<----.---->+        |  v     v  |       |  v     v  |
        //                      +--+     +--+       +<-+     +->+
        //
        // keeping things right w.r.t this enables us to get all the faces facing the right way

        // when we are building "normal" loop join, we are cycling around the sectors and the param here
        // need/cannot be set as the sector number will just be the loop index inside the routine,
        // and there is no "subsector"
        //
        // when we are building holes, we add multiple faces to the same sector, the sector param must be set
        // and the loop index inside this routine becomes the "subsector"
        void JoinLoops(VertLoop first_loop, VertLoop second_loop,
            CylSection first_sect, CylSection second_sect,
            Dictionary<(Vert, Vert), Edge> made_edges,
            JoinLoopMode mode = JoinLoopMode.BasicFace,
            int sector = -1)
        {
            EdgePropsFunc first_e_callback = first_sect.EdgeCallback ?? new EdgePropsFunc((s, i, t, et) => new EdgeProps());
            EdgePropsFunc second_e_callback = second_sect.EdgeCallback ?? new EdgePropsFunc((s, i, t, et) => new EdgeProps());

            FacePropsFunc first_p_callback = first_sect.FaceCallback ?? new FacePropsFunc((s, i, t) => new FaceProps());
            FacePropsFunc second_p_callback = second_sect.FaceCallback ?? new FacePropsFunc((s, i, t) => new FaceProps());

            // could maybe support different numbers here, but need to match them up based on
            // proximity and not written that yet, possibly overkill anyway
            Util.Assert(first_loop.Verts.Length == second_loop.Verts.Length);

            JoinLoopConnectMode connect_mode = mode == JoinLoopMode.BasicFace ? JoinLoopConnectMode.AssumeCorrect : JoinLoopConnectMode.SearchClosest;

            if (connect_mode == JoinLoopConnectMode.SearchClosest)
            {
                second_loop = PermuteLoopForClosestVerts(second_loop, first_loop.Verts.First());
            }

            for(int i = 0; i < first_loop.Verts.Length; i++)
            {
                Vert first_loop_vert = first_loop.Verts[i];
                Vert second_loop_vert = second_loop.Verts[i];

                int i_plus = (i + 1) % first_loop.Verts.Length;

                Vert first_loop_next_vert = first_loop.Verts[i_plus];
                Vert second_loop_next_vert = second_loop.Verts[i_plus];

                List<EdgeData> face = [];

                // how these 4 lines work
                // we are going to build edges from adjacent pairs of verts in the list
                // so each line has the vert from the start (for this face) vert in the edge
                // line #1 from the current v_idx on the first loop, to the next v_idx on the first loop
                //      sect-owner is the first sect, it's a circumferential edge, starting at vert *i*
                // line #2 from the next v_idx on the first loop, to the next v_idx on the second loop
                //      sect-owner is the first sect, it's a coaxial edge, starting at vert *i_plus*
                // line #3 from the next v_idx on the second loop, to the current v_idx on the second loop
                //      sect-owner is the second sect, it's a circumferential edge, starting at vert *i* (direction doesn't matter for how we map verts to edges in the callback)
                // line #4 from the current v_idx on the second loop, to the current v_idx on the first loop
                //      sect-owner is the first sect, it's a coaxial edge, starting at vert *i*
                //
                //               Circumferential direction
                //             /
                //     + --------------> +   <== first sect / first loop
                //     ^                 |
                //     |                 |  Coaxial direction
                //     |                 |/        (coaxial edges all belong to the "first" sect/loop)
                //     |                 |
                //     |                 v
                //     + <-------------- +   <== second sect /second loop
                //
                //     ^                 ^
                //     |                 |
                //     i               i_plus

                Topology inter_loop_topology = first_loop.Topology == second_loop.Topology ? first_loop.Topology : Topology.Crossing;

                Func<EdgeProps>[] props = new Func<EdgeProps>[4];

                switch (mode)
                {
                    case JoinLoopMode.BasicFace:
                        props[0] = () => first_e_callback(first_sect, i, first_loop.Topology, EdgeType.Circumferential);
                        props[1] = () => first_e_callback(first_sect, i_plus, inter_loop_topology, EdgeType.Axial);
                        props[2] = () => second_e_callback(second_sect, i, second_loop.Topology, EdgeType.Circumferential);
                        props[3] = () => first_e_callback(first_sect, i, inter_loop_topology, EdgeType.Axial);
                        break;
                    case JoinLoopMode.HoleEdge:
                        // the expression on [0] and [2] here is horrible, but:
                        // a) alternate hole-edge face edges are diagonal
                        // b) on alternate faces, the non-diagonal edges are either axial or circumferential
                        // c) and *inside* faces are reversed, swapping the diagonal, and non-diagonal ones
                        props[0] = () => first_e_callback(first_sect, i, first_loop.Topology, EdgeType.HoleEdge);
                        props[1] = () => first_e_callback(first_sect, i_plus, inter_loop_topology, EdgeType.HoleDiagonal);
                        props[2] = () => second_e_callback(second_sect, i, second_loop.Topology, EdgeType.HoleEdge);
                        props[3] = () => first_e_callback(first_sect, i, inter_loop_topology, EdgeType.HoleDiagonal);
                        break;
                    case JoinLoopMode.HoleInterior:
                        // because in AddHole, we call this case last, the ones of these for which EdgeType.Hole is not truue
                        // have already been queried and are not called again ----> "cunning"
                        props[0] = () => first_e_callback(first_sect, i, first_loop.Topology, EdgeType.Hole);
                        props[1] = () => first_e_callback(first_sect, i_plus, inter_loop_topology, EdgeType.Hole);
                        props[2] = () => second_e_callback(second_sect, i, second_loop.Topology, EdgeType.Hole);
                        props[3] = () => first_e_callback(first_sect, i, inter_loop_topology, EdgeType.Hole);
                        break;
                }

                face.Add(new EdgeData(first_loop_vert, props[0]));
                face.Add(new EdgeData(first_loop_next_vert, props[1]));
                face.Add(new EdgeData(second_loop_next_vert, props[2]));
                face.Add(new EdgeData(second_loop_vert, props[3]));

                FaceProps f_props = first_p_callback(first_sect, i, inter_loop_topology);

                int here_sector = i;
                int here_sub_sector = -1;

                // in "Create" operations we are making at most face per section:sector:topology combo
                // in "Modify" mode we go into a single section:sector and create multiple faces, so sub_sector comes into play
                if (mode != JoinLoopMode.BasicFace)
                {
                    here_sector = sector;
                    here_sub_sector = i;
                }

                AddEdgeMode edge_mode = mode == JoinLoopMode.BasicFace ? AddEdgeMode.Strict : AddEdgeMode.Permissive;

                AddFace(
                    face, f_props,
                    first_sect.SectionIdx, second_sect.SectionIdx,
                    here_sector, here_sub_sector,
                    inter_loop_topology,
                    edge_mode, made_edges);
            }
        }

        VertLoop PermuteLoopForClosestVerts(VertLoop loop, Vert vert)
        {
            Vector3 target = vert.Position;

            int closest = -1;
            float closest_dist2 = float.MaxValue;

            for(int i = 0; i < loop.Verts.Length; i++)
            {
                float dist2 = (loop.Verts[i].Position - target).LengthSquared();
                if (closest_dist2 > dist2)
                {
                    closest_dist2 = dist2;
                    closest = i;
                }
            }

            return loop.Shift(closest);
        }

        void FillLoop(VertLoop loop, CylSection sect, DiskFacing facing, Dictionary<(Vert, Vert), Edge> made_edges)
        {
            EdgePropsFunc e_callback = sect.EdgeCallback ?? new EdgePropsFunc((s, i, t, et) => new EdgeProps());
            FacePropsFunc p_callback = sect.FaceCallback ?? new FacePropsFunc((s, i, t) => new FaceProps());

            List<EdgeProps> edge_sharpnesses = [];

            for(int i = 0; i < loop.Verts.Length; i++)
            {
                edge_sharpnesses.Add(e_callback(sect, i, loop.Topology, EdgeType.Circumferential));
            }

            IEnumerable<Vert> verts = loop.Verts;

            if (facing == DiskFacing.Down)
            {
                // when we reverse everything, the vert *before* the edge becomes the vert *after* the edge
                // meaning the sharpnesses are out-by-one
                edge_sharpnesses = edge_sharpnesses.Skip(1).Concat(edge_sharpnesses.Take(1)).Reverse().ToList();

                verts = verts.Reverse();
            }

            IEnumerable<EdgeData> face = verts.Zip(edge_sharpnesses, (x, y) => new EdgeData(x, () => y));

            // kinda hacky, but -1 means an end-cap
            FaceProps f_props = p_callback(sect, -1, loop.Topology);

            // sub_sector here locked to 0 because we never do anything complex for end-caps
            AddFace(
                face.ToList(), f_props,
                sect.SectionIdx, sect.SectionIdx,
                -1, 0,
                loop.Topology,
                AddEdgeMode.Strict, made_edges);
        }

        [DebuggerDisplay("VIdx: {VIdx.Value} Props: (IsSharp: {Props.IsSharp}, Tag: {Props.Tag})")]
        struct EdgeData
        {
            public Vert Vert;
            public Func<EdgeProps> PropFunc;

            public EdgeData(Vert vert, Func<EdgeProps> prop_func)
            {
                Vert = vert;
                PropFunc = prop_func;
            }

            string GetDebuggerDisplay()
            {
                return ToString();
            }
        }

        enum AddEdgeMode
        {
            Strict,         //< when we first generate geometry, we expect every edge to appear exactly twice, forwards then backwards
            Permissive      //< when modifying geomtery, say by adding holes, edges can already exist...
        }

        enum JoinLoopMode
        {
            BasicFace,      //< faces on the inside, outside, or end-cap/annulus of the basic structure
            HoleEdge,       //< faces surrounding a hole on the inside or outside
            HoleInterior    //< faces in the thickness of a hole
        }

        void AddFace(
            List<EdgeData> edge_data, FaceProps p_props,
            SectionIdx first_sect_idx, SectionIdx second_sect_idx,
            int sector, int sub_sector,
            Topology topology,
            AddEdgeMode mode,
            Dictionary<(Vert, Vert), Edge> made_edges)
        {
            List<Edge> edges = [];
            List<Edge> backwards_edges = [];
            List<Edge> forwards_edges = [];

            for(int i = 0; i < edge_data.Count; i++)
            {
                Vert vert = edge_data[i].Vert;
                Vert next_vert = edge_data[(i + 1) % edge_data.Count].Vert;

                Edge edge;
                if (mode == AddEdgeMode.Permissive
                    && made_edges.TryGetValue((vert, next_vert), out edge))
                {
                    // store the real dictionary member for setting its "Forwards" later
                    forwards_edges.Add(edge);
                }
                else if (made_edges.TryGetValue((next_vert, vert), out edge))
                {
                    // store the real dictionary member for setting its "Backwards" later
                    backwards_edges.Add(edge);
                }
                else
                {
                    EIdx e_idx = new(NextEIdx++);
                    edge = new(vert, next_vert);

                    EdgeProps props = edge_data[i].PropFunc();

                    NewEdges[e_idx] = edge;
                    edge.IsSetSharp = props.IsSharp;
                    edge.Tag = props.Tag;

                    // store the real dictionary member for setting its "Forwards" later
                    forwards_edges.Add(edge);

                    // it's a new edge, so let the two verts know
                    vert.Edges.Add(edge);
                    next_vert.Edges.Add(edge);

                    made_edges[(vert, next_vert)] = edge;
                }

                edges.Add(edge);
            }

            Face face = new(edge_data.Select(x => x.Vert), edges);
            face.Tag = p_props.Tag;

            FIdx f_idx = new(NextFIdx++);
            NewFaces[f_idx] = face;

            // on the inside "first section" is the higher numbered one (because we go "up" the outside
            // and back *down* the inside) but for any sort of consistency, we need to name it after the lower numbered
            // on (and the difference is only ever 1)
            SectionIdx fixed_section = topology == Topology.Inside ? second_sect_idx : first_sect_idx;

            // all faces should be unique, in terms of section, sector, inside/outside
            Util.Assert(!FaceMap.ContainsKey((fixed_section, sector, sub_sector, topology)));
            FaceMap[(fixed_section, sector, sub_sector, topology)] = f_idx;

            // it's a new face, so let all the verts know
            foreach(Vert vert in face.Verts)
            {
                vert.Faces.Add(face);
            }

            // forward edges will have the new face on their right
            // backward ones on the left...
            foreach(Edge edge in backwards_edges)
            {
                edge.Backwards = face;
            }

            foreach(Edge edge in forwards_edges)
            {
                edge.Forwards = face;
            }
        }

        ImBounds GetEdgeBounds(Vert v1, Vert v2)
        {
            return v1.GetBounds().UnionedWith(v2.GetBounds());
        }

        VertLoop MakeOuterLoop(CylSection sect, Transform3D trans)
            => new (
                    LoopVertGenerator(sect.Radius, sect.Sectors, sect, trans, Topology.Outside),
                    Topology.Outside
                );

        VertLoop MakeInnerLoop(CylSection sect, Transform3D trans)
            => new (
                    LoopVertGenerator(sect.Radius - sect.Thickness, sect.Sectors, sect, trans, Topology.Inside),
                    Topology.Inside
                );

        VertLoop MakeHoleLoopFromFace(Face face, float clearance, Topology topology)
        {
            // we need two clearances and at least a tiny space for the hole...
            float c2 = clearance * clearance * 2.1f;

            Vert[] verts = face.Verts.ToArray();

            List<Vector3> dirs = [];

            Vert prev_vert = verts.Last();
            foreach(Vert vert in verts)
            {
                Vector3 diff = prev_vert.Position - vert.Position;

                float d2 = diff.LengthSquared();

                if (d2 < c2)
                {
                    return null;
                }

                dirs.Add(diff / Mathf.Sqrt(d2));

                prev_vert = vert;
            }

            List<Vert> v_idxs = [];

            for(int i = 0; i < verts.Length; i++)
            {
                Vector3 dir_prev_away = dirs[i];
                Vector3 dir_next_towards = dirs[(i + 1) % verts.Length];

                Vector3 new_pos = verts[i].Position + (dir_prev_away - dir_next_towards) * clearance;

                VIdx v_idx = new VIdx(NextVIdx++);
                Vert vert  = new Vert(new_pos);
                NewVerts[v_idx] = vert;

                v_idxs.Add(vert);
            }

            return new VertLoop(v_idxs, topology);
        }

        VertLoop MakeOuterFaceLoop(Face outer_face)
            => new (outer_face.Verts, Topology.Outside);

        VertLoop MakeInnerFaceLoop(Face inner_face)
            => new (inner_face.Verts, Topology.Inside);

        VertLoop MakeOuterHoleLoop(
            float radius,
            Vector3 coords_x, Vector3 coords_y, Vector3 coords_z,
            Vector3 centre, CylSection sect)
        {
            return new (
                    HoleLoopVertGenerator(radius, centre, coords_x, coords_y, coords_z, sect, Topology.Outside),
                    Topology.Outside
                );
        }

        VertLoop MakeInnerHoleLoop(
            float radius,
            Vector3 coords_x, Vector3 coords_y, Vector3 coords_z,
            Vector3 centre, CylSection sect)
        {
            VertLoop temp = new (
                    HoleLoopVertGenerator(radius, centre, coords_x, coords_y, coords_z, sect, Topology.Inside),
                    Topology.Inside
                );

            // Inside faces are the other way around...
            return temp.Reversed();
        }

        IEnumerable<Vert> HoleLoopVertGenerator(
            float radius,
            Vector3 centre,
            Vector3 coords_x, Vector3 coords_y, Vector3 coords_z,
            CylSection sect,
            Topology topology)
        {
            Transform3D extra_trans = new();

            extra_trans.Basis.Row0 = coords_x;
            extra_trans.Basis.Row1 = coords_y;
            extra_trans.Basis.Row2 = coords_z;
            extra_trans = extra_trans.Inverse();

            extra_trans.Origin = centre;// - trans.Origin;     //< the transform, and the centre, both contain the overall offset of this section

            // holes are always quads, because they are punched in quad faces
            return LoopVertGenerator(radius, 4, sect, extra_trans, topology, Mathf.Pi / 4).Reverse();
        }

        IEnumerable<Vert> LoopVertGenerator(float radius, int sectors, CylSection sect, Transform3D trans, Topology topology, float start_angle_radians = 0)
        {
            for(int i = 0; i < sectors; i++)
            {
                float angle = start_angle_radians + i * Mathf.Pi * 2.0f / sectors;

                Vector3 pos = new(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);

                pos = trans * pos;

                Vert vert = new(pos);

                VIdx v_idx = new(NextVIdx++);

                NewVerts[v_idx] = vert;

                if (sect.VertCallback != null)
                {
                    VertProps props = sect.VertCallback(sect, i, topology);
                    vert.IsSharp = props.IsSharp;
                    vert.Tag = props.Tag;
                }

                yield return vert;
            }
        }
    }
}