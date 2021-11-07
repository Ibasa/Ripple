using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Ibasa.Ripple.Tests
{
    public sealed class FaucetTests
    {        
        JsonRpcApi CreateApi()
        {
            var address = new Uri("https://s.altnet.rippletest.net:51234");
            var httpClient = new HttpClient();
            httpClient.BaseAddress = address;
            return new JsonRpcApi(httpClient);
        }

        [Fact]
        public async Task TestGenerate()
        {
            var api = CreateApi();
            var client = new FaucetClient();
            var seed = await client.Generate();

            seed.GetKeyPairs(out var _, out var keyPair);
            var publicKey = keyPair.PublicKey;
            var account = AccountId.FromPublicKey(publicKey.GetCanoncialBytes());
            var info = await Utils.WaitForAccount(api, account);

            Assert.Equal(info.AccountData.Account, account);
            Assert.True(info.AccountData.Balance.Drops > 0, "Balance is positive");
        }

        [Fact]
        public async Task TestRequestFunding()
        {
            var api = CreateApi();
            var client = new FaucetClient();

            var seed = Seed.Create(KeyType.Secp256k1);
            seed.GetKeyPairs(out var _, out var keyPair);
            var publicKey = keyPair.PublicKey;
            var account = AccountId.FromPublicKey(publicKey.GetCanoncialBytes());
            await client.RequestFunding(account);

            var info = await Utils.WaitForAccount(api, account);

            Assert.Equal(info.AccountData.Account, account);
            Assert.True(info.AccountData.Balance.Drops > 0, "Balance is positive");
        }
    }
}