using System;
using System.Threading.Tasks;
using System.Linq;

namespace Ibasa.Ripple.Tests
{
    public static class Utils
    {
        /// <summary>
        /// Wait for the account to exist in a validated ledger, then return current information.
        /// </summary>
        public static async Task<AccountInfoResponse> WaitForAccount(Api api, AccountId account)
        {
            var terminationTimeout = DateTime.UtcNow + TimeSpan.FromMinutes(5.0);

            var infoRequest = new AccountInfoRequest()
            {
                Ledger = LedgerSpecification.Validated,
                Account = account,
            };
            AccountInfoResponse infoResponse = null;
            while (infoResponse == null)
            {
                try
                {
                    infoResponse = await api.AccountInfo(infoRequest);
                }
                catch (RippleRequestException exc)
                {
                    if (DateTime.UtcNow > terminationTimeout)
                    {
                        throw new Exception(string.Format("Could not find account {0} within 5 minutes", account));
                    }

                    if (exc.Error != "actNotFound") { throw; }
                }

                if (infoResponse == null)
                {
                    System.Threading.Thread.Sleep(1000);
                }
            }

            infoRequest.Ledger = LedgerSpecification.Current;
            return await api.AccountInfo(infoRequest);
        }

        public static async Task<AccountInfoResponse[]> WaitForAccounts(Api api, params AccountId[] accounts)
        {
            var results = new AccountInfoResponse[accounts.Length];
            for (var i = 0; i < accounts.Length; ++i)
            {
                results[i] = await WaitForAccount(api, accounts[i]);
            }
            return results;
        }

        public static Task<AccountInfoResponse[]> WaitForAccounts(Api api, params TestAccount[] accounts)
        {
            return WaitForAccounts(api, accounts.Select(x => x.Address).ToArray());
        }
    }
}
