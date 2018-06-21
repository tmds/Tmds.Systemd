using System;

namespace Tmds.Systemd
{
    /// <summary>
    /// Log flags.
    /// </summary>
    [Flags]
    public enum LogFlags
    {
        /// <summary>No flags.</summary>
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
        Debug = 8,

        /// <summary>Drop the message instead of blocking.</summary>
        DropWhenBusy               = 1 << 4,
        /// <summary>Don't append a syslog identifier.</summary>
        DontAppendSyslogIdentifier = 1 << 5
    }
}
