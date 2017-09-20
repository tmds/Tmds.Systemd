using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using Xunit;
using Tmds.Systemd;

namespace Tmds.Systemd.Tests
{
    public class NotifyTests
    {
        const string NOTIFY_SOCKET = "NOTIFY_SOCKET";

        [Fact]
        public void SocketPath()
        {
            // NOTIFY_SOCKET not set
            ServiceManager.ResetSocketPath();
            Environment.SetEnvironmentVariable(NOTIFY_SOCKET, null);
            bool notified = ServiceManager.Notify(ServiceState.Ready);
            Assert.False(notified);

            // NOTIFY_SOCKET set to valid path
            ServiceManager.ResetSocketPath();
            string path;
            using (var server = CreateServerSocket(out path))
            {
                Environment.SetEnvironmentVariable(NOTIFY_SOCKET, path);
                notified = ServiceManager.Notify(ServiceState.Ready);
                string message = ReadMessage(server);

                Assert.True(notified);
            }

            // NOTIFY_SOCKET set to invalid path
            ServiceManager.ResetSocketPath();
            Environment.SetEnvironmentVariable(NOTIFY_SOCKET, "/tmp/fakesocket");
            notified = ServiceManager.Notify(ServiceState.Ready);
            Assert.False(notified);

            // NOTIFY_SOCKET set to abstract path
            ServiceManager.ResetSocketPath();
            using (var server = CreateServerSocket(out path, abstractPath: true))
            {
                Environment.SetEnvironmentVariable(NOTIFY_SOCKET, path);
                notified = ServiceManager.Notify(ServiceState.Ready);
                string message = ReadMessage(server);

                Assert.True(notified);
            }
        }

        public static IEnumerable<object[]> Serialization_TestData()
        {
            yield return new object[] { ServiceState.Ready, null, "READY=1" };
            yield return new object[] { ServiceState.Ready, new [] { ServiceState.Status("Started") }, "READY=1\nSTATUS=Started" };
        }

        [Theory]
        [MemberData(nameof(Serialization_TestData))]
        public void Serialization(ServiceState state, ServiceState[] states, string expectedMessage)
        {
            states = states ?? Array.Empty<ServiceState>();

            string path;
            using (var server = CreateServerSocket(out path))
            {
                bool notified = ServiceManager.Notify(path, state, states);
                string message = ReadMessage(server);

                Assert.True(notified);
                Assert.Equal(expectedMessage, message);
            }
        }

        public static IEnumerable<object[]> State_TestData()
        {
            yield return new object[] { ServiceState.Ready, "READY=1" };
            yield return new object[] { ServiceState.Reloading, "RELOADING=1" };
            yield return new object[] { ServiceState.Stopping, "STOPPING=1" };
            yield return new object[] { ServiceState.Watchdog, "WATCHDOG=1" };
            yield return new object[] { ServiceState.Status("Loading"), "STATUS=Loading" };
            yield return new object[] { ServiceState.Errno(2), "ERRNO=2" };
            yield return new object[] { ServiceState.BusError("org.freedesktop.DBus.Error.TimedOut"), "BUSERROR=org.freedesktop.DBus.Error.TimedOut" };
            yield return new object[] { ServiceState.MainPid(4711), "MAINPID=4711" };
        }

        [Theory]
        [MemberData(nameof(State_TestData))]
        public void State(ServiceState state, string expectedMessage)
        {
            Assert.Equal(expectedMessage, state.ToString());
        }

        private Socket CreateServerSocket(out string path, bool abstractPath = false)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
            path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            if (abstractPath)
            {
                path = "\0" + path;
            }
            var endPoint = new UnixDomainSocketEndPoint(path);
            if (abstractPath)
            {
                path = "@" + path.Substring(1);
            }
            socket.Bind(endPoint);
            return socket;
        }

        private string ReadMessage(Socket serverSocket)
        {
            var buffer = new byte[1024];

            int bytesRead = serverSocket.Receive(buffer);
            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            return message;
        }
    }
}
