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
    using FIdx = Idx<Face>;

    public class Cube : IGeneratorIdentity
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

        public static (Cube.VertName, Cube.VertName) GetVertsForEdge(Cube.EdgeName e_name)
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
    public class BuildFromCubes : PolyhedronBuilderBase
    {
        public struct VertCube
        {
            public IReadOnlyDictionary<Cube.VertName, Vert> VertMap
            {
                get;
                private set;
            }

            public Dictionary<Cube.EdgeName, Edge> EdgeMap { get; } = [];

            public Cube Cube
            {
                get;
                private set;
            }

            public VertCube(IDictionary<Cube.VertName, Vert> vert_map, Cube cube)
            {
                VertMap = vert_map.AsReadOnly();
                Cube = cube;
            }
        }

        Dictionary<Cube, int> Cubes;

        public BuildFromCubes()
        {
            Reset();
        }

        public Cube AddCube(Vector3I position, int merge_group = 1)
        {
            Cube cube = new(position);

            Cubes.Add(cube, merge_group);


            Dirty = true;

            return cube;
        }

        public void RemoveCube(Vector3I position)
        {
            Cubes = Cubes.Where(x => x.Key.Position != position).ToDictionary();
        }

        Dictionary<Cube.VertName, Vert> CubeToVerts(Cube cube, SpatialDictionary<VIdx, Vert> verts)
        {
            Dictionary<Cube.VertName, Vert> ret = [];

            int next_v_idx = 0;

            foreach(Cube.VertName v_name in VertNameUtils.AllVerts)
            {
                Vector3 pos = cube.GetVert(v_name);

                Vert vert = new(pos);

                VIdx v_idx = new(next_v_idx++);
                verts[v_idx] = vert;

                // any cube sharing this vert and saying this vert is sharp makes it so
                if (cube.IsVertSharp[v_name])
                {
                    vert.IsSharp = true;
                }

                if (!string.IsNullOrEmpty(cube.VertTag[v_name]))
                {
                    vert.Tag = cube.VertTag[v_name];
                }

                ret[v_name] = vert;
            }

            return ret;
        }

        public override void Reset()
        {
            base.Reset();

            Cubes = [];
        }

        protected override void PopulateMergeStock_Impl()
        {
            foreach(var pair in Cubes)
            {
                MergeStock.Add(
                    new AnnotatedPolyhedron
                    {
                        MergeGroup = pair.Value,
                        GeneratorIdentity = pair.Key,
                        Polyhedron = PopulateCube(pair.Key)
                    }
                );
            }
        }

        Surface PopulateCube(Cube cube)
        {
            SpatialDictionary<VIdx, Vert> verts = [];
            SpatialDictionary<EIdx, Edge> edges = [];
            SpatialDictionary<FIdx, Face> faces = [];
            Dictionary<Cube.VertName, Vert> named_verts = CubeToVerts(cube, verts);

            VertCube vc = new(named_verts, cube);

            Dictionary<(Vert start, Vert end), Edge> made_edges = [];

            int next_e_idx = 0;
            int next_f_idx = 0;

            foreach(var f_name in FaceNameUtils.AllFaces)
            {
                Vert[] face_verts = [.. FaceNameUtils.GetVertsForFace(f_name).Select(x => vc.VertMap[x])];
                List<Edge> face_edges;
                List<Edge> backwards_edges;
                List<Edge> forwards_edges;

                FindMakeFaceEdges(
                    vc,
                    made_edges, edges,
                    ref next_e_idx, face_verts,
                    out face_edges,
                    out backwards_edges,
                    out forwards_edges);

                Face face = new(face_verts, face_edges, cube);

                FIdx f_idx = new(next_f_idx++);
                faces[f_idx] = face;

                // it's a new face, so let all the verts know
                foreach (Vert vert in face.Verts)
                {
                    vert.Faces.Add(face);
                }

                // forward edges will have the new face on their right
                // backward ones on the left...
                foreach (Edge edge in backwards_edges)
                {
                    edge.Backwards = face;
                }

                foreach (Edge edge in forwards_edges)
                {
                    edge.Forwards = face;
                }
            }

            ApplyEdgeSharpness(vc, made_edges);

            foreach(Vert vert in verts.Values)
            {
                // we added the edges and faces to the verts in a fairly arbitraty order, but we need
                // them to both be clockwise, from outside the cube, looking inwards, and...
                //
                // we need the two edges of the face at position N to be N and N + 1

                // argument "surf" not needed if we aren't allowing splitting
                VertUtil.SortVertEdgesAndFaces(null, vert, false);
            }

            return new(verts, edges, faces);
        }

        private static void FindMakeFaceEdges(
            VertCube vc,
            Dictionary<(Vert start, Vert end), Edge> made_edges,
            SpatialDictionary<EIdx, Edge> edges,
            ref int next_e_idx,
            Vert[] face_verts,
            out List<Edge> face_edges,
            out List<Edge> backwards_edges,
            out List<Edge> forwards_edges)
        {
            face_edges = [];
            backwards_edges = [];
            forwards_edges = [];

            for (int i = 0; i < face_verts.Length; i++)
            {
                Vert vert = face_verts[i];
                int next_i = (i + 1) % face_verts.Length;
                Vert next_vert = face_verts[next_i];

                // we expect to see each edge forwards only once
                Util.Assert(!made_edges.ContainsKey((vert, next_vert)));

                Edge edge;
                if (made_edges.TryGetValue((next_vert, vert), out edge))
                {
                    face_edges.Add(edge);
                    backwards_edges.Add(edge);
                }
                else
                {
                    EIdx e_idx = new(next_e_idx++);
                    edge = new(vert, next_vert);

                    edges[e_idx] = edge;
                    face_edges.Add(edge);
                    forwards_edges.Add(edge);

                    // it's a new edge, so let the two verts know
                    edge.Start.Edges.Add(edge);
                    edge.End.Edges.Add(edge);

                    made_edges[(vert, next_vert)] = edge;
                }
            }
        }

        public static void ApplyEdgeSharpness(VertCube vc, Dictionary<(Vert start, Vert end), Edge> made_edges)
        {
            foreach (Cube.EdgeName e_name in EdgeNameUtils.AllEdges)
            {
                // surely this can be made easier???
                (Cube.VertName v1_name, Cube.VertName v2_name) = EdgeNameUtils.GetVertsForEdge(e_name);

                Vert v1 = vc.VertMap[v1_name];
                Vert v2 = vc.VertMap[v2_name];

                if (!made_edges.TryGetValue((v1, v2), out Edge edge))
                {
                    // one of these two *must* succeed
                    made_edges.TryGetValue((v2, v1), out edge);
                }

                if (vc.Cube.IsEdgeSharp[e_name])
                {
                    edge.IsSetSharp = true;
                }

                string edge_tag = vc.Cube.EdgeTag[e_name];
                if (!string.IsNullOrEmpty(edge_tag))
                {
                    edge.Tag = edge_tag;
                }
            }
        }
    }
}