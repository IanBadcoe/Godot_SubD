using System.Collections.Generic;
using Godot;
using System.Linq;
using System;
using System.Diagnostics;

namespace SubD
{
    internal static class Util
    {
        internal static Vector3 Sum(this IEnumerable<Vector3> that)
        {
            return that.Aggregate(Vector3.Zero, (x, y) => x + y);
        }

        internal static void ForEach<T>(this IEnumerable<T> that, Action<T> action)
        {
            foreach(T item in that)
            {
                action(item);
            }
        }

[Conditional("DEBUG")]
        internal static void Assert(bool b)
        {
            if (!b)
            {
                throw new InvalidOperationException();
            }
        }
    }
}