using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SubD
{
    // only a rather noddy implementation with just the few features I need
    [DebuggerDisplay("{Forwards.Count} {ReverseInner.Count}")]
    public class BidirectionalDictionary<T1, T2> : IEnumerable<KeyValuePair<T1, T2>>
    {
        Dictionary<T1, T2> Forwards = new Dictionary<T1, T2>();

        Dictionary<T2, T1> ReverseInner = new Dictionary<T2, T1>();

        // cannot implement two x IEnumerable ifaces on the same class, so get reverse iteration from this
        public IEnumerable<KeyValuePair<T2, T1>> Reverse
        {
            get => ReverseInner;
        }

        public T1 this [T2 idx]
        {
            get => ReverseInner[idx];
            set => Add(value, idx);
        }

        public T2 this [T1 idx]
        {
            get => Forwards[idx];
            set => Add(idx, value);
        }

        public int Count
        {
            get => Forwards.Count;
        }

        private void Add(T1 t1, T2 t2)
        {
            Forwards[t1] = t2;
            ReverseInner[t2] = t1;
        }

        public T2 Remove(T1 t1)
        {
            T2 t2 = Forwards[t1];
            Forwards.Remove(t1);
            ReverseInner.Remove(t2);

            return t2;
        }

        public T1 Remove(T2 t2)
        {
            T1 t1 = ReverseInner[t2];
            ReverseInner.Remove(t2);
            Forwards.Remove(t1);

            return t1;
        }

        public bool Contains(T1 key)
        {
            return Forwards.ContainsKey(key);
        }

        public bool Contains(T2 key)
        {
            return ReverseInner.ContainsKey(key);
        }

        public IEnumerable<T1> Keys
        {
            get => Forwards.Keys;
        }

        public IEnumerable<T2> Values
        {
            get => Forwards.Values;
        }

        public IEnumerator<KeyValuePair<T1, T2>> GetEnumerator() => Forwards.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Forwards.GetEnumerator();
    }
}