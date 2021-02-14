using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Tmds.Systemd;
using System.Threading;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Tmds.Systemd.Tests
{
    public class JournalTests
    {
        [Theory]
        [MemberData(nameof(GetSerializationData))]
        public void Serialization(Dictionary<string, object> serializedFields)
        {
            CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nl-BE");
            Dictionary<string, string> deserializedFields;
            using (var message = CreateJournalMessage())
            {
                foreach (var serializedValue in serializedFields)
                {
                    message.Append(serializedValue.Key, serializedValue.Value);
                }
                deserializedFields = ReadFields(message);
            }
            Thread.CurrentThread.CurrentCulture = originalCulture;

            Assert.Equal(serializedFields.Count, deserializedFields.Count);
            foreach (var serializedValue in serializedFields)
            {
                Assert.True(deserializedFields.ContainsKey(serializedValue.Key));
                Assert.Equal(deserializedFields[serializedValue.Key], Stringify(serializedValue.Value));
            }
        }

        public static IEnumerable<object[]> GetSerializationData()
        {
            yield return new object[]
            {
                // Single field
                new Dictionary<string, object>()
                {
                    { "FIELD1", "VALUE"}
                }
            };
            yield return new object[]
            {
                // Two fields
                new Dictionary<string, object>()
                {
                    { "FIELD1", "VALUE1"},
                    { "FIELD2", 20 }
                }
            };
            yield return new object[]
            {
                // Long value length
                new Dictionary<string, object>()
                {
                    { "FIELD1", 10},
                    { "FIELD2", new string('x', 8000) },
                    { "FIELD3", new string('y', 8000) }
                },
            };
            yield return new object[]
            {
                // Enumerable
                new Dictionary<string, object>()
                {
                    { "FIELD1", new[] { 10, 20 }}
                },
            };
            yield return new object[]
            {
                // Enumerable in Enumerable
                new Dictionary<string, object>()
                {
                    { "FIELD1", new object[] { new[] {10, 20}, 20 }}
                },
            };
            yield return new object[]
            {
                // Culture
                new Dictionary<string, object>()
                {
                    { "FIELD1", 10.5} // Formats as 10,5 in nl-BE culture
                },
            };
            yield return new object[]
            {
                // Culture in Enumerable
                new Dictionary<string, object>()
                {
                    { "FIELD1", new[] { 10.5 } } // Formats as 10,5 in nl-BE culture
                },
            };
        }

        [Fact]
        public void Log()
        {
            TestLogNonExisting();
            TestLogExisting();
            TestLogNonSupported();
        }

        [Theory]
        [InlineData(null)] // null
        [InlineData("")]   // empty
        [InlineData("_")]  // starts with underscore
        [InlineData("1")]  // starts with digit
        [InlineData("AAAAAAAAAABBBBBBBBBBAAAAAAAAAABBBBBBBBBBAAAAAAAAAABBBBBBBBBBCCCCD")] // longer than 64 chars
        [InlineData("a")]  // can only contain '[A-Z0-9'_]
        [InlineData("~")]  // can only contain '[A-Z0-9'_]
        public void JournalFieldName_Invalid(string name)
        {
            Assert.ThrowsAny<ArgumentException>(() => new JournalFieldName(name));
        }

        [Theory]
        [InlineData("A")]   // letter A
        [InlineData("Z")]   // letter Z
        [InlineData("A_")]  // underscore
        [InlineData("A0")]  // digit 0
        [InlineData("A9")]  // digit 9
        [InlineData("AAAAAAAAAABBBBBBBBBBAAAAAAAAAABBBBBBBBBBAAAAAAAAAABBBBBBBBBBCCCC")] // 64 chars
        public void JournalFieldName_Valid(string name)
        {
            JournalFieldName fieldName = name;
            Assert.Equal(fieldName.Length, name.Length);
            Assert.Equal(fieldName.ToString(), name);
        }

        [Theory]
        [InlineData("_", "X")]  // starts with underscore
        [InlineData("1", "X1")]  // starts with digit
        [InlineData("AAAAAAAAAABBBBBBBBBBAAAAAAAAAABBBBBBBBBBAAAAAAAAAABBBBBBBBBBCCCCD", "AAAAAAAAAABBBBBBBBBBAAAAAAAAAABBBBBBBBBBAAAAAAAAAABBBBBBBBBBCCCC")] // longer than 64 chars
        [InlineData("a", "A")]  // can only contain '[A-Z0-9'_]
        [InlineData("~", "X")]  // can only contain '[A-Z0-9'_]
        public void JournalMessageFieldNameSanitation(string input, string expected)
        {
            using (var message = CreateJournalMessage())
            {
                message.Append(input, "value");
                Dictionary<string, string> deserializedFields = ReadFields(message);
                var readKey = deserializedFields.First().Key;
                Assert.Equal(readKey, expected);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void AppendInt(int i)
        {
            using (var message = CreateJournalMessage())
            {
                message.Append((JournalFieldName)"A", (int)i);
                message.Append((string)"B", (int)i);
                message.Append((JournalFieldName)"C", (object)i);
                message.Append((string)"D", (object)i);

                Dictionary<string, string> deserializedFields = ReadFields(message);
                Assert.Equal(4, deserializedFields.Count);

                string expected = i.ToString(null, CultureInfo.InvariantCulture);
                Assert.Equal(expected, deserializedFields["A"]);
                Assert.Equal(expected, deserializedFields["B"]);
                Assert.Equal(expected, deserializedFields["C"]);
                Assert.Equal(expected, deserializedFields["D"]);
            }
        }

        [Fact]
        public void AppendSpan()
        {
            using (var message = CreateJournalMessage())
            {
                message.Append((JournalFieldName)"A", Encoding.UTF8.GetBytes("Hello").AsSpan());
                message.Append((JournalFieldName)"B", Encoding.UTF8.GetBytes("World").AsSpan());

                Dictionary<string, string> deserializedFields = ReadFields(message);
                Assert.Equal(2, deserializedFields.Count);

                Assert.Equal("Hello", deserializedFields["A"]);
                Assert.Equal("World", deserializedFields["B"]);
            }
        }

        private void TestLogNonExisting()
        {
            string nonExisting = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Journal.ConfigureJournalSocket(nonExisting);

            // Journal is not available
            Assert.False(Journal.IsAvailable);
            using (var message = Journal.GetMessage())
            {
                // This shouldn't throw.
                LogResult result = Journal.Log(LogFlags.Information, message);
                Assert.Equal(LogResult.NotAvailable, result);
            }
        }

        private void TestLogNonSupported()
        {
            Journal.ConfigureJournalSocket("/", isSupported : false);

            // Journal is not available
            Assert.False(Journal.IsAvailable);
            // Journal is not supported
            Assert.False(Journal.IsSupported);
            using (var message = Journal.GetMessage())
            {
                // Message is not enabled
                Assert.False(message.IsEnabled);

                // Append is a noop
                message.Append("FIELD", "Value");
                Assert.Empty(message.GetData());

                // This shouldn't throw.
                LogResult result = Journal.Log(LogFlags.Information, message);
                Assert.Equal(LogResult.NotSupported, result);
            }
        }

        private void TestLogExisting()
        {
            string socketPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            using (Socket serverSocket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified))
            {
                serverSocket.Blocking = false;
                serverSocket.Bind(new UnixDomainSocketEndPoint(socketPath));
                Journal.ConfigureJournalSocket(socketPath);

                // Journal is available
                Assert.True(Journal.IsAvailable);

                TestSimpleMessage(serverSocket, dontAppendSyslogIdentifier: false);
                TestSimpleMessage(serverSocket, dontAppendSyslogIdentifier: true);

                TestLongMessage(serverSocket);
            }
        }

        private void TestSimpleMessage(Socket serverSocket, bool dontAppendSyslogIdentifier)
        {
            Journal.SyslogIdentifier = "dotnet";
            using (var message = Journal.GetMessage())
            {
                // Message is enabled
                Assert.True(message.IsEnabled);

                message.Append("FIELD", "Value");

                // This shouldn't throw.
                LogFlags flags = LogFlags.Information;
                if (dontAppendSyslogIdentifier)
                {
                    flags |= LogFlags.DontAppendSyslogIdentifier;
                }
                LogResult result = Journal.Log(flags, message);
                Assert.Equal(LogResult.Success, result);

                var fields = ReadFields(serverSocket);
                if (dontAppendSyslogIdentifier)
                {
                    Assert.Equal(2, fields.Count);
                }
                else
                {
                    Assert.Equal(3, fields.Count);
                }
                Assert.Equal("Value", fields["FIELD"]);
                Assert.Equal("6", fields["PRIORITY"]);
                if (!dontAppendSyslogIdentifier)
                {
                    Assert.Equal(Journal.SyslogIdentifier, fields["SYSLOG_IDENTIFIER"]);
                }
            }
        }

        private void TestLongMessage(Socket serverSocket)
        {
            using (var message = Journal.GetMessage())
            {
                const int fieldCount = 15;
                string valueSuffix = new string('x', 4096);
                for (int i = 0; i < fieldCount; i++)
                {
                    message.Append($"FIELD{i}", $"{i} " + valueSuffix);
                }

                LogResult result = Journal.Log(LogFlags.Information, message);
                Assert.Equal(LogResult.Success, result);

                var fields = ReadFields(serverSocket);
                Assert.Equal(fieldCount + 2, fields.Count);
                for (int i = 0; i < fieldCount; i++)
                {
                    Assert.Equal($"{i} " + valueSuffix, fields[$"FIELD{i}"]);
                }
            }
        }

        private static Dictionary<string, string> ReadFields(Socket socket)
        {
            var datas = new List<ArraySegment<byte>>();
            int length = socket.Available;
            if (length > 0)
            {
                byte[] data = new byte[length];
                int bytesReceived = socket.Receive(data);
                datas.Add(new ArraySegment<byte>(data, 0, bytesReceived));
            }
            return ReadFields(datas);
        }

        private static Dictionary<string, string> ReadFields(JournalMessage message)
            => ReadFields(message.GetData());

        private static Dictionary<string, string> ReadFields(List<ArraySegment<byte>> datas)
        {
            var fields = new Dictionary<string, string>();
            byte[] bytes;
            using (var memoryStream = new MemoryStream())
            {
                foreach (var data in datas)
                {
                    memoryStream.Write(data.Array, data.Offset, data.Count);
                }
                bytes = memoryStream.ToArray();
            }
            Span<byte> remainder = bytes;
            while (remainder.Length > 0)
            {
                int fieldNameLength = remainder.IndexOf((byte)'\n');
                string fieldName = Encoding.UTF8.GetString(remainder.Slice(0, fieldNameLength));
                remainder = remainder.Slice(fieldNameLength + 1);
                int fieldValueLength = (int)BinaryPrimitives.ReadUInt64LittleEndian(remainder);
                remainder = remainder.Slice(8);
                string fieldValue = Encoding.UTF8.GetString(remainder.Slice(0, fieldValueLength));
                remainder = remainder.Slice(fieldValueLength + 1);
                fields.Add(fieldName, fieldValue);
            }
            return fields;
        }

        private static JournalMessage CreateJournalMessage()
        {
            return JournalMessage.Get(isEnabled: true);
        }

        private static string Stringify(object value, bool checkEnumerable = true)
        {
            if (value is string)
            {
                return (string)value;
            }
            else if (checkEnumerable && value is System.Collections.IEnumerable enumerable)
            {
                StringBuilder sb = new StringBuilder();
                bool first = true;
                foreach (var enumVal in enumerable)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }
                    first = false;
                    sb.Append(Stringify(enumVal, checkEnumerable: false));
                }
                return sb.ToString();
            }
            else
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}", value);
            }
        }
    }
}
