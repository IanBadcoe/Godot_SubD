using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

using VIdx = SubD.Idx<SubD.Vert>;
using EIdx = SubD.Idx<SubD.Edge>;
using PIdx = SubD.Idx<SubD.Poly>;

using EdgeSharpFunc = System.Func<SubD.CylSection, int, SubD.BuildFromCylinders.Topology, SubD.BuildFromCylinders.EdgeType, bool>;
using VertSharpFunc = System.Func<SubD.CylSection, int, SubD.BuildFromCylinders.Topology, bool>;

namespace SubD
{
    public class BuildFromCylinders
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

        public enum EdgeType
        {
            Circumferential,        //< running round the section
            Coaxial                   //< running forwards/backwards between sections
        }

        List<CylSection> Sections = [];

        // Surface building
        int NextVIdx = 0;
        int NextEIdx = 0;
        int NextPIdx = 0;

        Dictionary<VIdx, Vert> NewVerts = [];
        // unlike in the cube-builder, we only want to coalesce edges when they belong to the same VertLoop
        BidirectionalDictionary<EIdx, Edge> NewEdges = new();
        Dictionary<PIdx, Poly> NewPolys = [];

        public void AddSection(CylSection section)
        {
            Sections.Add(section);
        }

        public Surface ToSurface()
        {
            int num_sections = Sections.Count;

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

            for(int i = 0; i < num_sections; i++)
            {
                CylSection sect = Sections[i];

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
                    JoinLoops(prev_outer_loop, outer_loop,
                        prev_sect, sect);
                }

                if (is_first_section)
                {
                    if (is_section_hollow)
                    {
                        JoinLoops(inner_loop, outer_loop,
                            sect, sect);
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
                        JoinLoops(outer_loop, inner_loop,
                            sect, sect);

                        if (prev_outer_loop != null)
                        {
                            JoinLoops(inner_loop, prev_inner_loop,
                                sect, prev_sect);
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
                        JoinLoops(inner_loop, prev_inner_loop,
                            sect, prev_sect);
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

            foreach(VIdx v_idx in NewVerts.Keys)
            {
                Vert old_vert = NewVerts[v_idx];

                NewVerts[v_idx] = VertUtil.ToVertWithSortedEdgesAndPolys(old_vert, v_idx, NewEdges, NewPolys);
            }

            Surface ret = new(NewVerts, NewEdges, NewPolys);

            Reset();

            return ret;
        }

        private void Reset()
        {
            NextVIdx = 0;
            NextEIdx = 0;
            NextPIdx = 0;

            NewVerts = [];
            NewEdges = new();
            NewPolys = [];
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
        void JoinLoops(VertLoop first_loop, VertLoop second_loop,
            CylSection first_sect, CylSection second_sect)
        {
            EdgeSharpFunc first_callback = first_sect.EdgeSharpener ?? new EdgeSharpFunc((s, i, t, et) => false);
            EdgeSharpFunc second_callback = second_sect.EdgeSharpener ?? new EdgeSharpFunc((s, i, t, et) => false);

            // could maybe support different numbers here, but need to match them up based on
            // proximity and not written that yet, possibly overkill anyway
            Util.Assert(first_loop.VIdxs.Length == second_loop.VIdxs.Length);

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

                poly.Add(new EdgeData(first_loop_v_idx, first_callback(first_sect, i, first_loop.Topology, EdgeType.Circumferential)));
                poly.Add(new EdgeData(first_loop_next_v_idx, first_callback(first_sect, i_plus, inter_loop_topology, EdgeType.Coaxial)));
                poly.Add(new EdgeData(second_loop_next_v_idx, second_callback(second_sect, i, second_loop.Topology, EdgeType.Circumferential)));
                poly.Add(new EdgeData(second_loop_v_idx, first_callback(first_sect, i, inter_loop_topology, EdgeType.Coaxial)));

                AddPoly(poly);

//                AddPoly([prev_first_loop_v_idx, first_loop_vidx, second_loop_vidx, prev_second_loop_v_idx]);
            }
        }

        void FillLoop(VertLoop loop, CylSection sect, DiskFacing facing)
        {
            EdgeSharpFunc callback = sect.EdgeSharpener ?? new EdgeSharpFunc((s, i, t, et) => false);

            List<bool> edge_sharpnesses = [];

            for(int i = 0; i < loop.VIdxs.Length; i++)
            {
                edge_sharpnesses.Add(callback(sect, i, loop.Topology, EdgeType.Circumferential));
            }

            IEnumerable<VIdx> v_idxs = loop.VIdxs;

            if (facing == DiskFacing.Down)
            {
                // when we reverse everything, the vert *before* the edge becomes the vert *after* the edge
                // meaning the sharpnesses are out-by-one
                edge_sharpnesses = edge_sharpnesses.Skip(1).Concat(edge_sharpnesses.Take(1)).Reverse().ToList();

                v_idxs = v_idxs.Reverse();
            }

            IEnumerable<EdgeData> poly = v_idxs.Zip(edge_sharpnesses, (x, y) => new EdgeData(x, y));

            AddPoly(poly.ToList());
        }

        struct EdgeData
        {
            public VIdx VIdx;
            public bool IsSharp;

            public EdgeData(VIdx v_idx, bool is_sharp)
            {
                VIdx = v_idx;
                IsSharp = is_sharp;
            }
        }

        private void AddPoly(List<EdgeData> v_idxs)
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

                if (NewEdges.Contains(r_edge))
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

                    NewEdges[e_idx] = edge;
                    edge.IsSetSharp = v_idxs[i].IsSharp;

                    e_idxs.Add(e_idx);
                    // store the real dictionary member for setting its "Right" later
                    right_edges.Add(edge);

                    // it's a new edge, so let the two verts know
                    NewVerts[edge.Start].AddEIdx(e_idx);
                    NewVerts[edge.End].AddEIdx(e_idx);
                }
            }

            Poly poly = new(v_idxs.Select(x => x.VIdx), e_idxs);

            PIdx p_idx = new(NextPIdx++);
            NewPolys[p_idx] = poly;

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
                    LoopVertGenerator(sect.Radius, sect, trans, Topology.Outside),
                    Topology.Outside
                );

        VertLoop MakeInnerLoop(CylSection sect, Transform3D trans)
            => new (
                    LoopVertGenerator(sect.Radius - sect.Thickness, sect, trans, Topology.Inside),
                    Topology.Inside
                );

        IEnumerable<VIdx> LoopVertGenerator(float radius, CylSection sect, Transform3D trans, Topology topology)
        {
            for(int i = 0; i < sect.Sections; i++)
            {
                float angle = i * Mathf.Pi * 2.0f / sect.Sections;

                Vector3 pos = new(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);

                pos = trans * pos;

                Vert vert = new(pos);

                VIdx v_idx = new(NextVIdx++);

                NewVerts[v_idx] = vert;

                if (sect.VertSharpener != null)
                {
                    vert.IsSharp = sect.VertSharpener(sect, i, topology);
                }

                yield return v_idx;
            }
        }
    }
}