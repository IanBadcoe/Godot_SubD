using System.Collections.Generic;
using Godot;
using System.Linq;

namespace SubD
{
    internal static class Util
    {
        internal static Vector3 Sum(this IEnumerable<Vector3> that)
        {
            return that.Aggregate(Vector3.Zero, (x, y) => x + y);
        }
    }
}