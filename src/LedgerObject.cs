using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using Ibasa.Ripple.St;

namespace Ibasa.Ripple
{
    public abstract class LedgerObject
    {
        /// <summary>
        /// Each object in a ledger's state data has a unique ID. 
        /// The ID is derived by hashing important contents of the object, along with a namespace identifier.
        /// The ledger object type determines which namespace identifier to use and which contents to include in the hash.
        /// This ensures every ID is unique.
        /// To calculate the hash, rippled uses SHA-512 and then truncates the result to the first 256 bits.
        /// This algorithm, informally called SHA-512Half, provides an output that has comparable security to SHA-256, but runs faster on 64-bit processors.
        /// 
        /// Generally, a ledger object's ID is returned as the index field in JSON, at the same level as the object's contents.
        /// In transaction metadata, the ledger object's ID in JSON is LedgerIndex.
        /// </summary>
        private protected static Hash256 CalculateId(ReadOnlySpan<byte> data)
        {
            using (var sha512 = System.Security.Cryptography.SHA512.Create())
            {
                Span<byte> hashBuffer = stackalloc byte[64];
                sha512.TryComputeHash(data, hashBuffer, out var bytesWritten);
                return new Hash256(hashBuffer);
            }
        }

        private protected static AccountId ToAccountId(Hash160 hash)
        {
            Span<byte> bytes = stackalloc byte[20];
            hash.CopyTo(bytes);
            return new AccountId(bytes);
        }
        private protected static CurrencyCode ToCurrencyCode(Hash160 hash)
        {
            Span<byte> bytes = stackalloc byte[20];
            hash.CopyTo(bytes);
            return new CurrencyCode(bytes);
        }

        public static LedgerObject ReadSt(ref StReader reader)
        {
            var fieldId = reader.ReadFieldId();
            if(fieldId.TypeCode != StTypeCode.UInt16 || fieldId.FieldCode != 1)
            {
                throw new RippleException(
                    string.Format("Expected LedgerEntryType field, got {0}", fieldId));
            }
            var type = (StLedgerEntryType)reader.ReadUInt16();

            switch (type)
            {
                case StLedgerEntryType.AccountRoot:
                    return new AccountRootLedgerEntry(ref reader);
                case StLedgerEntryType.DirectoryNode:
                    return new DirectoryNodeLedgerEntry(ref reader);
                case StLedgerEntryType.RippleState:
                    return new RippleStateLedgerEntry(ref reader);
                case StLedgerEntryType.Ticket:
                    return new TicketLedgerEntry(ref reader);
                case StLedgerEntryType.SignerList:
                    return new SignerListLedgerEntry(ref reader);
                case StLedgerEntryType.Offer:
                    return new OfferLedgerEntry(ref reader);
                case StLedgerEntryType.LedgerHashes:
                    return new LedgerHashesLedgerEntry(ref reader);
                case StLedgerEntryType.Amendments:
                    return new AmendmentsLedgerEntry(ref reader);
                case StLedgerEntryType.FeeSettings:
                    return new FeeSettingsLedgerEntry(ref reader);
                case StLedgerEntryType.Escrow:
                    return new EscrowLedgerEntry(ref reader);
                case StLedgerEntryType.PayChannel:
                    return new PayChannelLedgerEntry(ref reader);
                case StLedgerEntryType.DepositPreauth:
                    return new DepositPreauthLedgerEntry(ref reader);
                case StLedgerEntryType.Check:
                    return new CheckLedgerEntry(ref reader);
                case StLedgerEntryType.NegativeUNL:
                    return new NegativeUNLLedgerEntry(ref reader);
                case StLedgerEntryType.NFTokenOffer:
                    return new NFTokenOfferLedgerEntry(ref reader);
                case StLedgerEntryType.NFTokenPage:
                    return new NFTokenPageLedgerEntry(ref reader);
            }

            throw new RippleException(string.Format("Unrecognized ledger entry type: {0}", type));
        }

