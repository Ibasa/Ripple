﻿using System;
using System.Diagnostics;

namespace Ibasa.Ripple
{
    public readonly struct Hash128 : IEquatable<Hash128>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        readonly long a;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        readonly long b;

        public Hash128(string hex)
        {
            a = long.Parse(hex.Substring(0, 16), System.Globalization.NumberStyles.HexNumber);
            b = long.Parse(hex.Substring(16, 16), System.Globalization.NumberStyles.HexNumber);
        }

        public Hash128(ReadOnlySpan<byte> bytes)
        {
            a = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(0, 8));
            b = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(8, 8));
        }

        public void CopyTo(Span<byte> destination)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination.Slice(0, 8), a);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination.Slice(8, 8), b);
        }

        public override string ToString()
        {
            return String.Format("{0,16:X}{1,16:X}", a, b).Replace(' ', '0');
        }

        public override bool Equals(object obj)
        {
            if (obj is Hash128)
            {
                return Equals((Hash128)obj);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(a, b);
        }

        public bool Equals(Hash128 other)
        {
            return a == other.a && b == other.b;
        }

        /// <summary>
        /// Returns a value that indicates whether two Hash128 values are equal.
        /// </summary>
        /// <param name="c1">The first value to compare.</param>
        /// <param name="c2">The second value to compare.</param>
        /// <returns>true if c1 and c2 are equal; otherwise, false.</returns>
        public static bool operator ==(Hash128 c1, Hash128 c2)
        {
            return c1.Equals(c2);
        }

        /// <summary>
        /// Returns a value that indicates whether two Hash128 objects have different values.
        /// </summary>
        /// <param name="c1">The first value to compare.</param>
        /// <param name="c2">The second value to compare.</param>
        /// <returns>true if c1 and c2 are not equal; otherwise, false.</returns>
        public static bool operator !=(Hash128 c1, Hash128 c2)
        {
            return !c1.Equals(c2);
        }
    }
    public readonly struct Hash160 : IEquatable<Hash160>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        readonly long a;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        readonly long b;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        readonly int c;

        public Hash160(string hex)
        {
            a = long.Parse(hex.Substring(0, 16), System.Globalization.NumberStyles.HexNumber);
            b = long.Parse(hex.Substring(16, 16), System.Globalization.NumberStyles.HexNumber);
            c = int.Parse(hex.Substring(32, 8), System.Globalization.NumberStyles.HexNumber);
        }

        public Hash160(ReadOnlySpan<byte> bytes)
        {
            a = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(0, 8));
            b = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(8, 8));
            c = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        }

        public void CopyTo(Span<byte> destination)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination.Slice(0, 8), a);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination.Slice(8, 8), b);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(destination.Slice(16, 4), c);
        }

        public override string ToString()
        {
            return String.Format("{0,16:X}{1,16:X}{2,8:X}", a, b, c).Replace(' ', '0');
        }

        public override bool Equals(object obj)
        {
            if (obj is Hash160)
            {
                return Equals((Hash160)obj);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(a, b, c);
        }

        public bool Equals(Hash160 other)
        {
            return a == other.a && b == other.b && c == other.c;
        }

        /// <summary>
        /// Returns a value that indicates whether two Hash160 values are equal.
        /// </summary>
        /// <param name="c1">The first value to compare.</param>
        /// <param name="c2">The second value to compare.</param>
        /// <returns>true if c1 and c2 are equal; otherwise, false.</returns>
        public static bool operator ==(Hash160 c1, Hash160 c2)
        {
            return c1.Equals(c2);
        }

        /// <summary>
        /// Returns a value that indicates whether two Hash160 objects have different values.
        /// </summary>
        /// <param name="c1">The first value to compare.</param>
        /// <param name="c2">The second value to compare.</param>
        /// <returns>true if c1 and c2 are not equal; otherwise, false.</returns>
        public static bool operator !=(Hash160 c1, Hash160 c2)
        {
            return !c1.Equals(c2);
        }
    }

    public readonly struct Hash256 : IEquatable<Hash256>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        readonly long a;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        readonly long b;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        readonly long c;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        readonly long d;

        public Hash256(string hex)
        {
            a = long.Parse(hex.Substring(0, 16), System.Globalization.NumberStyles.HexNumber);
            b = long.Parse(hex.Substring(16, 16), System.Globalization.NumberStyles.HexNumber);
            c = long.Parse(hex.Substring(32, 16), System.Globalization.NumberStyles.HexNumber);
            d = long.Parse(hex.Substring(48, 16), System.Globalization.NumberStyles.HexNumber);
        }

        public Hash256(ReadOnlySpan<byte> bytes)
        {
            a = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(0, 8));
            b = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(8, 8));
            c = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(16, 8));
            d = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(24, 8));
        }

        public void CopyTo(Span<byte> destination)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination.Slice(0, 8), a);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination.Slice(8, 8), b);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination.Slice(16, 8), c);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination.Slice(24, 8), d);
        }

        public override string ToString()
        {
            return string.Format("{0,16:X}{1,16:X}{2,16:X}{3,16:X}", a, b, c, d).Replace(' ', '0');
        }

        public override bool Equals(object obj)
        {
            if (obj is Hash256)
            {
                return Equals((Hash256)obj);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(a, b, c, d);
        }

        public bool Equals(Hash256 other)
        {
            return a == other.a && b == other.b && c == other.c && d == other.d;
        }

        /// <summary>
        /// Returns a value that indicates whether two Hash256 values are equal.
        /// </summary>
        /// <param name="c1">The first value to compare.</param>
        /// <param name="c2">The second value to compare.</param>
        /// <returns>true if c1 and c2 are equal; otherwise, false.</returns>
        public static bool operator ==(Hash256 c1, Hash256 c2)
        {
            return c1.Equals(c2);
        }

        /// <summary>
        /// Returns a value that indicates whether two Hash256 objects have different values.
        /// </summary>
        /// <param name="c1">The first value to compare.</param>
        /// <param name="c2">The second value to compare.</param>
        /// <returns>true if c1 and c2 are not equal; otherwise, false.</returns>
        public static bool operator !=(Hash256 c1, Hash256 c2)
        {
            return !c1.Equals(c2);
        }
    }
}