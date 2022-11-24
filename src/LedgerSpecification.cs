using System;
using System.Text.Json;

namespace Ibasa.Ripple
{
    /// <summary>
    /// <para>
    /// Many API methods require you to specify an instance of the ledger, with the data retrieved being considered up-to-date as of that particular version of the shared ledger. 
    /// </para>
    /// <para>
    /// The commands that accept a ledger version all work the same way.
    /// </para>
    /// <para>
    /// There are three ways you can specify which ledger you want to use:
    /// <list type="number">
    /// <item>
    /// Specify a ledger by its Ledger Index in the ledger_index parameter.
    /// Each closed ledger has a ledger index that is 1 higher than the previous ledger. 
    /// (The very first ledger had ledger index 1.)
    /// </item>
    /// <item>
    /// Specify a ledger by its Hash value in the ledger_hash parameter.
    /// </item>
    /// <item>
    /// Specify a ledger by one of the following shortcuts, in the ledger_index parameter:
    /// <list type="bullet">
    /// <item>
    /// validated for the most recent ledger that has been validated by the whole network 
    /// </item>
    /// <item>
    /// closed for the most recent ledger that has been closed for modifications and proposed for validation
    /// </item>
    /// <item>
    /// current for the server's current working version of the ledger
    /// </item>
    /// </list>
    /// </item>
    /// </list>
    /// </para>
    /// If you do not specify a ledger, the current (in-progress) ledger is chosen by default. 
    /// </summary>
    public struct LedgerSpecification : IEquatable<LedgerSpecification>
    {
        private uint index;
        private string? shortcut;
        private Hash256? hash;

        /// <summary>
        /// The most recent ledger that has been validated by the whole network.
        /// </summary>
        public static readonly LedgerSpecification Validated = new LedgerSpecification() { shortcut = "validated" };

        /// <summary>
        /// The most recent ledger that has been closed for modifications and proposed for validation.
        /// </summary>
        public static readonly LedgerSpecification Closed = new LedgerSpecification() { shortcut = "closed" };

        /// <summary>
        /// The server's current working version of the ledger.
        /// </summary>
        public static readonly LedgerSpecification Current = new LedgerSpecification();

        public LedgerSpecification(uint index)
        {
            if (index == 0)
            {
                throw new ArgumentOutOfRangeException("index", index, "index must be greater than zero");
            }

            this.index = index;
            this.shortcut = null;
            this.hash = null;
        }

        public LedgerSpecification(Hash256 hash)
        {
            this.index = 0;
            this.shortcut = null;
            this.hash = new Hash256?(hash);
        }

        internal static void Write(Utf8JsonWriter writer, LedgerSpecification specification)
        {
            if (specification.index == 0)
            {
                if (specification.hash.HasValue)
                {
                    writer.WriteString("ledger_hash", specification.hash.Value.ToString());
                }
                else if (specification.shortcut != null)
                {
                    writer.WriteString("ledger_index", specification.shortcut);
                }
                else
                {
                    writer.WriteString("ledger_index", "current");
                }
            }
            else
            {
                writer.WriteNumber("ledger_index", specification.index);
            }
        }

        public bool Equals(LedgerSpecification other)
        {
            return
                index == other.index &&
                hash == other.hash &&
                shortcut == other.shortcut;
        }

        public override bool Equals(object obj)
        {
            if(obj is LedgerSpecification)
            {
                return Equals((LedgerSpecification)obj);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(index, shortcut, hash);
        }

        public override string ToString()
        {
            if (index == 0)
            {
                if (hash.HasValue)
                {
                    return string.Format("Hash: {0}", hash.Value);
                }
                else if (shortcut != null)
                {
                    return string.Format("Shortcut: {0}", shortcut);
                }
                else
                {
                    return "Shortcut: current";
                }
            }
            else
            {
                return string.Format("Index: {0}", index);
            }
        }
    }
}