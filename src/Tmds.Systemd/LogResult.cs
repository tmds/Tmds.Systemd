namespace Tmds.Systemd
{
    /// <summary>Result of a log operation.</summary>
    public enum LogResult
    {
        /// <summary>Message sent succesfully.</summary>
        Success,
        /// <summary>Unknown error.</summary>
        UnknownError,
        /// <summary>Logging service is not available.</summary>
        NotAvailable,
        /// <summary>Logging service is not supported.</summary>
        NotSupported,
        /// <summary>Message is too large to be sent.</summary>
        Size,
        /// <summary>Logging would block.</summary>
        Busy

    }
}