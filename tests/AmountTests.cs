using System;
using System.Collections.Generic;
using Xunit;

namespace Ibasa.Ripple.Tests
{
    public class XrpAmountTests
    {
        [Theory]
        [InlineData(0L, "0 XRP")]
        [InlineData(1L, "0.000001 XRP")]
        [InlineData(1_000L, "0.001 XRP")]
        [InlineData(1_000_000L, "1 XRP")]
        [InlineData(1_000_000_000L, "1000 XRP")]
        [InlineData(1_000_000_000_000L, "1000000 XRP")]
        [InlineData(100_000_000_000_000_000L, "100000000000 XRP")]
        [InlineData(4_611_686_018_427_387_903L, "4611686018427.387903 XRP")]
        public void TestToString(long drops, string expected)
        {
            var amount = XrpAmount.FromDrops(drops);
            Assert.Equal(expected, amount.ToString());
        }

        [Theory]
        [InlineData(4_611_686_018_427_387_904L)]
        [InlineData(-4_611_686_018_427_387_904L)]
        public void TestOverflow(long drops)
        {
            var exc = Assert.Throws<ArgumentOutOfRangeException>(() => 
                XrpAmount.FromDrops(drops));
            Assert.Contains("absolute value of drops must be less than or equal to 4611686018427387903", exc.Message);
        }
    }

    public class IssuedAmountTests
    {
        [Theory]
        [InlineData("r4nmanwKSE6GpkTCrBjz8uanrGZabbpSfp", "GBP", "100", "100 GBP(r4nmanwKSE6GpkTCrBjz8uanrGZabbpSfp)")]
        public void TestToString(string issuer, string currencyCode, string value, string expected)
        {
            var amount = new IssuedAmount(new AccountId(issuer), new CurrencyCode(currencyCode), Currency.Parse(value));
            Assert.Equal(expected, amount.ToString());
        }

        [Fact]
        public void TestXRP()
        {
            var exc = Assert.Throws<ArgumentException>("currencyCode", () => new IssuedAmount(default, CurrencyCode.XRP, default));
            Assert.Equal("Can not be XRP (Parameter 'currencyCode')", exc.Message);
        }
    }

    public class AmountTests
    {
        [Theory]
        [InlineData("r4nmanwKSE6GpkTCrBjz8uanrGZabbpSfp", "GBP", "100", "100 GBP(r4nmanwKSE6GpkTCrBjz8uanrGZabbpSfp)")]
        public void TestToString(string issuer, string currencyCode, string value, string expected)
        {
            var amount = new Amount(new AccountId(issuer), new CurrencyCode(currencyCode), Currency.Parse(value));
            Assert.Equal(expected, amount.ToString());
        }

        [Fact]
        public void TestXRP()
        {
            var exc = Assert.Throws<ArgumentException>("currencyCode", () => new Amount(default, CurrencyCode.XRP, default));
            Assert.Equal("Can not be XRP (Parameter 'currencyCode')", exc.Message);
        }
    }
}
