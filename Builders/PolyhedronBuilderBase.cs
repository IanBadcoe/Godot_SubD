// #define PROFILE_ON

using System;
using System.Collections.Generic;
using System.Linq;

using Godot;

using Godot_Util;
using System.Data;
using Geom_Util;

namespace SubD.Builders
{
    // using VIdx = Idx<Vert>;
    // using EIdx = Idx<Edge>;
    // using FIdx = Idx<Face>;

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
    // - track, for all generated features (vert/edge/face), what set (because a feature can come from several
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

        protected bool Dirty {get; set; }

        public void ForbidSpecificMerge(IGeneratorIdentity id1, IGeneratorIdentity id2)
        {
            // put it in both ways, to make searching easier
            ForbiddenMerges.Add((id1, id2));
            ForbiddenMerges.Add((id2, id1));

            Dirty = true;
        }

        // polyhedra are:
        // - convex - not actually required, but there may be more awkward corner-cases I did not test if nonconvex
        // - tagged with a "merge-group-id" - defaults to zero, but that *is* a valid merge-group-id
        // -- polyhedra tagged the same are merged at all common faces
        // -- have the skeleton where I may add vert-vert and edge-edge merges later, if I need them
        // - tagged with a IGeneratorIdentity (GI)
        // -- can be null
        // -- chosen pairs of GIs can be specific excluded from merging, even if in the same group
        protected abstract void PopulateMergeStock_Impl();

        void PopulateMergeStock()
        {
            Dirty = false;

            PopulateMergeStock_Impl();
        }

        public IDictionary<int, Surface> ToSurfaces(bool merge = true)
        {
            if (Dirty)
            {
                PoorMansProfiler.Start("PopulateMergeStock");
                PopulateMergeStock();
                PoorMansProfiler.End("PopulateMergeStock");
            }
            if (MergeStock.Count == 0)
            {
                return null;
            }

            Dictionary<int, Surface> intermediate_merges = [];

            foreach(int merge_group in MergeStock.Select(x => x.MergeGroup).Distinct())
            {
                PoorMansProfiler.Start("DumbConcat");
                // first we dumbly concat all the parts of the merge-group, this means they all
                // come into the same surface but nothing is yet merged, everything is separate with
                // duplicate faces/edges/verts on any points of contact
                foreach (AnnotatedPolyhedron to_merge in MergeStock.Where(x => x.MergeGroup == merge_group))
                {
                    if (!intermediate_merges.ContainsKey(merge_group))
                    {
                        intermediate_merges[merge_group] = to_merge.Polyhedron;
                    }
                    else
                    {
                        intermediate_merges[merge_group].DumbConcat(to_merge.Polyhedron);
                    }
                }
                PoorMansProfiler.End("DumbConcat");

                Surface surf = intermediate_merges[merge_group];

                // we do it this way, will all the dumb merges first, because any vert-vert or edge-edge merges
                // we detect at intermediately merged stages, may have turned into face-face ones at the end
                // and having processed them as vert-vert or face-face would introduce new output geometry, which would
                // then invalidated the face-face we really wanted

                if (merge)
                {
                    MergeFaces(surf);
                    MergeEdges(surf);
                    MergeVerts(surf);
                }

                PoorMansProfiler.Start("Debug");
                surf.DebugValidate();
                PoorMansProfiler.End("Debug");
            }

            return intermediate_merges;
        }

        public Surface ToSurface(bool merge = true)
        {
            PoorMansProfiler.Start("ToSurfaces");
            IDictionary<int, Surface> intermediate_merges = ToSurfaces(merge);
            PoorMansProfiler.End("ToSurfaces");

            if (intermediate_merges == null)
            {
                return null;
            }

            PoorMansProfiler.Start("Concat");
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
            PoorMansProfiler.End("Concat");

            return ret;
        }

