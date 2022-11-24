using System;
using System.Text.Json;

namespace Ibasa.Ripple
{
    /// <summary>
    /// The "CurrencyType" type is a special field type that represents an issued currency with a code and optionally an issuer or XRP.
    /// </summary>
    public struct CurrencyType
    {
        public readonly AccountId? Issuer;
        public readonly CurrencyCode CurrencyCode;

        public static readonly CurrencyType XRP = new CurrencyType();

        public CurrencyType(CurrencyCode currencyCode)
        {
            CurrencyCode = currencyCode;
            Issuer = null;
        }

        public CurrencyType(AccountId issuer, CurrencyCode currencyCode)
        {
            if (currencyCode == CurrencyCode.XRP)
            {
                throw new ArgumentException("Can not be XRP", "currencyCode");
            }
            CurrencyCode = currencyCode;
            Issuer = issuer;
        }

        internal void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("currency", CurrencyCode.ToString());
            if (Issuer.HasValue)
            {
                writer.WriteString("issuer", Issuer.Value.ToString());
            }
            writer.WriteEndObject();
        }

        internal static CurrencyType ReadJson(JsonElement json)
        {
            var currencyCode = new CurrencyCode(json.GetProperty("currency").GetString());

            if (json.TryGetProperty("issuer", out var element))
            {
                return new CurrencyType(new AccountId(element.GetString()), currencyCode);
            }
            else
            {
                return new CurrencyType(currencyCode);
            }
        }

        public override string ToString()
        {
            if (Issuer.HasValue)
            {
                return string.Format("{1}({2})", CurrencyCode, Issuer.Value);
            }
            else
            {
                return CurrencyCode.ToString();
            }
        }

        public static implicit operator CurrencyType(CurrencyCode value)
        {
            return new CurrencyType(value);
        }
    }
}