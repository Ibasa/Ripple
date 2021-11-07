using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Xunit;

namespace Ibasa.Ripple.Tests
{
    public struct TestAccount
    {
        static readonly FaucetClient FaucetClient = new FaucetClient();

        public readonly AccountId Address;
        public readonly Seed Secret;

        private TestAccount(AccountId address, Seed secret)
        {
            Address = address;
            Secret = secret;
        }

        public static TestAccount FromSeed(Seed secret)
        {
            secret.GetKeyPairs(out var _, out var keyPair);
            var address = AccountId.FromPublicKey(keyPair.PublicKey.GetCanoncialBytes());
            return new TestAccount(address, secret);
        }

        public static async Task<TestAccount> Create()
        {
            var response = await FaucetClient.Generate();
            return FromSeed(response);
        }
    }

    public abstract class ApiTestsSetup<T> : IDisposable where T : Api
    {
        public readonly TestAccount TestAccountOne;
        public readonly TestAccount TestAccountTwo;

        public readonly T Api;

        protected abstract T CreateApi();

        public ApiTestsSetup()
        {
            TestAccountOne = TestAccount.Create().Result;
            TestAccountTwo = TestAccount.Create().Result;
            Api = CreateApi();

            // Wait for the two accounts from setup to exists
            Utils.WaitForAccount(Api, TestAccountOne.Address).Wait();
            Utils.WaitForAccount(Api, TestAccountOne.Address).Wait();
        }

        public void Dispose()
        {
            Api.DisposeAsync().AsTask().Wait();
        }
    }

    public sealed class SubmitException : Exception
    {
        public readonly string _message;
        public readonly SubmitResponse Response;

        public SubmitException(string message, SubmitResponse response)
        {
            _message = message;
            Response = response;
        }

        public override string Message => string.Format("{0}: {1}", _message, Response);
    }


    public abstract class ApiTests<T> where T : Api
    {
        protected readonly ApiTestsSetup<T> Setup;
        protected T Api { get { return Setup.Api; } }

        /// <summary>
        /// We always submit a single transaction and then wait for it to be validated before continuing with the tests.
        /// This is a little slow (ledgers only validate every 4s or so), but it keeps test logic simple.
        /// </summary>
        private async Task<(SubmitResponse, TransactionResponse)[]> SubmitTransactions(Func<Transaction, Tuple<ReadOnlyMemory<byte>, Hash256>> sign, IEnumerable<Transaction> transactions)
        {
            var submits = new List<(Transaction, SubmitResponse, Hash256)>();
            var minLedger = uint.MaxValue;
            var maxLedger = uint.MinValue;

            while (true)
            {
                var toSubmit = transactions
                    .Where(transaction => !submits.Exists(tuple => tuple.Item1.Equals(transaction)))
                    .ToArray();

                if (toSubmit.Length == 0)
                {
                    break;
                }

                var account = toSubmit[0].Account;
                AccountInfoResponse infoResponse = await Utils.WaitForAccount(Api, account);
                var feeResponse = await Api.Fee();

                var lastLedgerSequence = feeResponse.LedgerCurrentIndex + 8;
                var sequence = infoResponse.AccountData.Sequence;

                minLedger = Math.Min(minLedger, feeResponse.LedgerCurrentIndex);
                maxLedger = Math.Max(maxLedger, lastLedgerSequence);

                foreach (var transaction in transactions)
                {
                    if (transaction.Account != account)
                    {
                        throw new Exception(string.Format("Transaction in submit batch did not match first transactions account"));
                    }

                    transaction.LastLedgerSequence = lastLedgerSequence;
                    // Give each transaction a unique sequence number
                    transaction.Sequence = sequence++;
                    // Some transactions have higher or fixed fee requirements.
                    // Don't overwrite them.
                    if (transaction.Fee.Drops == 0)
                    {
                        transaction.Fee = feeResponse.Drops.MedianFee;
                    }

                    var signature = sign(transaction);

                    var request = new SubmitRequest();
                    request.FailHard = true;
                    request.TxBlob = signature.Item1;
                    var submitResponse = await Api.Submit(request);
                    if (submitResponse.EngineResult == EngineResult.telCAN_NOT_QUEUE)
                    {
                        // Server was too busy to queue, we should try again
                        // Need to reuse this sequence number
                        --sequence;
                    }
                    else if (!submitResponse.Accepted)
                    {
                        throw new SubmitException("Transaction was not accepted", submitResponse);
                    }
                    else
                    {
                        Assert.True(
                            submitResponse.Kept ||
                            submitResponse.Queued ||
                            submitResponse.Applied ||
                            submitResponse.Broadcast);

                        submits.Add((transaction, submitResponse, signature.Item2));
                    }
                }
            }

            var results = new (SubmitResponse, TransactionResponse)[submits.Count];
            for (int i = 0; i < submits.Count; ++i)
            {
                var submit = submits[i];

                var txRequest = new TxRequest()
                {
                    Transaction = submit.Item3,
                    MinLedger = minLedger,
                    MaxLedger = maxLedger,
                };
                while (true)
                {
                    var ledgerCurrent = await Api.LedgerCurrent();
                    var transactionResponse = await Api.Tx(txRequest);
                    Assert.Equal(submit.Item3, transactionResponse.Hash);
                    if (transactionResponse.Validated)
                    {
                        results[i] = (submit.Item2, transactionResponse);
                        break;
                    }

                    if (ledgerCurrent > txRequest.MaxLedger)
                    {
                        throw new SubmitException("Transaction was not validated", submit.Item2);
                    }

                    System.Threading.Thread.Sleep(2000);
                }
            }

            return results;
        }

        protected async Task<(SubmitResponse, TransactionResponse)[]> SubmitTransactions(Seed secret, IEnumerable<Transaction> transactions)
        {
            secret.GetKeyPairs(out var _, out var keyPair);
            return await SubmitTransactions(transaction =>
            {
                var bytes = transaction.Sign(keyPair, out var transactionHash);
                return Tuple.Create(bytes, transactionHash);
            }, transactions);
        }

        protected async Task<(SubmitResponse, TransactionResponse)> SubmitTransaction(Seed secret, Transaction transaction)
        {
            secret.GetKeyPairs(out var _, out var keyPair);
            var results = await SubmitTransactions(transaction =>
            {
                var bytes = transaction.Sign(keyPair, out var transactionHash);
                return Tuple.Create(bytes, transactionHash);
            }, new[] { transaction });
            return results.First();
        }

        protected async Task<(SubmitResponse, TransactionResponse)> SubmitTransaction(Tuple<AccountId, Seed>[] secrets, Transaction transaction)
        {
            var signers =
                secrets.Select(tuple =>
                {
                    tuple.Item2.GetKeyPairs(out var _, out var keyPair);
                    return ValueTuple.Create(tuple.Item1, keyPair);
                }).ToArray();

            var results = await SubmitTransactions(transaction =>
            {
                var bytes = transaction.Sign(signers, out var transactionHash);
                return Tuple.Create(bytes, transactionHash);
            }, new[] { transaction });
            return results.First();
        }

        protected void AssertMemoryEqual(ReadOnlyMemory<byte>? expected, ReadOnlyMemory<byte>? actual)
        {
            Assert.Equal(expected.HasValue, actual.HasValue);
            if (!expected.HasValue) return;

            Assert.Equal(expected.Value.ToArray(), actual.Value.ToArray());
        }

        public ApiTests(ApiTestsSetup<T> setup)
        {
            Setup = setup;
        }

        [Fact]
        public async Task TestPing()
        {
            await Api.Ping();
        }

        [Fact]
        public async Task TestRandom()
        {
            var random = await Api.Random();
            Assert.NotEqual(default, random);
        }

        [Fact]
        public async Task TestFee()
        {
            var response = await Api.Fee();
            Assert.NotEqual(0u, response.LedgerCurrentIndex);
        }

