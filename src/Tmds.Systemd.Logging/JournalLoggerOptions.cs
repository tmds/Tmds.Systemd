using System;

namespace Tmds.Systemd.Logging
{
    /// <summary>
    /// Options for the journal logger.
    /// </summary>
    public class JournalLoggerOptions
    {
        /// <summary>
        /// Default formatter for exceptions.
        /// </summary>
        public static Action<Exception, JournalMessage> DefaultExceptionFormatter => JournalLogger.DefaultExceptionFormatter;

        /// <summary>
        /// Gets or sets a value indicating whether messages are dropped when busy instead of blocking.
        /// </summary>
        public bool DropWhenBusy { get; set; }

        /// <summary>
        /// Gets or sets the syslog identifier added to each log message.
        /// </summary>
        public string SyslogIdentifier { get; set; } = Journal.SyslogIdentifier;

        /// <summary>
        /// Gets or sets a delegate that is used to format exceptions.
        /// </summary>
        public Action<Exception, JournalMessage> ExceptionFormatter { get; set; } = DefaultExceptionFormatter;
    }
}
