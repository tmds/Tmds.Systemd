using System;

namespace Tmds.Systemd.Logging
{
    /// <summary>
    /// Options for the journal logger.
    /// </summary>
    public class JournalLoggerOptions
    {
        /// <summary>Drop messages instead of blocking.</summary>
        public bool DropWhenBusy { get; set; }
        /// <summary>The syslog identifier added to each log message.</summary>
        public string SyslogIdentifier { get; set; } = Journal.SyslogIdentifier;
        /// <summary>Sets the full exception field instead of separate fields.</summary>
        public bool SetFullException { get; set; }
        /// <summary>Formats a full exception. If unset, <see cref="Exception.ToString"/> is used.</summary>
        public Func<Exception, string> FullExceptionFormatter { get; set; }
    }
}