using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ibasa.Ripple
{
    public sealed class AccountIdConverter : JsonConverter<AccountId>
    {
        public override AccountId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var str = reader.GetString();
            if (str == null) return default;
            return new AccountId(str);
        }

        public override void Write(Utf8JsonWriter writer, AccountId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    [JsonConverter(typeof(AccountIdConverter))]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Size = 20)]
    public struct AccountId : IEquatable<AccountId>, IComparable<AccountId>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private uint _data0;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private uint _data1;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private uint _data2;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private uint _data3;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private uint _data4;

        /// <summary>
        /// The issuer listed in the balance fields is rrrrrrrrrrrrrrrrrrrrBZbvji, which is referred to as ACCOUNT_ONE and is the encoding that corresponds to the numerical value 1.
        /// This convention is used because the addresses on the trustline are already specified in the HighLimit and LowLimit objects, so specifying them here would be redundant.
        /// </summary>
        public static AccountId AccountOne => new AccountId("rrrrrrrrrrrrrrrrrrrrBZbvji");

        private static Span<byte> UnsafeAsSpan(ref AccountId account)
        {
            return System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref account._data0, 5));
        }

        public AccountId(string base58) : this()
        {
            Span<byte> content = stackalloc byte[21];
            var count = Base58Check.ConvertFrom(base58, content);
            if (count != 21)
            {
                throw new ArgumentOutOfRangeException("base58", base58, "Expected exactly 21 bytes");
            }
            if (content[0] != 0x0)
            {
                throw new ArgumentOutOfRangeException("base58", base58, "Expected 0x0 prefix byte");
            }
            content.Slice(1).CopyTo(UnsafeAsSpan(ref this));
        }

        public AccountId(ReadOnlySpan<byte> bytes) : this()
        {
            if (bytes.Length != 20)
            {
                throw new ArgumentException("Expected exactly 20 bytes", "bytes");
            }
            bytes.CopyTo(UnsafeAsSpan(ref this));
        }

        public static AccountId FromPublicKey(ReadOnlySpan<byte> publicKey)
        {
            var shaHash = new byte[32];
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var done = sha256.TryComputeHash(publicKey, shaHash, out var bytesWritten);
                if (!done || bytesWritten != 32)
                {
                    throw new Exception("Unexpected failure of SHA256");
                }
            }
            var ripe = new Org.BouncyCastle.Crypto.Digests.RipeMD160Digest();
            ripe.BlockUpdate(shaHash, 0, 32);
            var ripeHash = new byte[20];
            var count = ripe.DoFinal(ripeHash, 0);
            if (count != 20)
            {
                throw new Exception("Unexpected failure of RipeMD160");
            }
            return new AccountId(ripeHash);
        }

        public override string ToString()
        {
            Span<byte> content = stackalloc byte[21];
            content[0] = 0x0;
            UnsafeAsSpan(ref this).CopyTo(content.Slice(1));
            return Base58Check.ConvertTo(content);
        }

        public void CopyTo(Span<byte> destination)
        {
            UnsafeAsSpan(ref this).CopyTo(destination);
        }

        public bool Equals(AccountId other)
        {
            var a = UnsafeAsSpan(ref this);
            var b = UnsafeAsSpan(ref other);
            for (int i = 0; i < 20; ++i)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        public override int GetHashCode()
        {
            var hash = new System.HashCode();
            foreach (var b in UnsafeAsSpan(ref this))
            {
                hash.Add(b);
            }
            return hash.ToHashCode();
        }

        public override bool Equals(object other)
        {
            if (other is AccountId)
            {
                return Equals((AccountId)other);
            }
            return false;
        }

        public int CompareTo(AccountId other)
        {
            var us = UnsafeAsSpan(ref this);
            var them = UnsafeAsSpan(ref other);
            for(int i = 0; i < 20; ++i)
            {
                if (us[i] < them[i]) { return -1; }
                else if (us[i] > them[i]) { return 1; }
            }
            return 0;
        }

        /// <summary>
        /// Returns a value that indicates whether two AccountId values are equal.
        /// </summary>
        /// <param name="c1">The first value to compare.</param>
        /// <param name="c2">The second value to compare.</param>
        /// <returns>true if c1 and c2 are equal; otherwise, false.</returns>
        public static bool operator ==(AccountId c1, AccountId c2)
        {
            return c1.Equals(c2);
        }

        /// <summary>
        /// Returns a value that indicates whether two AccountId objects have different values.
        /// </summary>
        /// <param name="c1">The first value to compare.</param>
        /// <param name="c2">The second value to compare.</param>
        /// <returns>true if c1 and c2 are not equal; otherwise, false.</returns>
        public static bool operator !=(AccountId c1, AccountId c2)
        {
            return !c1.Equals(c2);
        }
    }
}
