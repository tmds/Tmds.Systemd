using System;
using System.Text;

namespace Tmds.Systemd
{
    /// <summary>
    /// Represents a valid journal field name.
    /// </summary>
    public readonly struct JournalFieldName : IEquatable<JournalFieldName>
    {
        /// <summary>Priority value.</summary>
        public static readonly JournalFieldName Priority = "PRIORITY";
        /// <summary>Syslog identifier tag.</summary>
        public static readonly JournalFieldName SyslogIdentifier = "SYSLOG_IDENTIFIER";
        /// <summary>Human readable message.</summary>
        public static readonly JournalFieldName Message = "MESSAGE";

        private readonly byte[] _data;

        /// <summary>Constructor</summary>
        public JournalFieldName(string name)
        {
            Validate(name);
            _data = Encoding.ASCII.GetBytes(name);
        }

        /// <summary>Length of the name.</summary>
        public int Length => _data.Length;

        /// <summary>Conversion to ReadOnlySpan.</summary>
        public static implicit operator ReadOnlySpan<byte>(JournalFieldName str) => str._data;

        /// <summary>Conversion from string.</summary>
        public static implicit operator JournalFieldName(string str) => new JournalFieldName(str);

        /// <summary>Returns the string representation of this name.</summary>
        public override string ToString() => Encoding.ASCII.GetString(_data);
        /// <summary>Conversion to string.</summary>
        public static explicit operator string(JournalFieldName str) => str.ToString();

        /// <summary>Checks equality.</summary>
        public bool Equals(JournalFieldName other) => ReferenceEquals(_data, other._data) || SequenceEqual(_data, other._data);
        private bool SequenceEqual(byte[] data1, byte[] data2) => new Span<byte>(data1).SequenceEqual(data2);

        /// <summary>Equality comparison.</summary>
        public static bool operator ==(JournalFieldName a, JournalFieldName b) => a.Equals(b);
        /// <summary>Inequality comparison.</summary>
        public static bool operator !=(JournalFieldName a, JournalFieldName b) => !a.Equals(b);
        /// <summary>Checks equality.</summary>
        public override bool Equals(object other) => (other is JournalFieldName) && Equals((JournalFieldName)other);

        /// <summary>Returns the hash code for this name.</summary>
        public override int GetHashCode()
        {
            // Copied from x64 version of string.GetLegacyNonRandomizedHashCode()
            // https://github.com/dotnet/coreclr/blob/master/src/mscorlib/src/System/String.Comparison.cs
            var data = _data;
            int hash1 = 5381;
            int hash2 = hash1;
            foreach (int b in data)
            {
                hash1 = ((hash1 << 5) + hash1) ^ b;
            }
            return hash1 + (hash2 * 1566083941);
        }

        private static void Validate(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (name.Length == 0)
            {
                throw new ArgumentException($"{nameof(name)} cannot be empty.");
            }
            if (name.Length > 64)
            {
                throw new ArgumentException($"{nameof(name)} cannot be longer than 64 characters.");
            }
            if (name[0] == '_')
            {
                throw new ArgumentException($"{nameof(name)} cannot start with an underscore.");
            }
            if (char.IsDigit(name[0]))
            {
                throw new ArgumentException($"{nameof(name)} cannot start with a digit.");
            }
            foreach (char c in name)
            {
                if (!(char.IsDigit(c) || (c >= 'A' && c <='Z') || (c == '_')))
                {
                    throw new ArgumentException($"{nameof(name)} can only contain '[A-Z0-9'_]'.");
                }
            }
        }
    }
}