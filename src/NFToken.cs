using Ibasa.Ripple.St;
using System;
using System.Text.Json;

namespace Ibasa.Ripple
{
    /// <summary>
    /// The NFToken object represents a single non-fungible token (NFT).
    /// It is not stored on its own, but is contained in a NFTokenPage object alongside other NFToken objects.
    /// </summary>
    public readonly struct NFToken : IEquatable<NFToken>
    {
        /// <summary>
        /// This composite field uniquely identifies a token.
        /// </summary>
        public Hash256 NFTokenID{ get; }

        /// <summary>
        /// The URI field points to the data and/or metadata associated with the NFToken.
        /// </summary>
        public string URI { get; }

        public NFToken(Hash256 nfTokenId, string uri)
        {
            NFTokenID = nfTokenId;
            URI = uri;
        }

        internal NFToken(JsonElement json)
        {
            NFTokenID = new Hash256(json.GetProperty("NFTokenID").GetString());
            URI = json.GetProperty("URI").GetString();
        }

        internal NFToken(ref StReader reader)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId != StFieldId.Hash256_NFTokenID)
            {
                throw new Exception(string.Format("Expected {0} but got {1}", StFieldId.Hash256_NFTokenID, fieldId));
            }
            NFTokenID = reader.ReadHash256();
            fieldId = reader.ReadFieldId();
            if (fieldId != StFieldId.Blob_URI)
            {
                throw new Exception(string.Format("Expected {0} but got {1}", StFieldId.Blob_URI, fieldId));
            }
            URI = System.Text.Encoding.ASCII.GetString(reader.ReadBlob());
            fieldId = reader.ReadFieldId();
            if (fieldId != StFieldId.Object_ObjectEndMarker)
            {
                throw new Exception(string.Format("Expected {0} but got {1}", StFieldId.Object_ObjectEndMarker, fieldId));
            }
        }

        internal void WriteTo(ref StWriter writer)
        {
            writer.WriteStartObject(StObjectFieldCode.NFToken);
            writer.WriteHash256(StHash256FieldCode.NFTokenID, NFTokenID);

            Span<byte> bytes = stackalloc byte[URI.Length];
            System.Text.Encoding.ASCII.GetBytes(URI, bytes);
            writer.WriteBlob(StBlobFieldCode.URI, bytes);
            writer.WriteEndObject();
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NFTokenID, URI);
        }

        public override bool Equals(object obj)
        {
            if (obj is NFToken)
            {
                return Equals((NFToken)obj);
            }
            return false;
        }

        public bool Equals(NFToken other)
        {
            return
                NFTokenID.Equals(other.NFTokenID) &&
                URI.Equals(other.URI);
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize<NFToken>(this);
        }
    }
}