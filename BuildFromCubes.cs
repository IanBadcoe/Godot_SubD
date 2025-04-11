using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SubD;

using VIdx = SubD.Idx<SubD.Vert>;
using EIdx = SubD.Idx<SubD.Edge>;
using PIdx = SubD.Idx<SubD.Poly>;
using System.Diagnostics;
using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SubD
{
    public class Cube
    {
        // Top/Bottom = Y
        // Front/Back = Z
        // Left/Right = X
        public enum VertName
        {
            TopFrontLeft,
            TopFrontRight,
            TopBackLeft,
            TopBackRight,
            BottomFrontLeft,
            BottomFrontRight,
            BottomBackLeft,
            BottomBackRight,
        }

        public enum EdgeName
        {
            TopLeft,
            TopRight,
            TopFront,
            TopBack,
            BottomLeft,
            BottomRight,
            BottomFront,
            BottomBack,
            FrontRight,
            FrontLeft,
            BackRight,
            BackLeft,
        }

        public enum FaceName
        {
            Top,
            Bottom,
            Left,
            Right,
            Front,
            Back
        }

        Dictionary<VertName, bool> VertSharpnessInner = VertNameUtils.AllVerts.ToDictionary(x => x, x => false);

        Dictionary<EdgeName, bool> EdgeSharpnessInner = EdgeNameUtils.AllEdges.ToDictionary(x => x, x => false);

        Dictionary<VertName, string> VertTagsInner = VertNameUtils.AllVerts.ToDictionary(x => x, x => "");

        Dictionary<EdgeName, string> EdgeTagsInner = EdgeNameUtils.AllEdges.ToDictionary(x => x, x => "");

        public Vector3I Position
        {
            get;
            private set;
        }

        public Cube()
        {
            IsVertSharp = new IndexedProperty<VertName, bool>(VertSharpnessInner, false);
            IsEdgeSharp = new IndexedProperty<EdgeName, bool>(EdgeSharpnessInner, false);

            VertTag = new IndexedProperty<VertName, string>(VertTagsInner, false);
            EdgeTag = new IndexedProperty<EdgeName, string>(EdgeTagsInner, false);
        }

        public Cube(Vector3I position)
            : this()
        {
            Position = position;
        }

        public IndexedProperty<VertName, bool> IsVertSharp;
        public IndexedProperty<EdgeName, bool> IsEdgeSharp;
        public IndexedProperty<VertName, string> VertTag;
        public IndexedProperty<EdgeName, string> EdgeTag;

        static readonly Dictionary<VertName, Vector3> VertOffsets = new() {
            { VertName.BottomBackLeft, new Vector3(-0.5f, -0.5f, -0.5f) },
            { VertName.BottomBackRight, new Vector3( 0.5f, -0.5f, -0.5f) },
            { VertName.TopBackRight, new Vector3( 0.5f,  0.5f, -0.5f) },
            { VertName.TopBackLeft, new Vector3(-0.5f,  0.5f, -0.5f) },

            { VertName.BottomFrontLeft, new Vector3(-0.5f, -0.5f,  0.5f) },
            { VertName.BottomFrontRight, new Vector3( 0.5f, -0.5f,  0.5f) },
            { VertName.TopFrontRight, new Vector3( 0.5f,  0.5f,  0.5f) },
            { VertName.TopFrontLeft, new Vector3(-0.5f,  0.5f,  0.5f) },
        };

        public Vector3 GetVert(VertName v_name)
        {
            return VertOffsets[v_name] + Position;
        }
    }

    public static class VertNameUtils
    {
        static readonly Cube.VertName[] AllVertsInner =
        [
            Cube.VertName.TopFrontLeft,
            Cube.VertName.TopFrontRight,
            Cube.VertName.TopBackLeft,
            Cube.VertName.TopBackRight,
            Cube.VertName.BottomFrontLeft,
            Cube.VertName.BottomFrontRight,
            Cube.VertName.BottomBackLeft,
            Cube.VertName.BottomBackRight
        ];

        public static bool IsTop(this Cube.VertName vert)
        {
            return TopVerts.Contains(vert);
        }

        public static bool IsBottom(this Cube.VertName vert)
        {
            return BottomVerts.Contains(vert);
        }

        public static bool IsLeft(this Cube.VertName vert)
        {
            return LeftVerts.Contains(vert);
        }

        public static bool IsRight(this Cube.VertName vert)
        {
            return RightVerts.Contains(vert);
        }

        public static bool IsFront(this Cube.VertName vert)
        {
            return FrontVerts.Contains(vert);
        }

        public static bool IsBack(this Cube.VertName vert)
        {
            return BackVerts.Contains(vert);
        }

        public static IEnumerable<Cube.VertName> AllVerts
        {
            get => AllVertsInner;
        }

        public static IEnumerable<Cube.VertName> TopVerts
        {
            get => [Cube.VertName.TopBackLeft, Cube.VertName.TopBackRight, Cube.VertName.TopFrontLeft, Cube.VertName.TopFrontRight];
        }

        public static IEnumerable<Cube.VertName> BottomVerts
        {
            get => [Cube.VertName.BottomBackLeft, Cube.VertName.BottomBackRight, Cube.VertName.BottomFrontLeft, Cube.VertName.BottomFrontRight];
        }

        public static IEnumerable<Cube.VertName> RightVerts
        {
            get => [Cube.VertName.TopBackRight, Cube.VertName.TopFrontRight, Cube.VertName.BottomBackRight, Cube.VertName.BottomFrontRight];
        }

        public static IEnumerable<Cube.VertName> LeftVerts
        {
            get => [Cube.VertName.TopBackLeft, Cube.VertName.TopFrontLeft, Cube.VertName.BottomBackLeft, Cube.VertName.BottomFrontLeft];
        }

        public static IEnumerable<Cube.VertName> FrontVerts
        {
            get => [Cube.VertName.TopFrontRight, Cube.VertName.BottomFrontRight, Cube.VertName.TopFrontLeft, Cube.VertName.BottomFrontLeft];
        }

        public static IEnumerable<Cube.VertName> BackVerts
        {
            get => [Cube.VertName.TopBackRight, Cube.VertName.BottomBackRight, Cube.VertName.TopBackLeft, Cube.VertName.BottomBackLeft];
        }
    }

    static class EdgeNameUtils
    {
        static readonly Cube.EdgeName[] AllEdgesInner =
        [
            Cube.EdgeName.TopLeft,
            Cube.EdgeName.TopRight,
            Cube.EdgeName.TopFront,
            Cube.EdgeName.TopBack,
            Cube.EdgeName.BottomLeft,
            Cube.EdgeName.BottomRight,
            Cube.EdgeName.BottomFront,
            Cube.EdgeName.BottomBack,
            Cube.EdgeName.FrontRight,
            Cube.EdgeName.FrontLeft,
            Cube.EdgeName.BackRight,
            Cube.EdgeName.BackLeft,
        ];

        public static IEnumerable<Cube.EdgeName> AllEdges
        {
            get => AllEdgesInner;
        }

        static readonly Dictionary<Cube.EdgeName, (Cube.VertName, Cube.VertName)> EdgeIsBetween = new()
        {
            { Cube.EdgeName.BackLeft, (Cube.VertName.BottomBackLeft, Cube.VertName.TopBackLeft) },
            { Cube.EdgeName.BackRight, (Cube.VertName.BottomBackRight, Cube.VertName.TopBackRight) },
            { Cube.EdgeName.BottomBack, (Cube.VertName.BottomBackLeft, Cube.VertName.BottomBackRight) },
            { Cube.EdgeName.BottomFront, (Cube.VertName.BottomFrontLeft, Cube.VertName.BottomFrontRight) },
            { Cube.EdgeName.BottomLeft, (Cube.VertName.BottomFrontLeft, Cube.VertName.BottomBackLeft) },
            { Cube.EdgeName.BottomRight, (Cube.VertName.BottomFrontRight, Cube.VertName.BottomBackRight) },
            { Cube.EdgeName.FrontLeft, (Cube.VertName.BottomFrontLeft, Cube.VertName.TopFrontLeft) },
            { Cube.EdgeName.FrontRight, (Cube.VertName.BottomFrontRight, Cube.VertName.TopFrontRight) },
            { Cube.EdgeName.TopBack, (Cube.VertName.TopBackLeft, Cube.VertName.TopBackRight) },
            { Cube.EdgeName.TopFront, (Cube.VertName.TopFrontLeft, Cube.VertName.TopFrontRight) },
            { Cube.EdgeName.TopLeft, (Cube.VertName.TopBackLeft, Cube.VertName.TopFrontLeft) },
            { Cube.EdgeName.TopRight, (Cube.VertName.TopBackRight, Cube.VertName.TopFrontRight) },
        };

        public static (Cube.VertName, Cube.VertName) GetEdgeVerts(Cube.EdgeName e_name)
        {
            return EdgeIsBetween[e_name];
        }

        public static IEnumerable<Cube.VertName> Both(this (Cube.VertName v1, Cube.VertName v2) pair)
        {
            yield return pair.v1;
            yield return pair.v2;
        }

        public static bool IsTop(this Cube.EdgeName edge)
        {
            return TopEdges.Contains(edge);
        }

        public static bool IsBottom(this Cube.EdgeName edge)
        {
            return BottomEdges.Contains(edge);
        }

        public static bool IsLeft(this Cube.EdgeName edge)
        {
            return LeftEdges.Contains(edge);
        }

        public static bool IsRight(this Cube.EdgeName edge)
        {
            return RightEdges.Contains(edge);
        }

        public static bool IsFront(this Cube.EdgeName edge)
        {
            return FrontEdges.Contains(edge);
        }

        public static bool IsBack(this Cube.EdgeName edge)
        {
            return BackEdges.Contains(edge);
        }

        public static IEnumerable<Cube.EdgeName> TopEdges
        {
            get => AllEdges.Where(x => EdgeIsBetween[x].Both().All(x => x.IsTop()));
        }

        public static IEnumerable<Cube.EdgeName> BottomEdges
        {
            get => AllEdges.Where(x => EdgeIsBetween[x].Both().All(x => x.IsBottom()));
        }

        public static IEnumerable<Cube.EdgeName> LeftEdges
        {
            get => AllEdges.Where(x => EdgeIsBetween[x].Both().All(x => x.IsLeft()));
        }

        public static IEnumerable<Cube.EdgeName> RightEdges
        {
            get => AllEdges.Where(x => EdgeIsBetween[x].Both().All(x => x.IsRight()));
        }

        public static IEnumerable<Cube.EdgeName> FrontEdges
        {
            get => AllEdges.Where(x => EdgeIsBetween[x].Both().All(x => x.IsFront()));
        }

        public static IEnumerable<Cube.EdgeName> BackEdges
        {
            get => AllEdges.Where(x => EdgeIsBetween[x].Both().All(x => x.IsBack()));
        }
    }

    public static class FaceNameUtils
    {
        static Dictionary<Cube.FaceName, Cube.EdgeName[]> FaceEdges = new()
        {
            { Cube.FaceName.Top, [ Cube.EdgeName.TopLeft, Cube.EdgeName.TopBack, Cube.EdgeName.TopRight, Cube.EdgeName.TopFront ] },
            { Cube.FaceName.Bottom, [ Cube.EdgeName.BottomLeft, Cube.EdgeName.BottomFront, Cube.EdgeName.BottomRight, Cube.EdgeName.BottomBack ] },
            { Cube.FaceName.Front, [ Cube.EdgeName.FrontLeft, Cube.EdgeName.TopFront, Cube.EdgeName.FrontRight, Cube.EdgeName.BottomFront ] },
            { Cube.FaceName.Back, [ Cube.EdgeName.BackLeft, Cube.EdgeName.BottomBack, Cube.EdgeName.BackRight, Cube.EdgeName.TopBack ] },
            { Cube.FaceName.Right, [ Cube.EdgeName.FrontRight, Cube.EdgeName.TopRight, Cube.EdgeName.BackRight, Cube.EdgeName.BottomRight ] },
            { Cube.FaceName.Left, [ Cube.EdgeName.FrontLeft, Cube.EdgeName.BottomLeft, Cube.EdgeName.BackLeft, Cube.EdgeName.TopLeft ] },
        };

        static Dictionary<Cube.FaceName, Cube.VertName[]> FaceVerts = new()
        {
            { Cube.FaceName.Top, [ Cube.VertName.TopFrontLeft, Cube.VertName.TopBackLeft, Cube.VertName.TopBackRight, Cube.VertName.TopFrontRight ] },
            { Cube.FaceName.Bottom, [ Cube.VertName.BottomFrontLeft, Cube.VertName.BottomFrontRight, Cube.VertName.BottomBackRight, Cube.VertName.BottomBackLeft ] },
            { Cube.FaceName.Front, [ Cube.VertName.BottomFrontLeft, Cube.VertName.TopFrontLeft, Cube.VertName.TopFrontRight, Cube.VertName.BottomFrontRight ] },
            { Cube.FaceName.Back, [ Cube.VertName.TopBackLeft, Cube.VertName.BottomBackLeft, Cube.VertName.BottomBackRight, Cube.VertName.TopBackRight ] },
            { Cube.FaceName.Right, [ Cube.VertName.BottomFrontRight, Cube.VertName.TopFrontRight, Cube.VertName.TopBackRight, Cube.VertName.BottomBackRight ] },
            { Cube.FaceName.Left, [ Cube.VertName.TopFrontLeft, Cube.VertName.BottomFrontLeft, Cube.VertName.BottomBackLeft, Cube.VertName.TopBackLeft ] },
        };

        public static IEnumerable<Cube.EdgeName> GetEdgesForFace(Cube.FaceName f_name)
        {
            return FaceEdges[f_name];
        }

        public static IEnumerable<Cube.VertName> GetVertsForFace(Cube.FaceName f_name)
        {
            return FaceVerts[f_name];
        }

        static Cube.FaceName[] AllFacesInner = [Cube.FaceName.Top, Cube.FaceName.Bottom, Cube.FaceName.Left, Cube.FaceName.Right, Cube.FaceName.Front, Cube.FaceName.Back];

        public static IEnumerable<Cube.FaceName> AllFaces
        {
            get => AllFacesInner;
        }

    }

    [DebuggerDisplay("Count = {Cubes.Count}")]
    public class BuildFromCubes
    {
        public struct IdxCube
        {
            public IReadOnlyDictionary<Cube.VertName, VIdx> VertMap
            {
                get;
                private set;
            }

            public Cube Cube
            {
                get;
                private set;
            }

            public IdxCube(IDictionary<Cube.VertName, VIdx> vert_map, Cube cube)
            {
                VertMap = vert_map.AsReadOnly();
                Cube = cube;
            }
        }

        int NextVertIdx;
        int NextEdgeIdx;
        int NextPolyIdx;

        List<Cube> Cubes;
        BidirectionalDictionary<VIdx, Vert> Verts;
        BidirectionalDictionary<EIdx, Edge> Edges;
        BidirectionalDictionary<PIdx, Poly> Polys;

        public BuildFromCubes()
        {
            Reset();
        }

        public Cube AddCube(Vector3I position)
        {
            Cube cube = new(position);

            Cubes.Add(cube);

            return cube;
        }

        public void RemoveCube(Vector3I position)
        {
            Cubes = [.. Cubes.Where(x => x.Position != position)];
        }

        public Surface ToSurface(bool reset_after = true)
        {
            List<IdxCube> idx_cubes = new();

            foreach(var cube in Cubes)
            {
                idx_cubes.Add(new IdxCube(CubeToIdxVerts(cube), cube));
            }

            foreach(var cube in idx_cubes)
            {
                // Debug.Print($"Cube: {cube.Centre}");

                List<VIdx[]> real_faces = new();

                // if any face is the negative of some other face, then instead of adding this one, we need to remove the other
                // so that the two cubes join
                //
                // and we need to do that first, because edges can only have one Left or Right, and we might add a new
                // face that wants to take an edge from an old one
                foreach(var f_name in FaceNameUtils.AllFaces)
                {
                    VIdx[] globally_indexed_face = [.. FaceNameUtils.GetVertsForFace(f_name).Select(x => cube.VertMap[x])];

                    if (!ResolveOppositeFaces(globally_indexed_face))
                    {
                        real_faces.Add(globally_indexed_face);
                    }
                }

                foreach(VIdx[] globally_indexed_face in real_faces)
                {
                    List<EIdx> face_edges = new();
                    List<Edge> left_edges = new();
                    List<Edge> right_edges = new();

                    for(int i = 0; i < globally_indexed_face.Length; i++)
                    {
                        VIdx v_idx = globally_indexed_face[i];
                        int next_i = (i + 1) % globally_indexed_face.Length;
                        VIdx next_v_idx = globally_indexed_face[next_i];

                        Edge edge = new(v_idx, next_v_idx);
                        Edge r_edge = edge.Reversed();

                        if (Edges.Contains(edge))
                        {
                            // this can only happen, if we had a face using this edge already, in this direction, and subsequently removed it
                            // because otherwise, we see each edge exactly twice, first time forwards (second following clause)
                            // second time backwards (next clause)
                            EIdx e_idx = Edges[edge];
                            face_edges.Add(e_idx);
                            // store the real dictionary member for setting its "Left" later
                            right_edges.Add(Edges[e_idx]);
                        }
                        else if (Edges.Contains(r_edge))
                        {
                            EIdx e_idx = Edges[r_edge];
                            face_edges.Add(e_idx);
                            // store the real dictionary member for setting its "Left" later
                            left_edges.Add(Edges[e_idx]);
                        }
                        else
                        {
                            EIdx e_idx = new(NextEdgeIdx++);

                            Edges[edge] = e_idx;
                            face_edges.Add(e_idx);
                            // store the real dictionary member for setting its "Right" later
                            right_edges.Add(edge);

                            // it's a new edge, so let the two verts know
                            Verts[edge.Start].AddEIdx(e_idx);
                            Verts[edge.End].AddEIdx(e_idx);
                        }
                    }

                    foreach(Cube.EdgeName e_name in EdgeNameUtils.AllEdges)
                    {
                        // surely this can be made easier???
                        (Cube.VertName v1_name, Cube.VertName v2_name) = EdgeNameUtils.GetEdgeVerts(e_name);

                        VIdx v1 = cube.VertMap[v1_name];
                        VIdx v2 = cube.VertMap[v2_name];

                        Edge edge = new Edge(v1, v2);

                        EIdx? real_e_idx = Edges.Contains(edge) ? Edges[edge] : Edges.Contains(edge.Reversed()) ? Edges[edge.Reversed()] : null;

                        // edge may have been removed as internal geometry
                        if (!real_e_idx.HasValue)
                        {
                            continue;
                        }

                        if (cube.Cube.IsEdgeSharp[e_name])
                        {
                            Edges[real_e_idx.Value].IsSetSharp = true;
                        }

                        string edge_tag = cube.Cube.EdgeTag[e_name];
                        if (!string.IsNullOrEmpty(edge_tag))
                        {
                            Edges[real_e_idx.Value].Tag = edge_tag;
                        }
                    }

                    Poly poly = new(globally_indexed_face, face_edges);

                    PIdx p_idx = new(NextPolyIdx++);
                    Polys[poly] = p_idx;

                    // it's a new poly, so let all the verts know
                    foreach(Vert vert in poly.VIdxs.Select(x => Verts[x]))
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
            }

            foreach(VIdx v_idx in Verts.Keys)
            {
                Vert old_vert = Verts[v_idx];
                // we added the edges and polys to the verts in a fairly arbitraty order, but we need
                // them to both be clockwise, from outside the cube, looking inwards, and...
                //
                // we need the two edges of the poly at position N to be N and N + 1
                Verts[v_idx] = VertUtil.ToVertWithSortedEdgesAndPolys(old_vert, v_idx, Edges, Polys);
            }

            Surface ret = new Surface(Verts, Edges, Polys);

            if (reset_after)
            {
                Reset();
            }

            return ret;
        }

        private bool ResolveOppositeFaces(VIdx[] v_idxs)
        {
            VIdx[] v_r_temp = [.. Poly.StandardiseVIdxOrder(v_idxs.Reverse())];

#if DEBUG
            VIdx[] v_temp = [.. Poly.StandardiseVIdxOrder(v_idxs)];
#endif

            foreach(var pair in Polys)
            {
#if DEBUG
                // we should *never* find the same face come up twice the same way around
                Util.Assert(!pair.Value.VIdxs.SequenceEqual(v_temp));
#endif

                // if we see the reverse of an existing face, they cancel...
                if (pair.Value.VIdxs.SequenceEqual(v_r_temp))
                {
                    RemovePoly(pair.Key);

                    return true;
                }
            }

            return false;
        }

        private void RemovePoly(PIdx p_idx)
        {
            Poly poly = Polys.Remove(p_idx);

            List<Tuple<EIdx, Edge>> removed_edges = new();

            foreach(Edge edge in poly.EIdxs.Select(x => Edges[x]))
            {
                Util.Assert(edge.PIdxs.Contains(p_idx));

                edge.RemovePoly(p_idx);

                if (!edge.PIdxs.Any())
                {
                    EIdx e_idx = Edges[edge];

                    Edges.Remove(edge);

                    removed_edges.Add(new Tuple<EIdx, Edge>(e_idx, edge));
                }
            }

            foreach(Vert vert in poly.VIdxs.Select(x => Verts[x]))
            {
                vert.RemovePoly(p_idx);
            }

            foreach(var pair in removed_edges)
            {
                foreach(Vert vert in pair.Item2.VIdxs.Select(x => Verts[x]))
                {
                    Util.Assert(vert.EIdxs.Contains(pair.Item1));

                    vert.RemoveEdge(pair.Item1);

                    if (!vert.EIdxs.Any())
                    {
                        // vert realy should become empty of edges and polys at the same time...
                        Util.Assert(!vert.PIdxs.Any());

                        Verts.Remove(vert);
                    }
                }
            }
        }

        IDictionary<Cube.VertName, VIdx> CubeToIdxVerts(Cube cube)
        {
            Dictionary<Cube.VertName, VIdx> ret = new();

            foreach(Cube.VertName v_name in VertNameUtils.AllVerts)
            {
                Vector3 pos = cube.GetVert(v_name);

                Vert vert = new(pos);

                if (!Verts.Contains(vert))
                {
                    Verts[vert] = new VIdx(NextVertIdx++);
                }

                // any cube sharing this vert and saying this vert is sharp makes it so
                if (cube.IsVertSharp[v_name])
                {
                    vert.IsSharp = true;
                }

                if (!string.IsNullOrEmpty(cube.VertTag[v_name]))
                {
                    vert.Tag = cube.VertTag[v_name];
                }

                ret[v_name] = Verts[vert];
            }

            return ret;
        }

        void Reset()
        {
            NextVertIdx = 0;
            NextEdgeIdx = 0;
            NextPolyIdx = 0;

            Cubes = new();

            Verts = new();
            Edges = new();
            Polys = new();
        }
    }
}