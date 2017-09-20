using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Tmds.Systemd
{
    using SizeT = System.UIntPtr;
    public partial class ServiceManager
    {
        private static string s_socketPath;
        const string NOTIFY_SOCKET = "NOTIFY_SOCKET";

        public static bool Notify(ServiceState state, params ServiceState[] states)
        {
            string path = GetSocketPath();
            return Notify(path, state, states);
        }

        // For testing
        internal static unsafe bool Notify(string path, ServiceState state, ServiceState[] states)
        {
            if (path == null)
            {
                return false;
            }

            byte[] data;
            if (states.Length == 0)
            {
                data = Encoding.UTF8.GetBytes(state.ToString());
            }
            else
            {
                var ms = new MemoryStream();
                AppendState(ms, state);
                for (int i = 0; i < states.Length; i++)
                {
                    ms.WriteByte((byte)'\n');
                    AppendState(ms, states[i]);
                }
                data = ms.ToArray();
            }

            try
            {

                using (var socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified))
                {
                    var endPoint = new UnixDomainSocketEndPoint(path);
                    socket.Connect(endPoint);

                    socket.Send(data);

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string GetSocketPath()
        {
            if (s_socketPath == null)
            {
                s_socketPath = Environment.GetEnvironmentVariable(NOTIFY_SOCKET) ?? string.Empty;
                Environment.SetEnvironmentVariable(NOTIFY_SOCKET, null);
            }
            if (s_socketPath.Length == 0)
            {
                return null;
            }
            if (s_socketPath[0] == '@')
            {
                s_socketPath = "\0" + s_socketPath.Substring(1);
            }
            return s_socketPath;
        }

        // For testing
        internal static void ResetSocketPath()
        {
            s_socketPath = null;
        }

        private static void AppendState(MemoryStream stream, ServiceState state)
        {
            var buffer = Encoding.UTF8.GetBytes(state.ToString());
            stream.Write(buffer, 0, buffer.Length);
        }
    }
}
