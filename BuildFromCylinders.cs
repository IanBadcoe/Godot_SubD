using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

using VIdx = SubD.Idx<SubD.Vert>;
using EIdx = SubD.Idx<SubD.Edge>;
using PIdx = SubD.Idx<SubD.Poly>;

namespace SubD
{
    public class BuildFromCylinder
    {
        enum DiskFacing
        {
            Up,
            Down,
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
            bool was_prev_section_hollow = false;

            VertLoop prev_inner_loop = null;
            VertLoop prev_outer_loop = null;

            int num_sections = Sections.Count;

            // ***we cannot generate single section structures*** because they end up
            // with the inside->outside edges and the outside->inside edges being the same edges
            // and thus needing 2x Left and 2x Right polys, which we cannot store in the edge structure
            // (could be made to work with special casing, but who would want that anyway???)
            if (num_sections < 2)
            {
                return null;
            }

            Transform3D transform = Transform3D.Identity;

            for(int i = 0; i < num_sections; i++)
            {
                CylSection sect = Sections[i];

                bool is_first_section = i == 0;
                bool is_last_section = i == num_sections - 1;

                bool is_section_hollow = sect.Solidity == CylSection.SectionSolidity.Hollow;

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
                    JoinLoops(prev_outer_loop, outer_loop);
                }

                if (is_first_section)
                {
                    if (is_section_hollow)
                    {
                        JoinLoops(inner_loop, outer_loop);
                    }
                    else
                    {
                        FillLoop(outer_loop, DiskFacing.Down);
                    }
                }
                else if (is_last_section)
                {
                    // if that last section is hollow, but the previous was not, then there is no prev_inner_loop and we
                    // have to ignore the hollowness
                    if (is_section_hollow)
                    {
                        JoinLoops(outer_loop, inner_loop);

                        if (prev_outer_loop != null)
                        {
                            JoinLoops(inner_loop, prev_inner_loop);
                        }
                    }
                    else
                    {
                        FillLoop(outer_loop, DiskFacing.Up);
                    }
                }
                else
                {
                    if (is_section_hollow && was_prev_section_hollow)
                    {
                        JoinLoops(inner_loop, prev_inner_loop);
                    }
                    else if (is_section_hollow && !was_prev_section_hollow)
                    {
                        FillLoop(inner_loop, DiskFacing.Up);
                    }
                    else if (!is_section_hollow && was_prev_section_hollow)
                    {
                        FillLoop(prev_inner_loop, DiskFacing.Down);
                    }
                    else
                    {
                        // solid->solid only needs the one loop join we always do
                    }
                }

                prev_inner_loop = inner_loop;
                prev_outer_loop = outer_loop;

                was_prev_section_hollow = is_section_hollow;
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
        void JoinLoops(VertLoop first_loop, VertLoop second_loop)
        {
            // could maybe support different numbers here, but need to match them up based on
            // proximity and not written that yet, possibly overkill anyway
            Util.Assert(first_loop.VIdxs.Length == second_loop.VIdxs.Length);

            VIdx prev_first_loop_v_idx = first_loop.VIdxs.Last();
            VIdx prev_second_loop_v_idx = second_loop.VIdxs.Last();

            for(int i = 0; i < first_loop.VIdxs.Length; i++)
            {
                VIdx first_loop_vidx = first_loop.VIdxs[i];
                VIdx second_loop_vidx = second_loop.VIdxs[i];

                AddPoly([prev_first_loop_v_idx, first_loop_vidx, second_loop_vidx, prev_second_loop_v_idx]);

                prev_first_loop_v_idx = first_loop_vidx;
                prev_second_loop_v_idx = second_loop_vidx;
            }
        }

        void FillLoop(VertLoop loop, DiskFacing facing)
        {
            switch(facing)
            {
                case DiskFacing.Up:
                    FillLoopInner(loop);

                    break;
                case DiskFacing.Down:
                    FillLoopInner(loop.Reversed());

                    break;
            }
        }

        private void FillLoopInner(VertLoop loop)
        {
            AddPoly(loop.VIdxs);
        }

        private void AddPoly(VIdx[] v_idxs)
        {
            List<EIdx> e_idxs = [];
            List<Edge> left_edges = [];
            List<Edge> right_edges = [];

            for(int i = 0; i < v_idxs.Length; i++)
            {
                VIdx v_idx = v_idxs[i];
                VIdx next_v_idx = v_idxs[(i + 1) % v_idxs.Length];

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

                    NewEdges[edge] = e_idx;
                    e_idxs.Add(e_idx);
                    // store the real dictionary member for setting its "Right" later
                    right_edges.Add(edge);

                    // it's a new edge, so let the two verts know
                    NewVerts[edge.Start].AddEIdx(e_idx);
                    NewVerts[edge.End].AddEIdx(e_idx);
                }
            }

            Poly poly = new(v_idxs, e_idxs);

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

        VertLoop MakeOuterLoop(CylSection sect, Transform3D trans) => new(LoopVertGenerator(sect.Radius, sect.Sections, trans));

        VertLoop MakeInnerLoop(CylSection sect, Transform3D trans) => new(LoopVertGenerator(sect.Radius - sect.Thickness, sect.Sections, trans));

        IEnumerable<VIdx> LoopVertGenerator(float radius, int sections, Transform3D trans)
        {
            for(int i = 0; i < sections; i++)
            {
                float angle = i * Mathf.Pi * 2.0f / sections;

                Vector3 pos = new(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);

                pos = trans * pos;

                Vert vert = new(pos);

                VIdx v_idx = new(NextVIdx++);

                NewVerts[v_idx] = vert;

                yield return v_idx;
            }
        }
    }
}