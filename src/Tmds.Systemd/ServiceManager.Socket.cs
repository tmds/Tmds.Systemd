using System;
using System.IO;
using System.IO.Pipes;
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

        
        const int SO_TYPE = 3;
        const int SO_PROTOCOL = 38;
        const int SO_DOMAIN = 39;
        const int AF_INET = 2;
        const int AF_INET6 = 10;
        const int AF_UNIX = 1;
        const int SOL_SOCKET = 1;

        [DllImport("libc", SetLastError=true)]
        internal static unsafe extern int getsockopt(int sockfd, int level, int optname, byte* optval, uint* optlen);

        /// <summary>
        /// Instantiate Sockets for the file descriptors passed by the service manager.
        /// </summary>
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

            // private bool _isListening = false;
            reflectionMethods.IsListening.SetValue(socket, true);

            EndPoint endPoint;
            int domain = GetSockOpt(fd, SO_DOMAIN);
            if (domain == AF_INET)
            {
                endPoint = new IPEndPoint(IPAddress.Any, 0);
            }
            else if (domain == AF_INET6)
            {
                endPoint = new IPEndPoint(IPAddress.Any, 0);
            }
            else if (domain == AF_UNIX)
            {
                // public UnixDomainSocketEndPoint(string path)
                endPoint = (EndPoint)reflectionMethods.UnixDomainSocketEndPointConstructor.Invoke(new[] { "/" });
            }
            else
            {
                throw new NotSupportedException($"Unknown address family: SO_DOMAIN={domain}.");
            }
            // internal EndPoint _rightEndPoint;
            reflectionMethods.RightEndPoint.SetValue(socket, endPoint);

            int sockType = GetSockOpt(fd, SO_TYPE);
            reflectionMethods.SocketType.SetValue(socket, sockType);

            return (Socket)socket;
        }

        private class ReflectionMethods
        {
            public MethodInfo SafeCloseSocketCreate;
            public ConstructorInfo SocketConstructor;
            public FieldInfo RightEndPoint;
            public FieldInfo IsListening;
            public FieldInfo SocketType;
            public ConstructorInfo UnixDomainSocketEndPointConstructor;
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
            FieldInfo rightEndPoint = typeof(Socket).GetTypeInfo().GetField("_rightEndPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (rightEndPoint == null)
            {
                ThrowNotSupported(nameof(rightEndPoint));
            }
            FieldInfo isListening = typeof(Socket).GetTypeInfo().GetField("_isListening", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (isListening == null)
            {
                ThrowNotSupported(nameof(isListening));
            }
            FieldInfo socketType = typeof(Socket).GetTypeInfo().GetField("_socketType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (socketType == null)
            {
                ThrowNotSupported(nameof(socketType));
            }
            Assembly pipeStreamAssembly = typeof(PipeStream).GetTypeInfo().Assembly;
            Type unixDomainSocketEndPointType = pipeStreamAssembly.GetType("System.Net.Sockets.UnixDomainSocketEndPoint");
            if (unixDomainSocketEndPointType == null)
            {
                ThrowNotSupported(nameof(unixDomainSocketEndPointType));
            }
            ConstructorInfo unixDomainSocketEndPointConstructor = unixDomainSocketEndPointType.GetTypeInfo().GetConstructor(BindingFlags.Public | BindingFlags.NonPublic| BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (unixDomainSocketEndPointConstructor == null)
            {
                ThrowNotSupported(nameof(unixDomainSocketEndPointConstructor));
            }
            return new ReflectionMethods
            {
                SafeCloseSocketCreate = safeCloseSocketCreate,
                SocketConstructor = socketConstructor,
                RightEndPoint = rightEndPoint,
                IsListening = isListening,
                SocketType = socketType,
                UnixDomainSocketEndPointConstructor = unixDomainSocketEndPointConstructor
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

        private static unsafe int GetSockOpt(int fd, int optname)
        {
            int rv = 0;
            uint optlen = 4;
            if (getsockopt(fd, SOL_SOCKET, optname, (byte*)&rv, &optlen) == 0)
            {
                return rv;
            }
            else
            {
                int errno = Marshal.GetLastWin32Error();
                throw new IOException($"Error while calling getsockopt(SOL_SOCKET, {optname}): errno={errno}.");
            }
        }
    }
}
