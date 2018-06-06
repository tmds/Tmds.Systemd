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
    /// Interact with the journal service.
    /// </summary>
    public static class Journal
    {
        private const int MaxIovs = 20;
        private const int EINTR = 4;
        private const int EAGAIN = 11;
        private const int MSG_DONTWAIT = 0x40;

        private static Socket s_journalSocket;
        private static string s_journalSocketPath = "/run/systemd/journal/socket";
        private static bool? s_isAvailable = true;

        // for testing
        internal static void ConfigureJournalSocket(string journalSocketPath)
        {
            s_isAvailable = null;
            s_journalSocketPath = journalSocketPath;
        }

        /// <summary>Returns whether the journal service is available.</summary>
        public static bool IsAvailable
        {
            get
            {
                if (s_isAvailable == null)
                {
                    s_isAvailable = File.Exists(s_journalSocketPath);
                }
                return s_isAvailable.Value;
            }
        }

        /// <summary>The syslog identifier string added to each log message.</summary>
        public static string SyslogIdentifier { get; set; } = "dotnet";

        /// <summary>Obtain a cleared JournalMessage. The Message must be Disposed to return it.</summary>
        public static JournalMessage GetMessage()
        {
            return JournalMessage.Get(IsAvailable);
        }

        private static Socket GetJournalSocket()
        {
            if (!IsAvailable)
            {
                return null;
            }

            if (s_journalSocket == null)
            {
                Socket journalSocket = Volatile.Read(ref s_journalSocket);
                if (journalSocket == null)
                {
                    journalSocket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
                    journalSocket.Connect(new UnixDomainSocketEndPoint(s_journalSocketPath));
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
                message.Append(JournalFieldName.Priority, priority - 1);
            }
            if (SyslogIdentifier != null)
            {
                message.Append(JournalFieldName.SyslogIdentifier, SyslogIdentifier);
            }

            List<ArraySegment<byte>> data = message.GetData();
            int dataLength = data.Count;
            if (dataLength > MaxIovs)
            {
                // We should handle this the same way as EMSGSIZE, which we don't handle atm.
                ErrorWhileLogging("size exceeded");
                return;
            }
            Span<IOVector> iovs = stackalloc IOVector[dataLength];
            Span<GCHandle> handles = stackalloc GCHandle[dataLength];
            for (int i = 0; i < data.Count; i++)
            {
                handles[i] = GCHandle.Alloc(data[i].Array, GCHandleType.Pinned);
                iovs[i].Base = handles[i].AddrOfPinnedObject();
                iovs[i].Length = new IntPtr(data[i].Count);
            }
            int sendmsgFlags = 0;
            if ((flags & LogFlags.DropWhenBusy) != 0)
            {
                sendmsgFlags |= MSG_DONTWAIT;
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
                    int rv = sendmsg(socket.Handle.ToInt32(), &msg, sendmsgFlags).ToInt32();
                    if (rv < 0)
                    {
                        int errno = Marshal.GetLastWin32Error();
                        if (errno == EINTR)
                        {
                            loop = true;
                        }
                        else if (errno == EAGAIN)
                        { }
                        else
                        {
                            ErrorWhileLogging($"errno={errno}");
                        }
                    }
                } while (loop);
            }
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i].Free();
            }
        }

        private static void ErrorWhileLogging(string cause)
        {
            Console.WriteLine($"Error writing message to journal: {cause}");
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