        void MergeVerts(Surface surf)
        {
            // not sure if I need this
            // if I do, redo via RTree search in inner loop
            List<Vert> verts = surf.Verts.Values.ToList();

            for(int i = 0; i < verts.Count - 1; i++)
            {
                Vert vert1 = verts[i];

                if (!surf.Verts.Contains(vert1))
                {
                    // gone in an earier merge
                    continue;
                }

                for(int j = i + 1; j < verts.Count; j++)
                {
                    Vert vert2 = verts[j];

                    if (!surf.Verts.Contains(vert2))
                    {
                        // gone in an earier merge
                        continue;
                    }

                    if (AreVertMergeTargets(surf, vert1, vert2))
                    {
                        MergeVertPair(surf, vert1, vert2);
                    }
                }
            }
        }

        private void MergeVertPair(Surface surf, Vert vert1, Vert vert2)
        {
            throw new NotImplementedException();
        }

        private bool AreVertMergeTargets(Surface surf, Vert vert1, Vert vert2)
        {
            return false;       // <-- not written yet...
        }

        void MergeEdges(Surface surf)
        {
            // not sure if I need this
            // if I do, redo via RTree search in inner loop
            // List<Edge> edges = surf.Edges.Values.ToList();

            // for(int i = 0; i < edges.Count - 1; i++)
            // {
            //     Edge edge1 = edges[i];

            //     if (!surf.Edges.Contains(edge1))
            //     {
            //         // gone in an earier merge
            //         continue;
            //     }

            //     for(int j = i + 1; j < edges.Count; j++)
            //     {
            //         Edge edge2 = edges[j];

            //         if (!surf.Edges.Contains(edge2))
            //         {
            //             // gone in an earier merge
            //             continue;
            //         }

            //         if (AreEdgeMergeTargets(surf, edge1, edge2))
            //         {
            //             MergeEdgePair(surf, edge1, edge2);
            //         }
            //     }
            // }
        }

        private void MergeEdgePair(Surface surf, Edge edge1, Edge edge2)
        {
            throw new NotImplementedException();
        }

        private bool AreEdgeMergeTargets(Surface surf, Edge edge1, Edge edge2)
        {
            return false;       // <-- not written yet...
        }

        void MergeFaces(Surface surf)
        {
            PoorMansProfiler.Start("MergeFaces");
            // the Faces collection on surf is going to lose some members during this process
            // so the easiest way that does not involve restarting loops is
            // to build a list now, let it go out of date, and
            List<Face> faces = [.. surf.Faces.Values];

            for(int i = 0; i < faces.Count - 1; i++)
            {
                Face face1 = faces[i];

                ImBounds bounds = face1.GetBounds();

                List<Face> matching_faces = [.. surf.Faces.FindValues(bounds, IReadOnlyRTree.SearchMode.ExactMatch)
                    .Where(x => x != face1)];

                foreach(Face face2 in matching_faces)
                {
                    if (!surf.Faces.Contains(face1))
                    {
                        // gone in an earier merge
                        // no point running this inner loop at all now...
                        break;
                    }

                    if (!surf.Faces.Contains(face2))
                    {
                        // gone in an earier merge
                        continue;
                    }

                    PoorMansProfiler.Start("TargetCheck");
                    if (AreFaceMergeTargets(surf, face1, face2))
                    {
                        PoorMansProfiler.Start("MergeFacePair");
                        MergeFacePair(surf, face1, face2);
                        PoorMansProfiler.End("MergeFacePair");
                    }
                    PoorMansProfiler.End("TargetCheck");
                }
            }
            PoorMansProfiler.End("MergeFaces");
        }