        public static LedgerObject ReadJson(JsonElement json)
        {
            var type = json.GetProperty("LedgerEntryType").GetString();

            switch (type)
            {
                case "AccountRoot":
                    return new AccountRootLedgerEntry(json);
                case "DirectoryNode":
                    return new DirectoryNodeLedgerEntry(json);
                case "RippleState":
                    return new RippleStateLedgerEntry(json);
                case "Ticket":
                    return new TicketLedgerEntry(json);
                case "SignerList":
                    return new SignerListLedgerEntry(json);
                case "Offer":
                    return new OfferLedgerEntry(json);
                case "LedgerHashes":
                    return new LedgerHashesLedgerEntry(json);
                case "Amendments":
                    return new AmendmentsLedgerEntry(json);
                case "FeeSettings":
                    return new FeeSettingsLedgerEntry(json);
                case "Escrow":
                    return new EscrowLedgerEntry(json);
                case "PayChannel":
                    return new PayChannelLedgerEntry(json);
                case "DepositPreauth":
                    return new DepositPreauthLedgerEntry(json);
                case "Check":
                    return new CheckLedgerEntry(json);
                case "NegativeUNL":
                    return new NegativeUNLLedgerEntry(json);
                case "NFTokenOffer":
                    return new NFTokenOfferLedgerEntry(json);
                case "NFTokenPage":
                    return new NFTokenPageLedgerEntry(json);
            }

            throw new RippleException(string.Format("Unrecognized ledger entry type: {0}", type));
        }
    }

    public sealed partial class DirectoryNodeLedgerEntry : LedgerObject
    {
        public static Hash256 CalculateOwnerId(AccountId account)
        {
            Span<byte> buffer = stackalloc byte[22];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x004F);
            account.CopyTo(buffer.Slice(2));
            return CalculateId(buffer);
        }

