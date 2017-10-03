using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;
using Tmds.Systemd;

namespace Tmds.Systemd.Tests
{
    public class SocketTests
    {
        const int SD_LISTEN_FDS_START = 3;
        const string LISTEN_FDS = "LISTEN_FDS";
        const string LISTEN_PID = "LISTEN_PID";

        [Fact]
        public void Env()
        {
            string myPid = Process.GetCurrentProcess().Handle.ToString();
            // not set
            using (var fds = new FdSequence(3))
            {
                ServiceManager.Reset(fds[0]);
                Socket[] sockets = fds.GetListenSockets();
                Assert.Equal(0, sockets.Length);
            }

            // set
            using (var fds = new FdSequence(3))
            {
                ServiceManager.Reset(fds[0]);
                Environment.SetEnvironmentVariable(LISTEN_FDS, fds.Count.ToString());
                Environment.SetEnvironmentVariable(LISTEN_PID, myPid);
                
                // Retrieve Sockets
                Socket[] sockets = fds.GetListenSockets();
                Assert.Equal(3, sockets.Length);
                
                // Second call returns empty
                sockets = ServiceManager.GetListenSockets();
                Assert.Equal(0, sockets.Length);
            }

            ServiceManager.Reset(SD_LISTEN_FDS_START);
        }

        [Fact]
        public void PidMismatch()
        {
            using (var fds = new FdSequence(3))
            {
                string myPid = "1";
                string listenPid = "2";
                Socket[] sockets = fds.GetListenSockets(myPid, listenPid, fds[0], fds.Count);
                Assert.Equal(0, sockets.Length);
            }
        }

        [Fact]
        public void CorrectHandle()
        {
            using (var fds = new FdSequence(3))
            {
                Socket[] sockets = fds.GetListenSockets(fds[0], fds.Count);
                Assert.Equal(3, fds.Count);
                for (int i = 0; i < fds.Count; i++)
                {
                    Assert.Equal(fds[i], GetFd(sockets[i]));
                }
            }
        }

        [Fact]
        public void ListenSocketCanAccept()
        {
            using (var fds = new FdSequence(3))
            {
                Socket[] sockets = fds.GetListenSockets(fds[0], fds.Count);
                foreach (Socket server in sockets)
                {
                    Assert.Equal(server.SocketType, SocketType.Stream);
                    Assert.Equal(server.AddressFamily, AddressFamily.InterNetwork);
                    Assert.Equal(server.ProtocolType, ProtocolType.Tcp);
                    using (var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        client.Connect(server.LocalEndPoint);
                        using (var acceptedSocket = server.Accept())
                        { }
                    }
                }
            }
        }

        [Fact]
        public void Cloexec()
        {
            using (var fds = new FdSequence(1))
            {
                Socket[] sockets = fds.GetListenSockets(fds[0], fds.Count);
                var socket = sockets[0];
                using (var process = Process.Start("sleep", "10"))
                {
                    string fdPath = $"/proc/{process.Handle}/fd/{GetFd(sockets[0])}";
                    Assert.False(File.Exists(fdPath));
                    process.Kill();
                }
            }
        }

        private class FdSequence : IDisposable
        {
            private readonly static object s_gate = new object();
            private static int s_startFd = 2000;
            private int[] _fds;
            private Socket[] _listenSockets;

            public FdSequence(int count)
            {
                _fds = new int[count];
                lock (s_gate)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int fd = s_startFd++;
                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                        socket.Listen(10);
                        int rv = dup2(GetFd(socket), fd);
                        Assert.NotEqual(-1, rv);
                        _fds[i] = fd;
                    }
                }
            }

            public int this[int i] => _fds[i];

            public int Count => _fds.Length;

            public void Dispose()
            {
                for (int i = 0; i < Count; i++)
                {
                    Socket socket = null;
                    if (_listenSockets != null && i < _listenSockets.Length)
                    {
                        socket = _listenSockets[i];
                    }
                    if (socket != null)
                    {
                        _fds[i] = -1;
                        socket.Dispose();
                    }
                    else
                    {
                        int fd = _fds[i];
                        close(fd);
                    }
                }
            }

            public Socket[] GetListenSockets(int startFd, int fdCount)
                => _listenSockets ?? (_listenSockets = ServiceManager.GetListenSockets(string.Empty, string.Empty, startFd, fdCount));

            public Socket[] GetListenSockets(string myPid, string listenPid, int startFd, int fdCount)
                => _listenSockets ?? (_listenSockets = ServiceManager.GetListenSockets(myPid, listenPid, startFd, fdCount));

            public Socket[] GetListenSockets()
                => _listenSockets ?? (_listenSockets = ServiceManager.GetListenSockets());
        }

        [DllImport("libc", SetLastError=true)]
        internal static extern int dup2(int oldfd, int newfd);

        [DllImport("libc", SetLastError=true)]
        internal static extern int close(int fd);

        private static int GetFd(Socket socket)
        {
            return (int)socket.Handle;
        }
    }
}
