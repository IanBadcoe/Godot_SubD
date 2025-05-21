using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System;

using Godot;

using Godot_Util;
using Godot_Util.CSharp_Util;
using Geom_Util.Interfaces;
using Geom_Util;

namespace SubD
{
    using EIdx = Idx<Edge>;
    using VIdx = Idx<Vert>;
    using FIdx = Idx<Face>;

    using DistortFunc = Func<Vector3, Vector3>;

    [DebuggerDisplay("Verts = Vert.Count, Edges = Edge.Count, Faces = Face.Count")]
    public partial class Surface : IBounded
    {
        class OutVert
        {
            public Vert Vert;
            public Vector3 Normal;
            public int OutIdx = -1;
        }

        // at the moment, we need Faces to have IsSpatialEnabled = true
        // we could optionally turn that on for Edges and Verts, if we needed that
        // it might make sense to track that more at the surface level, but I am not
        // sure, another possibility would be for high-level algorithms to just turn it
        // on (and maybe off) as required...
        public SpatialDictionary<FIdx, Face> Faces
        {
            get;
            private set;
        }

        public SpatialDictionary<EIdx, Edge> Edges
        {
            get;
            private set;
        }

        public SpatialDictionary<VIdx, Vert> Verts
        {
            get;
            private set;
        }

        // mesh output workings, clear after each ToSurface
        Dictionary<VIdx, Dictionary<FIdx, OutVert>> OutVerts = [];

        public int NextVIdx { get; private set; } = 0;
        public int NextEIdx { get; private set; } = 0;
        public int NextFIdx { get; private set; } = 0;

        public Vector3 FaceNormal(Face face)
        {
            if (face.Normal == null)
            {
                Vector3[] verts = [.. face.Verts.Select(x => x.Position)];

                face.Normal = FaceUtil.FaceNormal(verts);
            }

            return face.Normal.Value;
        }

        public Vector3 EdgeNormal(EIdx e_idx)
        {
            Edge edge = Edges[e_idx];

            if (edge.Normal == null)
            {
                edge.Normal = edge.Faces.Select(x => FaceNormal(x)).Sum().Normalized();
            }

            return edge.Normal.Value;
        }

        public Vector3 VertNormal(Vert vert)
        {
            if (vert.Normal == null)
            {
                vert.Normal = vert.Faces.Select(x => FaceNormal(x)).Sum().Normalized();
            }

            return vert.Normal.Value;
        }

        public float FaceNormalsDotProduct(EIdx e_idx) => FaceNormalsDotProduct(Edges[e_idx]);

        public float FaceNormalsDotProduct(Edge edge)
        {
            return FaceNormal(edge.Backwards).Dot(FaceNormal(edge.Forwards));
        }

        public Surface(
            SpatialDictionary<VIdx, Vert> verts,
            SpatialDictionary<EIdx, Edge> edges,
            SpatialDictionary<FIdx, Face> faces)
        {
            Verts = verts;
            Edges = edges;
            Faces = faces;

            NextVIdx = Verts.Keys.Max().Value + 1;
            NextEIdx = Edges.Keys.Max().Value + 1;
            NextFIdx = Faces.Keys.Max().Value + 1;

            // DebugValidate();
        }

        public Surface(Surface old)
        {
            DumbConcat(old);
        }

