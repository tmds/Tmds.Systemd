namespace Tmds.Systemd.Logging
{
    /// <summary>
    /// Options for the journal logger.
    /// </summary>
    public class JournalLoggerOptions
    {
        /// <summary>Drop messages instead of blocking.</summary>
        public bool DropWhenBusy { get; set; }
    }
}