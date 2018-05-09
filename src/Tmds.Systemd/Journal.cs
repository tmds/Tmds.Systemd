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
    /// Interact with the systemd journal service.
    /// </summary>
    public partial class Journal
    {
        private const string JournalSocketPath = "/run/systemd/journal/socket";
        private const int MaxIovs = 100;
        private const int EINTR = 4;

        [ThreadStatic]
        private static IOVector[] s_iovs;
        [ThreadStatic]
        private static GCHandle[] s_gcHandles;

        private static Socket s_journalSocket;
        private static bool IsEnabled = File.Exists(JournalSocketPath);

        /// <summary>The syslog identifier string added to each message.</summary>
        public static string SyslogIdentifier { get; set; } = "dotnet";

        /// <summary>Obtain a cleared JournalMessage. The Message must be Disposed to return it.</summary>
        public static JournalMessage GetJournalMessage()
        {
            return JournalMessage.Get(IsEnabled);
        }

        private static Socket GetJournalSocket()
        {
            if (!IsEnabled)
            {
                return null;
            }

            if (s_journalSocket == null)
            {
                Socket journalSocket = Volatile.Read(ref s_journalSocket);
                if (journalSocket == null)
                {
                    journalSocket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
                    journalSocket.Connect(new UnixDomainSocketEndPoint(JournalSocketPath));
                    if (Interlocked.CompareExchange(ref s_journalSocket, journalSocket, null) != null)
                    {
                        journalSocket.Dispose();
                    }
                }
            }
            return s_journalSocket;
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
                        int errno = Marshal.GetLastWin32Error();
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