        [Conditional("DEBUG")]
        public void DebugValidate()
        {
            foreach (Vert vert in Verts.Values)
            {
                // we can transiently have these during operations on surfaces, but we expect people to have tidied up before now
                Util.Assert(vert.Edges.Count > 0);
                Util.Assert(vert.Faces.Count > 0);

                // all verts which reference an edge should be referenced by the edge
                foreach (Edge edge in vert.Edges)
                {
                    Util.Assert(edge.Verts.Contains(vert));
                }

                // all verts which reference a face should be referenced by the face
                foreach (Face face in vert.Faces)
                {
                    Util.Assert(face.Verts.Contains(vert));
                }

                // face N should lie between edges N and N + 1
                for (int i = 0; i < vert.Faces.Count; i++)
                {
                    Util.Assert(vert.Faces[i].Edges.Contains(vert.Edges[i]));
                    Util.Assert(vert.Faces[i].Edges.Contains(vert.Edges[(i + 1) % vert.Edges.Count]));
                }
            }

            foreach (Edge edge in Edges.Values)
            {
                Util.Assert(edge.Start != null);
                Util.Assert(edge.End != null);
                Util.Assert(edge.Backwards != null);
                Util.Assert(edge.Forwards != null);

                // all edges which reference a vert should be referenced by the vert
                foreach (Vert vert in edge.Verts)
                {
                    Util.Assert(vert.Edges.Contains(edge));
                }

                // all edges which reference a face should be referenced by the face
                foreach (Face face in edge.Faces)
                {
                    Util.Assert(face.Edges.Contains(edge));
                }
            }

            foreach (var face in Faces.Values)
            {
                // all faces which reference a vert should be referenced by the vert
                foreach (Vert vert in face.Verts)
                {
                    Util.Assert(vert.Faces.Contains(face));
                }

                // all faces which reference an edge should be referenced by the edge
                foreach (Edge edge in face.Edges)
                {
                    Util.Assert(edge.Faces.Contains(face));
                }

                Vert[] face_verts = [.. face.Verts];
                Edge[] face_edges = [.. face.Edges];

                Util.Assert(face_verts.Length > 0);
                Util.Assert(face_verts.Length == face_edges.Length);

                // edge N should lie between Verts N and N + 1
                for (int i = 0; i < face_edges.Length; i++)
                {
                    int next_i = (i + 1) % face_edges.Length;

                    Vert v1 = face_verts[i];
                    Vert v2 = face_verts[next_i];
                    Edge edge = face_edges[i];

                    Util.Assert(edge.Verts.Contains(v1));
                    Util.Assert(edge.Verts.Contains(v2));
                }
            }
        }

        public enum MeshMode
        {
            Surface,
            Edges,
            Normals
        }

        public Mesh ToMesh(MeshMode mesh_mode, MeshOptions? mesh_options = null)
        {
            MeshOptions options = mesh_options.HasValue ? mesh_options.Value : new MeshOptions();

            bool use_angle = options.SplitAngleDegrees.HasValue;
            if (use_angle)
            {
                float angle = options.SplitAngleDegrees.Value;
                float cos_angle = Mathf.Cos(angle * Mathf.Pi / 180);

                foreach(var pair in Edges)
                {
                    EIdx e_idx = pair.Key;
                    Edge edge = pair.Value;

                    // edge normal dot-product is 1 for parallel edges, 0 for perpendicular
                    // cos of the angle we want follows a similar pattern, and a lower cos is sharper
                    // edge_angle could go -ve for >90 degrees
                    float edge_angle = FaceNormalsDotProduct(e_idx);
                    edge.IsObservedSharp = edge_angle <= cos_angle;
                }
            }

            foreach(var v_pair in Verts.Where(x => x.Value.Edges.Any()))
            {
                VIdx v_idx = v_pair.Key;
                Vert vert = v_pair.Value;

                OutVerts[vert.Key] = [];

                bool force_all_separate_verts = vert.IsSharp;

                Edge[] vert_edges = [.. vert.Edges];
                Face[] vert_faces = [.. vert.Faces];

                Edge first_edge = vert.Edges.FirstOrDefault(x => force_all_separate_verts || x.IsSetSharp);

                if(first_edge == null)
                {
                    first_edge = vert.Edges.First();
                }

                int fei_idx = Array.IndexOf(vert_edges, first_edge);

                if(fei_idx != 0)
                {
                    vert_edges = [.. vert_edges.Skip(fei_idx), .. vert_edges.Take(fei_idx)];
                    vert_faces = [.. vert_faces.Skip(fei_idx), .. vert_faces.Take(fei_idx)];
                }

                // we should now have the edge and face-indexes cyclically permuted, such that if there is any sharp edge it is first
                // (if there are >1 no problem)

                OutVert current = null;
                int num_faces = 0;

                for(int i = 0; i < vert_edges.Length; i++)
                {
                    Face face = vert_faces[i];
                    Edge edge = vert_edges[i];

                    bool is_sharp = use_angle ? edge.IsObservedSharp : edge.IsSetSharp;

                    if (is_sharp || current == null /* || force_all_separate_verts */)
                    {
                        if (current != null)
                        {
                            current.Normal = current.Normal.Normalized();
                            num_faces = 0;
                        }

                        current = new()
                        {
                            Vert = Verts[v_idx]
                        };
                    }

                    OutVerts[vert.Key][face.Key] = current;
                    current.Normal += FaceNormal(face);

                    num_faces++;
                }

                if (num_faces > 0)
                {
                    current.Normal = current.Normal.Normalized();
                }
            }

            Mesh ret = null;

            switch (mesh_mode)
            {
                case MeshMode.Surface:
                    ret = OutputMeshSurface(options);
                    break;

                case MeshMode.Edges:
                    ret = OutputMeshLines(options);
                    break;

                case MeshMode.Normals:
                    ret = OutputMeshNormals(options);
                    break;
            }

            // could keep this, keep a "clean" flag and only wipe/regenerate on next entry if not clean
            // but that seems like a lot of data to hang onto on the off-chance...
            OutVerts = [];

            return ret;
        }

