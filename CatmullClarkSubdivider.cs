using System;
using System.Linq;
using System.Collections.Generic;

using Godot;

using Godot_Util;

using Geom_Util;

namespace SubD;

using VIdx = Idx<Vert>;
using EIdx = Idx<Edge>;
using FIdx = Idx<Face>;

struct AnnotatedVert
{
    public Vert Vert;
    public Edge OriginalEdge;       //< this is the edge which (in terms of the face we are splitting) followed VIdx
                                    //< (the edge may have been used forwards or backwards, so this doesn't mean VIdx == OriginalEdge.Start)

    public AnnotatedVert(Vert vert, Edge original_edge)
    {
        Vert = vert;
        OriginalEdge = original_edge;
    }
}

public class CatmullClarkSubdivider : Interfaces.ISubdivider
{
    int NextVIdx;
    int NextEIdx;
    int NextFIdx;

    SpatialDictionary<VIdx, Vert> NewVerts;
    SpatialDictionary<EIdx, Edge> NewEdges;
    SpatialDictionary<FIdx, Face> NewFaces;

    public Surface Subdivide(Surface input)
    {
        if (input.Verts.Count == 0)
        {
            return null;
        }

        NextVIdx = input.NextVIdx;  // retain all existing idxs and build new ones beyond that range
        NextEIdx = 0;               // no edges are carried over
        NextFIdx = 0;               // no faces are carried over

        // preserve the VIds of existing verts
        // all cloned verts are unfrozen and do not have cached Normals
        NewVerts = CloneVerts(input.Verts, SpatialStatus.Disabled);         // \
        NewEdges = [];                                                      //  > we don't need "IsSpatialEnabled", we can let whoever does need it turn it on
        NewFaces = [];                                                      // /

        Dictionary<Face, Vert> face_centre_map = [];

        // inject face centre verts
        foreach (Face face in input.Faces.Values)
        {
            VIdx v_idx = new(NextVIdx++);
            Vert vert = new(face.Centre);
            NewVerts[v_idx] = vert;

            face_centre_map[face] = vert;
        }

        Dictionary<Edge, Vert> edge_centre_map = [];

        // inject edge centre verts
        foreach (var edge in input.Edges.Values)
        {
            VIdx v_idx = new(NextVIdx++);
            Vert vert = null;

            if (edge.IsSetSharp)
            {
                // sharp edges just interpolate their original position
                vert = new Vert(
                    (
                          edge.Start.Position
                        + edge.End.Position
                    ) / 2);
            }
            else
            {
                // smooth edges interpolate the original end positions
                // AND the new face-centres
                vert = new Vert(
                    (
                           edge.Start.Position
                        + edge.End.Position
                        + face_centre_map[edge.Backwards].Position
                        + face_centre_map[edge.Forwards].Position
                    ) / 4);
            }

            NewVerts[v_idx] = vert;

            edge_centre_map[edge] = vert;
        }

        // move pre-existing verts
        foreach (var pair in input.Verts)
        {
            Vert input_vert = pair.Value;
            VIdx input_v_idx = pair.Key;

            int n_sharp_edges;

            // if the vert is tagged sharp then it is sharp irrespective of the edge settings
            // otherwise we follow:
            // n < 2 : smooth rule
            // n == 2 : crease rule
            // n > 2 : sharp rule
            n_sharp_edges = input_vert.Edges.Count(x => x.IsSetSharp);

            if (input_vert.IsSharp || n_sharp_edges > 2)
            {
                //   we use the sharp rule, which means we do not move
            }
            else if (n_sharp_edges < 2)
            {
                // smooth rule

                // num EIdxs == num FIdxs...
                int n = input_vert.Edges.Count;

                Vector3 face_points_avg
                    = input_vert.Faces
                        .Select(x => face_centre_map[x].Position)
                        .Sum() / n;

                Vector3 edge_mid_points_avg
                     = input_vert.Edges
                        .Select(x => x.MidPoint)
                        .Sum() / n;

                Vector3 new_pos = (face_points_avg + 2 * edge_mid_points_avg + (n - 3) * input_vert.Position) / n;

                // new vert is unfrozen, VIDxs were preserved from original ones to NewVerts
                NewVerts[input_v_idx].Position = new_pos;
            }
            else // (n_sharp_edges == 2)
            {
                // crease rule

                // the other ends of the two original crease vectors...
                Vector3 sum_crease_edges_other_ends
                    = input_vert.Edges
                        .Where(x => x.IsSetSharp)
                        .Select(x => x.OtherVert(input_vert).Position)
                        .Sum();

                Vector3 new_pos = input_vert.Position * 0.75f
                                + sum_crease_edges_other_ends * 0.125f;

                // new vert is unfrozen, VIDxs were preserved from original ones to NewVerts
                NewVerts[input_v_idx].Position = new_pos;
            }
        }

        Dictionary<(Vert, Vert), Edge> made_edges = [];

        foreach (var p_pair in input.Faces)
        {
            Face face = p_pair.Value;
            Edge prev_edge = face.Edges.Last();

            foreach (Edge edge in face.Edges)
            {
                // if we used this edge backwards, then we need to start at the End
                // otherwise the Start
                VIdx start_v_idx = edge.Forwards == face ? edge.Start.Key : edge.End.Key;
                Vert start = NewVerts[start_v_idx];

                AddFace(
                    [
                        new AnnotatedVert(start, edge),
                        new AnnotatedVert(edge_centre_map[edge], null),
                        new AnnotatedVert(face_centre_map[face], null),
                        new AnnotatedVert(edge_centre_map[prev_edge], prev_edge)
                    ],
                    face,
                    made_edges
                );

                prev_edge = edge;
            }
        }

        foreach (Vert vert in NewVerts.Values)
        {
            // we added the edges and faces to the verts in a fairly arbitraty order, but we need
            // them to both be clockwise, from outside the cube, looking inwards, and...
            //
            // we need the two edges of the face at position N to be N and N + 1

            // argument "surf" not needed if we aren't allowing splitting
            VertUtil.SortVertEdgesAndFaces(null, vert, false);
        }

        Surface ret = new(NewVerts, NewEdges, NewFaces);

        Reset();

        return ret;
    }

