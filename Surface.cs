using System.Collections.Generic;
using System.Linq;

using Godot;
using System.Diagnostics;
using System;

namespace SubD
{
    using EIdx = Idx<Edge>;
    using VIdx = Idx<Vert>;
    using PIdx = Idx<Poly>;

    using DistortFunc = Func<Vector3, Vector3>;

    [DebuggerDisplay("Verts = Vert.Count, Edges = Edge.Count, Faces = Face.Count")]
    public partial class Surface
    {
        public class OutVert
        {
            public Vert Vert;
            public Vector3 Normal;
            public int OutIdx = -1;
        }

        public Dictionary<PIdx, Poly> Polys
        {
            get;
            private set;
        }

        public BidirectionalDictionary<EIdx, Edge> Edges
        {
            get;
            private set;
        }

        public BidirectionalDictionary<VIdx, Vert> Verts
        {
            get;
            private set;
        }

        // mesh output workings, clear after each ToSurface
        Dictionary<(PIdx, VIdx), OutVert> OutVerts = [];

        public IEnumerable<VIdx> PolyVIdxs(PIdx p_idx) => Polys[p_idx].VIdxs;

        public IEnumerable<Vert> PolyVerts(PIdx p_idx) => PolyVIdxs(p_idx).Select(x => Verts[x]);

        public IEnumerable<EIdx> PolyEIdxs(PIdx p_idx) => Polys[p_idx].EIdxs;

        public IEnumerable<Edge> PolyEdges(PIdx p_idx) => PolyEIdxs(p_idx).Select(x => Edges[x]);

        public IEnumerable<VIdx> EdgeVIdxs(EIdx e_idx) => Edges[e_idx].VIdxs;

        public IEnumerable<Vert> EdgeVerts(EIdx e_idx) => EdgeVIdxs(e_idx).Select(x => Verts[x]);

        public IEnumerable<PIdx> EdgePIdxs(EIdx e_idx) => Edges[e_idx].PIdxs;

        public IEnumerable<Poly> EdgePolys(EIdx e_idx) => EdgePIdxs(e_idx).Select(x => Polys[x]);

        public IEnumerable<EIdx> VertEIdxs(VIdx v_idx) => Verts[v_idx].EIdxs;

        public IEnumerable<Edge> VertEdges(VIdx v_idx) => VertEIdxs(v_idx).Select(x => Edges[x]);

        public IEnumerable<PIdx> VertPIdxs(VIdx v_idx) => Verts[v_idx].PIdxs;

        public IEnumerable<Poly> VertPolys(VIdx v_idx) => VertPIdxs(v_idx).Select(x => Polys[x]);

        public Vector3 EdgeMidpoint(EIdx e_idx) => EdgeVerts(e_idx).Select(x => x.Position).Sum() / 2;

        public VIdx? GetVIdx(Vector3 pos)
        {
            Vert temp = new(pos);
            if (!Verts.Contains(temp))
            {
                return null;
            }

            // translate our correct-position-wrong-reference vert into a VIdx (by value)
            return Verts[temp];
        }

        public Vert GetVert(Vector3 pos)
        {
            VIdx? v_idx = GetVIdx(pos);

            return v_idx.HasValue ? Verts[v_idx.Value] : null;
        }

        public EIdx? GetEIdx(VIdx v1, VIdx v2)
        {
            Edge temp = new(v1, v2);

            if (Edges.Contains(temp))
            {
                // translate our correct-VIdxs-wrong-reference edge into an EIdx (by value)
                return Edges[temp];
            }

            Edge r_temp = temp.Reversed();

            if (Edges.Contains(r_temp))
            {
                // translate our correct-VIdxs-wrong-reference edge into an EIdx (by value)
                return Edges[r_temp];
            }

            return null;
        }

        public EIdx? GetEIdx(Vector3 p1, Vector3 p2)
        {
            VIdx? v1_idx = GetVIdx(p1);
            VIdx? v2_idx = GetVIdx(p2);

            if (v1_idx == null || v2_idx == null)
            {
                return null;
            }

            return GetEIdx(v1_idx.Value, v2_idx.Value);
        }

        public Edge GetEdge(Vector3 p1, Vector3 p2)
        {
            EIdx? e_idx = GetEIdx(p1, p2);

            return e_idx.HasValue ? Edges[e_idx.Value] : null;
        }

        public Edge GetEdge(VIdx v1, VIdx v2)
        {
            EIdx? e_idx = GetEIdx(v1, v2);

            return e_idx.HasValue ? Edges[e_idx.Value] : null;
        }

