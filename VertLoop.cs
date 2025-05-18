
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace SubD
{
    using VIdx = Idx<Vert>;
    using CylTypes;

    public class VertLoop
    {
        public Vert[] Verts
        {
            get;
            private set;
        }

        public Topology Topology
        {
            get;
            private set;
        }

        public VertLoop(IEnumerable<Vert> verts, Topology topology)
        {
            Verts = [.. verts];
            Topology = topology;
        }

        public VertLoop Reversed()
        {
            return new VertLoop(Verts.Reverse(), Topology);
        }

        public VertLoop Shift(int step)
        {
            step = (step + Verts.Length) % Verts.Length;
            return new VertLoop(Verts.Skip(step).Concat(Verts.Take(step)), Topology);
        }
    }
}