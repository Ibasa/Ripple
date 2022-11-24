using System;
using System.Buffers;
using System.Collections.ObjectModel;
using System.Text.Json;
using Ibasa.Ripple.St;

namespace Ibasa.Ripple
{
    /// <summary>
    /// The Signers field contains a multi-signature, which has signatures from up to 8 key pairs, that together should authorize the transaction.
    /// </summary>
    public struct Signer
    {
        /// <summary>
        /// The address associated with this signature, as it appears in the signer list.
        /// </summary>
        public AccountId Account { get; set; }

        /// <summary>
        /// A signature for this transaction, verifiable using the SigningPubKey.
        /// </summary>
        public ReadOnlyMemory<byte> TxnSignature { get; set; }

        /// <summary>
        /// The public key used to create this signature.
        /// </summary>
        public ReadOnlyMemory<byte> SigningPubKey { get; set; }

        internal Signer(JsonElement json)
        {
            var signer = json.GetProperty("Signer");
            Account = new AccountId(signer.GetProperty("Account").GetString());
            SigningPubKey = signer.GetProperty("SigningPubKey").GetBytesFromBase16();
            TxnSignature = signer.GetProperty("TxnSignature").GetBytesFromBase16();
        }

        internal Signer(ref StReader reader) { throw new NotImplementedException(); }

        internal void WriteTo(ref StWriter writer)
        {
            writer.WriteStartObject(StObjectFieldCode.Signer);
            writer.WriteBlob(StBlobFieldCode.SigningPubKey, SigningPubKey.Span);
            writer.WriteBlob(StBlobFieldCode.TxnSignature, TxnSignature.Span);
            writer.WriteAccount(StAccountIDFieldCode.Account, Account);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// The Memos field includes arbitrary messaging data with the transaction.
    /// </summary>
    public struct Memo
    {
        /// <summary>
        /// Arbitrary hex value, conventionally containing the content of the memo.
        /// </summary>
        public ReadOnlyMemory<byte> MemoData { get; }

        /// <summary>
        /// Hex value representing characters allowed in URLs.
        /// Conventionally containing information on how the memo is encoded, for example as a MIME type.
        /// </summary>
        public string MemoFormat { get; }

        /// <summary>
        /// Hex value representing characters allowed in URLs.
        /// Conventionally, a unique relation (according to RFC 5988 ) that defines the format of this memo.
        /// </summary>
        public string MemoType { get; }

        private static string _allowedChars =
                    "0123456789" +
                    "-._~:/?#[]@!$&'()*+,;=%" +
                    "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                    "abcdefghijklmnopqrstuvwxyz";

        private static void Validate(string value, string paramName)
        {
            foreach (var c in value)
            {
                if (!_allowedChars.Contains(c))
                {
                    var message = String.Format("The character '{0}' is not valid", c);
                    throw new ArgumentException(message, paramName);
                }
            }
        }

        public Memo(ReadOnlyMemory<byte> memoData, string memoFormat, string memoType)
        {
            Validate(memoFormat, nameof(memoFormat));
            Validate(memoType, nameof(memoType));

            MemoData = memoData;
            MemoFormat = memoFormat;
            MemoType = memoType;
        }

        internal Memo(JsonElement json) { throw new NotImplementedException(); }

        internal Memo(ref StReader reader) { throw new NotImplementedException(); }

        internal void WriteTo(ref StWriter writer)
        {
            writer.WriteStartObject(StObjectFieldCode.Memo);

            Span<byte> bytes = stackalloc byte[
                Math.Max(MemoFormat.Length, MemoType.Length)];

            System.Text.Encoding.ASCII.GetBytes(MemoType, bytes);
            writer.WriteBlob(StBlobFieldCode.MemoType, bytes.Slice(0, MemoType.Length));

            writer.WriteBlob(StBlobFieldCode.MemoData, MemoData.Span);

            System.Text.Encoding.ASCII.GetBytes(MemoFormat, bytes);
            writer.WriteBlob(StBlobFieldCode.MemoFormat, bytes.Slice(0, MemoFormat.Length));

            writer.WriteEndObject();
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MemoData, MemoFormat, MemoType);
        }

        public override bool Equals(object obj)
        {
            if (obj is Memo)
            {
                return Equals((Memo)obj);
            }
            return false;
        }

        public bool Equals(Memo other)
        {
            return
                MemoData.Equals(other.MemoData) &&
                MemoFormat.Equals(other.MemoFormat) &&
                MemoType.Equals(other.MemoType);
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize<Memo>(this);
        }
    }

