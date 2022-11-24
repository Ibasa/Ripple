using System;
using System.IO;
using System.Text.Json;

namespace Ibasa.Ripple
{
    /// <summary>
    /// The "Amount" type is a special field type that represents an amount of currency, either XRP or an issued currency.
    /// </summary>
    public readonly struct Amount
    {
        private readonly ulong value;
        private readonly AccountId issuer;
        private readonly CurrencyCode currencyCode;

        public XrpAmount? XrpAmount
        {
            get
            {
                return Ripple.XrpAmount.FromUInt64Bits(value);
            }
        }

        public IssuedAmount? IssuedAmount
        {
            get
            {
                var currency = Currency.FromUInt64Bits(value);
                if (currency.HasValue)
                {
                    return new IssuedAmount(issuer, currencyCode, currency.Value);
                }
                return null;
            }
        }

        public Amount(long drops)
        {
            if (drops < -0x3FFF_FFFF_FFFF_FFFF || drops > 0x3FFF_FFFF_FFFF_FFFF)
            {
                throw new ArgumentOutOfRangeException("drops", drops, string.Format("absolute value of drops must be less than or equal to {0}", 0x3FFF_FFFF_FFFF_FFFF));
            }
            this.value = (ulong)Math.Abs(drops);
            if (drops >= 0) 
            {
                this.value |= 0x4000_0000_0000_0000;
            }
            // These fields are only used for IssuedAmount but struct constructor has to set all fields.
            this.currencyCode = default;
            this.issuer = default;
        }

        public Amount(AccountId issuer, CurrencyCode currencyCode, Currency value)
        {
            if (currencyCode == CurrencyCode.XRP)
            {
                throw new ArgumentException("Can not be XRP", "currencyCode");
            }
            this.value = Currency.ToUInt64Bits(value);
            this.currencyCode = currencyCode;
            this.issuer = issuer;
        }

        public void WriteJson(Utf8JsonWriter writer)
        {
            var xrp = XrpAmount;
            if (xrp.HasValue)
            {
                xrp.Value.WriteJson(writer);
            }
            else
            {
                var issued = IssuedAmount;
                if (issued.HasValue)
                {
                    issued.Value.WriteJson(writer);
                }
                else
                {
                    throw new Exception("Unreachable");
                }
            }
        }

        internal static Amount ReadJson(JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object)
            {
                return Ripple.IssuedAmount.ReadJson(json);
            }
            else
            {
                return Ripple.XrpAmount.ReadJson(json);
            }
        }

        public override string ToString()
        {
            var xrp = XrpAmount;
            if (xrp.HasValue)
            {
                return xrp.Value.ToString();
            }
            else
            {
                var issued = IssuedAmount;
                if (issued.HasValue)
                {
                    return issued.Value.ToString();
                }
                else
                {
                    throw new Exception("Unreachable");
                }
            }
        }
    }

    /// <summary>
    /// An "Amount" that must be in XRP.
    /// </summary>
    public readonly struct XrpAmount
    {
        public readonly long Drops;

        public decimal XRP
        {
            get
            {
                return ((decimal)Drops) / 1000000;
            }
        }

        private XrpAmount(long drops)
        {
            if (drops < -0x3FFF_FFFF_FFFF_FFFF || drops > 0x3FFF_FFFF_FFFF_FFFF)
            {
                throw new ArgumentOutOfRangeException("drops", drops, string.Format("absolute value of drops must be less than or equal to {0}", 0x3FFF_FFFF_FFFF_FFFF));
            }
            Drops = drops;
        }

        public static XrpAmount FromDrops(long drops)
        {
            return new XrpAmount(drops);
        }

        public static XrpAmount FromXrp(decimal xrp)
        {
            // 1000000 drops per xrp, at most 3FFF_FFFF_FFFF_FFFF drops (2^63-1)
            // 4611686018427387903 / 1000000 = 4611686018427
            if (Math.Abs(xrp) > 4611686018427)
            {
                throw new ArgumentOutOfRangeException("xrp", xrp, string.Format("absolute value of xrp must be less than or equal to {0}", 4611686018427));
            }
            return new XrpAmount((long)(xrp * 1000000));
        }

        public static implicit operator Amount(XrpAmount value)
        {
            return new Amount(value.Drops);
        }

        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStringValue(Drops.ToString());
        }

        public static XrpAmount ReadJson(JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.String)
            {
                return new XrpAmount(long.Parse(json.GetString()));
            }
            else if (json.ValueKind == JsonValueKind.Number)
            {
                return new XrpAmount(json.GetInt64());
            }
            else
            {
                var message = String.Format("The requested operation requires an element of type 'String' or 'Number', but the target element has type '{0}'", json.ValueKind);
                throw new ArgumentException(message, "json");
            }
        }

        public static ulong ToUInt64Bits(XrpAmount value)
        {
            var bits = (ulong)Math.Abs(value.Drops);
            if (value.Drops >= 0)
            {
                bits |= 0x4000_0000_0000_0000;
            }
            return bits;
        }

        public static XrpAmount? FromUInt64Bits(ulong value)
        {
            if ((value & 0x8000_0000_0000_0000) != 0)
            {
                return null;
            }

            var drops = (long)(value & 0x3FFF_FFFF_FFFF_FFFF);
            if ((value & 0x4000_0000_0000_0000) == 0)
            {
                drops = -drops;
            }
            return FromDrops(drops);
        }

        public static XrpAmount Parse(string s)
        {
            return new XrpAmount(long.Parse(s));
        }

        public override string ToString()
        {
            return string.Format("{0} XRP", XRP);
        }
    }

    /// <summary>
    /// An "Amount" that must be an issued currency.
    /// </summary>
    public readonly struct IssuedAmount
    {
        public readonly Currency Value;
        public readonly AccountId Issuer;
        public readonly CurrencyCode CurrencyCode;

        public IssuedAmount(AccountId issuer, CurrencyCode currencyCode, Currency value)
        {
            if (currencyCode == CurrencyCode.XRP)
            {
                throw new ArgumentException("Can not be XRP", "currencyCode");
            }

            Issuer = issuer;
            CurrencyCode = currencyCode;
            Value = value;
        }

        public static implicit operator Amount(IssuedAmount value)
        {
            return new Amount(value.Issuer, value.CurrencyCode, value.Value);
        }

        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("currency", CurrencyCode.ToString());
            writer.WriteString("issuer", Issuer.ToString());
            writer.WriteString("value", Value.ToString());
            writer.WriteEndObject();
        }

        public static IssuedAmount ReadJson(JsonElement json)
        {
            return new IssuedAmount(
                new AccountId(json.GetProperty("issuer").GetString()),
                new CurrencyCode(json.GetProperty("currency").GetString()),
                Currency.Parse(json.GetProperty("value").GetString()));
        }

        public override string ToString()
        {
            return string.Format("{0} {1}({2})", Value, CurrencyCode, Issuer);
        }
    }
}