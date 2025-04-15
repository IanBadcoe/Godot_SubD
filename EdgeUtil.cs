using Godot;

namespace SubD
{
    public static class EdgeUtils
    {
        public static float EdgeVertDistance(Vector3 e_start, Vector3 e_end, Vector3 v)
        {
            Vector3 edge_dir = (e_end - e_start).Normalized();

            float projection_length = (v - e_start).Dot(edge_dir);

            Vector3 closest_point = e_start + edge_dir * projection_length;

            return (v - closest_point).Length();
        }
    }
}