    public struct PathElement
    {
        /// <summary>
        /// (Optional) If present, this path step represents rippling through the specified address.
        /// MUST NOT be provided if this step specifies the currency or issuer fields.
        /// </summary>
        public AccountId? Account { get; }

        /// <summary>
        /// (Optional) If present, this path step represents changing currencies through an order book.
        /// The currency specified indicates the new currency.
        /// MUST NOT be provided if this step specifies the account field.
        /// </summary>
        public CurrencyCode? Currency { get; }

        /// <summary>
        /// (Optional) If present, this path step represents changing currencies and this address defines the issuer of the new currency.If omitted in a step with a non-XRP currency, a previous step of the path defines the issuer.If present when currency is omitted, indicates a path step that uses an order book between same-named currencies with different issuers.
        /// MUST be omitted if the currency is XRP.
        /// MUST NOT be provided if this step specifies the account field.
        /// </summary>
        public AccountId? Issuer { get; }

        private PathElement(AccountId? account, CurrencyCode? currency, AccountId? issuer)
        {
            Account = account;
            Currency = currency;
            Issuer = issuer;
        }

        public static PathElement FromAccount(AccountId account)
        {
            return new PathElement(account, null, null);
        }

        public static PathElement FromIssuer(AccountId issuer)
        {
            return new PathElement(null, null, issuer);
        }

        public static PathElement FromCurrency(CurrencyCode currency)
        {
            return new PathElement(null, currency, null);
        }

        public static PathElement FromIssuedCurrency(AccountId issuer, CurrencyCode currency)
        {
            return new PathElement(null, currency, issuer);
        }

        public PathElement(JsonElement json)
        {
            JsonElement element;
            if (json.TryGetProperty("account", out element))
            {
                Account = new AccountId(element.GetString());
            }
            else
            {
                Account = default;
            }
            if (json.TryGetProperty("currency", out element))
            {
                Currency = new CurrencyCode(element.GetString());
            }
            else
            {
                Currency = default;
            }
            if (json.TryGetProperty("issuer", out element))
            {
                Issuer = new AccountId(element.GetString());
            }
            else
            {
                Issuer = default;
            }
        }
    }

    public sealed class Path : ReadOnlyCollection<PathElement>
    {
        private static PathElement[] Parse(JsonElement element)
        {
            var path = new PathElement[element.GetArrayLength()];
            for (var i = 0; i < path.Length; ++i)
            {
                path[i] = new PathElement(element[i]);
            }
            return path;
        }

        public Path(JsonElement element)
            : base(Parse(element))
        {
        }

        public Path(System.Collections.Generic.IEnumerable<PathElement> path)
            : base(System.Linq.Enumerable.ToArray(path))
        {
        }

        public Path(params PathElement[] path)
            : base(path)
        {
        }
    }


    public sealed class PathSet : ReadOnlyCollection<Path>
    {
        private static Path[] Parse(JsonElement element)
        {
            var paths = new Path[element.GetArrayLength()];
            for (var i = 0; i < paths.Length; ++i)
            {
                paths[i] = new Path(element[i]);
            }
            return paths;
        }

        public PathSet(JsonElement element) 
            : base(Parse(element))
        {
        }

        public PathSet(System.Collections.Generic.IEnumerable<Path> paths) 
            : base(System.Linq.Enumerable.ToArray(paths))
        {
        }

