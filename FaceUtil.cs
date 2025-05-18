using Godot;

using Godot_Util;

namespace SubD
{
    public static class FaceUtil
    {
        public static Vector3 FaceNormal(Vector3[] verts)
        {
            Vector3 last_delta = verts[1] - verts[0];

            Vector3 accum = Vector3.Zero;

            for(int i = 2; i < verts.Length; i++)
            {
                Vector3 delta = verts[i] - verts[0];

                Vector3 cross = delta.Cross(last_delta);

                accum += cross;

                last_delta = delta;
            }

            return accum.Normalized();
        }

        public static Vector3 FaceCentre(Vector3[] verts)
        {
            return verts.Sum() / verts.Length;
        }
    }
}