        [Fact]
        public async Task TestLedgerCurrentAndClosed()
        {
            var closed = await Api.LedgerClosed();
            var current = await Api.LedgerCurrent();

            Assert.True(current > closed.LedgerIndex, "current > closed");
            Assert.NotEqual(default, closed.LedgerHash);
        }

        [Fact]
        public async Task TestLedger_Order()
        {
            var request = new LedgerRequest();

            request.Ledger = LedgerSpecification.Validated;
            var validated = await Api.Ledger(request);
            request.Ledger = LedgerSpecification.Closed;
            var closed = await Api.Ledger(request);
            request.Ledger = LedgerSpecification.Current;
            var current = await Api.Ledger(request);

            Assert.True(validated.Validated);
            Assert.True(validated.Closed);

            // Closed might be validated as well
            Assert.True(closed.Closed);

            Assert.False(current.Validated);
            Assert.False(current.Closed);

            // Index increases
            Assert.True(validated.LedgerIndex <= closed.LedgerIndex);
            Assert.True(closed.LedgerIndex < current.LedgerIndex);

            // Time goes foward
            Assert.True(validated.Ledger.CloseTime <= closed.Ledger.CloseTime);

            // Coins go down
            Assert.True(validated.Ledger.TotalCoins >= closed.Ledger.TotalCoins);
        }

        [Fact]
        public async Task TestLedger_Transactions()
        {
            var request = new LedgerRequest();
            request.Ledger = LedgerSpecification.Validated;
            request.Transactions = true;
            var response = await Api.Ledger(request);

            Assert.NotNull(response.Transactions);
            foreach (var tx in response.Transactions)
            {
                Assert.NotEqual(default, tx);
            }
        }

        [Fact]
        public async Task TestAccountInfo()
        {
            var account = Setup.TestAccountOne.Address;

            var request = new AccountInfoRequest { Account = account };

            // Validated 
            request.Ledger = LedgerSpecification.Validated;
            var responseValidated = await Api.AccountInfo(request);
            Assert.True(responseValidated.Validated);
            Assert.NotNull(responseValidated.LedgerHash);
            Assert.NotEqual(new Hash256(), responseValidated.LedgerHash);
            Assert.NotEqual(default, responseValidated.LedgerIndex);
            Assert.Equal(account, responseValidated.AccountData.Account);


            // Current
            request.Ledger = LedgerSpecification.Current;
            var responseCurrent = await Api.AccountInfo(request);
            Assert.False(responseCurrent.Validated);
            Assert.Null(responseCurrent.LedgerHash);
            Assert.NotEqual(default, responseCurrent.LedgerIndex);
            Assert.Equal(account, responseCurrent.AccountData.Account);

            Assert.True(
                responseCurrent.LedgerIndex > responseValidated.LedgerIndex,
                "Current index > Validated index");
        }

        [Fact]
        public async Task TestServerInfo()
        {
            var response = await Api.ServerInfo();
            Assert.False(string.IsNullOrWhiteSpace(response.BuildVersion));
        }

        [Fact]
        public async Task TestManifest()
        {
            //TODO
            //var bytes = Base16.Decode("ED264807102805220DA0F312E71FC2C69E1552C9C5790F6C25E3729DEB573D5860");
            //PublicKey publicKey = new Secp256k1PublicKey(bytes);
            //var response = await Api.Manifest(publicKey);
        }

        [Fact]
        public async Task TestServerState()
        {
            var response = await Api.ServerState();
            Assert.NotEmpty(response.BuildVersion);
            Assert.NotEmpty(response.PubkeyNode);
            Assert.Null(response.PubkeyValidator);
            Assert.NotEqual(TimeSpan.Zero, response.ServerStateDuration);
            Assert.NotEqual(TimeSpan.Zero, response.Uptime);
        }

        [Fact]
        public async Task TestSubmit()
        {
            // Example tx blob from ripple documentation (https://xrpl.org/submit.html)
            var hex = "1200002280000000240000001E61D4838D7EA4C6800000000000000000000000000055534400000000004B4E9C06F24296074F7BC48F92A97916C6DC5EA968400000000000000B732103AB40A0490F9B7ED8DF29D246BF2D6269820A0EE7742ACDD457BEA7C7D0931EDB7447304502210095D23D8AF107DF50651F266259CC7139D0CD0C64ABBA3A958156352A0D95A21E02207FCF9B77D7510380E49FF250C21B57169E14E9B4ACFD314CEDC79DDD0A38B8A681144B4E9C06F24296074F7BC48F92A97916C6DC5EA983143E9D4A2B8AA0780F682D136F7A56D6724EF53754";
            var utf8 = System.Text.Encoding.UTF8.GetBytes(hex);
            var txBlob = new byte[Base16.GetDecodedFromUtf8Length(utf8.Length)];
            var status = Base16.DecodeFromUtf8(utf8, txBlob, out var _, out var _);
            Assert.Equal(System.Buffers.OperationStatus.Done, status);

            var submitRequest = new SubmitRequest();
            submitRequest.TxBlob = txBlob;
            var submitResponse = await Api.Submit(submitRequest);

            Assert.Equal(Base16.Encode(submitRequest.TxBlob.Span), Base16.Encode(submitResponse.TxBlob));
            Assert.Equal(EngineResult.terPRE_SEQ, submitResponse.EngineResult);
        }

        [Fact]
        public async Task TestAccountSet()
        {
            var account = Setup.TestAccountOne.Address;
            var secret = Setup.TestAccountOne.Secret;

            var transaction = new AccountSetTransaction();
            transaction.Account = account;
            transaction.Domain = System.Text.Encoding.ASCII.GetBytes("example.com");

            var (_, transactionResponse) = await SubmitTransaction(secret, transaction);
            var acr = Assert.IsType<AccountSetTransaction>(transactionResponse.Transaction);
            Assert.Equal(transaction.Account, acr.Account);
            Assert.Equal(transaction.Sequence, acr.Sequence);
            Assert.Equal(transaction.Fee, acr.Fee);
            AssertMemoryEqual(transaction.Domain, acr.Domain);

            var infoRequest = new AccountInfoRequest()
            {
                Ledger = new LedgerSpecification(transactionResponse.LedgerIndex.Value),
                Account = account,
            };
            var infoResponse = await Api.AccountInfo(infoRequest);
            Assert.Equal(account, infoResponse.AccountData.Account);
            AssertMemoryEqual(transaction.Domain, infoResponse.AccountData.Domain);
        }

        [Fact]
        public async Task TestSetRegularKey()
        {
            var account = Setup.TestAccountOne.Address;
            var secret = Setup.TestAccountOne.Secret;

            var seed = new Seed("ssKXuaAGcAXaKBf7d532v8KeypdoS");
            seed.GetKeyPairs(out var _, out var keyPair);

            var transaction = new SetRegularKeyTransaction();
            transaction.Account = account;
            transaction.RegularKey = AccountId.FromPublicKey(keyPair.PublicKey.GetCanoncialBytes());

            var (_, transactionResponse) = await SubmitTransaction(secret, transaction);
            var srkr = Assert.IsType<SetRegularKeyTransaction>(transactionResponse.Transaction);
            Assert.Equal(transaction.RegularKey, srkr.RegularKey);

            // Check we can do a noop AccountSet
            var accountSetTransaction = new AccountSetTransaction();
            accountSetTransaction.Account = account;
            accountSetTransaction.Sequence = transaction.Sequence + 1;
            accountSetTransaction.Fee = transaction.Fee;

            // Submit with our ed25519 keypair
            var (_, accountSetTransactionResponse) = await SubmitTransaction(seed, accountSetTransaction);
            var acr = Assert.IsType<AccountSetTransaction>(accountSetTransactionResponse.Transaction);

            var infoRequest = new AccountInfoRequest()
            {
                Ledger = new LedgerSpecification(transactionResponse.LedgerIndex.Value),
                Account = account,
            };
            var infoResponse = await Api.AccountInfo(infoRequest);
            Assert.Equal(account, infoResponse.AccountData.Account);
        }