        public PathSet(params Path[] paths)
            : base(paths)
        {
        }
    }

    public abstract partial class Transaction
    {
        /// <summary>
        /// Set of bit-flags for this transaction.
        /// </summary>
        public uint Flags { get; set; }

        /// <summary>
        /// (Optional) Hash value identifying another transaction.
        /// If provided, this transaction is only valid if the sending account's previously-sent transaction matches the provided hash.
        /// </summary>
        public Hash256? AccountTxnID { get; set; }

        /// <summary>
        /// The unique address of the account that initiated the transaction.
        /// </summary>
        public AccountId Account { get; set; }

        /// <summary>
        /// Integer amount of XRP, in drops, to be destroyed as a cost for distributing this transaction to the network. 
        /// Some transaction types have different minimum requirements.
        /// See Transaction Cost for details.
        /// </summary>
        public XrpAmount Fee { get; set; }

        /// <summary>
        /// The sequence number of the account sending the transaction. 
        /// A transaction is only valid if the Sequence number is exactly 1 greater than the previous transaction from the same account.
        /// </summary>
        public uint Sequence { get; set; }

        /// <summary>
        /// (Automatically added when signing) Hex representation of the public key that corresponds to the private key used to sign this transaction.
        /// If an empty string, indicates a multi-signature is present in the Signers field instead.
        /// </summary>
        public ReadOnlyMemory<byte> SigningPubKey { get; set; }

        /// <summary>
        /// (Optional) Additional arbitrary information used to identify this transaction.
        /// </summary>
        public ReadOnlyCollection<Memo> Memos { get; set; }

        /// <summary>
        /// (Optional) Array of objects that represent a multi-signature which authorizes this transaction.
        /// </summary>
        public ReadOnlyCollection<Signer> Signers { get; set; }

        /// <summary>
        /// (Automatically added when signing) The signature that verifies this transaction as originating from the account it says it is from.
        /// </summary>
        public ReadOnlyMemory<byte>? TxnSignature { get; set; }

        /// <summary>
        /// (Optional; strongly recommended) Highest ledger index this transaction can appear in.
        /// Specifying this field places a strict upper limit on how long the transaction can wait to be validated or rejected.
        /// See Reliable Transaction Submission for more details.
        /// </summary>
        public UInt32? LastLedgerSequence { get; set; }

        /// <summary>
        /// (Optional) Arbitrary integer used to identify the reason for this payment, or a sender on whose behalf this transaction is made.
        /// Conventionally, a refund should specify the initial payment's SourceTag as the refund payment's DestinationTag.
        /// </summary>
        public UInt32? SourceTag { get; set; }

        /// <summary>
        /// (Optional) The sequence number of the ticket to use in place of a Sequence number.
        /// If this is provided, Sequence must be 0.
        /// Cannot be used with AccountTxnID.
        /// </summary>
        public UInt32? TicketSequence { get; set; }


        public Transaction()
        {

        }

        private protected Transaction(JsonElement json)
        {
            JsonElement element;

            Account = new AccountId(json.GetProperty("Account").GetString());
            Fee = XrpAmount.ReadJson(json.GetProperty("Fee"));
            Sequence = json.GetProperty("Sequence").GetUInt32();
            if (json.TryGetProperty("AccountTxnID", out element))
            {
                AccountTxnID = new Hash256(element.GetString());
            }
            if (json.TryGetProperty("Flags", out element))
            {
                Flags = element.GetUInt32();
            }
            if (json.TryGetProperty("LastLedgerSequence", out element))
            {
                LastLedgerSequence = element.GetUInt32();
            }
            if (json.TryGetProperty("Memos", out element))
            {
                var MemosArray = new Memo[element.GetArrayLength()];
                for (int i = 0; i < MemosArray.Length; ++i)
                {
                    MemosArray[i] = new Memo(element[i]);
                }
                Memos = Array.AsReadOnly(MemosArray);
            }
            if (json.TryGetProperty("Signers", out element))
            {
                var SignersArray = new Signer[element.GetArrayLength()];
                for (int i = 0; i < SignersArray.Length; ++i)
                {
                    SignersArray[i] = new Signer(element[i]);
                }
                Signers = Array.AsReadOnly(SignersArray);
            }
            if (json.TryGetProperty("SourceTag", out element))
            {
                SourceTag = element.GetUInt32();
            }
            if (json.TryGetProperty("SigningPubKey", out element))
            {
                SigningPubKey = element.GetBytesFromBase16();
            }
            if (json.TryGetProperty("TicketSequence", out element))
            {
                TicketSequence = element.GetUInt32();
            }
            if (json.TryGetProperty("TxnSignature", out element))
            {
                TxnSignature = element.GetBytesFromBase16();
            }
        }