    void Reset()
    {
        NextEIdx = NextFIdx = NextVIdx = 0;

        NewVerts = null;
        NewEdges = null;
        NewFaces = null;
    }

    void AddFace(AnnotatedVert[] v_idxs, Face orig_face, Dictionary<(Vert, Vert), Edge> made_edges)
    {
        List<Edge> edges = [];
        List<Edge> backwards_edges = [];
        List<Edge> forwards_edges = [];

        for (int i = 0; i < v_idxs.Length; i++)
        {
            AnnotatedVert av = v_idxs[i];
            int next_i = (i + 1) % v_idxs.Length;
            AnnotatedVert av_next = v_idxs[next_i];

            bool is_backwards;
            Edge edge = AddEdge(av, av_next, made_edges, out is_backwards);

            edges.Add(edge);

            (is_backwards ? backwards_edges : forwards_edges).Add(edge);
        }

        Face face = new(v_idxs.Select(x => x.Vert), edges, orig_face.GIs);
        FIdx f_idx = new(NextFIdx++);
        NewFaces[f_idx] = face;
        face.SetMetadataFrom(orig_face);

        // it's a new face, so let all the verts know
        foreach (Vert vert in face.Verts)
        {
            vert.Faces.Add(face);
        }

        foreach (Edge edge in backwards_edges)
        {
            edge.Backwards = face;
        }

        foreach (Edge edge in forwards_edges)
        {
            edge.Forwards = face;
        }
    }

    Edge AddEdge(AnnotatedVert start, AnnotatedVert end, Dictionary<(Vert, Vert), Edge> made_edges, out bool is_backwards)
    {
        if (made_edges.TryGetValue((end.Vert, start.Vert), out Edge edge))
        {
            is_backwards = true;

            return edge;
        }

        is_backwards = false;

        EIdx e_idx = new(NextEIdx++);

        edge = new(start.Vert, end.Vert);

        NewEdges[e_idx] = edge;

        made_edges[(start.Vert, end.Vert)] = edge;

        start.Vert.Edges.Add(edge);
        end.Vert.Edges.Add(edge);

        // if there was an original edge, propogate metadata
        if (start.OriginalEdge != null)
        {
            edge.SetMetaDataFrom(start.OriginalEdge);
        }

        return edge;
    }

    SpatialDictionary<VIdx, Vert> CloneVerts(SpatialDictionary<VIdx, Vert> verts, SpatialStatus spatial_status)
    {
        SpatialDictionary<VIdx, Vert> ret = new(spatial_status);

        foreach (var pair in verts)
        {
            ret[pair.Key] = pair.Value.Clone(true);
        }

        return ret;
    }
}