        [Fact]
        public async Task TestXrpPayment()
        {
            var accountOne = Setup.TestAccountOne.Address;
            var accountTwo = Setup.TestAccountTwo.Address;
            var secret = Setup.TestAccountOne.Secret;

            ulong startingDrops;
            {
                var response = await Api.AccountInfo(new AccountInfoRequest()
                {
                    Ledger = LedgerSpecification.Current,
                    Account = accountTwo,
                });
                startingDrops = response.AccountData.Balance.Drops;
            }

            var transaction = new PaymentTransaction();
            transaction.Account = accountOne;
            transaction.Destination = accountTwo;
            transaction.DestinationTag = 1;
            transaction.Amount = new Amount(100);

            var (_, transactionResponse) = await SubmitTransaction(secret, transaction);
            var pr = Assert.IsType<PaymentTransaction>(transactionResponse.Transaction);
            Assert.Equal(transaction.Amount, pr.Amount);

            ulong endingDrops;
            {
                var response = await Api.AccountInfo(new AccountInfoRequest()
                {
                    Ledger = LedgerSpecification.Current,
                    Account = accountTwo,
                });
                endingDrops = response.AccountData.Balance.Drops;
            }

            Assert.Equal(100ul, endingDrops - startingDrops);
        }

        [Fact]
        public async Task TestGbpPayment()
        {
            var accountOne = Setup.TestAccountOne.Address;
            var accountTwo = Setup.TestAccountTwo.Address;
            var secretOne = Setup.TestAccountOne.Secret;
            var secretTwo = Setup.TestAccountTwo.Secret;

            // Set up a trust line
            var trustSet = new TrustSetTransaction();
            trustSet.Account = accountTwo;
            trustSet.LimitAmount = new IssuedAmount(accountOne, new CurrencyCode("GBP"), new Currency(1000m));

            // Submit and wait for the trust line
            var (_, _) = await SubmitTransaction(secretTwo, trustSet);

            // Send 100GBP
            var payment = new PaymentTransaction();
            payment.Account = accountOne;
            payment.Destination = accountTwo;
            payment.DestinationTag = 1;
            payment.Amount = new Amount(accountOne, new CurrencyCode("GBP"), new Currency(100m));

            var (_, transactionResponse) = await SubmitTransaction(secretOne, payment);
            var pr = Assert.IsType<PaymentTransaction>(transactionResponse.Transaction);
            Assert.Equal(payment.Amount, pr.Amount);

            // Check we have +100 GBP on our trust line
            var linesRequest = new AccountLinesRequest();
            linesRequest.Account = accountTwo;
            var linesResponse = await Api.AccountLines(linesRequest);
            var lines = new List<TrustLine>();
            await foreach (var line in linesResponse)
            {
                lines.Add(line);
            }
            var trustLine = lines.First();
            Assert.Equal(accountOne, trustLine.Account);
            Assert.Equal(new CurrencyCode("GBP"), trustLine.Currency);
            Assert.Equal(new Currency(100m), trustLine.Balance);

            var request = new AccountCurrenciesRequest()
            {
                Ledger = LedgerSpecification.Current,
                Account = accountOne,
            };
            var currencies = await Api.AccountCurrencies(request);
            Assert.Equal(new CurrencyCode("GBP"), Assert.Single(currencies.SendCurrencies));
            Assert.Equal(new CurrencyCode("GBP"), Assert.Single(currencies.ReceiveCurrencies));
        }

        [Fact]
        public async Task TestNoRipple()
        {
            // We need a fresh account setup for this test
            var testAccount = await TestAccount.Create();
            await Utils.WaitForAccount(Api, testAccount.Address);

            // All tests will be against this new account,
            // also all but the last will have Transactions set true.
            var request = new NoRippleCheckRequest();
            request.Ledger = LedgerSpecification.Validated;
            request.Account = testAccount.Address;
            request.Transactions = true;

            // No trust lines yet, check that the user has no problems
            {
                request.Role = "user";
                var response = await Api.NoRippleCheck(request);
                Assert.NotEqual(default, response.LedgerIndex);
                Assert.Empty(response.Problems);
                Assert.Empty(response.Transactions);
            }

            // No trust lines yet, check that the gateway has one problem to set the default ripple flag
            {
                request.Role = "gateway";
                var response = await Api.NoRippleCheck(request);
                Assert.True(response.Validated);
                Assert.NotEqual(default, response.LedgerHash);
                Assert.NotEqual(default, response.LedgerIndex);
                Assert.Equal("You should immediately set your default ripple flag", Assert.Single(response.Problems));
                var transaction = Assert.Single(response.Transactions);
                transaction.Account = testAccount.Address;
                var accountSet = Assert.IsType<AccountSetTransaction>(transaction);
                Assert.Equal(AccountSetFlags.DefaultRipple, accountSet.SetFlag);
            }

            // Make a GBP trust line to account 1
            {
                var trustSet = new TrustSetTransaction();
                trustSet.Account = testAccount.Address;
                trustSet.LimitAmount = new IssuedAmount(Setup.TestAccountOne.Address, new CurrencyCode("GBP"), new Currency(1000m));

                // Submit and wait for the trust line
                var (_, _) = await SubmitTransaction(testAccount.Secret, trustSet);
            }

            // Trust lines setup, check that the user has one problem
            {
                request.Role = "user";
                var response = await Api.NoRippleCheck(request);
                Assert.True(response.Validated);
                Assert.NotEqual(default, response.LedgerHash);
                Assert.NotEqual(default, response.LedgerIndex);
                var expected = "You should probably set the no ripple flag on your GBP line to " + Setup.TestAccountOne.Address.ToString();
                Assert.Equal(expected, Assert.Single(response.Problems));
                var transaction = Assert.Single(response.Transactions);
                transaction.Account = testAccount.Address;
                var trustSet = Assert.IsType<TrustSetTransaction>(transaction);
                Assert.Equal(Setup.TestAccountOne.Address, trustSet.LimitAmount.Issuer);
                Assert.Equal(new CurrencyCode("GBP"), trustSet.LimitAmount.CurrencyCode);
                Assert.Equal(new Currency(1000m), trustSet.LimitAmount.Value);
                Assert.Equal(TrustSetFlags.SetNoRipple, trustSet.Flags);
            }

            // Trust lines setup, check that the gateway has one problem to set the default ripple flag
            {
                // This is the last request we make that COULD return transactions so check that if this is set false we don't see any
                request.Transactions = false;
                request.Role = "gateway";
                var response = await Api.NoRippleCheck(request);
                Assert.True(response.Validated);
                Assert.NotEqual(default, response.LedgerHash);
                Assert.NotEqual(default, response.LedgerIndex);
                Assert.Equal("You should immediately set your default ripple flag", Assert.Single(response.Problems));
                // Empty because request.Transactions = false
                Assert.Empty(response.Transactions);
            }

            // Set the no ripple flag and make sure gateway now returns no issues
            {
                var accountSet = new AccountSetTransaction();
                accountSet.Account = testAccount.Address;
                accountSet.SetFlag = AccountSetFlags.DefaultRipple;

                // Submit and wait for the flag set
                var (_, _) = await SubmitTransaction(testAccount.Secret, accountSet);

                // We will have waited for the AccountSet transaction to be validated as part of submit, so we should be able to query the current ledger here
                request.Ledger = LedgerSpecification.Current;
                var response = await Api.NoRippleCheck(request);
                Assert.False(response.Validated);
                Assert.Equal(default, response.LedgerHash);
                Assert.NotEqual(default, response.LedgerIndex);
                Assert.Empty(response.Problems);
                Assert.Empty(response.Transactions);
            }
        }