        void MergeFacePair(Surface surf, Face face1, Face face2)
        {
            // new method
            // completely remove the two faces:
            // - remove them from surface
            // - remove them from their edges
            // - and their verts
            // merge the four pairs of edges:
            // - select one edge and its verts to be "leaving" and one to be "remaining"
            // - merge the edge's verts:
            // -- walk the faces of leaving vert, swapping it for the remaining vert
            // -- ditto for the edges of the leaving vert
            // -- union edges of leaving vert into remaining vert
            // -- ditto for its faces
            // -- remove leaving vert
            // - add the remaining face of the leaving edge to the remaining edge
            // - remove the leaving edge from the surface (should be no more references)
            // - remove its verts from the surface (should be no more references)

            surf.RemoveAndRemoveReferences(face1);
            surf.RemoveAndRemoveReferences(face2);

            // first we need to know which edges are equivalent
            List<(Edge edge1, Edge edge2)> paired_edges = PairEdges(face1.Edges, face2.Edges);

            foreach ((Edge leaving_edge, Edge remaining_edge) in paired_edges)
            {
                // can this be the same operation as a general edge merge?
                //
                // not sure, in that case we actually don't want to do a merge, but rather just redirect the
                // face-connections to the edges so that they flow past the original gap

                // when merging 4 cubes around a common edge, c:
                //
                // (end view on the edges under discussion)
                // +---+---+
                // | 1 | 2 |
                // |   |   |
                // +---c---+
                // | 4 | 3 |
                // |   |   |
                // +---+---+
                //
                // when we get to merge-in the final cube, the edge c is already common
                //
                // (e.g. if we started with four separate but coincident edges, c1, c2, c3, c4:
                //  c1 + c2 -> c2'      (lets assume we preserve the second edge in each merge)
                //  c2' + c3 -> c3'
                //  c3' + c4 -> c4'
                //
                //  and the final merge will be trying to merge c4' with itself...)
                //
                if (leaving_edge != remaining_edge)
                {
                    MergeEdgesWithFaceSpace(surf, leaving_edge, remaining_edge);
                }
                else
                {
                    // if the edge *is* alerady common, then it's not automatically being removed...
                    //
                    // however, in some cases, it's got no faces left on it (if you consider the diagram
                    // in the comment above, at the end, the remaining edge c4' is stranded crossing a gap that no faces bridge
                    // anymore)
                    //
                    // its verts are still in use (for the edges of the four surrounding quads)
                    //
                    // but sometimes (e.g. seen testing filled 2x2x2 volume of cubes with random forbidden merges) there are cases
                    // where it is
                    //
                    // but the edge will be out of use now (however if I have the following conditional
                    // that will hopefully cover any cases where it hasn't, if there are any...)
                    if (!leaving_edge.Faces.Any())
                    {
                        leaving_edge.Start.Edges.Remove(leaving_edge);
                        leaving_edge.End.Edges.Remove(leaving_edge);

                        surf.Edges.Remove(leaving_edge.Key);
                    }
                }
            }

            // because of the order we paired them up in, and the second edge argument of MergeEdgesWithFaceSpace
            // being the "remaining" edge, the verts of face2 are the ones potentially still in use and the verts of face1 the
            // discarded ones *HOWEVER* a vert of face2 may not now have any faces left (if face1/face2 were all it had)

            // if we need to split any verts, we'll modify the vert on face2,
            // so take a copy for iteration
            Vert[] remaining_verts = face2.Verts.Where(x => x.Faces.Any()).ToArray();
            foreach(Vert vert in remaining_verts)
            {
                // we messed with the edges are faces on these
                // verts, sort them back into the correct order...
                VertUtil.SortVertEdgesAndFaces(surf, vert, true);
            }

            // and the verts which are not still required, are the union of the verts from the two faces
            // *minus* the ones in remaining_verts
            //
            // (any new verts added by splitting in SortVertEdgesAndFaces are a) definitely in use and b) not in either
            // face, so they won't feature here anyway...)
            foreach(Vert vert in face1.Verts.Concat(face2.Verts).Where(x => !remaining_verts.Contains(x)).Distinct())
            {
                // check it is not still referenced by any edge/face
                Util.Assert(!surf.Edges.Values.SelectMany(x => x.Verts).Distinct().Contains(vert));
                Util.Assert(!surf.Faces.Values.SelectMany(x => x.Verts).Distinct().Contains(vert));

                surf.Verts.Remove(vert.Key);
            }

#if DEBUG
            foreach((Edge leaving_edge, Edge remaining_edge) in paired_edges)
            {
                if (leaving_edge != remaining_edge)
                {
                    // edge1 is the
                    Util.Assert(!surf.Edges.Contains(leaving_edge));
                    Util.Assert(!surf.Verts.Values.SelectMany(x => x.Edges).Distinct().Contains(leaving_edge));
                    Util.Assert(!surf.Faces.Values.SelectMany(x => x.Edges).Distinct().Contains(leaving_edge));
                }
            }

            Util.Assert(!surf.Faces.Values.Concat(surf.Verts.Values.SelectMany(x => x.Faces)).Distinct().Concat(surf.Edges.Values.SelectMany(x => x.Faces)).Distinct().Contains(face1));
            Util.Assert(!surf.Faces.Values.Concat(surf.Verts.Values.SelectMany(x => x.Faces)).Distinct().Concat(surf.Edges.Values.SelectMany(x => x.Faces)).Distinct().Contains(face1));
#endif

            // should be a good surface again now...
            surf.DebugValidate();
        }

