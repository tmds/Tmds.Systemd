using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Tmds.Systemd
{
    /// <summary>
    /// Interact with the systemd system manager.
    /// </summary>
    public partial class ServiceManager
    {
        private static string _invocationId;
        private static bool? _isRunningAsService;
        private static readonly object _gate = new object();

        /// <summary>
        /// Returns whether the process is running as part of a unit.
        /// </summary>
        public static bool IsRunningAsService => _isRunningAsService ?? (bool)(_isRunningAsService = CheckServiceManager());

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

        private static bool CheckServiceManager()
        {
            // No point in testing anything unless it's Unix
            if (Environment.OSVersion.Platform != PlatformID.Unix)
            {
                return false;
            }

            // We've got invocation id, it's systemd >= 232 running a unit
            if (InvocationId != null)
            {
                return true;
            }

            // Either it's not a service, or systemd is < 232, do a bit more digging
            try
            {
                // Test parent process
                var parentPid = getppid();
                var ppidString = parentPid.ToString(NumberFormatInfo.InvariantInfo);

                // If parent PID is not 1, this may be a user unit, in this case it must match MANAGERPID envvar
                if (parentPid != 1
                    && Environment.GetEnvironmentVariable("MANAGERPID") != ppidString)
                {
                    return false;
                }

                // Check process name for the parent process to match "systemd\n"
                using (var commFile = File.Open("/proc/" + ppidString + "/comm", FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return commFile.ReadByte() == 's'
                        && commFile.ReadByte() == 'y'
                        && commFile.ReadByte() == 's'
                        && commFile.ReadByte() == 't'
                        && commFile.ReadByte() == 'e'
                        && commFile.ReadByte() == 'm'
                        && commFile.ReadByte() == 'd'
                        && commFile.ReadByte() == '\n'
                        && commFile.ReadByte() == -1;
                }
            }
            catch
            {
            }

            return false;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int getppid();
    }
}
