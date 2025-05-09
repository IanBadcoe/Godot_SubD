using System;
using System.Diagnostics;

namespace SubD
{
    [DebuggerDisplay("{Value}")]
    public struct Idx<IdxType> : IComparable<Idx<IdxType>>
    {
        public int Value
        {
            get;
            private set;
        }

        public Idx(int value)
        {
            Value = value;
        }

        public static bool operator==(Idx<IdxType> lhs, Idx<IdxType> rhs)
        {
            return lhs.Value == rhs.Value;
        }

        public static bool operator!=(Idx<IdxType> lhs, Idx<IdxType> rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            Idx<IdxType>? rhs = obj as Idx<IdxType>?;

            return rhs.HasValue ? this == rhs : false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Value.GetHashCode());
        }

        // IComparable
        public int CompareTo(Idx<IdxType> other)
        {
            return Value - other.Value;
        }

        // just so we can print something in the stupid debugger
        public static string Idx2String(Idx<IdxType>? idx)
        {
            if (!idx.HasValue)
            {
                return "null";
            }

            // :-D
            return idx.Value.Value.ToString();
        }

        public readonly bool HasValue => Value != -1;

        public readonly static Idx<IdxType> Empty = new(-1);
    }
}