using System;
using System.Diagnostics;
using Godot;

namespace SubD
{
    [DebuggerDisplay("Position = {Position}")]
    public class Vert
    {
        public Vector3 Position
        {
            get;
            private set;
        }

        public Vert(Vector3 pos)
        {
            Position = pos;
        }

        public static bool operator==(Vert lhs, Vert rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (ReferenceEquals(lhs, null) || ReferenceEquals(rhs, null))
            {
                return false;
            }

            return lhs.Position == rhs.Position;
        }

        public static bool operator!=(Vert lhs, Vert rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            Vert vert = obj as Vert;

            return vert == this;
        }

        public override int GetHashCode()
        {
            return Position.GetHashCode();
        }
    }
}