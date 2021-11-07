using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Ibasa.Ripple.Tests
{
    public class WebSocketApiTestsSetup : ApiTestsSetup<WebSocketApi>
    {
        protected override WebSocketApi CreateApi()
        {
            var address = new Uri("wss://s.altnet.rippletest.net:51233");
            var clientWebSocket = new ClientWebSocket();
            clientWebSocket.ConnectAsync(address, CancellationToken.None).Wait();
            return new WebSocketApi(clientWebSocket);
        }
    }

    [Collection("WebSocket")]
    public class WebSocketApiTests : ApiTests<WebSocketApi>, IClassFixture<WebSocketApiTestsSetup>
    {
        public WebSocketApiTests(WebSocketApiTestsSetup setup) : base(setup)
        {
        }

        [Fact]
        public async Task TestGetPath_Event()
        {
            var accounts = await Task.WhenAll(TestAccount.Create(), TestAccount.Create(), TestAccount.Create(), TestAccount.Create());
            await Utils.WaitForAccounts(Api, accounts);
            var gbp = new CurrencyCode("GBP");

            var A1 = accounts[0];
            var A2 = accounts[1];
            var G3 = accounts[2];
            var M1 = accounts[3];

            // Start listening for updates
            var tcs = new TaskCompletionSource<PathFindResponse>();
            var expectedId = new TaskCompletionSource<uint>();
            Api.OnPathFind += (api, id, response) =>
            {
                Assert.Equal(expectedId.Task.Result, id);
                Assert.Same(Api, api);
                if (response.Alternatives.Count != 0)
                {
                    tcs.TrySetResult(response);
                }
            };

            // Ask for a path to send XRP to A2 from A1 via GBP
            var pathFindRequest = new PathFindRequest();
            pathFindRequest.SourceAccount = A1.Address;
            pathFindRequest.DestinationAccount = A2.Address;
            pathFindRequest.DestinationAmount = XrpAmount.FromXrp(10m);
            pathFindRequest.SourceCurrencies = new[] { new CurrencyType(gbp) };
            var (requestId, pathFindResponse) = await Api.PathFind(pathFindRequest);
            expectedId.SetResult(requestId);

            // Should be empty
            Assert.Equal(pathFindRequest.SourceAccount, pathFindResponse.SourceAccount);
            Assert.Equal(pathFindRequest.DestinationAccount, pathFindResponse.DestinationAccount);
            Assert.Equal(pathFindRequest.DestinationAmount, pathFindResponse.DestinationAmount);
            Assert.Empty(pathFindResponse.Alternatives);

            // Set up trust lines, payments and offers
            var accountSet = new AccountSetTransaction();
            accountSet.Account = G3.Address;
            accountSet.SetFlag = AccountSetFlags.DefaultRipple;
            var (_, _) = await SubmitTransaction(G3.Secret, accountSet);

            var trustSet = new TrustSetTransaction();
            trustSet.LimitAmount = new IssuedAmount(G3.Address, gbp, new Currency(100m));
            trustSet.Account = A1.Address;
            var (_, _) = await SubmitTransaction(A1.Secret, trustSet);
            trustSet.Account = A2.Address;
            var (_, _) = await SubmitTransaction(A2.Secret, trustSet);
            trustSet.LimitAmount = new IssuedAmount(G3.Address, gbp, new Currency(1000m));
            trustSet.Account = M1.Address;
            var (_, _) = await SubmitTransaction(M1.Secret, trustSet);

            var payment = new PaymentTransaction();
            payment.Account = G3.Address;
            payment.Amount = new IssuedAmount(G3.Address, gbp, new Currency(50m));
            payment.Destination = A1.Address;
            var (_, _) = await SubmitTransaction(G3.Secret, payment);
            payment.Destination = A2.Address;
            var (_, _) = await SubmitTransaction(G3.Secret, payment);
            payment.Amount = new IssuedAmount(G3.Address, gbp, new Currency(100m));
            payment.Destination = M1.Address;
            var (_, _) = await SubmitTransaction(G3.Secret, payment);

            var offerCreate = new OfferCreateTransaction();
            offerCreate.Account = M1.Address;
            offerCreate.TakerPays = new IssuedAmount(G3.Address, gbp, new Currency(1m));
            offerCreate.TakerGets = XrpAmount.FromXrp(10m);
            var (_, _) = await SubmitTransaction(M1.Secret, offerCreate);

            // Assert for the path that should eventually be found
            void AssertPath(PathFindResponse pathFindResponse)
            {
                Assert.Equal(pathFindRequest.SourceAccount, pathFindResponse.SourceAccount);
                Assert.Equal(pathFindRequest.DestinationAccount, pathFindResponse.DestinationAccount);
                Assert.Equal(pathFindRequest.DestinationAmount, pathFindResponse.DestinationAmount);
                var alternative = Assert.Single(pathFindResponse.Alternatives);
                Assert.Equal(new IssuedAmount(A1.Address, gbp, new Currency(1m)), alternative.SourceAmount);
                var path = Assert.Single(alternative.PathsComputed);
                Assert.Collection(path,
                    item => Assert.Equal(G3.Address, item.Account),
                    item => Assert.Equal(CurrencyCode.XRP, item.Currency));
            }

            // Wait for the path to be found
            pathFindResponse = tcs.Task.Result;
            AssertPath(pathFindResponse);

            // Ask for the path explictly 
            pathFindResponse = await Api.PathFindStatus();
            AssertPath(pathFindResponse);

            // ... and try and make a payment with that path
            payment = new PaymentTransaction();
            payment.Account = pathFindRequest.SourceAccount;
            payment.Destination = pathFindRequest.DestinationAccount;
            payment.Amount = pathFindRequest.DestinationAmount;
            var alternative = Assert.Single(pathFindResponse.Alternatives);
            payment.SendMax = alternative.SourceAmount;
            payment.Paths = alternative.PathsComputed;
            var (_, _) = await SubmitTransaction(A1.Secret, payment);

            // Stop listening for the path
            await Api.PathFindClose();
        }
    }
}