        public static Hash256 CalculateOfferId(CurrencyCode takerPaysCurrency, CurrencyCode takerGetsCurrency, AccountId takerPaysIssuer, AccountId takerGetsIssuer, ulong offers)
        {
            // The first page of an Offer Directory has a special ID: the higher 192 bits define the order book, and the remaining 64 bits define the exchange rate of the offers in that directory.
            // (The ID is big-endian, so the book is in the more significant bits, which come first, and the quality is in the less significant bits which come last.) 
            // This provides a way to iterate through an order book from best offers to worst.Specifically: the first 192 bits are the first 192 bits of the SHA-512Half of the following values, concatenated in order:
            //
            //The Book Directory space key(0x0042)
            //The 160-bit currency code from the TakerPaysCurrency
            //The 160-bit currency code from the TakerGetsCurrency
            //The AccountID from the TakerPaysIssuer
            //The AccountID from the TakerGetsIssuer
            //The lower 64 bits of an Offer Directory's ID represent the TakerPays amount divided by TakerGets amount from the offer(s) in that directory as a 64-bit number in the XRP Ledger's internal amount format.
            //
            Span<byte> buffer = stackalloc byte[38];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0042);
            takerPaysCurrency.CopyTo(buffer.Slice(2));
            takerGetsCurrency.CopyTo(buffer.Slice(22));
            takerPaysIssuer.CopyTo(buffer.Slice(42));
            takerGetsIssuer.CopyTo(buffer.Slice(62));
            Hash256 hash = CalculateId(buffer);
            return hash;
        }

        public static Hash256 CalculatePage(Hash256 root, uint page)
        {
            Span<byte> buffer = stackalloc byte[38];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0064);
            root.CopyTo(buffer.Slice(2));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(34), page);
            return CalculateId(buffer);
        }
    }

    public sealed partial class RippleStateLedgerEntry : LedgerObject
    {
        public Hash256 ID => CalculateId(LowLimit.Issuer, HighLimit.Issuer, Balance.CurrencyCode);

        public static Hash256 CalculateId(AccountId low, AccountId high, CurrencyCode currencyCode)
        {
            Span<byte> buffer = stackalloc byte[62];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0072);
            low.CopyTo(buffer.Slice(2));
            high.CopyTo(buffer.Slice(22));
            currencyCode.CopyTo(buffer.Slice(42));
            return CalculateId(buffer);
        }
    }

    public sealed partial class PayChannelLedgerEntry : LedgerObject
    {
        public static Hash256 CalculateId(AccountId source, AccountId destination, uint sequence)
        {
            Span<byte> buffer = stackalloc byte[46];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0078);
            source.CopyTo(buffer.Slice(2));
            destination.CopyTo(buffer.Slice(22));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(42), sequence);
            return CalculateId(buffer);
        }
    }


    public sealed partial class OfferLedgerEntry : LedgerObject
    {
        public Hash256 ID => CalculateId(Account, Sequence);

        public static Hash256 CalculateId(AccountId account, uint sequence)
        {
            Span<byte> buffer = stackalloc byte[26];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x006F);
            account.CopyTo(buffer.Slice(2));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(22), sequence);
            return CalculateId(buffer);
        }
    }

    public sealed partial class EscrowLedgerEntry : LedgerObject
    {
        public static Hash256 CalculateId(AccountId account, uint sequence)
        {
            Span<byte> buffer = stackalloc byte[26];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0075);
            account.CopyTo(buffer.Slice(2));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(22), sequence);
            return CalculateId(buffer);
        }
    }

    public sealed partial class LedgerHashesLedgerEntry : LedgerObject
    {
        //public Hash256 ID => CalculateId()
        public static Hash256 CalculateId(AccountId account, uint sequence)
        {
            //There are two formats for LedgerHashes object IDs, depending on whether the object is a "recent history" sub-type or a "previous history" sub-type.
            //The "recent history" LedgerHashes object has an ID that is the SHA - 512Half of the LedgerHashes space key(0x0073). In other words, the "recent history" always has the ID B4979A36CDC7F3D3D5C31A4EAE2AC7D7209DDA877588B9AFC66799692AB0D66B.
            //The "previous history" LedgerHashes objects have an ID that is the SHA - 512Half of the following values, concatenated in order:
            //The LedgerHashes space key(0x0073)
            //The 32 - bit Ledger Index of a flag ledger in the object's Hashes array, divided by 65536.
            //Tip:
            //Dividing by 65536 keeps the most significant 16 bits, which are the same for all the flag ledgers listed in a "previous history" object, and only those ledgers.You can use this fact to look up the LedgerHashes object that contains the hash of any flag ledger.
            throw new NotImplementedException();
        }
    }

    public sealed partial class FeeSettingsLedgerEntry : LedgerObject
    {
        public static Hash256 ID => new Hash256("4BC50C9B0D8515D3EAAE1E74B29A95804346C491EE1A95BF25E4AAB854A6A651");
    }

    public sealed partial class NegativeUNLLedgerEntry : LedgerObject
    {
        public static Hash256 ID => new Hash256("2E8A59AA9D3B5B186B0B9E0F62E6C02587CA74A4D778938E957B6357D364B244");
    }

    public sealed partial class AmendmentsLedgerEntry : LedgerObject
    {
        public static Hash256 ID => new Hash256("7DB0788C020F02780A673DC74757F23823FA3014C1866E72CC4CD8B226CD6EF4");
    }

    public sealed partial class DepositPreauthLedgerEntry : LedgerObject
    {
        public Hash256 ID => CalculateId(Account, Authorize);

        public static Hash256 CalculateId(AccountId owner, AccountId preauthorized)
        {
            Span<byte> buffer = stackalloc byte[42];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0070);
            owner.CopyTo(buffer.Slice(2));
            preauthorized.CopyTo(buffer.Slice(22));
            return CalculateId(buffer);
        }
    }

    public sealed partial class CheckLedgerEntry : LedgerObject
    {
        public static Hash256 CalculateId(AccountId account, uint sequence)
        {
            Span<byte> buffer = stackalloc byte[2+ 20 + 4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0043);
            account.CopyTo(buffer.Slice(2));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(22), sequence);
            return CalculateId(buffer);
        }

        public Hash256 ID => CalculateId(Account, Sequence);
    }

    public sealed partial class SignerListLedgerEntry : LedgerObject
    {
        public static Hash256 CalculateId(AccountId account, uint signerListID)
        {
            Span<byte> buffer = stackalloc byte[2 + 20 + 4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0053);
            account.CopyTo(buffer.Slice(2));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(22), signerListID);
            return CalculateId(buffer);
        }
    }

    public sealed partial class TicketLedgerEntry : LedgerObject
    {
        public static Hash256 CalculateId(AccountId account, uint ticketSequence)
        {
            Span<byte> buffer = stackalloc byte[2 + 20 + 4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0054);
            account.CopyTo(buffer.Slice(2));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(22), ticketSequence);
            return CalculateId(buffer);
        }

        public Hash256 ID => CalculateId(Account, TicketSequence);
    }


    public sealed partial class AccountRootLedgerEntry : LedgerObject
    {
        public static Hash256 CalculateId(AccountId account)
        {
            Span<byte> buffer = stackalloc byte[22];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x0061);
            account.CopyTo(buffer.Slice(2));
            return CalculateId(buffer);
        }

        public Hash256 ID => CalculateId(Account);
    }

    /// <summary>
    /// Each member of the SignerEntries field is an object that describes that signer in the list.
    /// </summary>
    public struct SignerEntry : IEquatable<SignerEntry>
    {
        /// <summary>
        /// An XRP Ledger address whose signature contributes to the multi-signature.
        /// It does not need to be a funded address in the ledger.
        /// </summary>
        public AccountId Account { get; private set; }

        /// <summary>
        /// The weight of a signature from this signer.
        /// A multi-signature is only valid if the sum weight of the signatures provided meets or exceeds the signer list's SignerQuorum value.
        /// </summary>
        public UInt16 SignerWeight { get; private set; }

        public SignerEntry(JsonElement json)
        {
            var entry = json.GetProperty("SignerEntry");
            Account = new AccountId(entry.GetProperty("Account").GetString());
            SignerWeight = entry.GetProperty("SignerWeight").GetUInt16();
        }

        public SignerEntry(ref StReader reader)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId != StFieldId.UInt16_SignerWeight)
            {
                throw new Exception(string.Format("Could not read SignerEntry, expected {0} but got {1}", StFieldId.UInt16_SignerWeight, fieldId));
            }
            SignerWeight = reader.ReadUInt16();
            fieldId = reader.ReadFieldId();
            if (fieldId == StFieldId.Hash256_WalletLocator)
            {
                var _ = reader.ReadHash256();
                if (!reader.TryReadFieldId(out fieldId))
                {
                    throw new Exception("Could not read SignerEntry, end of st data reached but non-optional fields still not set");
                }
            }
            if (fieldId != StFieldId.AccountID_Account)
            {
                throw new Exception(string.Format("Could not read SignerEntry, expected {0} but got {1}", StFieldId.AccountID_Account, fieldId));
            }
            Account = reader.ReadAccount();
            fieldId = reader.ReadFieldId();
            if (fieldId != StFieldId.Object_ObjectEndMarker)
            {
                throw new Exception(string.Format("Could not read SignerEntry, expected {0} but got {1}", StFieldId.Object_ObjectEndMarker, fieldId));
            }
        }

        public void WriteTo(ref StWriter writer)
        {
            writer.WriteStartObject(StObjectFieldCode.SignerEntry);
            writer.WriteUInt16(StUInt16FieldCode.SignerWeight, SignerWeight);
            writer.WriteAccount(StAccountIDFieldCode.Account, Account);
            writer.WriteEndObject();
        }

        public SignerEntry(AccountId account, UInt16 signerWeight)
        {
            Account = account;
            SignerWeight = signerWeight;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Account, SignerWeight);
        }

        public override bool Equals(object obj)
        {
            if (obj is SignerEntry)
            {
                return Equals((SignerEntry)obj);
            }
            return false;
        }

        public bool Equals(SignerEntry other)
        {
            return Account == other.Account && SignerWeight == other.SignerWeight;
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize<SignerEntry>(this);
        }
    }

    /// <summary>
    /// Each DisabledValidator object represents one disabled validator. In JSON, a DisabledValidator object has one field, DisabledValidator, which in turn contains another object with the following fields:
    /// </summary>
    public struct DisabledValidator : IEquatable<DisabledValidator>
    {
        /// <summary>
        /// The ledger index when the validator was added to the Negative UNL.
        /// </summary>
        public uint FirstLedgerSequence { get; private set; }

        /// <summary>
        /// The master public key of the validator, in hexadecimal.
        /// </summary>
        public ReadOnlyMemory<byte> PublicKey { get; private set; }

        public DisabledValidator(JsonElement json)
        {
            var entry = json.GetProperty("DisabledValidator");
            FirstLedgerSequence = entry.GetProperty("FirstLedgerSequence").GetUInt32();
            PublicKey = entry.GetProperty("PublicKey").GetBytesFromBase16();
        }

        public DisabledValidator(ref StReader reader)
        {
            FirstLedgerSequence = reader.ReadUInt32();
            PublicKey = reader.ReadBlob();
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(FirstLedgerSequence, PublicKey);
        }

        public override bool Equals(object obj)
        {
            if (obj is DisabledValidator)
            {
                return Equals((DisabledValidator)obj);
            }
            return false;
        }

        public bool Equals(DisabledValidator other)
        {
            return FirstLedgerSequence == other.FirstLedgerSequence && PublicKey.Equals(other.PublicKey);
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize<DisabledValidator>(this);
        }
    }

    /// <summary>
    /// Object describing the status of amendments that have majority support but are not yet enabled.
    /// </summary>
    public struct Majority
    {
        /// <summary>
        /// The Amendment ID of the pending amendment.
        /// </summary>
        public Hash256 Amendment { get; private set; }

        /// <summary>
        /// The close_time field of the ledger version where this amendment most recently gained a majority.
        /// </summary>
        public DateTimeOffset CloseTime { get; private set; }

        public Majority(JsonElement json)
        {
            var entry = json.GetProperty("Majority");
            CloseTime = Epoch.ToDateTimeOffset(entry.GetProperty("CloseTime").GetUInt32());
            Amendment = new Hash256(entry.GetProperty("Amendment").GetString());
        }

        public Majority(ref StReader reader)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId != StFieldId.UInt32_CloseTime)
            {
                throw new Exception(string.Format("Could not read Majority, expected {0} but got {1}", StFieldId.UInt32_CloseTime, fieldId));
            }
            CloseTime = Epoch.ToDateTimeOffset(reader.ReadUInt32());
            fieldId = reader.ReadFieldId();
            if (fieldId != StFieldId.Hash256_Amendment)
            {
                throw new Exception(string.Format("Could not read Majority, expected {0} but got {1}", StFieldId.Hash256_Amendment, fieldId));
            }
            Amendment = reader.ReadHash256();
            fieldId = reader.ReadFieldId();
            if (fieldId != StFieldId.Object_ObjectEndMarker)
            {
                throw new Exception(string.Format("Could not read Majority, expected {0} but got {1}", StFieldId.Object_ObjectEndMarker, fieldId));
            }
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(CloseTime, Amendment);
        }

        public override bool Equals(object obj)
        {
            if (obj is Majority)
            {
                return Equals((Majority)obj);
            }
            return false;
        }

        public bool Equals(Majority other)
        {
            return CloseTime == other.CloseTime && Amendment == other.Amendment;
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize<Majority>(this);
        }
    }


    public sealed class LedgerHeader
    {
        /// <summary>
        /// The ledger index of the ledger.
        /// </summary>
        public uint Sequence { get; private set; }

        /// <summary>
        /// The SHA-512Half of this ledger's state tree information.
        /// </summary>
        public Hash256 AccountHash { get; private set; }

        /// <summary>
        /// The approximate time this ledger version closed. This value is rounded based on the close_time_resolution.
        /// </summary>
        public DateTimeOffset CloseTime { get; private set; }

        /// <summary>
        /// The ledger_hash value of the previous ledger version that is the direct predecessor of this one.
        /// If there are different versions of the previous ledger index, this indicates from which one the ledger was derived.
        /// </summary>
        public Hash256 ParentHash { get; private set; }

        /// <summary>
        /// The approximate time the parnet ledger version closed.
        /// </summary>
        public DateTimeOffset ParentCloseTime { get; private set; }

        /// <summary>
        /// The total number of drops of XRP owned by accounts in the ledger. 
        /// This omits XRP that has been destroyed by transaction fees. 
        /// The actual amount of XRP in circulation is lower because some accounts are "black holes" whose keys are not known by anyone.
        /// </summary>
        public ulong TotalCoins { get; private set; }

        /// <summary>
        /// The SHA-512Half of the transactions included in this ledger.
        /// </summary>
        public Hash256 TransactionHash { get; private set; }

        /// <summary>
        /// An integer in the range [2,120] indicating the maximum number of seconds by which the close_time could be rounded.
        /// </summary>
        public byte CloseTimeResolution { get; private set; }

        /// <summary>
        /// A bit-map of flags relating to the closing of this ledger.
        /// </summary>
        public int CloseFlags { get; private set; }

        public LedgerHeader(StReader reader)
        {
            Sequence = reader.ReadUInt32();
            TotalCoins = reader.ReadUInt64();
            ParentHash = reader.ReadHash256();
            TransactionHash = reader.ReadHash256();
            AccountHash = reader.ReadHash256();
            ParentCloseTime = Epoch.ToDateTimeOffset(reader.ReadUInt32());
            CloseTime = Epoch.ToDateTimeOffset(reader.ReadUInt32());
            CloseTimeResolution = reader.ReadUInt8();
            CloseFlags = reader.ReadUInt8();
        }
    }
}