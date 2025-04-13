
using System;
using System.Collections.Generic;
using System.Linq;

namespace SubD
{
    using VIdx = Idx<Vert>;

    public class VertLoop
    {
        public VIdx[] VIdxs
        {
            get;
            private set;
        }

        public VertLoop(IEnumerable<VIdx> v_idxs)
        {
            VIdxs = [.. v_idxs];
        }

        internal VertLoop Reversed()
        {
            return new VertLoop(VIdxs.Reverse());
        }
    }
}