using System;

namespace Tmds.Systemd.Logging
{
    /// <summary>
    /// Options for the journal logger.
    /// </summary>
    public class JournalLoggerOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether messages are dropped when busy instead of blocking.
        /// </summary>
        public bool DropWhenBusy { get; set; }

        /// <summary>
        /// Gets or sets the syslog identifier added to each log message.
        /// </summary>
        public string SyslogIdentifier { get; set; } = Journal.SyslogIdentifier;

        /// <summary>
        /// Gets or sets a delegate that formats an exception and sets message fields.
        /// If unset, the default fields are set.
        /// </summary>
        public Action<Exception, JournalMessage> ExceptionFormatter { get; set; }

        /// <summary>
        /// The default message field name for a full exception stack trace.
        /// </summary>
        public static readonly JournalFieldName FullExceptionName = "FULL_EXCEPTION";

        /// <summary>
        /// The default implementation that writes the full exception stack trace into the
        /// <see cref="FullExceptionName"/> field of the journal message.
        /// </summary>
        /// <remarks>
        /// This implementation serves as a common default that may be used to write the full
        /// exception details in a place where log reader software can find it. Callers can provide
        /// their own implementation and either call into this as a basis or use another exception
        /// format and write to the <see cref="FullExceptionName"/> message field.
        /// </remarks>
        public static readonly Action<Exception, JournalMessage> FullExceptionFormatter = (ex, msg) =>
            msg.Append(FullExceptionName, ex.ToString());
    }
}