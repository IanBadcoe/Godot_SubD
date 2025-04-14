
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
        public VIdx[] VIdxs
        {
            get;
            private set;
        }

        public Topology Topology
        {
            get;
            private set;
        }

        public VertLoop(IEnumerable<VIdx> v_idxs, Topology topology)
        {
            VIdxs = [.. v_idxs];
            Topology = topology;
        }

        internal VertLoop Reversed()
        {
            return new VertLoop(VIdxs.Reverse(), Topology);
        }
    }
}