        private void MergeEdgesWithFaceSpace(Surface surf, Edge leaving, Edge remaining)
        {
            // copied from big comment at the top of MergeFacePair
            // method:
            // - select one edge and its verts to be "leaving" and one to be "remaining"
            // - merge the edge's verts:
            // -- walk the faces of leaving vert, swapping it for the remaining vert
            // -- ditto for the edges of the leaving vert
            // -- union edges of leaving vert into remaining vert (dropping the leaving edge)
            // -- ditto for its faces
            // -- remove leaving vert
            // - add the remaining face of the leaving edge to the remaining edge
            // - completely remove the leaving edge (removing refernces to it)
            // -- if this makes any vert unused, remove that too
            // - fix the edge and face order for the two remaining verts

            Vert leaving_vert1 = leaving.Start;
            Vert leaving_vert2 = leaving.End;

            Vert remaining_vert1;
            Vert remaining_vert2;

            if (OrthoDist(leaving_vert1, remaining.Start) < OrthoDist(leaving_vert1, remaining.End))
            {
                remaining_vert1 = remaining.Start;
                remaining_vert2 = remaining.End;
            }
            else
            {
                remaining_vert1 = remaining.End;
                remaining_vert2 = remaining.Start;
            }

            VertUtil.SwapVertReferences(leaving_vert1, remaining_vert1, leaving_vert1);
            VertUtil.SwapVertReferences(leaving_vert2, remaining_vert2, leaving_vert2);

            remaining_vert1.Edges = [.. remaining_vert1.Edges.Concat(leaving_vert1.Edges).Distinct().Where(x => x != leaving)];
            remaining_vert1.Faces = [.. remaining_vert1.Faces.Concat(leaving_vert1.Faces).Distinct()];

            remaining_vert2.Edges = [.. remaining_vert2.Edges.Concat(leaving_vert2.Edges).Distinct().Where(x => x != leaving)];
            remaining_vert2.Faces = [.. remaining_vert2.Faces.Concat(leaving_vert2.Faces).Distinct()];

            Face moving_face = leaving.Forwards ?? leaving.Backwards;

            if (remaining.Forwards == null)
            {
                remaining.Forwards = moving_face;
            }
            else
            {
                remaining.Backwards = moving_face;
            }

            moving_face.Edges = [.. moving_face.Edges.Select(x => x == leaving ? remaining : x)];

            // we can clear the edges up now, but the verts may still be in use by another edge
            // we are about to remove, so better to do that after we get out of the loop calling this
            surf.Edges.Remove(leaving.Key);

            static float OrthoDist(Vert v1, Vert v2)
            {
                var d = (v1.Position - v2.Position).Abs();

                return MathfExtensions.Max(d.X, d.Y, d.Z);
            }
        }

        List<(Edge, Edge)> PairEdges(Edge[] edges1, Edge[] edges2)
        {
            // we should know for a fact that the two faces face opposite ways and thus rotate in opposite
            // directions... so let's find the closest pair of edge mid-points and rotate opposite ways from those

            int length = edges1.Length;

            Util.Assert(length == edges2.Length);

            (int f1_idx, int f2_idx) = FindClosestEdgePairIdxs(edges1, edges2);

            List<(Edge, Edge)> ret = [];

            for (int i = 0; i < length; i++)
            {
                int f1_pos = (f1_idx + i + length) % length;
                int f2_pos = (f2_idx - i + length) % length;

                ret.Add((edges1[f1_pos], edges2[f2_pos]));
            }

            return ret;
        }