        [Fact]
        public async Task TestGatewayBalances()
        {
            // We need a fresh accounts setup for this test
            var accounts = await Task.WhenAll(TestAccount.Create(), TestAccount.Create(), TestAccount.Create(), TestAccount.Create());
            await Utils.WaitForAccounts(Api, accounts);
            var gatewayAccount = accounts[0];
            var account1 = accounts[1];
            var account2 = accounts[2];
            var hotwallet = accounts[3];

            var testAccounts = new[] { account1, account2 };

            // Setup trust lines from account 1 and 2 to the gateway and send some money
            foreach (var account in testAccounts)
            {
                var trustSet = new TrustSetTransaction();
                trustSet.Account = account.Address;
                // GBP
                trustSet.LimitAmount = new IssuedAmount(gatewayAccount.Address, new CurrencyCode("GBP"), new Currency(1000m));
                var (_, _) = await SubmitTransaction(account.Secret, trustSet);
                // BTC
                trustSet.LimitAmount = new IssuedAmount(gatewayAccount.Address, new CurrencyCode("BTC"), new Currency(100m));
                var (_, _) = await SubmitTransaction(account.Secret, trustSet);

                var payment = new PaymentTransaction();
                payment.Account = gatewayAccount.Address;
                payment.Destination = account.Address;

                // GBP
                payment.Amount = new Amount(gatewayAccount.Address, new CurrencyCode("GBP"), new Currency(50m));
                var (_, _) = await SubmitTransaction(gatewayAccount.Secret, payment);
                // BTC
                payment.Amount = new Amount(gatewayAccount.Address, new CurrencyCode("BTC"), new Currency(10m));
                var (_, _) = await SubmitTransaction(gatewayAccount.Secret, payment);
            }

            // Send some GBP to the hotwallet
            {
                var trustSet = new TrustSetTransaction();
                trustSet.Account = hotwallet.Address;
                trustSet.LimitAmount = new IssuedAmount(gatewayAccount.Address, new CurrencyCode("GBP"), new Currency(100m));
                var (_, _) = await SubmitTransaction(hotwallet.Secret, trustSet);

                var payment = new PaymentTransaction();
                payment.Account = gatewayAccount.Address;
                payment.Destination = hotwallet.Address;
                payment.Amount = new Amount(gatewayAccount.Address, new CurrencyCode("GBP"), new Currency(24m));
                var (_, _) = await SubmitTransaction(gatewayAccount.Secret, payment);
            }

            // Check gateway balance sums correctly
            {
                var request = new GatewayBalancesRequest
                {
                    Account = gatewayAccount.Address,
                    HotWallet = new[] { hotwallet.Address }
                };
                var response = await Api.GatewayBalances(request);

                Assert.Equal(2, response.Obligations.Count);
                Assert.Equal(new Currency(100m), response.Obligations[new CurrencyCode("GBP")]);
                Assert.Equal(new Currency(20m), response.Obligations[new CurrencyCode("BTC")]);

                var balances = Assert.Single(response.Balances);
                Assert.Equal(hotwallet.Address, balances.Key);
                var balance = Assert.Single(balances.Value);
                Assert.Equal(new CurrencyCode("GBP"), balance.Key);
                Assert.Equal(new Currency(24m), balance.Value);

                Assert.Empty(response.Assets);
            }

            // Check account balance sums correctly
            {
                var request = new GatewayBalancesRequest
                {
                    Account = account1.Address
                };
                var response = await Api.GatewayBalances(request);
                Assert.Empty(response.Obligations);
                Assert.Empty(response.Balances);

                var asset = Assert.Single(response.Assets);
                Assert.Equal(gatewayAccount.Address, asset.Key);
                var currencies = asset.Value;
                Assert.Equal(2, currencies.Count);
                Assert.Equal(new Currency(50m), currencies[new CurrencyCode("GBP")]);
                Assert.Equal(new Currency(10m), currencies[new CurrencyCode("BTC")]);
            }
        }

        [Fact(Skip = "This takes about 13 minutes to run")]
        public async Task TestAccountDelete()
        {
            // Make a fresh account to delete and send funds to account 1
            var deleteAccount = await TestAccount.Create();
            await Utils.WaitForAccount(Api, deleteAccount.Address);

            var accountOne = Setup.TestAccountOne;

            ulong startingDrops;
            {
                var response = await Api.AccountInfo(new AccountInfoRequest()
                {
                    Ledger = LedgerSpecification.Current,
                    Account = accountOne.Address,
                });
                startingDrops = response.AccountData.Balance.Drops;
            }

            var transaction = new AccountDeleteTransaction();
            transaction.Account = deleteAccount.Address;
            transaction.Destination = accountOne.Address;
            // Set the fee to a fixed number so our assert later is correct
            transaction.Fee = XrpAmount.FromXrp(5);

            // Got to wait for 256 ledgers to pass
            var info = await Api.AccountInfo(new AccountInfoRequest { Account = deleteAccount.Address });

            while (true)
            {
                var ledger = await Api.LedgerCurrent();
                if (ledger > info.AccountData.Sequence + 256) break;
                System.Threading.Thread.Sleep(60 * 1000);
            }

            var (_, transactionResponse) = await SubmitTransaction(deleteAccount.Secret, transaction);
            var adr = Assert.IsType<AccountDeleteTransaction>(transactionResponse.Transaction);
            Assert.Equal(transaction.Account, adr.Account);
            Assert.Equal(transaction.Destination, adr.Destination);
            Assert.Equal(transaction.DestinationTag, adr.DestinationTag);

            ulong endingDrops;
            {
                var response = await Api.AccountInfo(new AccountInfoRequest()
                {
                    Ledger = LedgerSpecification.Current,
                    Account = accountOne.Address,
                });
                endingDrops = response.AccountData.Balance.Drops;
            }

            Assert.Equal(
                info.AccountData.Balance.Drops - transaction.Fee.Drops,
                endingDrops - startingDrops);
        }

