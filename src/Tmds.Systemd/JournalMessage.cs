using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Tmds.Systemd
{
    /// <summary>Represents a structured log message.</summary>
    public class JournalMessage : IDisposable
    {
        // If the buffer can't fit at least a character, the Encoder throws.
        private const int MaximumBytesPerUtf8Char = 4;
        private static Encoding s_encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        [ThreadStatic]
        private static JournalMessage s_cachedMessage;

        private readonly List<ArraySegment<byte>> _data;
        private readonly Encoder _encoder;
        private readonly StringBuilder _stringBuilder;
        private readonly char[] _charBuffer;
        private byte[] _currentSegment;
        private int _bytesWritten;
        private bool _isEnabled;

        private JournalMessage()
        {
            _data = new List<ArraySegment<byte>>();
            _encoder = s_encoding.GetEncoder();
            _stringBuilder = new StringBuilder();
            _charBuffer = new char[1024];
        }

        /// <summary>Destructor.</summary>
        ~JournalMessage()
        {
            Clear();
        }

        internal static JournalMessage Get(bool isEnabled)
        {
            JournalMessage message = s_cachedMessage;
            if (message == null)
            {
                message = new JournalMessage();
            }
            else
            {
                s_cachedMessage = null;
            }

            message._isEnabled = isEnabled;

            return message;
        }

        /// <summary>Adds a field to the message.</summary>
        public void Append(string name, object value)
        {
            if (!_isEnabled)
            {
                return;
            }

            // Field name
            AppendString(name.AsSpan());

            // Separator
            AppendChar('\n');

            // Field value
            // Reserve space for length
            EnsureCapacity(8);
            Span<byte> valueLengthAt = CurrentRemaining;
            _bytesWritten += 8;
            // value
            int bytesWritten = AppendObject(value);
            // Fill in length
            BinaryPrimitives.WriteUInt64LittleEndian(valueLengthAt, (ulong)bytesWritten);

            // Separator
            AppendChar('\n');
        }

        private int AppendObject(object value, bool checkEnumerable = true)
        {
            int bytesWritten = 0;
            if (value is string) // Special-case string since it implements IEnumerable
            {
                bytesWritten = AppendString(((string)value).AsSpan());
            }
            else if (checkEnumerable && value is IEnumerable enumerable)
            {
                bool addSeparator = false;
                foreach (object o in enumerable)
                {
                    if (addSeparator)
                    {
                        bytesWritten += AppendChar(',');
                        bytesWritten += AppendChar(' ');
                    }
                    bytesWritten += AppendObject(o, checkEnumerable: false);
                    addSeparator = true;
                }
            }
            else
            {
                // StringBuilder leverages an internal ISpanFormattable interface
                // that avoids allocations on .NET Core 2.1.
                _stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0}", value);
                bytesWritten += AppendStringBuilder(_stringBuilder);
                _stringBuilder.Clear();
            }
            return bytesWritten;
        }

        private int AppendStringBuilder(StringBuilder sb)
        {
            int bytesWritten = 0;
            int length = sb.Length;
            int offset = 0;
            while (length > 0)
            {
                int destinationLength = Math.Min(length, _charBuffer.Length);
                // .NET Core 2.1 can stackalloc charBuffer
                sb.CopyTo(offset, _charBuffer, 0,  destinationLength);
                bytesWritten += AppendString(new Span<char>(_charBuffer, 0, destinationLength));
                length -= destinationLength;
                offset += destinationLength;
            }
            return bytesWritten;
        }

        private void Clear()
        {
            AppendCurrent();
            foreach (var segment in _data)
            {
                ArrayPool<byte>.Shared.Return(segment.Array);
            }
            _data.Clear();
        }

        /// <summary>Returns the JournalMessage.</summary>
        public void Dispose()
        {
            Clear();

            if (s_cachedMessage == null
            || (this._data.Count > s_cachedMessage._data.Count)) // Prefer caching longer _data
            {
                s_cachedMessage = this;
            }
        }

        internal List<ArraySegment<byte>> GetData()
        {
            AppendCurrent();
            return _data;
        }

        internal bool IsEmpty => _bytesWritten == 0 && _data.Count == 0;

        private void AppendCurrent()
        {
            if (_currentSegment != null)
            {
                _data.Add(new ArraySegment<byte>(_currentSegment, 0, _bytesWritten));
                _bytesWritten = 0;
                _currentSegment = null;
            }
        }

        private int AppendChar(char c)
        {
            if (c <= 127)
            {
                EnsureCapacity(1);

                _currentSegment[_bytesWritten] = (byte)c;
                _bytesWritten++;
                return 1;
            }
            else
            {
                return _AppendMultiByteChar(c);
            }
        }

        private unsafe int _AppendMultiByteChar(char value)
        {
            EnsureCapacity(MaximumBytesPerUtf8Char);
            var destination = CurrentRemaining;

            int bytesUsed = 0;
            int charsUsed = 0;
            fixed (byte* destinationBytes = &MemoryMarshal.GetReference(destination))
            {
                _encoder.Convert(&value, 1, destinationBytes, destination.Length, false, out charsUsed, out bytesUsed, out _);
            }

            _bytesWritten += bytesUsed;
            return bytesUsed;
        }

        private int AppendString(ReadOnlySpan<char> buffer)
        {
            int bytesWritten = 0;

            while (buffer.Length > 0)
            {
                EnsureCapacity(MaximumBytesPerUtf8Char, desiredSize: buffer.Length);
                Span<byte> destination = CurrentRemaining;

                int bytesUsed = 0;
                int charsUsed = 0;
                unsafe
                {
                    fixed (char* sourceChars = &MemoryMarshal.GetReference(buffer))
                    fixed (byte* destinationBytes = &MemoryMarshal.GetReference(destination))
                    {
                        _encoder.Convert(sourceChars, buffer.Length, destinationBytes, destination.Length, false, out charsUsed, out bytesUsed, out _);
                    }
                }
                buffer = buffer.Slice(charsUsed);
                _bytesWritten += bytesUsed;
                bytesWritten += bytesUsed;
            }

            return bytesWritten;
        }

        private void EnsureCapacity(int minSize, int desiredSize = 0)
        {
            if (_currentSegment == null || (_currentSegment.Length - _bytesWritten) <  + minSize)
            {
                AppendCurrent();
                desiredSize = Math.Max(minSize, Math.Max(desiredSize, 4096));
                _currentSegment = ArrayPool<byte>.Shared.Rent(minimumLength: desiredSize);
                _bytesWritten = 0;
            }
        }

        private Span<byte> CurrentRemaining => new Span<byte>(_currentSegment, _bytesWritten, _currentSegment.Length - _bytesWritten);
    }
}