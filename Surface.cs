using System.Collections.Generic;
using System.Linq;

using EIdx = SubD.Idx<SubD.Edge>;
using VIdx = SubD.Idx<SubD.Vert>;
using PIdx = SubD.Idx<SubD.Poly>;
using Godot;
using System.Diagnostics;
using System.Linq.Expressions;

namespace SubD
{
    [DebuggerDisplay("Verts = Vert.Count, Edges = Edge.Count, Faces = Face.Count")]
    public class Surface
    {
        public BidirectionalDictionary<PIdx, Poly> Polys
        {
            get;
            private set;
        }

        public IEnumerable<VIdx> PolyVIdxs(PIdx p_idx) => Polys[p_idx].VIdxs;

        public IEnumerable<Vert> PolyVerts(PIdx p_idx) => PolyVIdxs(p_idx).Select(x => Verts[x]);

        public IEnumerable<EIdx> PolyEIdxs(PIdx p_idx) => Polys[p_idx].EIdxs;

        public IEnumerable<Edge> PolyEdges(PIdx p_idx) => PolyEIdxs(p_idx).Select(x => Edges[x]);

        public BidirectionalDictionary<EIdx, Edge> Edges
        {
            get;
            private set;
        }

        public IEnumerable<VIdx> EdgeVIdxs(EIdx e_idx) => Edges[e_idx].VIdxs;

        public IEnumerable<Vert> EdgeVerts(EIdx e_idx) => EdgeVIdxs(e_idx).Select(x => Verts[x]);

        public IEnumerable<PIdx> EdgePIdxs(EIdx e_idx) => Edges[e_idx].PIdxs;

        public IEnumerable<Poly> EdgePolys(EIdx e_idx) => EdgePIdxs(e_idx).Select(x => Polys[x]);

        public BidirectionalDictionary<VIdx, Vert> Verts
        {
            get;
            private set;
        }

        public IEnumerable<EIdx> VertEIdxs(VIdx v_idx) => Verts[v_idx].EIdxs;

        public IEnumerable<Edge> VertEdges(VIdx v_idx) => VertEIdxs(v_idx).Select(x => Edges[x]);

        public IEnumerable<PIdx> VertPIdxs(VIdx v_idx) => Verts[v_idx].PIdxs;

        public IEnumerable<Poly> VertPolys(VIdx v_idx) => VertPIdxs(v_idx).Select(x => Polys[x]);

        public Vector3 EdgeMidpoint(EIdx e_idx) => EdgeVerts(e_idx).Select(x => x.Position).Sum() / 2;

        VIdx? GetVIdx(Vector3 pos)
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
                Vector3[] verts = PolyVerts(p_idx).Select(x => x.Position).ToArray();

                Vector3 last_delta = verts[1] - verts[0];

                Vector3 accum = Vector3.Zero;

                for(int i = 2; i < verts.Length; i++)
                {
                    Vector3 delta = verts[i] - verts[0];

                    Vector3 cross = delta.Cross(last_delta);

                    accum += cross;

                    last_delta = delta;
                }

                poly.Normal = accum.Normalized();
            }

            return poly.Normal.Value;
        }

        public Vector3 VertNormal(VIdx v_idx)
        {
            Vert vert = Verts[v_idx];

            if (vert.Normal == null)
            {
                vert.Normal = vert.PIdxs.Select(x => PolyNormal(x)).Sum() / vert.PIdxs.Count();
            }

            return vert.Normal.Value;
        }

        public Surface(
            BidirectionalDictionary<VIdx, Vert> verts,
            BidirectionalDictionary<EIdx, Edge> edges,
            BidirectionalDictionary<PIdx, Poly> polys)
        {
            // allow no more changes
            foreach(Vert vert in verts.Values)
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
            foreach(var pair in verts)
            {
                // all verts which reference an edge should be referenced by the edge
                foreach(Edge edge in pair.Value.EIdxs.Select(x => edges[x]))
                {
                    Util.Assert(edge.VIdxs.Contains(pair.Key));
                }

                // all verts which reference a poly should be referenced by the poly
                foreach(Poly poly in pair.Value.PIdxs.Select(x => polys[x]))
                {
                    Util.Assert(poly.VIdxs.Contains(pair.Key));
                }
            }

            foreach(var pair in edges)
            {
                Util.Assert(pair.Value.Left.HasValue);
                Util.Assert(pair.Value.Right.HasValue);

                // all edges which reference a vert should be referenced by the vert
                foreach(Vert vert in pair.Value.VIdxs.Select(x => verts[x]))
                {
                    Util.Assert(vert.EIdxs.Contains(pair.Key));
                }

                // all edges which reference a poly should be referenced by the poly
                foreach(Poly poly in pair.Value.PIdxs.Select(x => polys[x]))
                {
                    Util.Assert(poly.EIdxs.Contains(pair.Key));
                }
            }

            foreach(var pair in polys)
            {
                // all polys which reference a vert should be referenced by the vert
                foreach(Vert vert in pair.Value.VIdxs.Select(x => verts[x]))
                {
                    Util.Assert(vert.PIdxs.Contains(pair.Key));
                }

                // all polys which reference an edge should be referenced by the edge
                foreach(Edge edge in pair.Value.EIdxs.Select(x => edges[x]))
                {
                    Util.Assert(edge.PIdxs.Contains(pair.Key));
                }
            }
#endif

            Verts = verts;
            Edges = edges;
            Polys = polys;
        }

        public Mesh ToMesh()
        {
            ArrayMesh mesh = new();

            Dictionary<VIdx, int> vert_remap = new();
            int next_vert_idx = 0;

            // some verts can have been deleted, so we need to make the indices contiguous again
            foreach(VIdx v_idx in Verts.Keys)
            {
                vert_remap[v_idx] = next_vert_idx++;
            }

            Vector3[] verts = Verts.OrderBy(x => x.Key).Select(x => x.Value.Position).ToArray();
            List<int> idxs = new();
            Vector3[] normals = Verts.OrderBy(x => x.Key).Select(x => VertNormal(x.Key)).ToArray();

            // split our polys apart into individual triangles
            foreach(var p_idx in Polys.Keys)
            {
                VIdx[] v_idxs = PolyVIdxs(p_idx).ToArray();

                // build the poly from a fan of trianges around vert-0
                for(int p = 1; p < v_idxs.Length - 1; p++)
                {
                    idxs.Add(vert_remap[v_idxs[0]]);
                    idxs.Add(vert_remap[v_idxs[p]]);
                    idxs.Add(vert_remap[v_idxs[p + 1]]);
                }
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Index] = idxs.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals;

            // Create the Mesh.
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            return mesh;
        }

         public Mesh ToMeshLines(bool sharp_only)
        {
            ArrayMesh mesh = new();

            Dictionary<VIdx, int> vert_remap = new();
            int next_vert_idx = 0;

            // some verts can have been deleted, so we need to make the indices contiguous again
            foreach(VIdx v_idx in Verts.Keys)
            {
                vert_remap[v_idx] = next_vert_idx++;
            }

            Vector3[] verts = Verts.OrderBy(x => x.Key).Select(x => x.Value.Position).ToArray();
            List<int> idxs = new();
            Vector3[] normals = Verts.OrderBy(x => x.Key).Select(x => VertNormal(x.Key)).ToArray();

            foreach(Edge edge in Edges.Values.Where(x => !sharp_only || x.IsSharp))
            {
                idxs.Add(vert_remap[edge.Start]);
                idxs.Add(vert_remap[edge.End]);
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Index] = idxs.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals;

            // Create the Mesh.
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

            return mesh;
        }
    }
}