        [Fact]
        public async Task TestSignerListSet()
        {
            // Make a fresh account to set signer list on
            var testAccount = await TestAccount.Create();
            await Utils.WaitForAccount(Api, testAccount.Address);

            // Make up three "accounts"
            var random = new Random();
            var buffer = new byte[16];
            Func<KeyType, Seed> makeSeed = type =>
            {
                random.NextBytes(buffer);
                return new Seed(buffer, type);
            };

            var accounts = new[]
            {
                TestAccount.FromSeed(makeSeed(KeyType.Secp256k1)),
                TestAccount.FromSeed(makeSeed(KeyType.Secp256k1)),
                TestAccount.FromSeed(makeSeed(KeyType.Ed25519)),
            };

            var signerListSet = new SignerListSetTransaction();
            signerListSet.Account = testAccount.Address;
            signerListSet.SignerQuorum = 2;
            signerListSet.SignerEntries = Array.AsReadOnly(new[] {
                new SignerEntry(accounts[0].Address, 1),
                new SignerEntry(accounts[1].Address, 1),
                new SignerEntry(accounts[2].Address, 2),
            });

            var (_, transactionResponse) = await SubmitTransaction(testAccount.Secret, signerListSet);
            var slsr = Assert.IsType<SignerListSetTransaction>(transactionResponse.Transaction);
            Assert.Equal(signerListSet.Account, slsr.Account);
            Assert.Equal(signerListSet.SignerQuorum, slsr.SignerQuorum);
            Assert.Equal(signerListSet.SignerEntries[0], slsr.SignerEntries[0]);
            Assert.Equal(signerListSet.SignerEntries[1], slsr.SignerEntries[1]);
            Assert.Equal(signerListSet.SignerEntries[2], slsr.SignerEntries[2]);

            // Check the signer list show in account info now
            var accountInfoRequest = new AccountInfoRequest { Account = testAccount.Address, SignerLists = true };
            var accountInfoResponse = await Api.AccountInfo(accountInfoRequest);

            Assert.Equal(SignerListFlags.lsfOneOwnerCount, accountInfoResponse.SignerList.Flags);
            Assert.Equal(signerListSet.SignerQuorum, accountInfoResponse.SignerList.SignerQuorum);
            // The SignerList object sorts the entries by account id so we can't just check [0] == [0]
            Assert.Equal(3, accountInfoResponse.SignerList.SignerEntries.Count);
            Assert.Contains(signerListSet.SignerEntries[0], accountInfoResponse.SignerList.SignerEntries);
            Assert.Contains(signerListSet.SignerEntries[1], accountInfoResponse.SignerList.SignerEntries);
            Assert.Contains(signerListSet.SignerEntries[2], accountInfoResponse.SignerList.SignerEntries);

            // And multi sign a transaction (paying accounts[0])
            var payment = new PaymentTransaction();
            payment.Account = testAccount.Address;
            payment.Destination = accounts[0].Address;
            payment.Amount = XrpAmount.FromDrops(50_000_000);

            // Try and sign with just account[0] and account[1]
            {
                var signers = new Tuple<AccountId, Seed>[]
                {
                    Tuple.Create(accounts[0].Address, accounts[0].Secret),
                    Tuple.Create(accounts[1].Address, accounts[1].Secret),
                };

                var (_, paymentResponse) = await SubmitTransaction(signers, payment);
                var pr = Assert.IsType<PaymentTransaction>(paymentResponse.Transaction);
                Assert.Equal(payment.Amount, pr.Amount);
            }

            // Try and sign with just account[2]
            {
                var signers = new Tuple<AccountId, Seed>[]
                {
                    Tuple.Create(accounts[2].Address, accounts[2].Secret),
                };

                var (_, paymentResponse) = await SubmitTransaction(signers, payment);
                var pr = Assert.IsType<PaymentTransaction>(paymentResponse.Transaction);
                Assert.Equal(payment.Amount, pr.Amount);
            }

            accountInfoResponse = await Api.AccountInfo(new AccountInfoRequest()
            {
                Ledger = LedgerSpecification.Current,
                Account = accounts[0].Address,
            });

            Assert.Equal(100_000_000ul, accountInfoResponse.AccountData.Balance.Drops);
        }

        [Fact]
        public async Task TestChecks()
        {
            var accountOne = Setup.TestAccountOne;
            var accountTwo = Setup.TestAccountTwo;

            // Create a check
            var checkCreate = new CheckCreateTransaction();
            checkCreate.Account = accountOne.Address;
            checkCreate.Destination = accountTwo.Address;
            checkCreate.SendMax = new Amount(500);
            checkCreate.InvoiceID = new Hash256("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9");

            var (_, checkCreateResponse) = await SubmitTransaction(accountOne.Secret, checkCreate);
            var ccrr = Assert.IsType<CheckCreateTransaction>(checkCreateResponse.Transaction);
            Assert.Equal(checkCreate.Account, ccrr.Account);
            Assert.Equal(checkCreate.Destination, ccrr.Destination);
            Assert.Equal(checkCreate.SendMax, ccrr.SendMax);
            Assert.Equal(checkCreate.InvoiceID, ccrr.InvoiceID);

            // Cancel that check with account two
            var checkCancel = new CheckCancelTransaction();
            checkCancel.Account = accountTwo.Address;
            checkCancel.CheckID = CheckLedgerEntry.CalculateId(accountOne.Address, ccrr.Sequence);
            var (_, checkCancelResponse) = await SubmitTransaction(accountTwo.Secret, checkCancel);
            var ccanr = Assert.IsType<CheckCancelTransaction>(checkCancelResponse.Transaction);
            Assert.Equal(checkCancel.Account, ccanr.Account);
            Assert.Equal(checkCancel.CheckID, ccanr.CheckID);


            // Create a new check
            var sndCheckCreate = new CheckCreateTransaction();
            sndCheckCreate.Account = accountOne.Address;
            sndCheckCreate.Destination = accountTwo.Address;
            sndCheckCreate.SendMax = new Amount(1000);

            var (_, sndCheckCreateResponse) = await SubmitTransaction(accountOne.Secret, sndCheckCreate);
            ccrr = Assert.IsType<CheckCreateTransaction>(sndCheckCreateResponse.Transaction);
            Assert.Equal(sndCheckCreate.Account, ccrr.Account);
            Assert.Equal(sndCheckCreate.Destination, ccrr.Destination);
            Assert.Equal(sndCheckCreate.SendMax, ccrr.SendMax);
            Assert.Equal(sndCheckCreate.InvoiceID, ccrr.InvoiceID);

            // Try to claim the check
            var checkCash = new CheckCashTransaction();
            checkCash.Account = accountTwo.Address;
            checkCash.CheckID = CheckLedgerEntry.CalculateId(accountOne.Address, ccrr.Sequence);
            checkCash.Amount = new Amount(750);
            var (_, checkCashResponse) = await SubmitTransaction(accountTwo.Secret, checkCash);
            var ccashr = Assert.IsType<CheckCashTransaction>(checkCashResponse.Transaction);
            Assert.Equal(checkCash.Account, ccashr.Account);
            Assert.Equal(checkCash.CheckID, ccashr.CheckID);
            Assert.Equal(checkCash.Amount, ccashr.Amount);
            Assert.Equal(checkCash.DeliverMin, ccashr.DeliverMin);
        }

        [Fact]
        public async Task TestAccountLines()
        {
            // We need a fresh accounts setup for this test
            var account1 = await TestAccount.Create();
            var account2 = await TestAccount.Create();
            var account3 = await TestAccount.Create();
            await Utils.WaitForAccounts(Api, account1.Address, account2.Address, account3.Address);


            var currencies = new CurrencyCode[20];
            currencies[0] = new CurrencyCode("GBP");
            currencies[1] = new CurrencyCode("BTC");
            currencies[2] = new CurrencyCode("USD");
            currencies[3] = new CurrencyCode("EUR");
            for (int i = 4; i < currencies.Length; ++i)
            {
                var bytes = String.Concat(Enumerable.Repeat(i.ToString("x2"), 20));
                currencies[i] = new CurrencyCode(bytes);
            }

            // Setup a whole load of trust lines from 2 and 3 to 1
            var testAccounts = new[] { account2, account3 };
            foreach (var account in testAccounts)
            {
                var trustSetTransactions = new List<Transaction>();
                for (int i = 0; i < currencies.Length; ++i)
                {
                    var currency = currencies[i];

                    var trustSet = new TrustSetTransaction();
                    trustSet.Account = account.Address;
                    trustSet.LimitAmount = new IssuedAmount(account1.Address, currency, new Currency(1000m));
                    trustSetTransactions.Add(trustSet);
                }
                await SubmitTransactions(account.Secret, trustSetTransactions);
            }

            var paymentTransactions = new List<Transaction>();
            foreach (var account in testAccounts)
            {
                for (int i = 0; i < currencies.Length; ++i)
                {
                    var currency = currencies[i];

                    var payment = new PaymentTransaction();
                    payment.Account = account1.Address;
                    payment.Destination = account.Address;
                    payment.Amount = new Amount(account1.Address, currency, new Currency(i + 1));
                    paymentTransactions.Add(payment);
                }
            }
            await SubmitTransactions(account1.Secret, paymentTransactions);

            // Check all the trust lines from account 1
            var request = new AccountLinesRequest();
            request.Account = account1.Address;
            request.Limit = 10; // Artifically low limit to try and force paging
            var response = await Api.AccountLines(request);
            Assert.Equal(account1.Address, response.Account);
            var results = new Dictionary<AccountId, List<TrustLine>>();
            results[account2.Address] = new List<TrustLine>();
            results[account3.Address] = new List<TrustLine>();
            await foreach (var line in response)
            {
                var amounts = results[line.Account];
                amounts.Add(line);
            }

            void CheckAmounts(List<TrustLine> lines)
            {
                Assert.Equal(currencies.Length, lines.Count);

                for (int i = 0; i < currencies.Length; ++i)
                {
                    var currency = currencies[i];
                    var amount = new Currency(i + 1);

                    Assert.Contains(lines, line =>
                        line.Currency == currency &&
                        line.Balance == -amount &&
                        line.LimitPeer == new Currency(1000m) &&
                        line.Limit == Currency.Zero &&
                        line.NoRipple && !line.NoRipplePeer);
                }

            }

            CheckAmounts(results[account2.Address]);
            CheckAmounts(results[account3.Address]);
        }

