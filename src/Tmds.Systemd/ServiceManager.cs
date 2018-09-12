using System;

namespace Tmds.Systemd
{
    /// <summary>
    /// Interact with the systemd system manager.
    /// </summary>
    public partial class ServiceManager
    {
        private static string _invocationId;
        private static readonly object _gate = new object();

        /// <summary>
        /// Returns whether the process is running as part of a unit.
        /// </summary>
        public static bool IsRunningAsService => InvocationId != null;

        /// <summary>
        /// Returns unique identifier of the runtime cycle of the unit.
        /// </summary>
        public static string InvocationId
        {
            get
            {
                if (_invocationId == null)
                {
                    _invocationId = Environment.GetEnvironmentVariable("INVOCATION_ID") ?? string.Empty;
                }
                if (_invocationId == string.Empty)
                {
                    return null;
                }
                return _invocationId;
            }
        }
    }
}
