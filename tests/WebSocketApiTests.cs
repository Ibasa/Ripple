﻿using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Ibasa.Ripple.Tests
{
    public struct TestAccount
    {
        public readonly string Address;
        public readonly string Secret;
        public readonly ulong Amount;

        public TestAccount(string address, string secret, ulong amount)
        {
            Address = address;
            Secret = secret;
            Amount = amount;
        }
    }

    public class WebSocketClientSetup : IDisposable
    {
        static readonly HttpClient HttpClient = new HttpClient();

        public readonly ClientWebSocket WebSocket;
        public readonly WebSocketApi SocketApi;

        public readonly TestAccount TestAccountOne;
        public readonly TestAccount TestAccountTwo;

        public WebSocketClientSetup()
        {
            var address = new Uri("wss://s.altnet.rippletest.net:51233");
            WebSocket = new ClientWebSocket();
            WebSocket.ConnectAsync(address, CancellationToken.None).Wait();
            SocketApi = new WebSocketApi(WebSocket);

            TestAccountOne = CreateAccount();
            TestAccountTwo = CreateAccount();
        }

        TestAccount CreateAccount()
        {
            var response = HttpClient.PostAsync("https://faucet.altnet.rippletest.net/accounts", null).Result;
            var json = response.Content.ReadAsStringAsync().Result;
            var document = System.Text.Json.JsonDocument.Parse(json);
            return new TestAccount(
                document.RootElement.GetProperty("account").GetProperty("address").GetString(),
                document.RootElement.GetProperty("account").GetProperty("secret").GetString(),
                document.RootElement.GetProperty("balance").GetUInt64() * 1000000UL);
        }

        public void Dispose()
        {
            WebSocket.Dispose();
        }
    }

    [Collection("WebSocket")]
    public class WebSocketApiTests : IClassFixture<WebSocketClientSetup>
    {
        WebSocketClientSetup fixture;

        public WebSocketApiTests(WebSocketClientSetup fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public async Task TestPing()
        {
            await fixture.SocketApi.Ping();
        }

        [Fact]
        public async Task TestRandom()
        {
            var random = await fixture.SocketApi.Random();
            Assert.NotEqual(default, random);
        }

        [Fact]
        public async Task TestAccount()
        {
            var account = new AccountID(fixture.TestAccountOne.Address);

            var request = new AccountInfoRequest()
            {
                Account = account,
            };
            var response = await fixture.SocketApi.AccountInfo(request);
            Assert.Equal(account, response.AccountData.Account);
            Assert.Equal(fixture.TestAccountOne.Amount, response.AccountData.Balance);
        }
    }
}