        private ReadOnlyMemory<byte> Serialize(bool forSigning)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            Serialize(bufferWriter, forSigning);
            return bufferWriter.WrittenMemory;
        }

        private protected abstract void Serialize(IBufferWriter<byte> bufferWriter, bool forSigning);

        private const uint hpTXN = 0x54584E00u; // TXN
        private const uint hpSTX = 0x53545800u; // STX
        private const uint hpSMT = 0x534D5400u; // SMT

        public ReadOnlyMemory<byte> Sign(KeyPair keyPair, out Hash256 hash)
        {
            Signers = null;
            TxnSignature = null;
            SigningPubKey = keyPair.PublicKey.GetCanoncialBytes();
            var bufferWriter = new ArrayBufferWriter<byte>();

            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bufferWriter.GetSpan(4), hpSTX);
            bufferWriter.Advance(4);
            Serialize(bufferWriter, true);

            // Calculate signature and serialize again
            TxnSignature = keyPair.Sign(bufferWriter.WrittenSpan);
            bufferWriter.Clear();

            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bufferWriter.GetSpan(4), hpTXN);
            bufferWriter.Advance(4);

            Serialize(bufferWriter, false);

            using (var sha512 = System.Security.Cryptography.SHA512.Create())
            {
                Span<byte> hashSpan = stackalloc byte[64];
                sha512.TryComputeHash(bufferWriter.WrittenSpan, hashSpan, out var bytesWritten);
                hash = new Hash256(hashSpan.Slice(0, 32));
            }

