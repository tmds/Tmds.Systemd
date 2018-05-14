using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;
using Tmds.Systemd;
using System.Threading;

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
            // -- No Journal --
            string nonExisting = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            ServiceManager.ConfigureJournalSocket(nonExisting);

            // Journal is not available
            Assert.False(ServiceManager.IsJournalAvailable);
            using (var message = ServiceManager.GetJournalMessage())
            {
                // Message is not enabled
                Assert.False(message.IsEnabled);

                // Append is a noop
                message.Append("Field", "Value");
                Assert.Equal(0, message.GetData().Count);

                // This shouldn't throw.
                ServiceManager.Log(LogFlags.Information, message);
            }
        }

        private static Dictionary<string, string> ReadFields(JournalMessage message)
        {
            var fields = new Dictionary<string, string>();
            byte[] bytes;
            using (var memoryStream = new MemoryStream())
            {
                foreach (var data in message.GetData())
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
