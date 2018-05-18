using System;
using System.Text;

namespace Tmds.Systemd
{
    /// <summary>
    /// Represents a valid journal field name.
    /// </summary>
    public readonly struct LogFieldName : IEquatable<LogFieldName>
    {
        private readonly byte[] _data;

        /// <summary>Constructor</summary>
        public LogFieldName(string name)
        {
            Validate(name);
            _data = Encoding.ASCII.GetBytes(name);
        }

        /// <summary>Length of the name.</summary>
        public int Length => _data.Length;

        /// <summary>Conversion to ReadOnlySpan.</summary>
        public static implicit operator ReadOnlySpan<byte>(LogFieldName str) => str._data;

        /// <summary>Conversion from string.</summary>
        public static implicit operator LogFieldName(string str) => new LogFieldName(str);

        /// <summary>Returns the string representation of this name.</summary>
        public override string ToString() => Encoding.ASCII.GetString(_data);
        /// <summary>Conversion to string.</summary>
        public static explicit operator string(LogFieldName str) => str.ToString();

        /// <summary>Checks equality.</summary>
        public bool Equals(LogFieldName other) => ReferenceEquals(_data, other._data) || SequenceEqual(_data, other._data);
        private bool SequenceEqual(byte[] data1, byte[] data2) => new Span<byte>(data1).SequenceEqual(data2);

        /// <summary>Equality comparison.</summary>
        public static bool operator ==(LogFieldName a, LogFieldName b) => a.Equals(b);
        /// <summary>Inequality comparison.</summary>
        public static bool operator !=(LogFieldName a, LogFieldName b) => !a.Equals(b);
        /// <summary>Checks equality.</summary>
        public override bool Equals(object other) => (other is LogFieldName) && Equals((LogFieldName)other);

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