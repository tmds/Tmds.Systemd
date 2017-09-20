using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Tmds.Systemd
{
    public partial class ServiceManager
    {
        // not const, for testing
        static int SD_LISTEN_FDS_START = 3;
        const string LISTEN_FDS = "LISTEN_FDS";
        const string LISTEN_PID = "LISTEN_PID";

        const int F_SETFD = 2;
        const int FD_CLOEXEC = 1;

        [DllImport("libc", SetLastError=true)]
        internal static extern int fcntl(int fd, int cmd, int val);

        public static Socket[] GetListenSockets()
        {
            lock (_gate)
            {
                string listenFds = Environment.GetEnvironmentVariable(LISTEN_FDS);
                int fdCount = 0;
                if (listenFds == null || !int.TryParse(listenFds, out fdCount) || fdCount <= 0)
                {
                    return Array.Empty<Socket>();
                }
                string listenPid = Environment.GetEnvironmentVariable(LISTEN_PID);
                string myPid = Process.GetCurrentProcess().Handle.ToString();
                return GetListenSockets(myPid, listenPid, SD_LISTEN_FDS_START, fdCount);
            }
        }

        // internal for testing
        internal static Socket[] GetListenSockets(string myPid, string listenPid, int startFd, int fdCount)
        {
            Socket[] sockets = Array.Empty<Socket>();
            if (myPid == listenPid)
            {
                ReflectionMethods reflectionMethods = LookupMethods();
                sockets = new Socket[fdCount];
                for (int i = 0; i < fdCount; i++)
                {
                    sockets[i] = CreateSocketFromFd(startFd + i, reflectionMethods);
                }
            }
            Environment.SetEnvironmentVariable(LISTEN_FDS, null);
            Environment.SetEnvironmentVariable(LISTEN_PID, null);
            return sockets;
        }

        private static Socket CreateSocketFromFd(int fd, ReflectionMethods reflectionMethods)
        {
            // set CLOEXEC
            fcntl(fd, F_SETFD, FD_CLOEXEC);

            // static unsafe SafeCloseSocket CreateSocket(IntPtr fileDescriptor)
            var fileDescriptor = new IntPtr(fd);
            var safeCloseSocket = reflectionMethods.SafeCloseSocketCreate.Invoke(null, new object [] { fileDescriptor });

            // private Socket(SafeCloseSocket fd)
            var socket = reflectionMethods.SocketConstructor.Invoke(new[] { safeCloseSocket });
            return (Socket)socket;
        }

        private class ReflectionMethods
        {
            public MethodInfo SafeCloseSocketCreate;
            public ConstructorInfo SocketConstructor;
        }

        private static ReflectionMethods LookupMethods()
        {
            Assembly socketAssembly = typeof(Socket).GetTypeInfo().Assembly;
            Type safeCloseSocketType = socketAssembly.GetType("System.Net.Sockets.SafeCloseSocket");
            if (safeCloseSocketType == null)
            {
                ThrowNotSupported(nameof(safeCloseSocketType));
            }
            MethodInfo safeCloseSocketCreate = safeCloseSocketType.GetTypeInfo().GetMethod("CreateSocket", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(IntPtr) }, null);
            if (safeCloseSocketCreate == null)
            {
                ThrowNotSupported(nameof(safeCloseSocketCreate));
            }
            ConstructorInfo socketConstructor = typeof(Socket).GetTypeInfo().GetConstructor(BindingFlags.Public | BindingFlags.NonPublic| BindingFlags.Instance, null, new[] { safeCloseSocketType }, null);
            if (socketConstructor == null)
            {
                ThrowNotSupported(nameof(socketConstructor));
            }
            return new ReflectionMethods
            {
                SafeCloseSocketCreate = safeCloseSocketCreate,
                SocketConstructor = socketConstructor
            };
        }

        private static void ThrowNotSupported(string var)
        {
            throw new NotSupportedException($"Creating a Socket from a file descriptor is not supported on this platform. '{var}' not found.");
        }

        // for testing
        internal static void Reset(int fdStart)
        {
            SD_LISTEN_FDS_START = fdStart;
            Environment.SetEnvironmentVariable(LISTEN_FDS, null);
            Environment.SetEnvironmentVariable(LISTEN_PID, null);
        }
    }
}