            return bufferWriter.WrittenMemory.Slice(4);
        }

        public ReadOnlyMemory<byte> Sign(ReadOnlySpan<ValueTuple<AccountId, KeyPair>> signers, out Hash256 hash)
        {
            var signerArray = new Signer[signers.Length];
            for (int i = 0; i < signers.Length; ++i)
            {
                var signer = new Signer();
                signer.Account = signers[i].Item1;
                signer.SigningPubKey = signers[i].Item2.PublicKey.GetCanoncialBytes();
                signer.TxnSignature = null;
                signerArray[i] = signer;
            }
            Signers = Array.AsReadOnly(signerArray);
            TxnSignature = null;
            SigningPubKey = null;
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Calculate signatures and then serialize again
            for(int i = 0; i < signers.Length; ++i)
            {
                // For each signer we need to write the account being signed for the the buffer, sign that then rewind
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bufferWriter.GetSpan(4), hpSMT);
                bufferWriter.Advance(4);
                Serialize(bufferWriter, true);
                Signers[i].Account.CopyTo(bufferWriter.GetSpan(20));
                bufferWriter.Advance(20);

                signerArray[i].TxnSignature = signers[i].Item2.Sign(bufferWriter.WrittenSpan);
                bufferWriter.Clear();
            }

            Array.Sort<Signer>(signerArray, (x, y) => x.Account.CompareTo(y.Account));
            Signers = Array.AsReadOnly(signerArray);

            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bufferWriter.GetSpan(4), hpTXN);
            bufferWriter.Advance(4);

            Serialize(bufferWriter, false);

            using (var sha512 = System.Security.Cryptography.SHA512.Create())
            {
                Span<byte> hashSpan = stackalloc byte[64];
                sha512.TryComputeHash(bufferWriter.WrittenSpan, hashSpan, out var bytesWritten);
                hash = new Hash256(hashSpan.Slice(0, 32));
            }

            return bufferWriter.WrittenMemory.Slice(4);
        }
    }

    [Flags]
    public enum AccountSetFlags : uint
    {
        None = 0,

        /// <summary>
        /// Require a destination tag to send transactions to this account.
        /// </summary>
        RequireDest = 1,

        /// <summary>
        /// Require authorization for users to hold balances issued by this address. Can only be enabled if the address has no trust lines connected to it.
        /// </summary>
        RequireAuth = 2,

        /// <summary>
        /// XRP should not be sent to this account. (Enforced by client applications, not by rippled)
        /// </summary>
        DisallowXRP = 3,

        /// <summary>
        /// Disallow use of the master key pair. Can only be enabled if the account has configured another way to sign transactions, such as a Regular Key or a Signer List.
        /// </summary>
        DisableMaster = 4,

        /// <summary>
        /// Track the ID of this account's most recent transaction. Required for AccountTxnID
        /// </summary>
        AccountTxnID = 5,

        /// <summary>
        /// Permanently give up the ability to freeze individual trust lines or disable Global Freeze. This flag can never be disabled after being enabled.
        /// </summary>
        NoFreeze = 6,

        /// <summary>
        /// Freeze all assets issued by this account.
        /// </summary>
        GlobalFreeze = 7,

        /// <summary>
        /// Enable rippling on this account's trust lines by default. New in: rippled 0.27.3
        /// </summary>
        DefaultRipple = 8,

        /// <summary>
        /// Enable Deposit Authorization on this account. (Added by the DepositAuth amendment.)
        /// </summary>
        DepositAuth = 9,
    }

    /// <summary>
    /// Transactions of the TrustSet type support additional values in the Flags field, as follows:
    /// </summary>
    [Flags]
    public enum TrustSetFlags : uint
    {
        None = 0,

        /// <summary>
        /// Authorize the other party to hold currency issued by this account.
        /// (No effect unless using the asfRequireAuth AccountSet flag.)
        /// Cannot be unset.
        /// </summary>
        SetfAuth = 0x00010000,
        /// <summary>
        /// Enable the No Ripple flag, which blocks rippling between two trust lines of the same currency if this flag is enabled on both.
        /// </summary>
        SetNoRipple = 0x00020000,
        /// <summary>
        /// Disable the No Ripple flag, allowing rippling on this trust line.)
        /// </summary>
        ClearNoRipple = 0x00040000,
        /// <summary>
        /// Freeze the trust line.
        /// </summary>
        SetFreeze = 0x00100000,
        /// <summary>
        /// Unfreeze the trust line.
        /// </summary>
        ClearFreeze = 0x00200000,
    }

    public sealed partial class TrustSetTransaction
    {
        public new TrustSetFlags Flags
        {
            get { return (TrustSetFlags)base.Flags; }
            set { base.Flags = (uint)value; }
        }
    }


    [Flags]
    public enum OfferCreateFlags
    {
        None = 0,

        /// <summary>
        /// If enabled, the offer does not consume offers that exactly match it, and instead becomes an Offer object in the ledger.
        /// It still consumes offers that cross it.
        /// </summary>
        Passive = 0x00010000,

        /// <summary>
        /// Treat the offer as an Immediate or Cancel order.
        /// If enabled, the offer never becomes a ledger object: it only tries to match existing offers in the ledger.
        /// If the offer cannot match any offers immediately, it executes "successfully" without trading any currency.
        /// In this case, the transaction has the result code tesSUCCESS, but creates no Offer objects in the ledger.
        /// </summary>
        ImmediateOrCancel = 0x00020000,

        /// <summary>
        /// Treat the offer as a Fill or Kill order.
        /// Only try to match existing offers in the ledger, and only do so if the entire TakerPays quantity can be obtained.
        /// If the fix1578 amendment is enabled and the offer cannot be executed when placed, the transaction has the result code tecKILLED;
        /// otherwise, the transaction uses the result code tesSUCCESS even when it was killed without trading any currency.
        /// </summary>
        FillOrKill = 0x00040000,

        /// <summary>
        /// Exchange the entire TakerGets amount, even if it means obtaining more than the TakerPays amount in exchange.
        /// </summary>
        Sell = 0x00080000,
    }

    public sealed partial class OfferCreateTransaction
    {
        public new OfferCreateFlags Flags
        {
            get { return (OfferCreateFlags)base.Flags; }
            set { base.Flags = (uint)value; }
        }
    }

    [Flags]
    public enum PaymentFlags
    {
        /// <summary>
        /// Do not use the default path; only use paths included in the Paths field.
        /// This is intended to force the transaction to take arbitrage opportunities.
        /// Most clients do not need this.
        /// </summary>
        NoDirectRipple = 0x00010000,

        /// <summary>
        /// If the specified Amount cannot be sent without spending more than SendMax, reduce the received amount instead of failing outright.
        /// See Partial Payments for more details.
        /// </summary>
        PartialPayment = 0x00020000,

        /// <summary>
        /// Only take paths where all the conversions have an input:output ratio that is equal or better than the ratio of Amount:SendMax.
        /// See Limit Quality for details.
        /// </summary>
        LimitQuality = 0x00040000,
    }

    public sealed partial class PaymentTransaction
    {
        public new PaymentFlags Flags
        {
            get { return (PaymentFlags)base.Flags; }
            set { base.Flags = (uint)value; }
        }
    }

    [Flags]
    public enum PaymentChannelClaimFlags
    {
        /// <summary>
        /// Clear the channel's Expiration time.
        /// (Expiration is different from the channel's immutable CancelAfter time.)
        /// Only the source address of the payment channel can use this flag.
        /// </summary>
        Renew = 0x00010000,

        /// <summary>
        /// Request to close the channel.
        /// Only the channel source and destination addresses can use this flag.
        /// This flag closes the channel immediately if it has no more XRP allocated to it after processing the current claim, or if the destination address uses it.
        /// If the source address uses this flag when the channel still holds XRP, this schedules the channel to close after SettleDelay seconds have passed.
        /// (Specifically, this sets the Expiration of the channel to the close time of the previous ledger plus the channel's SettleDelay time, unless the channel already has an earlier Expiration time.)
        /// If the destination address uses this flag when the channel still holds XRP, any XRP that remains after processing the claim is returned to the source address.
        /// </summary>
        Close = 0x00020000,
    }

    public sealed partial class PaymentChannelClaimTransaction
    {
        public new PaymentChannelClaimFlags Flags
        {
            get { return (PaymentChannelClaimFlags)base.Flags; }
            set { base.Flags = (uint)value; }
        }

        private const uint hpCLM = 0x434C4D00u; // CLM

        public static byte[] Authorize(KeyPair keyPair, Hash256 channelId, XrpAmount amount)
        {
            Span<byte> buffer = stackalloc byte[44];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer, hpCLM);
            channelId.CopyTo(buffer.Slice(4));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(36), (ulong)amount.Drops);
            return keyPair.Sign(buffer);
        }

        public static bool Verify(PublicKey publicKey, ReadOnlySpan<byte> signature, Hash256 channelId, XrpAmount amount)
        {
            Span<byte> buffer = stackalloc byte[44];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer, hpCLM);
            channelId.CopyTo(buffer.Slice(4));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(36), (ulong)amount.Drops);
            return publicKey.Verify(buffer, signature);
        }
    }
}