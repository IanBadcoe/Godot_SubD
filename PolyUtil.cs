using System.Collections.Generic;
using Godot;

namespace SubD
{
    public static class PolyUtil
    {
        public static Vector3 PolyNormal(Vector3[] verts)
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

        public static Vector3 PolyCentre(Vector3[] verts)
        {
            return verts.Sum() / verts.Length;
        }
    }
}