        public Vector3 PolyNormal(PIdx p_idx)
        {
            Poly poly = Polys[p_idx];

            if (poly.Normal == null)
            {
                Vector3[] verts = [.. PolyVerts(p_idx).Select(x => x.Position)];

                poly.Normal = PolyUtil.PolyNormal(verts);
            }

            return poly.Normal.Value;
        }

        public Vector3 EdgeNormal(EIdx e_idx)
        {
            Edge edge = Edges[e_idx];

            if (edge.Normal == null)
            {
                edge.Normal = edge.PIdxs.Select(x => PolyNormal(x)).Sum().Normalized();
            }

            return edge.Normal.Value;
        }

        public Vector3 VertNormal(VIdx v_idx)
        {
            Vert vert = Verts[v_idx];

            if (vert.Normal == null)
            {
                vert.Normal = vert.PIdxs.Select(x => PolyNormal(x)).Sum().Normalized();
            }

            return vert.Normal.Value;
        }

        public float FaceNormalsDotProduct(EIdx e_idx) => FaceNormalsDotProduct(Edges[e_idx]);

        public float FaceNormalsDotProduct(Edge edge)
        {
            return PolyNormal(edge.Left.Value).Dot(PolyNormal(edge.Right.Value));
        }

        public Vector3 PolyCentre(PIdx p_idx)
        {
            return PolyVerts(p_idx).Select(x => x.Position).Sum() / Polys[p_idx].VIdxs.Length;
        }

        public Surface(
            Dictionary<VIdx, Vert> verts,
            BidirectionalDictionary<EIdx, Edge> edges,
            Dictionary<PIdx, Poly> polys)
            : this(new BidirectionalDictionary<VIdx, Vert>(verts), edges, polys)
        {}