        [Fact]
        public async Task TestLedgerData()
        {
            var request = new LedgerDataRequest
            {
                Ledger = LedgerSpecification.Validated
            };

            do
            {
                var response = await Api.LedgerData(request);
                Assert.NotNull(response.LedgerHash);

                foreach (var obj in response.State)
                {
                    Assert.NotNull(obj.Item1);
                    if (obj.Item1 is AccountRootLedgerEntry)
                    {
                        var accountRoot = (AccountRootLedgerEntry)obj.Item1;
                        Assert.Equal(accountRoot.ID, obj.Item2);
                    }
                    else if (obj.Item1 is AmendmentsLedgerEntry)
                    {
                        Assert.Equal(AmendmentsLedgerEntry.ID, obj.Item2);
                    }
                    else if (obj.Item1 is CheckLedgerEntry)
                    {
                        var check = (CheckLedgerEntry)obj.Item1;
                        Assert.Equal(check.ID, obj.Item2);
                    }
                    else if (obj.Item1 is DepositPreauthLedgerEntry)
                    {
                        var depositPreauth = (DepositPreauthLedgerEntry)obj.Item1;
                        Assert.Equal(depositPreauth.ID, obj.Item2);
                    }
                    else if (obj.Item1 is DirectoryNodeLedgerEntry)
                    {
                        var directoryNode = (DirectoryNodeLedgerEntry)obj.Item1;
                        // TODO
                        // Assert.Equal(directoryNode.ID, obj.Item2);
                    }
                    else if (obj.Item1 is EscrowLedgerEntry)
                    {
                        var escrow = (EscrowLedgerEntry)obj.Item1;
                        // TODO
                        // Assert.Equal(escrow.ID, obj.Item2);
                    }
                    else if (obj.Item1 is FeeSettingsLedgerEntry)
                    {
                        Assert.Equal(FeeSettingsLedgerEntry.ID, obj.Item2);
                    }
                    else if (obj.Item1 is LedgerHashesLedgerEntry)
                    {
                        // TODO
                        // Assert.Equal(LedgerHashes.ID, obj.Item2);
                    }
                    else if (obj.Item1 is NegativeUNLLedgerEntry)
                    {
                        Assert.Equal(NegativeUNLLedgerEntry.ID, obj.Item2);
                    }
                    else if (obj.Item1 is OfferLedgerEntry)
                    {
                        var offer = (OfferLedgerEntry)obj.Item1;
                        Assert.Equal(offer.ID, obj.Item2);
                    }
                    else if (obj.Item1 is PayChannelLedgerEntry)
                    {
                        var payChannel = (PayChannelLedgerEntry)obj.Item1;
                        // TODO
                        // Assert.Equal(payChannel.ID, obj.Item2);
                    }
                    else if (obj.Item1 is RippleStateLedgerEntry)
                    {
                        var rippleState = (RippleStateLedgerEntry)obj.Item1;
                        Assert.Equal(rippleState.ID, obj.Item2);
                    }
                    else if (obj.Item1 is SignerListLedgerEntry)
                    {
                        var signerList = (SignerListLedgerEntry)obj.Item1;
                        // TODO
                        // Assert.Equal(signerList.ID, obj.Item2);
                    }
                    else if (obj.Item1 is TicketLedgerEntry)
                    {
                        var ticket = (TicketLedgerEntry)obj.Item1;
                        Assert.Equal(ticket.ID, obj.Item2);
                    }
                }

                request.Ledger = new LedgerSpecification(response.LedgerIndex);
                request.Marker = response.Marker;
            } while (request.Marker.HasValue);
        }

        [Fact]
        public async Task TestLedgerEntry()
        {
            var request = new LedgerEntryRequest
            {
                Ledger = LedgerSpecification.Validated
            };
            LedgerEntryResponse response;

            request.Index = FeeSettingsLedgerEntry.ID;
            response = await Api.LedgerEntry(request);
            Assert.NotNull(response.LedgerHash);
            if (response.Node is FeeSettingsLedgerEntry)
            {
                var feeSettings = (FeeSettingsLedgerEntry)response.Node;
                Assert.NotEqual(0ul, feeSettings.BaseFee);
                Assert.NotEqual(0u, feeSettings.ReferenceFeeUnits);
                Assert.NotEqual(0u, feeSettings.ReserveBase);
                Assert.NotEqual(0u, feeSettings.ReserveIncrement);
            }
            else
            {
                throw new Exception("Expected FeeSettings");
            }

            request.Index = AmendmentsLedgerEntry.ID;
            response = await Api.LedgerEntry(request);
            Assert.NotNull(response.LedgerHash);
            if (response.Node is AmendmentsLedgerEntry)
            {
                var amendments = (AmendmentsLedgerEntry)response.Node;
                Assert.Equal(0u, amendments.Flags);
            }
            else
            {
                throw new Exception("Expected Amendments");
            }
        }

        [Fact]
        public async Task TestDepositAuthorized()
        {
            var request = new DepositAuthorizedRequest
            {
                Ledger = LedgerSpecification.Validated,
                SourceAccount = Setup.TestAccountOne.Address,
                DestinationAccount = Setup.TestAccountTwo.Address,
            };

            var response = await Api.DepositAuthorized(request);
            Assert.NotNull(response.LedgerHash);
            Assert.Equal(request.SourceAccount, response.SourceAccount);
            Assert.Equal(request.DestinationAccount, response.DestinationAccount);
            Assert.True(response.DepositAuthorized);
        }

