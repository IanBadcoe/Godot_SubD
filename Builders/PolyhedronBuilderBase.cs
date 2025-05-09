using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

using Godot;

using Godot_Util;
using Godot_Util.CSharp_Util;
using Geom_Util;

namespace SubD.Builders
{
    using VIdx = Idx<Vert>;
    using EIdx = Idx<Edge>;
    using PIdx = Idx<Poly>;

        // the basis of merging is as follows:
    // all the incoming polyhedra are in a MergeGroup (currently an int, but can become an object
    // if we develop any group-wise settings) polyhedra from different merge groups do not merge
    // *at all* and are as if they are in different surfaces (in fact, if you use ToSurface*s* thats
    // exactly what you get.)
    //
    // The one thing this does not allow, however, is a -merges-> b -merges-> c BUT a -does-not-merge-> c
    // (such as might be required, for example, to make a ring with a minimum-sized gap in it)
    // so additionally, we:
    // - track for all uncoming polyhedra, an Object GeneratorIdentity (GI)
    // - have a HashSet<Object, Object> ForbiddenMerges
    // - track, for all generated features (vert/edge/poly), what set (because a feature can come from several
    //   say if it is the edge where 4 cubes meet) of GIs it came from
    // -- (actually, we will only track the GIs that actually feature in ForbiddenMerges, an optimisation)
    //
    // and the total rule is:
    // - different merge-groups do not merge (absolute rule)
    // - within a group, everything merges *unless* we specifically tag two of the generators of two candidate
    //   merge features as not to be merged in ForbiddenMerges
    //
    // Note: For 100% coherent cases, this totally makes sense and is reproducible and hopefully insensitive to
    //   (say) the order that generators are added.  However, in more ambiguous cases, such as if:
    //   - we have three cubes: A, B, C
    //     - which all share an edge E
    //     - and are all in the same group
    //  - but with, additionally, A and C forbidden to merge
    //
    //   Then, if the order is ABC (or BAC), then AB will be processed first and (obviously) completely merge
    //   (including E), and when C comes along later, it will stay separate on that edge, but still merging any
    //   other B:C common features.
    //
    //   e.g. we will end up with (A|B).C (where "|" indicates complete merge and "." indicates a more selective situation
    //
    //   Which is a different outcome from if the order was CBA or BCA, which would lead to A.(BC)
    //   And if the order is CAB or ACB then A and C will come first, remain separate, and whichever one B does the complete
    //   merge with will depend on the arbitrary order which the search finds features in, and may even lead to some sort of
    //   mixed outcome: A.B.C
    //
    //   complete merge of ABC  | ABC forbid AC       | BCA forbid AC     | ACB in this simple example (and maybe in anything)
    //   +-------+-------+      | +-------+-------+   | +-------+-------+ | made from cubes) will just give one of the above
    //   | B     | C     |      | | B     |\  C   |   | | B     | C     | | but there may be more complex cases where a mix of the
    //   |       |       |      | |       | |     |   | |       |       | | cleaner outcomes can happen
    //   |       |       |      | |       | |     |   | |       |       | |
    //   +-------+-------+      | +-------+ +-----+   | +-------+-------+ |
    //   | A     |\             | | A     |\ \        | |\       \        |
    //   |       | E (fully     | |       | E E'      | |  -----+ E       |
    //   |       |     merged)  | |       |           | | A     |\        |
    //   +-------+              | +-------+           | +-------+ E'      |
    //
    // However any less ambiguous case, such as
    // PQR
    // W S
    // VUT
    // - where everything merges except P-W should be well behaved, the ambiguity only arises in the case where we have some
    // feature(s) (such as, original example, E) which ambiguously could merge with A, or C, but not both...

    public abstract class PolyhedronBuilderBase
    {
        public class AnnotatedPolyhedron
        {
            public int MergeGroup;
            public Surface Polyhedron;
            public IGeneratorIdentity GeneratorIdentity;        // just so we can tell where a feature (vert/edge/face) came from in the merged
                                                                  // and thus (e.g.) look up whether it should merge with features from some other
                                                                  // generator, e.g.
                                                                  // in BuildFromCubes, the GI will be the cube that was added initially, and we'll
                                                                  // have the ability to look up two GIs (Cubes in that case) and determine whether
                                                                  // they have been set specifically *not* to merge
        }

        protected List<AnnotatedPolyhedron> MergeStock {get; set; }

        HashSet<(IGeneratorIdentity Id1, IGeneratorIdentity Id2)> ForbiddenMerges = [];

        public void SetForbidSpecificMerge(IGeneratorIdentity id1, IGeneratorIdentity id2)
        {
            ForbiddenMerges.Add((id1, id2));
        }

        // polyhedra are:
        // - convex
        // - tagged with a "group-id"
        // -- polyhedra tagged the same are merged at all points of contact
        // --- UNLESS they are a pair, specifically excluded from merging
        protected abstract void PopulateMergeStock();

        public Surface ToSurface()
        {
            PopulateMergeStock();

            if (MergeStock.Count == 0)
            {
                return null;
            }

            Dictionary<int, Surface> intermediate_merges = [];

            foreach(int merge_group in MergeStock.Select(x => x.MergeGroup).Distinct())
            {
                foreach(AnnotatedPolyhedron to_merge in MergeStock.Where(x => x.MergeGroup == merge_group))
                {
                    if (!intermediate_merges.ContainsKey(merge_group))
                    {
                        intermediate_merges[merge_group] = to_merge.Polyhedron;
                    }
                    else
                    {
                        MergeOne(intermediate_merges[merge_group], to_merge);
                    }
                }
            }

            Surface ret = null;

            foreach(Surface surf in intermediate_merges.Values)
            {
                if (ret == null)
                {
                    ret = surf;
                }
                else
                {
                    ret.DumbConcat(surf);
                }
            }

            Reset();

            return ret;
        }

        private void MergeOne(Surface merge_target, AnnotatedPolyhedron to_merge)
        {
            // first we concat the new surface into the target in a "dumb" manner which doesn't do any sort of merging and is just
            // we still have the two surfaces separate
            merge_target.DumbConcat(to_merge.Polyhedron);
        }

        public virtual void Reset()
        {
            MergeStock = [];
            ForbiddenMerges = [];
        }
    }
}