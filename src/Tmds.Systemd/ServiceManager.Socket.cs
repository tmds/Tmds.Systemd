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

        const int SOL_SOCKET = 1;
        
        const int SO_TYPE = 3;
        const int SO_PROTOCOL = 38;
        const int SO_DOMAIN = 39;
        const int SO_ACCEPTCONM = 30;

        const int AF_INET = 2;
        const int AF_INET6 = 10;
        const int AF_UNIX = 1;

        const int SOCK_STREAM = 1;
        const int SOCK_DGRAM = 2;
        const int SOCK_RAW = 3;
        const int SOCK_RDM = 4;
        const int SOCK_SEQPACKET = 5;

        const int IPPROTO_ICMP = 1;
        const int IPPROTO_TCP = 6;
        const int IPPROTO_UDP = 17;
        const int IPPROTO_ICMPV6 = 58;

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
                string myPid = Process.GetCurrentProcess().Id.ToString();
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
            bool listening = GetSockOpt(fd, SO_ACCEPTCONM) != 0;
            reflectionMethods.IsListening.SetValue(socket, listening);

            EndPoint endPoint;
            AddressFamily addressFamily = ConvertAddressFamily(GetSockOpt(fd, SO_DOMAIN));
            if (addressFamily == AddressFamily.InterNetwork)
            {
                endPoint = new IPEndPoint(IPAddress.Any, 0);
            }
            else if (addressFamily == AddressFamily.InterNetworkV6)
            {
                endPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
            }
            else if (addressFamily == AddressFamily.Unix)
            {
                // public UnixDomainSocketEndPoint(string path)
                endPoint = (EndPoint)reflectionMethods.UnixDomainSocketEndPointConstructor.Invoke(new[] { "/" });
            }
            else
            {
                throw new NotSupportedException($"Unknown address family: {addressFamily}.");
            }
            // internal EndPoint _rightEndPoint;
            reflectionMethods.RightEndPoint.SetValue(socket, endPoint);
            // private AddressFamily _addressFamily;
            reflectionMethods.AddressFamily.SetValue(socket, addressFamily);

            SocketType sockType = ConvertSocketType(GetSockOpt(fd, SO_TYPE));
            // private SocketType _socketType;
            reflectionMethods.SocketType.SetValue(socket, sockType);

            ProtocolType protocolType = ConvertProtocolType(GetSockOpt(fd, SO_PROTOCOL));
            // private ProtocolType _protocolType;
            reflectionMethods.ProtocolType.SetValue(socket, protocolType);

            return (Socket)socket;
        }

        private class ReflectionMethods
        {
            public MethodInfo SafeCloseSocketCreate;
            public ConstructorInfo SocketConstructor;
            public FieldInfo RightEndPoint;
            public FieldInfo IsListening;
            public FieldInfo SocketType;
            public FieldInfo AddressFamily;
            public FieldInfo ProtocolType;
            public ConstructorInfo UnixDomainSocketEndPointConstructor;
        }

        private static ReflectionMethods LookupMethods()
        {
            Assembly socketAssembly = typeof(Socket).GetTypeInfo().Assembly;
            Type safeCloseSocketType = socketAssembly.GetType("System.Net.Sockets.SafeSocketHandle") ?? // .NET Core 3.0+
                                       socketAssembly.GetType("System.Net.Sockets.SafeCloseSocket");
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
            FieldInfo addressFamily = typeof(Socket).GetTypeInfo().GetField("_addressFamily", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (addressFamily == null)
            {
                ThrowNotSupported(nameof(addressFamily));
            }
            FieldInfo protocolType = typeof(Socket).GetTypeInfo().GetField("_protocolType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (protocolType == null)
            {
                ThrowNotSupported(nameof(protocolType));
            }

            // .NET Core 2.1+
            Type unixDomainSocketEndPointType = socketAssembly.GetType("System.Net.Sockets.UnixDomainSocketEndPoint");
            if (unixDomainSocketEndPointType == null)
            {
                // .NET Core 2.0
                Assembly pipeStreamAssembly = typeof(PipeStream).GetTypeInfo().Assembly;
                unixDomainSocketEndPointType = pipeStreamAssembly.GetType("System.Net.Sockets.UnixDomainSocketEndPoint");
            }
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
                AddressFamily = addressFamily,
                ProtocolType = protocolType,
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

        private static SocketType ConvertSocketType(int socketType)
        {
            if (socketType == SOCK_STREAM)
            {
                return SocketType.Stream;
            }
            else if (socketType == SOCK_DGRAM)
            {
                return SocketType.Dgram;
            }
            else if (socketType == SOCK_RAW)
            {
                return SocketType.Raw;
            }
            else if (socketType == SOCK_RDM)
            {
                return SocketType.Rdm;
            }
            else if (socketType == SOCK_SEQPACKET)
            {
                return SocketType.Seqpacket;
            }
            else
            {
                throw new NotSupportedException($"Unknown socket type: SO_TYPE={socketType}.");
            }
        }

        private static AddressFamily ConvertAddressFamily(int addressFamily)
        {
            if (addressFamily == AF_INET)
            {
                return AddressFamily.InterNetwork;
            }
            else if (addressFamily == AF_INET6)
            {
                return AddressFamily.InterNetworkV6;
            }
            else if (addressFamily == AF_UNIX)
            {
                return AddressFamily.Unix;
            }
            else
            {
                throw new NotSupportedException($"Unknown Address Family: SO_DOMAIN={addressFamily}.");
            }
        }

        private static ProtocolType ConvertProtocolType(int protocolType)
        {
            if (protocolType == IPPROTO_ICMP)
            {
                return ProtocolType.Icmp;
            }
            else if (protocolType == IPPROTO_ICMPV6)
            {
                return ProtocolType.IcmpV6;
            }
            else if (protocolType == IPPROTO_TCP)
            {
                return ProtocolType.Tcp;
            }
            else if (protocolType == IPPROTO_UDP)
            {
                return ProtocolType.Udp;
            }
            else
            {
                throw new NotSupportedException($"Unknown protocol type: SO_PROTOCOL={protocolType}.");
            }
        }
    }
}