        public Surface(
            BidirectionalDictionary<VIdx, Vert> verts,
            BidirectionalDictionary<EIdx, Edge> edges,
            Dictionary<PIdx, Poly> polys)
        {
            Verts = verts;
            Edges = edges;
            Polys = polys;

            // allow no more (geometry) changes
            foreach(Vert vert in Verts.Values)
            {
                vert.Freeze();
            }

            foreach(Edge edge in edges.Values)
            {
                edge.Freeze();
            }

            // poly is immutable anyway

            // debug-only topology validation
#if DEBUG
            foreach(var pair in Verts)
            {
                // all verts which reference an edge should be referenced by the edge
                foreach(Edge edge in pair.Value.EIdxs.Select(x => Edges[x]))
                {
                    Util.Assert(edge.VIdxs.Contains(pair.Key));
                }

                // all verts which reference a poly should be referenced by the poly
                foreach(Poly poly in pair.Value.PIdxs.Select(x => Polys[x]))
                {
                    Util.Assert(poly.VIdxs.Contains(pair.Key));
                }

                PIdx[] vert_p_idxs = [.. VertPIdxs(pair.Key)];
                Poly[] vert_polys = [.. vert_p_idxs.Select(x => Polys[x])];
                EIdx[] vert_eidxs = [.. VertEIdxs(pair.Key)];

                // poly N should lie between edges N and N + 1
                for(int i = 0; i < vert_polys.Length; i++)
                {
                    Util.Assert(vert_polys[i].EIdxs.Contains(vert_eidxs[i]));
                    Util.Assert(vert_polys[i].EIdxs.Contains(vert_eidxs[(i + 1) % vert_eidxs.Length]));
                }
            }

            foreach(var pair in Edges)
            {
                Util.Assert(pair.Value.Left.HasValue);
                Util.Assert(pair.Value.Right.HasValue);

                // all edges which reference a vert should be referenced by the vert
                foreach(Vert vert in pair.Value.VIdxs.Select(x => Verts[x]))
                {
                    Util.Assert(vert.EIdxs.Contains(pair.Key));
                }

                // all edges which reference a poly should be referenced by the poly
                foreach(Poly poly in pair.Value.PIdxs.Select(x => Polys[x]))
                {
                    Util.Assert(poly.EIdxs.Contains(pair.Key));
                }
            }

            foreach(var pair in Polys)
            {
                // all polys which reference a vert should be referenced by the vert
                foreach(Vert vert in pair.Value.VIdxs.Select(x => Verts[x]))
                {
                    Util.Assert(vert.PIdxs.Contains(pair.Key));
                }

                // all polys which reference an edge should be referenced by the edge
                foreach(Edge edge in pair.Value.EIdxs.Select(x => Edges[x]))
                {
                    Util.Assert(edge.PIdxs.Contains(pair.Key));
                }

                VIdx[] poly_v_idxs = [.. pair.Value.VIdxs];
                Edge[] poly_edges = [.. pair.Value.EIdxs.Select(x => Edges[x])];

                Util.Assert(poly_v_idxs.Length == poly_edges.Length);

                // edge N should lie between Verts N and N + 1
                for(int i = 0; i < poly_edges.Length; i++)
                {
                    int next_i = (i + 1) % poly_edges.Length;

                    VIdx v1 = poly_v_idxs[i];
                    VIdx v2 = poly_v_idxs[next_i];
                    Edge edge = poly_edges[i];

                    Util.Assert(edge.VIdxs.Contains(v1));
                    Util.Assert(edge.VIdxs.Contains(v2));
                }
            }
#endif
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
                float cos_angle = MathF.Cos(angle * MathF.PI / 180);

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

            foreach(var v_pair in Verts.Where(x => x.Value.EIdxs.Any()))
            {
                VIdx v_idx = v_pair.Key;
                Vert vert = v_pair.Value;

                bool force_all_separate_verts = vert.IsSharp;

                EIdx[] vert_eidxs = [.. VertEIdxs(v_idx)];
                PIdx[] vert_pidxs = [.. VertPIdxs(v_idx)];

                EIdx first_e_idx = vert_eidxs.FirstOrDefault(x => force_all_separate_verts || Edges[x].IsSetSharp, EIdx.Empty);

                if(first_e_idx == EIdx.Empty)
                {
                    first_e_idx = vert_eidxs.First();
                }

                int fei_idx = Array.IndexOf(vert_eidxs, first_e_idx);

                if(fei_idx != 0)
                {
                    vert_eidxs = [.. vert_eidxs.Skip(fei_idx), .. vert_eidxs.Take(fei_idx)];
                    vert_pidxs = [.. vert_pidxs.Skip(fei_idx), .. vert_pidxs.Take(fei_idx)];
                }

                // we should now have the edge and face-indexes cyclically permuted, such that if there is any sharp edge it is first
                // (if there are >1 no problem)

                OutVert current = null;
                int num_polys = 0;

                for(int i = 0; i < vert_eidxs.Length; i++)
                {
                    PIdx p_idx = vert_pidxs[i];
                    Edge edge = Edges[vert_eidxs[i]];

                    bool is_sharp = use_angle ? edge.IsObservedSharp : edge.IsSetSharp;

                    if (is_sharp || current == null /* || force_all_separate_verts */)
                    {
                        if (current != null)
                        {
                            current.Normal = current.Normal.Normalized();
                            num_polys = 0;
                        }

                        current = new()
                        {
                            Vert = Verts[v_idx]
                        };
                    }

                    OutVerts[(p_idx, v_idx)] = current;
                    current.Normal += PolyNormal(p_idx);
                    num_polys++;
                }

                if (num_polys > 0)
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

        private Mesh OutputMeshNormals(MeshOptions options)
        {
            List<Vector3> verts = [];
            List<Vector3> normals = [];
            List<int> idxs = [];

            bool include_poly = options.Normals_IncludePoly;
            bool include_edge = options.Normals_IncludeEdge;
            bool include_vert = options.Normals_IncludeVert;
            bool include_vert_split = options.Normals_IncludeSplitVert;

            if (include_edge)
            {
                foreach(EIdx e_idx in Edges.Keys)
                {
                    Vector3 edge_centre = EdgeMidpoint(e_idx);
                    Vector3 normal = EdgeNormal(e_idx);
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
                foreach(VIdx v_idx in Verts.Keys)
                {
                    Vector3 normal = VertNormal(v_idx);

                    PIdx any_vert_poly = VertPIdxs(v_idx).First();
                    OutVert out_vert = OutVerts[(any_vert_poly, v_idx)];
                    idxs.Add(verts.Count);
                    verts.Add(out_vert.Vert.Position);
                    normals.Add(normal);
                    idxs.Add(verts.Count);
                    verts.Add(out_vert.Vert.Position + normal * options.DrawNormalsLength);
                    normals.Add(normal);
                }
            }

            if (include_vert_split)
            {
                foreach(OutVert out_vert in OutVerts.Values)
                {
                    idxs.Add(verts.Count);
                    verts.Add(out_vert.Vert.Position);
                    normals.Add(out_vert.Normal);
                    idxs.Add(verts.Count);
                    verts.Add(out_vert.Vert.Position + out_vert.Normal * options.DrawNormalsLength);
                    normals.Add(out_vert.Normal);
                }
            }

            if (include_poly)
            {
                foreach(PIdx p_idx in Polys.Keys)
                {
                    Vector3 centre = PolyCentre(p_idx);
                    Vector3 normal = PolyNormal(p_idx);

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

        private Mesh OutputMeshLines(MeshOptions options)
        {
            List<Vector3> verts = [];
            List<Vector3> normals = [];
            List<int> idxs = [];

            bool include_sharp = options.Edges_IncludeSharp;
            bool include_smooth = options.Edges_IncludeSmooth;
            bool use_angle = options.Edges_DetermineSmoothnessFromAngle;
            bool use_filter = options.Edges_Filter != null;
            float vert_offset = options.Edges_Offset;

            foreach(var pair in Polys)
            {
                PIdx p_idx = pair.Key;
                Poly poly = pair.Value;

                foreach(VIdx v_idx in poly.VIdxs)
                {
                    OutVert out_vert = OutVerts[(p_idx, v_idx)];
                    if (out_vert.OutIdx == -1)
                    {
                        out_vert.OutIdx = verts.Count;
                        Vector3 position = out_vert.Vert.Position;

                        if (vert_offset != 0)
                        {
                            position += VertNormal(v_idx) * vert_offset;
                        }
                        verts.Add(position);
                        normals.Add(out_vert.Normal);
                    }
                }
            }

            foreach(PIdx p_idx in Polys.Keys)
            {
                // we will output each line twice, once for each adjoiing poly
                // but those polys have different normals, so *not* doing this loses info
                // OTOH even doing it, one will be invisible or there will be Z-fighgint
                //
                // a more-correct approach might be to shift the edges 1 pixel *in* to their face
                // so both can show, but that would require shader magic, I suspect...
                foreach(Edge edge in PolyEdges(p_idx))
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
                        bool is_sharp = false;

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

                    idxs.Add(OutVerts[(p_idx, edge.Start)].OutIdx);
                    idxs.Add(OutVerts[(p_idx, edge.End)].OutIdx);
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

        private Mesh OutputMeshSurface(MeshOptions options)
        {
            List<Vector3> verts = [];
            List<Vector3> normals = [];
            List<int> idxs = [];
            bool use_filter = options.Polys_filter != null;

            foreach(var pair in Polys)
            {
                PIdx p_idx = pair.Key;
                Poly poly = pair.Value;

                if (use_filter && !options.Polys_filter(poly))
                {
                    continue;
                }

                foreach(VIdx v_idx in poly.VIdxs)
                {
                    OutVert out_vert = OutVerts[(p_idx, v_idx)];
                    if (out_vert.OutIdx == -1)
                    {
                        out_vert.OutIdx = verts.Count;
                        verts.Add(out_vert.Vert.Position);
                        normals.Add(out_vert.Normal);
                    }
                }
            }

            // split our polys apart into individual triangles
            foreach(var pair in Polys)
            {
                PIdx p_idx = pair.Key;
                Poly poly = pair.Value;

                if (use_filter && !options.Polys_filter(poly))
                {
                    continue;
                }

                int vert_0_idx = OutVerts[(p_idx, poly.VIdxs[0])].OutIdx;

                // build the poly from a fan of trianges around vert-0
                for(int p = 1; p < poly.VIdxs.Length - 1; p++)
                {
                    idxs.Add(vert_0_idx);
                    idxs.Add(OutVerts[(p_idx, poly.VIdxs[p])].OutIdx);
                    idxs.Add(OutVerts[(p_idx, poly.VIdxs[p + 1])].OutIdx);
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

        public Surface Distort(DistortFunc distortion)
        {
            BidirectionalDictionary<VIdx, Vert> NewVerts = [];

            foreach(VIdx v_idx in Verts.Keys)
            {
                Vert vert = Verts[v_idx];

                Vert new_vert = new Vert(distortion(vert.Position), vert.EIdxs, vert.PIdxs);
                new_vert.SetMetadataFrom(vert);

                NewVerts[v_idx] = new_vert;
            }

            BidirectionalDictionary<EIdx, Edge> NewEdges = [];

            foreach(var pair in Edges)
            {
                Edge edge = pair.Value;
                Edge new_edge = new Edge(edge.Start, edge.End, edge.Left, edge.Right);
                new_edge.SetMetaDataFrom(edge);
                NewEdges[pair.Key] = new_edge;
            }

            Dictionary<PIdx, Poly> NewPolys = [];

            foreach(var pair in Polys)
            {
                Poly poly = pair.Value;
                Poly new_poly = new Poly(poly.VIdxs, poly.EIdxs);
                new_poly.SetMetadataFrom(poly);
                NewPolys[pair.Key] = new_poly;
            }

            return new Surface(NewVerts, NewEdges, NewPolys);
        }
    }
}