        [Fact]
        public async Task TestUsdOffer()
        {
            var accounts = await Task.WhenAll(TestAccount.Create(), TestAccount.Create());
            var usd = new CurrencyCode("USD");

            // Set up a trust line
            var trustSet = new TrustSetTransaction();
            trustSet.Account = accounts[1].Address;
            trustSet.LimitAmount = new IssuedAmount(accounts[0].Address, usd, new Currency(1000m));

            // Submit and wait for the trust line
            var (_, _) = await SubmitTransaction(accounts[1].Secret, trustSet);

            // Offer 100USD for 10XRP from accountOne
            var offer = new OfferCreateTransaction();
            offer.Account = accounts[0].Address;
            offer.TakerPays = XrpAmount.FromXrp(10.0m);
            offer.TakerGets = new IssuedAmount(accounts[0].Address, usd, new Currency(100m));

            var (_, transactionResponse) = await SubmitTransaction(accounts[0].Secret, offer);
            var or = Assert.IsType<OfferCreateTransaction>(transactionResponse.Transaction);
            Assert.Equal(offer.Account, or.Account);
            Assert.Equal(offer.TakerPays, or.TakerPays);
            Assert.Equal(offer.TakerGets, or.TakerGets);

            // Offer 5XRP for 50USD from accountTwo
            var counterOffer = new OfferCreateTransaction();
            counterOffer.Account = accounts[1].Address;
            counterOffer.TakerPays = new IssuedAmount(accounts[0].Address, usd, new Currency(50m));
            counterOffer.TakerGets = XrpAmount.FromXrp(5.0m);

            var (_, counterTransactionResponse) = await SubmitTransaction(accounts[1].Secret, counterOffer);
            var cor = Assert.IsType<OfferCreateTransaction>(counterTransactionResponse.Transaction);
            Assert.Equal(counterOffer.Account, cor.Account);
            Assert.Equal(counterOffer.TakerPays, cor.TakerPays);
            Assert.Equal(counterOffer.TakerGets, cor.TakerGets);

            // Check the offer in the ledger
            var ledgerEntryRequest = new LedgerEntryRequest();
            ledgerEntryRequest.Ledger = LedgerSpecification.Current;
            ledgerEntryRequest.Index = OfferLedgerEntry.CalculateId(accounts[0].Address, or.Sequence);
            var ledgerEntryResponse = await Api.LedgerEntry(ledgerEntryRequest);
            var offerData = Assert.IsType<OfferLedgerEntry>(ledgerEntryResponse.Node);

            Assert.Equal(accounts[0].Address, offerData.Account);
            Assert.Equal(XrpAmount.FromXrp(5.0m), offerData.TakerPays);
            Assert.Equal(new IssuedAmount(accounts[0].Address, usd, new Currency(50m)), offerData.TakerGets);

            // Check the offer book
            var bookOffersRequest = new BookOffersRequest();
            bookOffersRequest.Ledger = LedgerSpecification.Current;
            bookOffersRequest.TakerPays = CurrencyType.XRP;
            bookOffersRequest.TakerGets = new CurrencyType(accounts[0].Address, usd);
            var bookOffersResponse = await Api.BookOffers(bookOffersRequest);
            var bookOffer = bookOffersResponse.Offers.Single();

            Assert.Equal(accounts[0].Address, bookOffer.Offer.Account);
            Assert.Equal(XrpAmount.FromXrp(5.0m), bookOffer.Offer.TakerPays);
            Assert.Equal(new IssuedAmount(accounts[0].Address, usd, new Currency(50m)), bookOffer.Offer.TakerGets);

            Assert.Equal(100000m, bookOffer.Quality);
            Assert.Equal(new IssuedAmount(accounts[0].Address, usd, new Currency(50m)), bookOffer.OwnerFunds);
        }

        [Fact]
        public async Task TestGetPath_NoPath()
        {
            var accounts = await Task.WhenAll(TestAccount.Create(), TestAccount.Create());
            var btc = new CurrencyCode("BTC");

            // Ask for a path to send BTC to account 1 from account 0
            var ripplePathFindRequest = new RipplePathFindRequest();
            ripplePathFindRequest.SourceAccount = accounts[0].Address;
            ripplePathFindRequest.DestinationAccount = accounts[1].Address;
            ripplePathFindRequest.DestinationAmount = new Amount(accounts[1].Address, btc, new Currency(10m));

            var ripplePathFindResponse = await Api.RipplePathFind(ripplePathFindRequest);
            Assert.Equal(ripplePathFindResponse.SourceAccount, ripplePathFindResponse.SourceAccount);
            Assert.Equal(ripplePathFindResponse.DestinationAccount, ripplePathFindResponse.DestinationAccount);
            Assert.Equal(ripplePathFindRequest.DestinationAmount, ripplePathFindResponse.DestinationAmount);
            Assert.Equal(new[] { CurrencyCode.XRP }, ripplePathFindResponse.DestinationCurrencies);
            Assert.Empty(ripplePathFindResponse.Alternatives);
        }

        [Fact]
        public async Task TestGetPath_Direct()
        {
            var accounts = await Task.WhenAll(TestAccount.Create(), TestAccount.Create());
            var btc = new CurrencyCode("BTC");

            var trustSet = new TrustSetTransaction();
            trustSet.Account = accounts[1].Address;
            trustSet.LimitAmount = new IssuedAmount(accounts[0].Address, btc, new Currency(100m));
            var (_, _) = await SubmitTransaction(accounts[1].Secret, trustSet);

            // Ask for a path to send BTC to account 1 from account 0
            var ripplePathFindRequest = new RipplePathFindRequest();
            ripplePathFindRequest.SourceAccount = accounts[0].Address;
            ripplePathFindRequest.DestinationAccount = accounts[1].Address;
            ripplePathFindRequest.DestinationAmount = new Amount(accounts[1].Address, btc, new Currency(10m));

            var ripplePathFindResponse = await Api.RipplePathFind(ripplePathFindRequest);
            Assert.Equal(ripplePathFindResponse.SourceAccount, ripplePathFindResponse.SourceAccount);
            Assert.Equal(ripplePathFindResponse.DestinationAccount, ripplePathFindResponse.DestinationAccount);
            Assert.Equal(ripplePathFindRequest.DestinationAmount, ripplePathFindResponse.DestinationAmount);
            Assert.Equal(new[] { btc, CurrencyCode.XRP }, ripplePathFindResponse.DestinationCurrencies);
            var alternative = Assert.Single(ripplePathFindResponse.Alternatives);
            Assert.Equal(new IssuedAmount(accounts[0].Address, btc, new Currency(10m)), alternative.SourceAmount);
            Assert.Empty(alternative.PathsComputed);
        }

        [Fact]
        public async Task TestGetPath_Ripple()
        {
            var accounts = await Task.WhenAll(TestAccount.Create(), TestAccount.Create(), TestAccount.Create());
            var btc = new CurrencyCode("BTC");

            // Set up some trust lines...
            var trustSet = new TrustSetTransaction();
            trustSet.Account = accounts[2].Address;
            trustSet.LimitAmount = new IssuedAmount(accounts[1].Address, btc, new Currency(100m));
            var (_, _) = await SubmitTransaction(accounts[2].Secret, trustSet);

            trustSet.Account = accounts[1].Address;
            trustSet.LimitAmount = new IssuedAmount(accounts[0].Address, btc, new Currency(100m));
            var (_, _) = await SubmitTransaction(accounts[1].Secret, trustSet);

            // Ask for a path to send BTC to account 2 from account 0
            var ripplePathFindRequest = new RipplePathFindRequest();
            ripplePathFindRequest.SourceAccount = accounts[0].Address;
            ripplePathFindRequest.DestinationAccount = accounts[2].Address;
            ripplePathFindRequest.DestinationAmount = new Amount(accounts[2].Address, btc, new Currency(10m));
            ripplePathFindRequest.SourceCurrencies = new[] { CurrencyType.XRP, new CurrencyType(accounts[0].Address, btc) };
            var ripplePathFindResponse = await Api.RipplePathFind(ripplePathFindRequest);
            Assert.Equal(ripplePathFindResponse.SourceAccount, ripplePathFindResponse.SourceAccount);
            Assert.Equal(ripplePathFindResponse.DestinationAccount, ripplePathFindResponse.DestinationAccount);
            Assert.Equal(ripplePathFindRequest.DestinationAmount, ripplePathFindResponse.DestinationAmount);
            Assert.Equal(new[] { btc, CurrencyCode.XRP }, ripplePathFindResponse.DestinationCurrencies);
            var alternative = Assert.Single(ripplePathFindResponse.Alternatives);
            Assert.Equal(new IssuedAmount(accounts[0].Address, btc, new Currency(10m)), alternative.SourceAmount);
            var path = Assert.Single(alternative.PathsComputed);
            var pathItem = Assert.Single(path);
            Assert.Equal(accounts[1].Address, pathItem.Account);

            // ... and try and make a payment with that path
            var payment = new PaymentTransaction();
            payment.Account = ripplePathFindRequest.SourceAccount;
            payment.Destination = ripplePathFindRequest.DestinationAccount;
            payment.Amount = ripplePathFindRequest.DestinationAmount;
            payment.SendMax = alternative.SourceAmount;
            payment.Paths = alternative.PathsComputed;
            var (_, _) = await SubmitTransaction(accounts[0].Secret, payment);
        }

