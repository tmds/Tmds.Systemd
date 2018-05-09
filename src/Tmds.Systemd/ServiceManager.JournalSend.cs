using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Tmds.Systemd
{
    using SizeT = System.UIntPtr;
    using SSizeT = System.IntPtr;

    /// <summary>
    /// Log flags.
    /// </summary>
    [Flags]
    public enum LogFlags
    {
        /// <summary>Specifies that a logging category should not write any messages.</summary>
        None = 0,
        /// <summary>System is unusable.</summary>
        Emergency = 1,
        /// <summary>Action must be taken immediately.</summary>
        Alert = 2,
        /// <summary>Critical conditions.</summary>
        Critical = 3,
        /// <summary>Error conditions.</summary>
        Error = 4,
        /// <summary>Warning conditions.</summary>
        Warning = 5,
        /// <summary>Normal but significant conditions.</summary>
        Notice = 6,
        /// <summary>Informational.</summary>
        Information = 7,
        /// <summary>Debug-level messages.</summary>
        Debug = 8
    }

    public partial class ServiceManager
    {
        private const string JournalSocketPath = "/run/systemd/journal/socket";
        private const int MaxIovs = 100;
        private const int EINTR = 4;

        [ThreadStatic]
        private static IOVector[] s_iovs;
        [ThreadStatic]
        private static GCHandle[] s_gcHandles;

        private static Socket s_journalSocket;
        private static bool s_hasJournal = true;

        /// <summary>The syslog identifier string added to each message.</summary>
        public static string SyslogIdentifier { get; set; } = "dotnet";

        private static Socket GetJournalSocket()
        {
            if (!s_hasJournal)
            {
                return null;
            }

            if (s_journalSocket == null)
            {
                Socket journalSocket = Volatile.Read(ref s_journalSocket);
                if (journalSocket == null)
                {
                    s_hasJournal = File.Exists(JournalSocketPath);
                    if (s_hasJournal)
                    {
                        journalSocket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
                        journalSocket.Connect(new UnixDomainSocketEndPoint(JournalSocketPath));
                        if (Interlocked.CompareExchange(ref s_journalSocket, journalSocket, null) != null)
                        {
                            journalSocket.Dispose();
                        }
                    }
                }
            }
            return s_journalSocket;
        }

        /// <summary>
        /// Submit a log entry to the journal.
        /// </summary>
        public static void Log(LogFlags flags, IReadOnlyList<KeyValuePair<string, object>> fields)
        {
            using (var message = JournalMessage.Get())
            {
                foreach (var field in fields)
                {
                    message.Append(field.Key, field.Value);
                }
                Log(flags, message);
            }
        }

        /// <summary>
        /// Submit a log entry to the journal.
        /// </summary>
        public static unsafe void Log(LogFlags flags, JournalMessage message)
        {
            if (message.IsEmpty)
            {
                return;
            }

            Socket socket = GetJournalSocket();
            if (socket == null)
            {
                return;
            }

            int priority = (int)flags & 0xf;
            if (priority != 0)
            {
                message.Append("PRIORITY", priority - 1);
            }
            if (SyslogIdentifier != null)
            {
                message.Append("SYSLOG_IDENTIFIER", SyslogIdentifier);
            }

            List<ArraySegment<byte>> data = message.GetData();
            int dataLength = data.Count;
            Span<IOVector> iovs = stackalloc IOVector[dataLength <= MaxIovs ? dataLength : 0];
            if (iovs.IsEmpty)
            {
                iovs = GetHeapIovs(dataLength);
            }
            Span<GCHandle> handles = stackalloc GCHandle[dataLength <= MaxIovs ? dataLength : 0];
            if (handles.IsEmpty)
            {
                handles = GetHeapHandles(dataLength);
            }
            for (int i = 0; i < data.Count; i++)
            {
                handles[i] = GCHandle.Alloc(data[i].Array, GCHandleType.Pinned);
                iovs[i].Base = handles[i].AddrOfPinnedObject();
                iovs[i].Length = new IntPtr(data[i].Count);
            }
            fixed (IOVector* pIovs = &MemoryMarshal.GetReference(iovs))
            {
                bool loop;
                do
                {
                    loop = false;
                    msghdr msg;
                    msg.msg_iov = pIovs;
                    msg.msg_iovlen = (SizeT)dataLength;
                    int rv = sendmsg(socket.Handle.ToInt32(), &msg, 0).ToInt32();
                    if (rv < 0)
                    {
                        var errno = Marshal.GetLastWin32Error();
                        if (errno == EINTR)
                        {
                            loop = true;
                        }
                        else
                        {
                            Console.WriteLine($"Error writing message to journal: errno={errno}.");
                        }
                    }
                } while (loop);
            }
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i].Free();
            }
        }

        private static Span<IOVector> GetHeapIovs(int length)
        {
            if (s_iovs == null || s_iovs.Length < length)
            {
                s_iovs = new IOVector[length];
            }
            return new Span<IOVector>(s_iovs, 0, length);
        }

        private static Span<GCHandle> GetHeapHandles(int length)
        {
            if (s_gcHandles == null || s_gcHandles.Length < length)
            {
                s_gcHandles = new GCHandle[length];
            }
            return new Span<GCHandle>(s_gcHandles, 0, length);
        }

        private unsafe struct IOVector
        {
            public IntPtr Base;
            public IntPtr Length;
        }

        private unsafe struct msghdr
        {
            public IntPtr msg_name; //optional address
            public uint msg_namelen; //size of address
            public IOVector* msg_iov; //scatter/gather array
            public SizeT msg_iovlen; //# elements in msg_iov
            public void* msg_control; //ancillary data, see below
            public SizeT msg_controllen; //ancillary data buffer len
            public int msg_flags; //flags on received message
        }

        [DllImport ("libc", SetLastError=true)]
        private static unsafe extern SSizeT sendmsg(int sockfd, msghdr* msg, int flags);
    }
}
