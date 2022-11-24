using System;
using System.Buffers;

namespace Ibasa.Ripple.St
{
    /// <summary>
    /// When you combine a field's type code and field code, you get the field's unique identifier, which is prefixed before the field in the final serialized blob.
    /// The size of the Field ID is one to three bytes depending on the type code and field codes it combines.
    /// </summary>
    public partial struct StFieldId
    {
        public readonly StTypeCode TypeCode;
        public readonly uint FieldCode;

        public StFieldId(StTypeCode typeCode, uint fieldCode)
        {
            TypeCode = typeCode;
            FieldCode = fieldCode;
        }

        public static bool operator ==(StFieldId a, StFieldId b)
        {
            return a.TypeCode == b.TypeCode && a.FieldCode == b.FieldCode;
        }

        public static bool operator !=(StFieldId a, StFieldId b)
        {
            return a.TypeCode != b.TypeCode || a.FieldCode != b.FieldCode;
        }

        public bool Equals(StFieldId other)
        {
            return this == other;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TypeCode, FieldCode);
        }

        public override bool Equals(object obj)
        {
            if (obj is StFieldId)
            {
                return Equals((StFieldId)obj);
            }
            return false;
        }

        public override string ToString()
        {
            return string.Format("({0}, {1})", TypeCode, FieldCode);
        }
    }

    public struct StWriter
    {
        readonly IBufferWriter<byte> bufferWriter;

        public StWriter(IBufferWriter<byte> bufferWriter)
        {
            this.bufferWriter = bufferWriter;
        }

        void WriteFieldId(StTypeCode typeCode, uint fieldCode)
        {
            var iTypeCode = (uint)typeCode;
            if (iTypeCode < 16 && fieldCode < 16)
            {
                var span = bufferWriter.GetSpan(1);
                span[0] = (byte)(iTypeCode << 4 | fieldCode);
                bufferWriter.Advance(1);
            }
            else if (iTypeCode < 16 && fieldCode >= 16)
            {
                var span = bufferWriter.GetSpan(2);
                span[0] = (byte)(iTypeCode << 4);
                span[1] = (byte)fieldCode;
                bufferWriter.Advance(2);
            }
            else if (iTypeCode >= 16 && fieldCode < 16)
            {
                var span = bufferWriter.GetSpan(2);
                span[0] = (byte)fieldCode;
                span[1] = (byte)typeCode;
                bufferWriter.Advance(2);
            }
            else // typeCode >= 16 && fieldCode >= 16
            {
                var span = bufferWriter.GetSpan(3);
                span[0] = 0;
                span[1] = (byte)typeCode;
                span[2] = (byte)fieldCode;
                bufferWriter.Advance(3);
            }
        }

        void WriteLengthPrefix(int length)
        {
            if (length <= 192)
            {
                var span = bufferWriter.GetSpan(1);
                span[0] = (byte)(length);
                bufferWriter.Advance(1);
            }
            else if (length <= 12480)
            {
                var target = length - 193;
                var byte1 = target / 256;
                var byte2 = target - (byte1 * 256);

                // 193 + ((byte1 - 193) * 256) + byte2
                var span = bufferWriter.GetSpan(2);
                span[0] = (byte)(byte1 - 193);
                span[1] = (byte)byte2;
                bufferWriter.Advance(2);
            }
            else
            {
                var target = length - 12481;
                var byte1 = target / 65536;
                var byte2 = (target - (byte1 * 65536)) / 256;
                var byte3 = target - (byte1 * 65536) - (byte2 * 256);

                //12481 + ((byte1 - 241) * 65536) + (byte2 * 256) + byte3
                var span = bufferWriter.GetSpan(3);
                span[0] = (byte)(byte1 - 241);
                span[1] = (byte)byte2;
                span[3] = (byte)byte3;
                bufferWriter.Advance(3);
            }
        }
        public void WriteUInt8(StUInt8FieldCode fieldCode, byte value)
        {
            WriteFieldId(StTypeCode.UInt8, (uint)fieldCode);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bufferWriter.GetSpan(2), value);
            bufferWriter.Advance(2);
        }

        public void WriteUInt16(StUInt16FieldCode fieldCode, ushort value)
        {
            WriteFieldId(StTypeCode.UInt16, (uint)fieldCode);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bufferWriter.GetSpan(2), value);
            bufferWriter.Advance(2);
        }

        public void WriteTransactionType(StTransactionType type)
        {
            WriteFieldId(StTypeCode.UInt16, 2);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bufferWriter.GetSpan(2), (ushort)type);
            bufferWriter.Advance(2);
        }

        public void WriteUInt32(StUInt32FieldCode fieldCode, uint value)
        {
            WriteFieldId(StTypeCode.UInt32, (uint)fieldCode);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bufferWriter.GetSpan(4), value);
            bufferWriter.Advance(4);
        }

        public void WriteAmount(StAmountFieldCode fieldCode, XrpAmount value)
        {
            WriteFieldId(StTypeCode.Amount, (uint)fieldCode);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bufferWriter.GetSpan(8), XrpAmount.ToUInt64Bits(value));
            bufferWriter.Advance(8);
        }

        public void WriteAmount(StAmountFieldCode fieldCode, IssuedAmount value)
        {
            WriteFieldId(StTypeCode.Amount, (uint)fieldCode);
            var span = bufferWriter.GetSpan(48);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bufferWriter.GetSpan(8), Currency.ToUInt64Bits(value.Value));
            value.CurrencyCode.CopyTo(span.Slice(8));
            value.Issuer.CopyTo(span.Slice(28));
            bufferWriter.Advance(48);
        }

        public void WriteAmount(StAmountFieldCode fieldCode, Amount value)
        {
            var xrp = value.XrpAmount;
            if (xrp.HasValue)
            {
                WriteAmount(fieldCode, xrp.Value);
            }
            else
            {
                WriteAmount(fieldCode, value.IssuedAmount.Value);
            }
        }

        /// <summary>
        /// Write byte array as a vector field. value can be null and is treated as an array of length 0.
        /// </summary>
        /// <param name="fieldCode"></param>
        /// <param name="value"></param>
        public void WriteBlob(StBlobFieldCode fieldCode, ReadOnlySpan<byte> value)
        {
            WriteFieldId(StTypeCode.Blob, (uint)fieldCode);
            if (value == null)
            {
                WriteLengthPrefix(0);
            }
            else
            {
                WriteLengthPrefix(value.Length);
                value.CopyTo(bufferWriter.GetSpan(value.Length));
                bufferWriter.Advance(value.Length);
            }
        }

        public void WriteAccount(StAccountIDFieldCode fieldCode, AccountId value)
        {
            WriteFieldId(StTypeCode.AccountID, (uint)fieldCode);
            WriteLengthPrefix(20);
            value.CopyTo(bufferWriter.GetSpan(20));
            bufferWriter.Advance(20);
        }

        public void WriteHash128(StHash128FieldCode fieldCode, Hash128 value)
        {
            WriteFieldId(StTypeCode.Hash128, (uint)fieldCode);
            value.CopyTo(bufferWriter.GetSpan(16));
            bufferWriter.Advance(16);
        }

        public void WriteHash256(StHash256FieldCode fieldCode, Hash256 value)
        {
            WriteFieldId(StTypeCode.Hash256, (uint)fieldCode);
            value.CopyTo(bufferWriter.GetSpan(32));
            bufferWriter.Advance(32);
        }

        public void WriteStartArray(StArrayFieldCode fieldCode)
        {
            WriteFieldId(StTypeCode.Array, (uint)fieldCode);
        }

        public void WriteEndArray()
        {
            WriteFieldId(StTypeCode.Array, 1);
        }

        public void WriteStartObject(StObjectFieldCode fieldCode)
        {
            WriteFieldId(StTypeCode.Object, (uint)fieldCode);
        }

        public void WriteEndObject()
        {
            WriteFieldId(StTypeCode.Object, 1);
        }

        public void WritePathSet(StPathSetFieldCode fieldCode, PathSet value)
        {
            WriteFieldId(StTypeCode.PathSet, (uint)fieldCode);

            var first = true;
            Span<byte> span;

            foreach (var path in value)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    span = bufferWriter.GetSpan(1);
                    span[0] = 0xFF;
                    bufferWriter.Advance(1);
                }

                foreach (var element in path)
                {
                    var flags =
                        (element.Account.HasValue ? 0x01 : 0x00) |
                        (element.Currency.HasValue ? 0x10 : 0x00) |
                        (element.Issuer.HasValue ? 0x20 : 0x00);

                    var size =
                        (element.Account.HasValue ? 20 : 0) +
                        (element.Currency.HasValue ? 20 : 0) +
                        (element.Issuer.HasValue ? 20 : 0) +
                        1;

                    span = bufferWriter.GetSpan(size);

                    span[0] = (byte)flags;

                    var offset = 1;
                    if(element.Account.HasValue)
                    {
                        element.Account.Value.CopyTo(span.Slice(offset));
                        offset += 20;
                    }
                    if (element.Currency.HasValue)
                    {
                        element.Currency.Value.CopyTo(span.Slice(offset));
                        offset += 20;
                    }
                    if (element.Issuer.HasValue)
                    {
                        element.Issuer.Value.CopyTo(span.Slice(offset));
                        offset += 20;
                    }

                    bufferWriter.Advance(size);
                }
            }

            span = bufferWriter.GetSpan(1);
            span[0] = 0x0;
            bufferWriter.Advance(1);
        }
    }

    public ref struct StReader
    {
        readonly ReadOnlySpan<byte> data;
        public int ConsumedBytes { get; private set; }

        public StReader(ReadOnlySpan<byte> stData)
        {
            this.data = stData;
            this.ConsumedBytes = 0;
        }

        public bool TryReadFieldId(out StFieldId field)
        {
            if (data.Length <= ConsumedBytes)
            {
                field = new StFieldId(StTypeCode.NotPresent, 0);
                return false;
            }

            var byte1 = data[ConsumedBytes];
            if(byte1 == 0)
            {
                // low 4 bits == 0 && high 4 bits == 0
                if(data.Length <= ConsumedBytes + 2)
                {
                    field = new StFieldId(StTypeCode.NotPresent, 0);
                    return false;
                }

                field = new StFieldId(
                    (StTypeCode)data[ConsumedBytes + 1],
                    data[ConsumedBytes + 2]);
                ConsumedBytes += 3;
                return true;
            }
            else if (byte1 < 16)
            {
                // low 4 bits <> 0 && high 4 bits == 0
                if (data.Length <= ConsumedBytes + 1)
                {
                    field = new StFieldId(StTypeCode.NotPresent, 0);
                    return false;
                }

                field = new StFieldId(
                    (StTypeCode)data[ConsumedBytes + 1],
                    byte1);
                ConsumedBytes += 2;
                return true;
            }
            else if ((byte1 & 0x0F) == 0 && (byte1 & 0xF0) != 0)
            {
                // low 4 bits == 0 && high 4 bits <> 0
                if (data.Length <= ConsumedBytes + 1)
                {
                    field = new StFieldId(StTypeCode.NotPresent, 0);
                    return false;
                }

                field = new StFieldId(
                    (StTypeCode)(byte1 >> 4),
                    data[ConsumedBytes + 1]);
                ConsumedBytes += 2;
                return true;
            }
            else
            {
                // low 4 bits <> 0 && high 4 bits <> 0
                field = new StFieldId(
                    (StTypeCode)(byte1 >> 4),
                    (uint)byte1 & 0x0F);
                ConsumedBytes += 1;
                return true;
            }
        }

        public StFieldId ReadFieldId()
        {
            if(TryReadFieldId(out var fieldId))
            {
                return fieldId;
            }
            throw new Exception();
        }

        bool TryReadLengthPrefix(out int length)
        {
            if (data.Length <= ConsumedBytes)
            {
                length = 0;
                return false;
            }

            var byte1 = data[ConsumedBytes];
            if(byte1 <= 192)
            {
                ConsumedBytes += 1;
                length = byte1;
                return true;
            } 
            else if(byte1 <= 240)
            {
                if (data.Length <= ConsumedBytes + 1)
                {
                    length = 0;
                    return false;
                }

                var byte2 = data[ConsumedBytes + 1];
                ConsumedBytes += 2;
                length = 193 + ((byte1 - 193) * 256) + byte2;
                return true;
            }
            else
            {
                if (data.Length <= ConsumedBytes + 2)
                {
                    length = 0;
                    return false;
                }

                var byte2 = data[ConsumedBytes + 1];
                var byte3 = data[ConsumedBytes + 2];
                ConsumedBytes += 3;
                length = 12481 + ((byte1 - 241) * 65536) + (byte2 * 256) + byte3;
                return true;
            }
        }

        public int ReadLengthPrefix()
        {
            if (!TryReadLengthPrefix(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadUInt8(out byte value)
        {
            if (data.Length < ConsumedBytes + 1)
            {
                value = 0;
                return false;
            }

            value = data[ConsumedBytes];
            ConsumedBytes += 1;
            return true;
        }

        public byte ReadUInt8()
        {
            if (!TryReadUInt8(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadUInt16(out ushort value)
        {
            if (data.Length < ConsumedBytes + 2)
            {
                value = 0;
                return false;
            }

            value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(ConsumedBytes, 2));
            ConsumedBytes += 2;
            return true;
        }

        public ushort ReadUInt16()
        {
            if(!TryReadUInt16(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadUInt32(out uint value)
        {
            if (data.Length < ConsumedBytes + 4)
            {
                value = 0;
                return false;
            }

            value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice(ConsumedBytes, 4));
            ConsumedBytes += 4;
            return true;
        }

        public uint ReadUInt32()
        {
            if (!TryReadUInt32(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadUInt64(out ulong value)
        {
            if (data.Length < ConsumedBytes + 8)
            {
                value = 0;
                return false;
            }

            value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(data.Slice(ConsumedBytes, 8));
            ConsumedBytes += 8;
            return true;
        }

        public ulong ReadUInt64()
        {
            if (!TryReadUInt64(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadAmount(out Amount value)
        {
            if (data.Length < ConsumedBytes + 8)
            {
                value = default;
                return false;
            }

            var amount = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(data.Slice(ConsumedBytes, 8));
            var xrp = XrpAmount.FromUInt64Bits(amount);
            if (xrp.HasValue)
            {
                ConsumedBytes += 8;
                value = new Amount(xrp.Value.Drops);
                return true;
            }
            else
            {
                if (data.Length < ConsumedBytes + 48)
                {
                    value = default;
                    return false;
                }

                var currency = Currency.FromUInt64Bits(amount);
                System.Diagnostics.Debug.Assert(currency.HasValue);
                var code = new CurrencyCode(data.Slice(ConsumedBytes + 8, 20));
                var issuer = new AccountId(data.Slice(ConsumedBytes + 28, 20));
                value = new Amount(issuer, code, currency.Value);
                ConsumedBytes += 48;
                return true;
            }
        }

        public Amount ReadAmount()
        {
            if (!TryReadAmount(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public IssuedAmount ReadIssuedAmount()
        {
            var amount = ReadAmount();
            var issuedAmount = amount.IssuedAmount;
            if (issuedAmount.HasValue)
            {
                return issuedAmount.Value;
            }
            else
            {
                throw new RippleException(
                    string.Format("Got unexpected xrp amount: {0}", amount.XrpAmount));
            }
        }

        public XrpAmount ReadXrpAmount()
        {
            var amount = ReadAmount();
            var xrpAmount = amount.XrpAmount;
            if (xrpAmount.HasValue)
            {
                return xrpAmount.Value;
            }
            else
            {
                throw new RippleException(
                    string.Format("Got unexpected issued amount: {0}", amount.IssuedAmount));
            }
        }

        public bool TryReadBlob(out byte[] value)
        {
            if(!TryReadLengthPrefix(out var length))
            {
                value = default;
                return false;
            }

            if (data.Length < ConsumedBytes + length)
            {
                value = default;
                return false;
            }

            value = data.Slice(ConsumedBytes, length).ToArray();
            ConsumedBytes += length;
            return true;
        }

        public byte[] ReadBlob()
        {
            if (!TryReadBlob(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadAccount(out AccountId value)
        {
            var currentOffset = ConsumedBytes;
            if(!TryReadLengthPrefix(out var length))
            {
                value = default;
                return false;
            }

            if(length != 20)
            {
                ConsumedBytes = currentOffset;
                value = default;
                return false;
            }

            if (data.Length < ConsumedBytes + 20)
            {
                value = default;
                return false;
            }


            value = new AccountId(data.Slice(ConsumedBytes, 20));
            ConsumedBytes += 20;
            return true;
        }

        public AccountId ReadAccount()
        {
            if (!TryReadAccount(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadCurrencyCode(out CurrencyCode value)
        {
            if (data.Length < ConsumedBytes + 20)
            {
                value = default;
                return false;
            }

            value = new CurrencyCode(data.Slice(ConsumedBytes, 20));
            ConsumedBytes += 20;
            return true;
        }

        public CurrencyCode ReadCurrencyCode()
        {
            if (!TryReadCurrencyCode(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadVector256(out Hash256[] value)
        {
            var currentOffset = ConsumedBytes;
            if (!TryReadLengthPrefix(out var length))
            {
                value = default;
                return false;
            }

            if (length % 32 != 0)
            {
                ConsumedBytes = currentOffset;
                value = default;
                return false;
            }

            if (data.Length < ConsumedBytes + length)
            {
                value = default;
                return false;
            }

            value = new Hash256[length / 32];
            for (int i = 0; i < value.Length; ++i)
            {
                value[i] = new Hash256(data.Slice(ConsumedBytes, 32));
                ConsumedBytes += 32;
            }
            return true;
        }

        public Hash256[] ReadVector256()
        {
            if (!TryReadVector256(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadHash256(out Hash256 value)
        {
            if (data.Length < ConsumedBytes + 32)
            {
                value = default;
                return false;
            }

            value = new Hash256(data.Slice(ConsumedBytes, 32));
            ConsumedBytes += 32;
            return true;
        }

        public Hash256 ReadHash256()
        {
            if (!TryReadHash256(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadHash160(out Hash160 value)
        {
            if (data.Length < ConsumedBytes + 20)
            {
                value = default;
                return false;
            }

            value = new Hash160(data.Slice(ConsumedBytes, 20));
            ConsumedBytes += 20;
            return true;
        }

        public Hash160 ReadHash160()
        {
            if (!TryReadHash160(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public bool TryReadHash128(out Hash128 value)
        {
            if (data.Length < ConsumedBytes + 16)
            {
                value = default;
                return false;
            }

            value = new Hash128(data.Slice(ConsumedBytes, 16));
            ConsumedBytes += 16;
            return true;
        }

        public Hash128 ReadHash128()
        {
            if (!TryReadHash128(out var value))
            {
                throw new Exception();
            }
            return value;
        }

        public PathSet ReadPathSet()
        {
            var paths = new System.Collections.Generic.List<Path>();
            var path = new System.Collections.Generic.List<PathElement>();

            while(true)
            {
                var type = data[ConsumedBytes++];

                if (type == 0x00 || type == 0xFF)
                {
                    paths.Add(new Path(path));
                    path.Clear();

                    if (type == 0x00) break;
                }
                else if (type == 0x01)
                {
                    var account = new AccountId(data.Slice(ConsumedBytes, 20));
                    path.Add(PathElement.FromAccount(account));
                    ConsumedBytes += 20;
                }
                else if (type == 0x10)
                {
                    var currency = new CurrencyCode(data.Slice(ConsumedBytes, 20));
                    path.Add(PathElement.FromCurrency(currency));
                    ConsumedBytes += 20;
                }
                else if (type == 0x20)
                {
                    var issuer = new AccountId(data.Slice(ConsumedBytes, 20));
                    path.Add(PathElement.FromIssuer(issuer));
                    ConsumedBytes += 20;
                }
                else if (type == 0x30)
                {
                    var currency = new CurrencyCode(data.Slice(ConsumedBytes, 20));
                    var issuer = new AccountId(data.Slice(ConsumedBytes + 20, 20));
                    path.Add(PathElement.FromIssuedCurrency(issuer, currency));
                    ConsumedBytes += 40;
                }
                else
                {
                    throw new RippleException(string.Format("Bad path element {0} in pathset", type));
                }
            }

            return new PathSet(paths);
        }
    }
}