        [Fact]
        public async Task TestGetPath_OrderBook()
        {
            var accounts = await Task.WhenAll(TestAccount.Create(), TestAccount.Create(), TestAccount.Create(), TestAccount.Create());
            var gbp = new CurrencyCode("GBP");

            var A1 = accounts[0];
            var A2 = accounts[1];
            var G3 = accounts[2];
            var M1 = accounts[3];

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

            // Ask for a path to send XRP to A2 from A1 via GBP
            var ripplePathFindRequest = new RipplePathFindRequest();
            ripplePathFindRequest.SourceAccount = A1.Address;
            ripplePathFindRequest.DestinationAccount = A2.Address;
            ripplePathFindRequest.DestinationAmount = XrpAmount.FromXrp(10m);
            ripplePathFindRequest.SourceCurrencies = new[] { new CurrencyType(gbp) };
            var ripplePathFindResponse = await Api.RipplePathFind(ripplePathFindRequest);
            Assert.Equal(ripplePathFindResponse.SourceAccount, ripplePathFindResponse.SourceAccount);
            Assert.Equal(ripplePathFindRequest.DestinationAccount, ripplePathFindResponse.DestinationAccount);
            Assert.Equal(ripplePathFindRequest.DestinationAmount, ripplePathFindResponse.DestinationAmount);
            Assert.Equal(new[] { gbp, CurrencyCode.XRP }, ripplePathFindResponse.DestinationCurrencies);
            var alternative = Assert.Single(ripplePathFindResponse.Alternatives);
            Assert.Equal(new IssuedAmount(A1.Address, gbp, new Currency(1m)), alternative.SourceAmount);
            var path = Assert.Single(alternative.PathsComputed);
            Assert.Collection(path,
                item => Assert.Equal(G3.Address, item.Account),
                item => Assert.Equal(CurrencyCode.XRP, item.Currency));

            // ... and try and make a payment with that path
            payment = new PaymentTransaction();
            payment.Account = ripplePathFindRequest.SourceAccount;
            payment.Destination = ripplePathFindRequest.DestinationAccount;
            payment.Amount = ripplePathFindRequest.DestinationAmount;
            payment.SendMax = alternative.SourceAmount;
            payment.Paths = alternative.PathsComputed;
            var (_, _) = await SubmitTransaction(A1.Secret, payment);
        }

        [Theory]
        [InlineData(KeyType.Secp256k1)]
        [InlineData(KeyType.Ed25519)]
        public async Task TestPaymentChannel(KeyType keyType)
        {
            var accounts = await Task.WhenAll(TestAccount.Create(), TestAccount.Create());
            var accountOne = accounts[0];
            var accountTwo = accounts[1];

            // Setup a seed and key pair to use for the channel
            var seed = Seed.Create(keyType);
            seed.GetKeyPairs(out var _, out var keyPair);

            // Setup a payment channel from account one to two
            var paymentChannelCreate = new PaymentChannelCreateTransaction();
            paymentChannelCreate.Account = accountOne.Address;
            paymentChannelCreate.Destination = accountTwo.Address;
            paymentChannelCreate.Amount = XrpAmount.FromXrp(100m);
            paymentChannelCreate.SettleDelay = TimeSpan.FromHours(24);
            paymentChannelCreate.PublicKey = keyPair.PublicKey.GetCanoncialBytes();
            var (_, transactionResponse1) = await SubmitTransaction(accountOne.Secret, paymentChannelCreate);
            var pccreater = Assert.IsType<PaymentChannelCreateTransaction>(transactionResponse1.Transaction);
            Assert.Equal(accountOne.Address, pccreater.Account);
            Assert.Equal(accountTwo.Address, pccreater.Destination);

            // Calculate the payment channel ID
            var channelId = PayChannelLedgerEntry.CalculateId(pccreater.Account, pccreater.Destination, pccreater.Sequence);

            // Ask for account channels
            var accountChannelsRequest = new AccountChannelsRequest();
            accountChannelsRequest.Ledger = LedgerSpecification.Current;
            accountChannelsRequest.Account = accountOne.Address;
            var accountChannelsResponse = await Api.AccountChannels(accountChannelsRequest);
            Assert.Equal(accountChannelsRequest.Account, accountChannelsResponse.Account);
            var accountChannel = Assert.Single(accountChannelsResponse.Channels);
            Assert.Equal(channelId, accountChannel.ChannelId);
            Assert.Equal(accountOne.Address, accountChannel.Account);
            Assert.Equal(accountTwo.Address, accountChannel.DestinationAccount);
            Assert.Equal(XrpAmount.FromXrp(100m), accountChannel.Amount);
            Assert.Equal(XrpAmount.FromXrp(0m), accountChannel.Balance);
            Assert.Equal(TimeSpan.FromHours(24), accountChannel.SettleDelay);

            // Amount to claim
            var claimAmount = XrpAmount.FromXrp(10m);

            // Generate a claim signature via the API
            var channelAuthorizeRequest = new ChannelAuthorizeRequest();
            channelAuthorizeRequest.ChannelId = channelId;
            channelAuthorizeRequest.Amount = claimAmount;
            channelAuthorizeRequest.Seed = seed;
            var signatureApi = await Api.ChannelAuthorize(channelAuthorizeRequest);

            // Generate a claim signature locally (Verification that our C# algo matches the rippled algo)
            var signatureLocal = PaymentChannelClaimTransaction.Authorize(keyPair, channelId, claimAmount);
            AssertMemoryEqual(signatureApi, signatureLocal);

            // Verify the signature via the api and locally
            var channelVerifyRequest = new ChannelVerifyRequest();
            channelVerifyRequest.Amount = claimAmount;
            channelVerifyRequest.ChannelId = channelId;
            channelVerifyRequest.Signature = signatureLocal;
            channelVerifyRequest.PublicKey = keyPair.PublicKey;
            var verifiedApi = await Api.ChannelVerify(channelVerifyRequest);
            Assert.True(verifiedApi);

            var verifiedLocal = PaymentChannelClaimTransaction.Verify(keyPair.PublicKey, signatureLocal, channelId, claimAmount);
            Assert.True(verifiedLocal);

            // Claim some funds
            var paymentChannelClaim = new PaymentChannelClaimTransaction();
            paymentChannelClaim.Account = accountTwo.Address;
            paymentChannelClaim.Channel = channelId;
            paymentChannelClaim.Balance = claimAmount;
            paymentChannelClaim.Signature = signatureLocal;
            paymentChannelClaim.PublicKey = keyPair.PublicKey.GetCanoncialBytes();
            var (_, transactionResponse2) = await SubmitTransaction(accountTwo.Secret, paymentChannelClaim);
            var pcclaimr = Assert.IsType<PaymentChannelClaimTransaction>(transactionResponse2.Transaction);
            Assert.Equal(accountTwo.Address, pcclaimr.Account);
            Assert.Equal(channelId, pcclaimr.Channel);

            // Close the channel
            var paymentChannelClose = new PaymentChannelClaimTransaction();
            paymentChannelClose.Account = accountOne.Address;
            paymentChannelClose.Channel = channelId;
            paymentChannelClose.Flags = PaymentChannelClaimFlags.Close;
            var (_, transactionResponse3) = await SubmitTransaction(accountOne.Secret, paymentChannelClose);
            var pccloser = Assert.IsType<PaymentChannelClaimTransaction>(transactionResponse3.Transaction);
            Assert.Equal(accountOne.Address, pccloser.Account);
            Assert.Equal(channelId, pccloser.Channel);
            Assert.Equal(PaymentChannelClaimFlags.Close, pccloser.Flags);


        }
    }
}