        // candidate for being off in some static utility class
        static (int, int) FindClosestEdgePairIdxs(Edge[] edges1, Edge[] edges2)
        {
            int length = edges1.Length;

            Util.Assert(length == edges2.Length);

            int f1_idx = 0;
            int f2_idx = 0;
            Func<int, int, float> get_edges_d2 = (int f1_idx, int f2_idx) =>
            {
                return edges1[f1_idx].MidPoint.DistanceSquaredTo(edges2[f2_idx].MidPoint);
            };

            float best_d2 = get_edges_d2(f1_idx, f2_idx);

            bool changed;

            do
            {
                changed = false;

                int new_f1 = (f1_idx + 1) % length;

                float new_d2 = get_edges_d2(new_f1, f2_idx);

                if (new_d2 < best_d2)
                {
                    f1_idx = new_f1;
                    best_d2 = new_d2;

                    changed = true;
                }
                else
                {
                    new_f1 = (f1_idx + length - 1) % length;

                    new_d2 = get_edges_d2(new_f1, f2_idx);

                    if (new_d2 < best_d2)
                    {
                        f1_idx = new_f1;
                        best_d2 = new_d2;

                        changed = true;
                    }
                }

                int new_f2 = (f2_idx + 1) % length;

                new_d2 = get_edges_d2(f1_idx, new_f2);

                if (new_d2 < best_d2)
                {
                    f2_idx = new_f2;
                    best_d2 = new_d2;

                    changed = true;
                }
                else
                {
                    new_f2 = (f2_idx + length - 1) % length;

                    new_d2 = get_edges_d2(f1_idx, new_f2);

                    if (new_d2 < best_d2)
                    {
                        f2_idx = new_f2;
                        best_d2 = new_d2;

                        changed = true;
                    }
                }
            }
            while (changed);

            return (f1_idx, f2_idx);
        }

        bool AreFaceMergeTargets(Surface surf, Face face1, Face face2)
        {
            // we already got all the faces from the same MergeGroup, so no need to check that
            // (not that we can, we don't have the info...)

            // can't possibly be the same if they have different numbers of verts
            if (face1.Verts.Length != face2.Verts.Length)
            {
                return false;
            }

            if (IsMergeForbidden(face1, face2))
            {
                return false;
            }

            return FaceGeometryMergeable(surf, face1, face2);
        }

        bool FaceGeometryMergeable(Surface surf, Face face1, Face face2)
        {
            // we expect the normals to be exactly opposite, but allow a little wiggle room for
            // limited precision
            if (surf.FaceNormal(face1).Dot(surf.FaceNormal(face2)) > -0.99f)
            {
                return false;
            }

            List<Vert> verts2 = [.. face2.Verts];

            // likewise the vert positions
            foreach(Vert v1 in face1.Verts)
            {
                bool found = false;

                for(int i = 0; i < verts2.Count; i++)
                {
                    Vert v2 = verts2[i];

                    if (v1.Position.DistanceSquaredTo(v2.Position) < 0.0001f)
                    {
                        found = true;
                        verts2.Remove(v2);
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        bool IsMergeForbidden(IHasGeneratiorIdentities feature1, IHasGeneratiorIdentities feature2)
        {
            foreach (IGeneratorIdentity gi1 in feature1.GIs)
            {
                foreach (IGeneratorIdentity gi2 in feature2.GIs)
                {
                    // if both features have one GI and it is the same
                    // then they are from the same polyhedron and cannot need merging
                    // AND I *think* any features sharing a generator post-merge
                    // cannot need merging again
                    //
                    // but time will tell
                    //
                    // will need to store the "original generator" otherwise and do the following test
                    // only on that...
                    if (gi1 == gi2)
                    {
                        return true;
                    }

                    if (ForbiddenMerges.Contains((gi1, gi2)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        void MergeOne(Surface merge_target, AnnotatedPolyhedron to_merge)
        {
            // first we concat the new surface into the target in a "dumb" manner which doesn't do any sort of merging and is just
            // we still have the two surfaces separate
            merge_target.DumbConcat(to_merge.Polyhedron);

            // Now we detect the three types of possible merge: face-face, edge-edge
        }

        public virtual void Reset()
        {
            MergeStock = [];
            ForbiddenMerges = [];
        }
    }
}