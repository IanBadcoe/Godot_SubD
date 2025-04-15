using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using System.Diagnostics;
using System.Collections.ObjectModel;

namespace SubD
{
    using CylTypes;

    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;
    using PIdx = Idx<Poly>;
    using SectionIdx = Idx<CylSection>;

    using VertPropsFunc = Func<CylSection, int, CylTypes.Topology, CylTypes.VertProps>;
    using EdgePropsFunc = Func<CylSection, int, CylTypes.Topology, CylTypes.EdgeType, CylTypes.EdgeProps>;
    using PolyPropsFunc = Func<CylSection, int, CylTypes.Topology, CylTypes.PolyProps>;
    using System.Reflection.Metadata.Ecma335;

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
        int NextPIdx = 0;
        int NextSectionIdx = 0;

        Dictionary<VIdx, Vert> NewVerts = [];
        BidirectionalDictionary<EIdx, Edge> NewEdges = new();
        Dictionary<PIdx, Poly> NewPolys = [];

        BidirectionalDictionary<(SectionIdx section, int sector, int sub_sector, Topology Topology), PIdx> PolyMap = [];

        private void Reset()
        {
            NextVIdx = 0;
            NextEIdx = 0;
            NextPIdx = 0;

            NewVerts = [];
            NewEdges = new();
            NewPolys = [];

            PolyMap = [];
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
            // and thus needing 2x Left and 2x Right polys, which we cannot store in the edge structure
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

                transform = transform * sect.Transform;

                // literally everything has an outer loop
                VertLoop outer_loop = MakeOuterLoop(sect, transform);
                VertLoop inner_loop = is_section_hollow ? MakeInnerLoop(sect, transform) : null;

                if (!is_first_section)
                {
                    // always need this, except on the very first section where there is no prev_section_outer
                    JoinLoops(prev_outer_loop, outer_loop, prev_sect, sect);
                }

                if (is_first_section)
                {
                    if (is_section_hollow)
                    {
                        JoinLoops(inner_loop, outer_loop, sect, sect);
                    }
                    else
                    {
                        FillLoop(outer_loop, sect, DiskFacing.Down);
                    }
                }
                else if (is_last_section)
                {
                    // if that last section is hollow, but the previous was not, then there is no prev_inner_loop and we
                    // have to ignore the hollowness
                    if (is_section_hollow)
                    {
                        JoinLoops(outer_loop, inner_loop, sect, sect);

                        if (prev_outer_loop != null)
                        {
                            JoinLoops(inner_loop, prev_inner_loop, sect, prev_sect);
                        }
                    }
                    else
                    {
                        FillLoop(outer_loop, sect, DiskFacing.Up);
                    }
                }
                else
                {
                    if (is_section_hollow && was_prev_section_hollow)
                    {
                        JoinLoops(inner_loop, prev_inner_loop, sect, prev_sect);
                    }
                    else if (is_section_hollow && !was_prev_section_hollow)
                    {
                        FillLoop(inner_loop, sect, DiskFacing.Up);
                    }
                    else if (!is_section_hollow && was_prev_section_hollow)
                    {
                        FillLoop(prev_inner_loop, prev_sect, DiskFacing.Down);
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
                            AddHole(sect, sect_idx, sector, props.HoleProps.Value);
                        }
                    }
                }
            }

            foreach(VIdx v_idx in NewVerts.Keys)
            {
                Vert old_vert = NewVerts[v_idx];

                NewVerts[v_idx] = VertUtil.ToVertWithSortedEdgesAndPolys(old_vert, v_idx, NewEdges, NewPolys);
            }

            Surface ret = new(NewVerts, NewEdges, NewPolys);

            Reset();

            return ret;
        }

        private void AddHole(CylSection sect, SectionIdx sect_idx, int sector, HoleProps hole_props)
        {
            // we are looking for unmodified polys from the Create phase
            // so there are no sub_sectors (== -1) as yet
            PIdx outer_p_idx = PolyMap[(sect_idx, sector, -1, Topology.Outside)];
            PIdx inner_p_idx = PolyMap[(sect_idx, sector, -1, Topology.Inside)];

            Poly outer_poly = NewPolys[outer_p_idx];
            Poly inner_poly = NewPolys[inner_p_idx];

            Vector3 outer_centre = PolyCentre(outer_poly);
            Vector3 inner_centre = PolyCentre(inner_poly);

            float outer_radius = PolyRadiusForHole(outer_poly, outer_centre);
            float inner_radius = PolyRadiusForHole(inner_poly, inner_centre);

            VertLoop outer_hole_loop;
            VertLoop inner_hole_loop;

            if (hole_props.Clearance.HasValue)
            {
                outer_hole_loop = MakeHoleLoopFromPoly(outer_poly, hole_props.Clearance.Value, Topology.Outside);
                inner_hole_loop = MakeHoleLoopFromPoly(inner_poly, hole_props.Clearance.Value, Topology.Inside);

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

                Vector3[] outer_poly_verts = [..outer_poly.VIdxs.Select(x => NewVerts[x].Position)];
                Vector3 coords_y = PolyUtil.PolyNormal(outer_poly_verts);
                Vector3 coords_x = (outer_poly_verts[0] - outer_poly_verts[1]).Normalized();
                Vector3 coords_z = coords_y.Cross(coords_x);

                outer_hole_loop = MakeOuterHoleLoop(hole_props.Radius, coords_x, coords_y, coords_z, outer_centre, sect);
                inner_hole_loop = MakeInnerHoleLoop(hole_props.Radius, coords_x, coords_y, coords_z, inner_centre, sect).Reversed();
            }

            RemovePoly(outer_p_idx);
            RemovePoly(inner_p_idx);

            VertLoop outer_poly_loop = MakeOuterPolyLoop(outer_poly);
            VertLoop inner_poly_loop = MakeInnerPolyLoop(inner_poly).Reversed();

            JoinLoops(outer_poly_loop, outer_hole_loop, sect, sect, JoinLoopMode.HoleEdge, sector);
            JoinLoops(inner_hole_loop, inner_poly_loop, sect, sect, JoinLoopMode.HoleEdge, sector);
            // by doing this one last, the edges of the interior polys have already had their callbacks called
            // with the correct EdgeType, meaning the "HoleInterior" logic can ignore them
            JoinLoops(outer_hole_loop, inner_hole_loop, sect, sect, JoinLoopMode.HoleInterior, sector);
        }

        private void RemovePoly(PIdx p_idx)
        {
            Poly poly = NewPolys[p_idx];

            foreach(Vert vert in poly.VIdxs.Select(x => NewVerts[x]))
            {
                vert.RemovePoly(p_idx);
            }

            foreach(Edge edge in poly.EIdxs.Select(x => NewEdges[x]))
            {
                edge.RemovePoly(p_idx);
            }

            NewPolys.Remove(p_idx);
            PolyMap.Remove(p_idx);
        }

        private float PolyRadiusForHole(Poly poly, Vector3 centre)
        {
            return poly.EIdxs
                .Select(x => NewEdges[x])
                .Select(x => EdgeUtils.EdgeVertDistance(NewVerts[x.Start].Position, NewVerts[x.End].Position, centre))
                .Min();
        }

        private Vector3 PolyCentre(Poly poly)
        {
            return PolyUtil.PolyCentre([.. poly.VIdxs.Select(x => NewVerts[x].Position)]);
        }

        private Vector3 PolyNormal(Poly poly)
        {
            return PolyUtil.PolyNormal([..poly.VIdxs.Select(x => NewVerts[x].Position)]);
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
        // keeping things right w.r.t this enables us to get all the polys facing the right way

        // when we are building "normal" loop join, we are cycling around the sectors and the param here
        // need/cannot be set as the sector number will just be the loop index inside the routine,
        // and there is no "subsector"
        //
        // when we are building holes, we add multiple polys to the same sector, the sector param must be set
        // and the loop index inside this routine becomes the "subsector"
        void JoinLoops(VertLoop first_loop, VertLoop second_loop,
            CylSection first_sect, CylSection second_sect,
            JoinLoopMode mode = JoinLoopMode.BasicPoly, int sector = -1)
        {
            EdgePropsFunc first_e_callback = first_sect.EdgeCallback ?? new EdgePropsFunc((s, i, t, et) => new EdgeProps());
            EdgePropsFunc second_e_callback = second_sect.EdgeCallback ?? new EdgePropsFunc((s, i, t, et) => new EdgeProps());

            PolyPropsFunc first_p_callback = first_sect.PolyCallback ?? new PolyPropsFunc((s, i, t) => new PolyProps());
            PolyPropsFunc second_p_callback = second_sect.PolyCallback ?? new PolyPropsFunc((s, i, t) => new PolyProps());

            // could maybe support different numbers here, but need to match them up based on
            // proximity and not written that yet, possibly overkill anyway
            Util.Assert(first_loop.VIdxs.Length == second_loop.VIdxs.Length);

            JoinLoopConnectMode connect_mode = mode == JoinLoopMode.BasicPoly ? JoinLoopConnectMode.AssumeCorrect : JoinLoopConnectMode.SearchClosest;

            if (connect_mode == JoinLoopConnectMode.SearchClosest)
            {
                second_loop = PermuteLoopForClosestVerts(second_loop, first_loop.VIdxs.First());
            }

            for(int i = 0; i < first_loop.VIdxs.Length; i++)
            {
                VIdx first_loop_v_idx = first_loop.VIdxs[i];
                VIdx second_loop_v_idx = second_loop.VIdxs[i];

                int i_plus = (i + 1) % first_loop.VIdxs.Length;

                VIdx first_loop_next_v_idx = first_loop.VIdxs[i_plus];
                VIdx second_loop_next_v_idx = second_loop.VIdxs[i_plus];

                List<EdgeData> poly = [];

                // how these 4 lines work
                // we are going to build edges from adjacent pairs of verts in the list
                // so each line has the vert from the start (for this poly) vert in the edge
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
                    case JoinLoopMode.BasicPoly:
                        props[0] = () => first_e_callback(first_sect, i, first_loop.Topology, EdgeType.Circumferential);
                        props[1] = () => first_e_callback(first_sect, i_plus, inter_loop_topology, EdgeType.Axial);
                        props[2] = () => second_e_callback(second_sect, i, second_loop.Topology, EdgeType.Circumferential);
                        props[3] = () => first_e_callback(first_sect, i, inter_loop_topology, EdgeType.Axial);
                        break;
                    case JoinLoopMode.HoleEdge:
                        // the expression on [0] and [2] here is horrible, but:
                        // a) alternate hole-edge poly edges are diagonal
                        // b) on alternate polys, the non-diagonal edges are either axial or circumferential
                        // c) and *inside* polys are reversed, swapping the diagonal, and non-diagonal ones
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

                poly.Add(new EdgeData(first_loop_v_idx, props[0]));
                poly.Add(new EdgeData(first_loop_next_v_idx, props[1]));
                poly.Add(new EdgeData(second_loop_next_v_idx, props[2]));
                poly.Add(new EdgeData(second_loop_v_idx, props[3]));

                PolyProps p_props = first_p_callback(first_sect, i, inter_loop_topology);

                int here_sector = i;
                int here_sub_sector = -1;

                // in "Create" operations we are making at most poly per section:sector:topology combo
                // in "Modify" mode we go into a single section:sector and create multiple polys, so sub_sector comes into play
                if (mode != JoinLoopMode.BasicPoly)
                {
                    here_sector = sector;
                    here_sub_sector = i;
                }

                AddEdgeMode edge_mode = mode == JoinLoopMode.BasicPoly ? AddEdgeMode.Strict : AddEdgeMode.Permissive;

                AddPoly(poly, p_props, first_sect.SectionIdx, second_sect.SectionIdx, here_sector, here_sub_sector, inter_loop_topology, edge_mode);
            }
        }

        private VertLoop PermuteLoopForClosestVerts(VertLoop loop, VIdx v_idx)
        {
            Vector3 target = NewVerts[v_idx].Position;

            int closest = -1;
            float closest_dist2 = float.MaxValue;

            for(int i = 0; i < loop.VIdxs.Length; i++)
            {
                float dist2 = (NewVerts[loop.VIdxs[i]].Position - target).LengthSquared();
                if (closest_dist2 > dist2)
                {
                    closest_dist2 = dist2;
                    closest = i;
                }
            }

            return loop.Shift(closest);
        }

        void FillLoop(VertLoop loop, CylSection sect, DiskFacing facing)
        {
            EdgePropsFunc e_callback = sect.EdgeCallback ?? new EdgePropsFunc((s, i, t, et) => new EdgeProps());
            PolyPropsFunc p_callback = sect.PolyCallback ?? new PolyPropsFunc((s, i, t) => new PolyProps());

            List<EdgeProps> edge_sharpnesses = [];

            for(int i = 0; i < loop.VIdxs.Length; i++)
            {
                edge_sharpnesses.Add(e_callback(sect, i, loop.Topology, EdgeType.Circumferential));
            }

            IEnumerable<VIdx> v_idxs = loop.VIdxs;

            if (facing == DiskFacing.Down)
            {
                // when we reverse everything, the vert *before* the edge becomes the vert *after* the edge
                // meaning the sharpnesses are out-by-one
                edge_sharpnesses = edge_sharpnesses.Skip(1).Concat(edge_sharpnesses.Take(1)).Reverse().ToList();

                v_idxs = v_idxs.Reverse();
            }

            IEnumerable<EdgeData> poly = v_idxs.Zip(edge_sharpnesses, (x, y) => new EdgeData(x, () => y));

            // kinda hacky, but -1 means an end-cap
            PolyProps p_props = p_callback(sect, -1, loop.Topology);

            // sub_sector here locked to 0 because we never do anything complex for end-caps
            AddPoly(poly.ToList(), p_props, sect.SectionIdx, sect.SectionIdx, -1, 0, loop.Topology, AddEdgeMode.Strict);
        }

        [DebuggerDisplay("VIdx: {VIdx.Value} Props: (IsSharp: {Props.IsSharp}, Tag: {Props.Tag})")]
        struct EdgeData
        {
            public VIdx VIdx;
            public Func<EdgeProps> PropFunc;

            public EdgeData(VIdx v_idx, Func<EdgeProps> prop_func)
            {
                VIdx = v_idx;
                PropFunc = prop_func;
            }

            private string GetDebuggerDisplay()
            {
                return ToString();
            }
        }

        enum AddEdgeMode
        {
            Strict,         //< when we first generate geometry, we expect every edge to appeach exactly once, forwards then backwards
            Permissive      //< when modifying geomtery, say by adding holes, edges can already exist...
        }

        enum JoinLoopMode
        {
            BasicPoly,      //< polys on the inside, outside, or end-cap/annulus of the basic structure
            HoleEdge,       //< polys surrounding a hole on the inside or outside
            HoleInterior    //< polys in the thickness of a hole
        }

        private void AddPoly(
            List<EdgeData> v_idxs, PolyProps p_props,
            SectionIdx first_sect_idx, SectionIdx second_sect_idx,
            int sector, int sub_sector,
            Topology topology,
            AddEdgeMode mode)
        {
            List<EIdx> e_idxs = [];
            List<Edge> left_edges = [];
            List<Edge> right_edges = [];

            for(int i = 0; i < v_idxs.Count; i++)
            {
                VIdx v_idx = v_idxs[i].VIdx;
                VIdx next_v_idx = v_idxs[(i + 1) % v_idxs.Count].VIdx;

                Edge edge = new(v_idx, next_v_idx);
                Edge r_edge = edge.Reversed();

                if (mode == AddEdgeMode.Permissive
                    && NewEdges.Contains(edge))
                {
                    EIdx e_idx = NewEdges[edge];
                    e_idxs.Add(e_idx);
                    // store the real dictionary member for setting its "Right" later
                    right_edges.Add(NewEdges[e_idx]);
                }
                else if (NewEdges.Contains(r_edge))
                {
                    EIdx e_idx = NewEdges[r_edge];
                    e_idxs.Add(e_idx);
                    // store the real dictionary member for setting its "Left" later
                    left_edges.Add(NewEdges[e_idx]);
                }
                else
                {
                    // we shouldn't ever see an edge more than twice
                    // once in each direction...
                    Util.Assert(!NewEdges.Contains(edge));

                    EIdx e_idx = new(NextEIdx++);

                    EdgeProps props = v_idxs[i].PropFunc();

                    NewEdges[e_idx] = edge;
                    edge.IsSetSharp = props.IsSharp;
                    edge.Tag = props.Tag;

                    e_idxs.Add(e_idx);
                    // store the real dictionary member for setting its "Right" later
                    right_edges.Add(edge);

                    // it's a new edge, so let the two verts know
                    NewVerts[edge.Start].AddEIdx(e_idx);
                    NewVerts[edge.End].AddEIdx(e_idx);
                }
            }

            Poly poly = new(v_idxs.Select(x => x.VIdx), e_idxs);
            poly.Tag = p_props.Tag;

            PIdx p_idx = new(NextPIdx++);
            NewPolys[p_idx] = poly;

            // on the inside "first section" is the higher numbered one (because we go "up" the outside
            // and back *down* the inside) but for any sort of consistency, we need to name it after the lower numbered
            // on (and the difference is only ever 1)
            SectionIdx fixed_section = topology == Topology.Inside ? second_sect_idx : first_sect_idx;

            // all polys should be unique, in terms of section, sector, inside/outside
            Util.Assert(!PolyMap.Contains((fixed_section, sector, sub_sector, topology)));
            PolyMap[(fixed_section, sector, sub_sector, topology)] = p_idx;

            // it's a new poly, so let all the verts know
            foreach(Vert vert in poly.VIdxs.Select(x => NewVerts[x]))
            {
                vert.AddPIdx(p_idx);
            }

            // forward edges will have the new face on their right
            // backward ones on the left...
            foreach(Edge edge in left_edges)
            {
                edge.Left = p_idx;
            }

            foreach(Edge edge in right_edges)
            {
                edge.Right = p_idx;
            }
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

        private VertLoop MakeHoleLoopFromPoly(Poly poly, float clearance, Topology topology)
        {
            // we need two clearances and at least a tiny space for the hole...
            float c2 = clearance * clearance * 2.1f;

            Vert[] verts = poly.VIdxs.Select(x => NewVerts[x]).ToArray();

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

            List<VIdx> v_idxs = [];

            for(int i = 0; i < verts.Length; i++)
            {
                Vector3 dir_prev_away = dirs[i];
                Vector3 dir_next_towards = dirs[(i + 1) % verts.Length];

                Vector3 new_pos = verts[i].Position + (dir_prev_away - dir_next_towards) * clearance;

                VIdx v_idx = new VIdx(NextVIdx++);
                NewVerts[v_idx] = new Vert(new_pos);

                v_idxs.Add(v_idx);
            }

            return new VertLoop(v_idxs, topology);
        }

        private VertLoop MakeOuterPolyLoop(Poly outer_poly)
            => new (outer_poly.VIdxs, Topology.Outside);

        private VertLoop MakeInnerPolyLoop(Poly inner_poly)
            => new (inner_poly.VIdxs, Topology.Inside);

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

            // Inside polys are the other way around...
            return temp.Reversed();
        }

        IEnumerable<VIdx> HoleLoopVertGenerator(
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
            return LoopVertGenerator(radius, 4, sect, extra_trans, topology, MathF.PI / 4).Reverse();
        }

        IEnumerable<VIdx> LoopVertGenerator(float radius, int sectors, CylSection sect, Transform3D trans, Topology topology, float start_angle_radians = 0)
        {
            for(int i = 0; i < sectors; i++)
            {
                float angle = start_angle_radians + i * Mathf.Pi * 2.0f / sectors;

                Vector3 pos = new(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);

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

                yield return v_idx;
            }
        }
    }
}