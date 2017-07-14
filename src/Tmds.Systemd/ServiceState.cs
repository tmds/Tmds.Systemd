using System;

namespace Tmds.Systemd
{
    public struct ServiceState
    {
        private string _state;

        public static ServiceState Ready => new ServiceState("READY=1");
        public static ServiceState Reloading => new ServiceState("RELOADING=1");
        public static ServiceState Stopping => new ServiceState("STOPPING=1");
        public static ServiceState Watchdog => new ServiceState("WATCHDOG=1");
        public static ServiceState Status(string value) => new ServiceState($"STATUS={value}");
        public static ServiceState Errno(int value) => new ServiceState($"ERRNO={value}");
        public static ServiceState BusError(string value) => new ServiceState($"BUSERROR={value}");
        public static ServiceState MainPid(int value) => new ServiceState($"MAINPID={value}");

        public ServiceState(string state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public override string ToString() => _state;
    }
}