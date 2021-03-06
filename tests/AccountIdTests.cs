using System;
using System.Collections.Generic;
using Xunit;

namespace Ibasa.Ripple.Tests
{
    public class AccountIdTests
    {
        public static IEnumerable<object[]> accounts
        {
            get
            {
                yield return new object[] { "r4nmanwKSE6GpkTCrBjz8uanrGZabbpSfp" };
                yield return new object[] { "r3kmLJN5D28dHuH8vZNUZpMC43pEHpaocV" };
                yield return new object[] { "rEopG7qc7ZWFMvCSH3GYeJJ7GPAQnKmxgw" };
                yield return new object[] { "rrrrrrrrrrrrrrrrrrrrrhoLvTp" };
            }
        }

        [Theory]
        [MemberData(nameof(accounts))]
        public void TestRoundTrip(string base58)
        {
            var account = new AccountId(base58);
            Assert.Equal(base58, account.ToString());
        }

        [Theory]
        [MemberData(nameof(accounts))]
        public void TestCopy(string base58)
        {
            var account = new AccountId(base58);
            var copy = account;
            Assert.Equal(account.ToString(), copy.ToString());
        }

        [Theory]
        [MemberData(nameof(accounts))]
        public void TestBoxing(string base58)
        {
            var account = new AccountId(base58);
            var copy = (object)account;
            Assert.Equal(account.ToString(), copy.ToString());
        }

        [Theory]
        [InlineData("0330E7FC9D56BB25D6893BA3F317AE5BCF33B3291BD63DB32654A313222F7FD020", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh")]
        [InlineData("02565C453B8D74C194379C39B2B2AB68E7EFA203815248AE8769C1AD5AE10048E1", "rLVTBQ4pSQcj5rouKERrEwan1SRvC1grXH")]
        public void TestFromPublicKey(string publicKey, string expected)
        {
            var bytes = new byte[33];
            Base16.DecodeFromUtf8(System.Text.Encoding.UTF8.GetBytes(publicKey), bytes, out var _, out var _);
            var account = AccountId.FromPublicKey(bytes);
            Assert.Equal(expected, account.ToString());
        }

        [Theory]
        [InlineData(19)]
        [InlineData(21)]
        public void TestFromInvalidBytes(int length)
        {
            var bytes = new byte[length];
            var exc = Assert.Throws<ArgumentException>(() =>
            {
                new AccountId(bytes);
            });
            Assert.Equal("Expected exactly 20 bytes (Parameter 'bytes')", exc.Message);
        }

        [Theory]
        [InlineData("sQJm86", "Expected exactly 21 bytes")]
        [InlineData("rW6hb6", "Expected exactly 21 bytes")]
        [InlineData("QLbzfJH5BT1FS9apRLKV3G8dWEAjwnKaa", "Expected 0x0 prefix byte")]
        [InlineData("rrrrrrrrrrrrrrrrrrrrfKh8zc", "Expected exactly 21 bytes")]
        public void TestFromInvalidBase58(string base58, string message)
        {
            var exc = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                new AccountId(base58);
            });
            var expected =
                message +
                " (Parameter 'base58')" +
                Environment.NewLine +
                "Actual value was " +
                base58 +
                ".";
            Assert.Equal(expected, exc.Message);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(1, 0, 1)]
        [InlineData(0, 1, -1)]
        [InlineData(256, 255, 1)]
        [InlineData(255, 256, -1)]
        public void TestCompareTo(ulong x, ulong y, int expected)
        {
            var bytes = new byte[20];

            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes, x);
            var ax = new AccountId(bytes);

            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes, y);
            var ay = new AccountId(bytes);

            Assert.Equal(expected, ax.CompareTo(ay));
        }
    }
}
