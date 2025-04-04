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

        public IEnumerable<Vert> EdgeVerts(EIdx e_idx) => Edges[e_idx].VIdxs.Select(x => Verts[x]);

        public BidirectionalDictionary<VIdx, Vert> Verts
        {
            get;
            private set;
        }

        public Vector3 EdgeMidpoint(EIdx e_idx) => EdgeVerts(e_idx).Select(x => x.Position).Aggregate(Vector3.Zero, (x, y) => x + y) / 2;

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
                    Debug.Assert(edge.VIdxs.Contains(pair.Key));
                }

                // all verts which reference a poly should be referenced by the poly
                foreach(Poly poly in pair.Value.PIdxs.Select(x => polys[x]))
                {
                    Debug.Assert(poly.VIdxs.Contains(pair.Key));
                }
            }

            foreach(var pair in edges)
            {
                // all edges which reference a vert should be referenced by the vert
                foreach(Vert vert in pair.Value.VIdxs.Select(x => verts[x]))
                {
                    Debug.Assert(vert.EIdxs.Contains(pair.Key));
                }

                // all edges which reference a poly should be referenced by the poly
                foreach(Poly poly in pair.Value.PIdxs.Select(x => polys[x]))
                {
                    Debug.Assert(poly.EIdxs.Contains(pair.Key));
                }
            }

            foreach(var pair in polys)
            {
                // all polys which reference a vert should be referenced by the vert
                foreach(Vert vert in pair.Value.VIdxs.Select(x => verts[x]))
                {
                    Debug.Assert(vert.PIdxs.Contains(pair.Key));
                }

                // all polys which reference an edge should be referenced by the edge
                foreach(Edge edge in pair.Value.EIdxs.Select(x => edges[x]))
                {
                    Debug.Assert(edge.PIdxs.Contains(pair.Key));
                }
            }
#endif

            Verts = verts;
            Edges = edges;
            Polys = polys;
        }

        public Mesh ToMesh()
        {
            ArrayMesh mesh = new ArrayMesh();

            Dictionary<VIdx, int> vert_remap = new Dictionary<VIdx, int>();
            int next_vert_idx = 0;

            // some verts can have been deleted, so we need to make the indices contiguous again
            foreach(VIdx v_idx in Verts.Keys)
            {
                vert_remap[v_idx] = next_vert_idx++;
            }

            Vector3[] verts = Verts.OrderBy(x => x.Key).Select(x => x.Value.Position).ToArray();

            List<int> idxs = new List<int>();

            // split our polys apart into individual triangles
            foreach(var p_idx in Polys.Keys)
            {
                VIdx[] v_idxs = PolyVIdxs(p_idx).ToArray();

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

            // Create the Mesh.
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            return mesh;
        }
    }
}