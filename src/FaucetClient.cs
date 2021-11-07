using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Ibasa.Ripple
{
    /// <summary>
    /// Interface to the XRPL testnet faucet
    /// </summary>
    public sealed class FaucetClient : IDisposable
    {
        private HttpClient httpClient;

        public FaucetClient()
            : this(new Uri("https://faucet.altnet.rippletest.net"))
        {
        }

        public FaucetClient(Uri uri)
        {
            httpClient = new HttpClient();
            httpClient.BaseAddress = uri;
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }

        public async Task RequestFunding(AccountId accountId)
        {
            var content = new StringContent(
                string.Format("{{\"destination\": \"{0}\"}}", accountId),
                System.Text.Encoding.UTF8,
                "application/json");
            var response = await httpClient.PostAsync("/accounts", content);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var document = System.Text.Json.JsonDocument.Parse(json);
            return;
            //var account = document.RootElement.GetProperty("account");

          //  return new TestAccount(
          //      new AccountId(account.GetProperty("address").GetString()),
          //      new Seed(account.GetProperty("secret").GetString()));
        }

        public async Task<Seed> Generate()
        {
            var response = await httpClient.PostAsync("/accounts", null);
            var json = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            var document = System.Text.Json.JsonDocument.Parse(json);
            // ValueKind = Object : "{"account":{"xAddress":"T7j5GjC8KFZ3jjbX2XsGzSj8ci4MVgUPEDHZKZpta1ywP9y","secret":"shLrJiQniUKVSYgG7YQ6BsGeq9c65","classicAddress":"rfLMWnokuVeXXFAJG7vs1XuTG8992n59mq","address":"rfLMWnokuVeXXFAJG7vs1XuTG8992n59mq"},"amount":1000,"balance":1000}"
            var account = document.RootElement.GetProperty("account");
            return new Seed(account.GetProperty("secret").GetString());
        }
    }
}
