using System;
using Xunit;

namespace Ibasa.Ripple.Tests
{
    public class Base58Tests
    {

        [Theory]
        [InlineData("")]
        [InlineData("rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz")]
        public void TestRoundTrip(string data)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
            var base58 = Base58.ConvertTo(bytes);
            Array.Clear(bytes, 0, bytes.Length);
            Base58.ConvertFrom(base58, bytes);
            var result = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.Equal(data, result);
        }

        [Theory]
        [InlineData("r4nmanwKSE6GpkTCrBjz8uanrGZabbpSfp")]
        public void TestAccount(string base58)
        {
            var bytes = new byte[21];
            Base58Check.ConvertFrom(base58, bytes);
            Assert.Equal(base58, Base58Check.ConvertTo(bytes));
        }
    }
}