        Mesh OutputMeshNormals(MeshOptions options)
        {
            List<Vector3> verts = [];
            List<Vector3> normals = [];
            List<int> idxs = [];

            bool include_face = options.Normals_IncludeFace;
            bool include_edge = options.Normals_IncludeEdge;
            bool include_vert = options.Normals_IncludeVert;
            bool include_vert_split = options.Normals_IncludeSplitVert;

            if (include_edge)
            {
                foreach(var pair in Edges)
                {
                    Vector3 edge_centre = pair.Value.MidPoint;
                    Vector3 normal = EdgeNormal(pair.Key);
                    idxs.Add(verts.Count);
                    verts.Add(edge_centre);
                    normals.Add(normal);
                    idxs.Add(verts.Count);
                    verts.Add(edge_centre + normal * options.DrawNormalsLength);
                    normals.Add(normal);
                }
            }

            if (include_vert)
            {
                foreach(Vert vert in Verts.Values)
                {
                    Vector3 normal = VertNormal(vert);

                    idxs.Add(verts.Count);
                    verts.Add(vert.Position);
                    normals.Add(normal);
                    idxs.Add(verts.Count);
                    verts.Add(vert.Position + normal * options.DrawNormalsLength);
                    normals.Add(normal);
                }
            }

            if (include_vert_split)
            {
                foreach(OutVert out_vert in OutVerts.Values.SelectMany(x => x.Values))
                {
                    idxs.Add(verts.Count);
                    verts.Add(out_vert.Vert.Position);
                    normals.Add(out_vert.Normal);
                    idxs.Add(verts.Count);
                    verts.Add(out_vert.Vert.Position + out_vert.Normal * options.DrawNormalsLength);
                    normals.Add(out_vert.Normal);
                }
            }

            if (include_face)
            {
                foreach(var pair in Faces)
                {
                    Vector3 centre = pair.Value.Centre;
                    Vector3 normal = FaceNormal(pair.Value);

                    idxs.Add(verts.Count);
                    verts.Add(centre);
                    normals.Add(normal);
                    idxs.Add(verts.Count);
                    verts.Add(centre + normal * options.DrawNormalsLength);
                    normals.Add(normal);
                }
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = idxs.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();

            ArrayMesh mesh = new();

            // there can be no indices, if we picked too restrictive options, and AddSurfaceFromArrays doesn't like that
            if (idxs.Any())
            {
                // Create the Mesh.
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            }

            return mesh;
        }

        Mesh OutputMeshLines(MeshOptions options)
        {
            List<Vector3> verts = [];
            List<Vector3> normals = [];
            List<int> idxs = [];

            bool include_sharp = options.Edges_IncludeSharp;
            bool include_smooth = options.Edges_IncludeSmooth;
            bool use_angle = options.Edges_DetermineSmoothnessFromAngle;
            bool use_filter = options.Edges_Filter != null;
            float vert_offset = options.Edges_Offset;

            foreach(var pair in Faces)
            {
                FIdx f_idx = pair.Key;
                Face face = pair.Value;

                foreach(Vert vert in face.Verts)
                {
                    OutVert out_vert = OutVerts[vert.Key][face.Key];
                    if (out_vert.OutIdx == -1)
                    {
                        out_vert.OutIdx = verts.Count;
                        Vector3 position = out_vert.Vert.Position;

                        if (vert_offset != 0)
                        {
                            position += VertNormal(vert) * vert_offset;
                        }
                        verts.Add(position);
                        normals.Add(out_vert.Normal);
                    }
                }
            }

            foreach(var p_pair in Faces)
            {
                Face face = p_pair.Value;

                // we will output each line twice, once for each adjoiing face
                // but those faces have different normals, so *not* doing this loses info
                // OTOH even doing it, one will be invisible or there will be Z-fighgint
                //
                // a more-correct approach might be to shift the edges 1 pixel *in* to their face
                // so both can show, but that would require shader magic, I suspect...
                foreach(Edge edge in face.Edges)
                {
                    if (use_filter)
                    {
                        if (!options.Edges_Filter(edge))
                        {
                            continue;
                        }
                    }
                    else if (!include_sharp || !include_smooth) //< otherwise we are including everything and can skip this complexity
                    {
                        bool is_sharp;

                        if (use_angle)
                        {
                            is_sharp = edge.IsObservedSharp;
                        }
                        else
                        {
                            is_sharp = edge.IsSetSharp;
                        }

                        if (is_sharp && !include_sharp)
                        {
                            continue;
                        }

                        if (!is_sharp && !include_smooth)
                        {
                            continue;
                        }
                    }

                    idxs.Add(OutVerts[edge.Start.Key][face.Key].OutIdx);
                    idxs.Add(OutVerts[edge.End.Key][face.Key].OutIdx);
                }
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = idxs.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();

            ArrayMesh mesh = new();

            // there can be no indices, if we picked too restrictive options, and AddSurfaceFromArrays doesn't like that
            if (idxs.Any())
            {
                // Create the Mesh.
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            }

            return mesh;
        }

        Mesh OutputMeshSurface(MeshOptions options)
        {
            List<Vector3> verts = [];
            List<Vector3> normals = [];
            List<int> idxs = [];
            bool use_filter = options.Faces_filter != null;

            foreach(var pair in Faces)
            {
                Face face = pair.Value;

                if (use_filter && !options.Faces_filter(face))
                {
                    continue;
                }

                foreach(Vert vert in face.Verts)
                {
                    OutVert out_vert = OutVerts[vert.Key][face.Key];
                    if (out_vert.OutIdx == -1)
                    {
                        out_vert.OutIdx = verts.Count;
                        verts.Add(out_vert.Vert.Position);
                        normals.Add(out_vert.Normal);
                    }
                }
            }

            // split our faces apart into individual triangles
            foreach(var pair in Faces)
            {
                Face face = pair.Value;

                if (use_filter && !options.Faces_filter(face))
                {
                    continue;
                }

                int vert_0_idx = OutVerts[face.Verts[0].Key][face.Key].OutIdx;

                // build the face from a fan of trianges around vert-0
                for(int p = 1; p < face.Verts.Length - 1; p++)
                {
                    idxs.Add(vert_0_idx);
                    idxs.Add(OutVerts[face.Verts[p].Key][face.Key].OutIdx);
                    idxs.Add(OutVerts[face.Verts[p + 1].Key][face.Key].OutIdx);
                }
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = idxs.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();

            ArrayMesh mesh = new();

            // Create the Mesh.
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            return mesh;
        }

        public Surface Distorted(DistortFunc distortion)
        {
            Surface ret = new(this);

            ret.Distort(distortion);

            return ret;
        }

        public void Distort(DistortFunc distortion)
        {
            foreach(Vert vert in Verts.Values)
            {
                vert.Position = distortion(vert.Position);

                // this will invalidate the normal
                vert.Normal = null;
            }

            foreach(Edge edge in Edges.Values)
            {
                edge.Normal = null;
            }

            foreach(Face face in Faces.Values)
            {
                face.Normal = null;
            }
        }

        public VIdx AddVert(Vert vert)
        {
            VIdx v_idx = new(NextVIdx++);
            Verts[v_idx] = vert;

            return v_idx;
        }

        public EIdx AddEdge(Edge edge)
        {
            EIdx e_idx = new(NextEIdx++);
            Edges[e_idx] = edge;

            return e_idx;
        }

        public FIdx AddFace(Face face)
        {
            FIdx f_idx = new(NextFIdx++);
            Faces[f_idx] = face;

            return f_idx;
        }

        public void DumbConcat(Surface surf)
        {
            Dictionary<VIdx, Vert> v_idx_remaps = [];
            Dictionary<EIdx, Edge> e_idx_remaps = [];
            Dictionary<FIdx, Face> f_idx_remaps = [];

            List<Vert> verts_added = [];
            List<Edge> edges_added = [];
            List<Face> faces_added = [];

            // initial phase: the new features will have wrong internal references pointing into surf instead
            // of our own, but we'll fix those up in a second phase...
            foreach(Vert old_vert in surf.Verts.Values)
            {
                Vert vert = new(old_vert);
                VIdx v_idx = AddVert(vert);

                verts_added.Add(vert);
                v_idx_remaps[old_vert.Key] = vert;
            }

            foreach(Edge old_edge in surf.Edges.Values)
            {
                Edge edge = new(old_edge);
                EIdx e_idx = AddEdge(edge);

                edges_added.Add(edge);
                e_idx_remaps[old_edge.Key] = edge;
            }

            foreach(Face old_face in surf.Faces.Values)
            {
                Face face = new(old_face);
                FIdx f_idx = AddFace(face);

                faces_added.Add(face);
                f_idx_remaps[old_face.Key] = face;
            }

            // second phase: now fixup the internal references to point to the other new stuff we just added
            foreach(Vert vert in verts_added)
            {
                vert.Edges = [.. vert.Edges.Select(x => e_idx_remaps[x.Key])];
                vert.Faces = [.. vert.Faces.Select(x => f_idx_remaps[x.Key])];
            }

            foreach(Edge edge in edges_added)
            {
                edge.Start = v_idx_remaps[edge.Start.Key];
                edge.End = v_idx_remaps[edge.End.Key];

                if (edge.Backwards != null)
                {
                    edge.Backwards = f_idx_remaps[edge.Backwards.Key];
                }

                if (edge.Forwards != null)
                {
                    edge.Forwards = f_idx_remaps[edge.Forwards.Key];
                }
            }

            foreach(Face face in faces_added)
            {
                face.Verts = [.. face.Verts.Select(x => v_idx_remaps[x.Key])];
                face.Edges = [.. face.Edges.Select(x => e_idx_remaps[x.Key])];
            }

            // DebugValidate();
        }

        public void RemoveAndRemoveReferences(Face face)
        {
            // remove the face first, so it doesn't get updated by any chained CompletelyRemove(edge)
            // below, and hence can continue to be used as an archive of what the edge/verts were
            Faces.Remove(face);

            foreach (Edge edge in face.Edges)
            {
                edge.RemoveFace(face);
            }

            foreach (Vert vert in face.Verts)
            {
                vert.Faces = [.. vert.Faces.Where(x => x != face)];
            }
        }

        public void RemoveAndRemoveReferences(Edge edge)
        {
            Edges.Remove(edge);

            foreach(Face face in edge.Faces)
            {
                face.Edges = [.. face.Edges.Where(x => x != edge)];
            }

            foreach(Vert vert in edge.Verts)
            {
                vert.Edges.Remove(edge);
            }
        }

        public void RemoveAndRemoveReferences(Vert vert)
        {
            Verts.Remove(vert);

            foreach(Face face in vert.Faces)
            {
                face.Verts = [.. face.Verts.Where(x => x != vert)];
            }

            foreach(Edge edge in vert.Edges)
            {
                edge.RemoveVert(vert);
            }
        }

        public ImBounds GetBounds()
        {
            return Faces.Values.Aggregate(new ImBounds(), (x, y) => x.UnionedWith(y.GetBounds()));
        }
    }
}