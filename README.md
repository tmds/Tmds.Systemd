[![Travis](https://api.travis-ci.org/tmds/Tmds.Systemd.svg?branch=master)](https://travis-ci.org/tmds/Tmds.Systemd)
[![NuGet](https://img.shields.io/nuget/v/Tmds.Systemd.svg)](https://www.nuget.org/packages/Tmds.Systemd)

Tmds.Systemd is a .NET Core library for interacting with systemd.

## Tmds.Systemd package

This package supports .NET Core 2.0+.

```C#
namespace Tmds.Systemd
{
  static class ServiceManager
  {
    // Notify service manager about start-up completion and other service status changes.
    bool Notify(ServiceState state, params ServiceState[] states);
    // Instantiate Sockets for the file descriptors passed by the service manager.
    Socket[] GetListenSockets();
    // Whether the process is running as part of a unit.
    bool IsRunningAsService;
    // Unique identifier of the runtime cycle of the unit.
    string InvocationId;
  }
  static class Journal
  {
    // Returns whether the journal service can be available.
    bool IsSupported { get; }
    // Returns whether the journal service is currently available.
    bool IsAvailable { get; }
    // The syslog identifier added to each log message.
    SyslogIdentifier { get; set; } = null;
    // Obtain a cleared JournalMessage. The Message must be Disposed to return it.
    JournalMessage GetMessage();
    // Submit a log entry to the journal.
    LogResult Log(LogFlags flags, JournalMessage message);
  }
  enum LogFlags
  { None,
    // Log levels.
    Emergency, ..., Debug,
    // Don't append a syslog identifier.
    DontAppendSyslogIdentifier,
    // Drop message instead of blocking.
    DropWhenBusy
  }
  enum LogResult
  { Success, UnknownError, NotAvailable, NotSupported, ... }
  class JournalMessage : IDisposable
  {
    // Appends a field to the message.
    JournalMessage Append(string name          , Type value);
    JournalMessage Append(JournalFieldName name, Type value);
  }
  // Represents a valid journal field name.
  struct JournalFieldName
  {
    static readonly JournalFieldName Priority;
    static readonly JournalFieldName SyslogIdentifier;
    static readonly JournalFieldName Message;
    // Implicit conversion from string. Throws when name is not valid.
    static implicit operator JournalFieldName(string str);
  }
}
```

## Tmds.Systemd.Logging package

This package allows to easly add journal logging to an ASP.NET Core application by adding the following line to the host building step:

This package supports .NET Core 2.1+.

```diff
         public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
             WebHost.CreateDefaultBuilder(args)
+            .ConfigureLogging(_ =>  _ .AddJournal())
             .UseStartup<Startup>();
     }
```

The logging can be configured with the following options:
```C#
class JournalLoggerOptions
{
  // Drop messages instead of blocking.
  bool DropWhenBusy { get; set; }
  // The syslog identifier added to each log message.
  string SyslogIdentifier { get; set; } = Journal.SyslogIdentifier;
}
```

The logger can be configured in `appsettings.json` using the `Journal` alias. The level specified in `Logging.Journal.LogLevel` overrides anything set in `Logging.LogLevel`. For example:

```json
"Logging": {
  "LogLevel": {
    "IncludeScopes": false,
    "Default": "Debug",
    "System": "Information",
    "Microsoft": "Information"
  },
  "Journal": {
    "SyslogIdentifier": "dotnet",
    "LogLevel": {
      "Default": "Warning",
      "System": "Warning",
      "Microsoft": "Warning",
      "Microsoft.AspNetCore.Hosting.Internal.WebHost": "Information"
    }
  }
}
```

The logging added is **structured logging**. For example, these entries are stored for a GET request:

```json
{
	"PRIORITY" : "6",
	"SYSLOG_IDENTIFIER" : "dotnet",
	"LOGGER" : "Microsoft.AspNetCore.Hosting.Internal.WebHost",
	"EVENTID" : "1",
	"MESSAGE" : "Request starting HTTP/1.1 GET http://localhost:5000/  ",
	"PROTOCOL" : "HTTP/1.1",
	"METHOD" : "GET",
	"SCHEME" : "http",
	"HOST" : "localhost:5000",
	"PATHBASE" : "",
	"PATH" : "/",
	"QUERYSTRING" : "",
	"REQUESTPATH" : "/",
	"CONNECTIONID" : "0HLDSN5JGSU79",
	"REQUESTID" : "0HLDSN5JGSU79:00000001",
}
{
	"PRIORITY" : "6",
	"SYSLOG_IDENTIFIER" : "dotnet",
	"LOGGER" : "Microsoft.AspNetCore.Hosting.Internal.WebHost",
	"EVENTID" : "2",
	"STATUSCODE" : "307",
	"REQUESTPATH" : "/",
	"CONNECTIONID" : "0HLDSN5JGSU79",
	"REQUESTID" : "0HLDSN5JGSU79:00000001",
	"MESSAGE" : "Request finished in 10.9215ms 307 ",
	"ELAPSEDMILLISECONDS" : "10.9215",
}
```

To follow the journal logging live you can use this command `journalctl -f -t dotnet -o json-pretty | grep -v \"_`.

## Using systemd with .NET Core applications

Services can be created on the system-level systemd instance or on a user-level instance that is running as long as the user is
logged in (unless lingering is enabled). The systemd commands, like `systemctl`, work on the system daemon by default. Passing
the `--user` flag targets the user daemon.

Manually created configuration files are placed under `/etc/systemd/system/ ` and `~/.config/systemd/user/` respectively.
For system unit files, ensure `chmod 664` and `chown root:root`.

On Fedora with [.NET SIG packages](http://fedoraloves.net), the SELinux context needs to be updated by running the following commands:

```sh
sudo yum install -y policycoreutils-python-util
sudo semanage fcontext -a -t bin_t /usr/lib/dotnet/dotnet
sudo restorecon -v /usr/lib64/dotnet/dotnet
```

Services are described with a file named `<unitname>.service` and look like this:

```ini
[Service]
Type=simple
WorkingDirectory=/home/username/app
ExecStart=/usr/bin/dotnet /home/username/app/App.dll
Restart=no
SyslogIdentifier=mydaemon
User=username
Group=groupname
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

The values used in the file for `Type` is the default of `simple`. As soon as the application has started, it is considered ready.
To control when the application is ready, you can set this to `notify` and call `ServiceManager.Notify(ServiceState.Ready)`.

The `ExecStart` must use a rooted path for the executable. If you are using .NET Core on RHEL7, you need to enable the proper
software collection (scl) as part of the `ExecStart`. For example, `ExecStart=/bin/scl enable rh-dotnet22 -- dotnet /home/username/app/App.dll`.

`Restart` is `no` is the default value, it means the application should not be restarted if it exits.
For long running services setting this to `on-failure` is recommended.

ASP.NET Core applications will throw an unhandled exception when they fail to bind the server address. The .NET Core runtime will turn
that into a process abort. On systems using `systemd-coredump` (like Fedora) this will show up in the journal and a coredump will be created.
Because this is a bit heavy, you may want to print out the exception and return a non-zero exit code instead:
```cs
public static int Main(string[] args)
{
    try
    {
        CreateWebHostBuilder(args).Build().Run();
        return 0;
    }
    catch (System.Exception e)
    {
        Console.Error.Write("Unhandled exception: ");
        Console.Error.WriteLine(e);
        return 1;
    }
}
```

The `WantedBy` `multi-user.target` indicates that, when enabled (i.e. installed), the service should be started with the system.

`SyslogIdentifier` is the log identifier used for application output from standard output and standard error. When unset, the `ExecStart` process name
will be used. Logging performed using the `Tmds.Systemd.Journal` class (and `Tmds.Systemd.Logging` package) is not aware of the value set here.
ASP.NET Core application will output some messages to standard out by default on startup and shutdown. To omit these, you can use the
SuppressStatusMessages method on the [HostBuilder](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.hostbuilder), for example: `.SuppressStatusMessages(Console.IsInputRedirected)`.

`Environment` can be used to set environment variables. Multiple `Environment` lines can be added to the service file.

After creating/editing the service file, the following commands can be used:

```
systemctl daemon-reload     # make systemd reload the unit files and pick up changes
systemctl start <unitname>  # start the service now
systemctl enable <unitname> # install the service so it gets started automatically (at the next boot)
```

To check the unit status and logging, use the following commands:

```
systemctl status <unitname> # status of the unit
journalctl -t <syslogid>    # log output
```

### SIGTERM handling

When systemd stops a service it does so by sending the SIGTERM signal. At that point, the service
should shut down cleanly. This signal can be handled via the `AppDomain.CurrentDomain.ProcessExit` event.
That event must be blocked during the shutdown and finally set `Environment.ExitCode` to `0` on clean shutdown.

A proper wire-up for this is part of:

- The ASP.NET Core [WebHost](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host) and [Generic Host](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host).
- The preview [System.CommandLine](System.CommandLine) package. See [Process termination handling](https://github.com/dotnet/command-line-api/wiki/Process-termination-handling).

### Socket based activation

systemd supports socket-based activation. This can be used for services that expose their functionality
via a socket. systemd will create the socket and keep it available. When someone accesses that socket
(for example, makes a TCP connection), systemd will start the service and pass the socket to it.
As long as no-one is using the service, the service will not be started. Optionally, a service can decide
to exit when it has finished its work (for example, it was idle for some time). When a new connection is made,
systemd will start it again.

The following code shows an example of obtaining the systemd socket using `ServiceManager.GetListenSockets`.
It also uses `Socket.Poll` to exit cleanly when no client has connected for some time.

```cs
class Program
{
    const int AutoExitTimeoutMs = 30 * 1000; // 30 sec

    static int Main(string[] args)
    {
        try
        {
            Socket acceptSocket = ServiceManager.GetListenSockets()[0];
            acceptSocket.Blocking = false;

            while (true)
            {
                if (!acceptSocket.Poll(AutoExitTimeoutMs * 1000, SelectMode.SelectRead))
                {
                    // Timeout expired, exit service.
                    Console.WriteLine("Service idle, exiting.");
                    return 0;
                }

                // Handle client
                try
                {
                    using (Socket clientSocket = acceptSocket.Accept())
                    {
                        clientSocket.Send(Encoding.UTF8.GetBytes("Hello"));
                    }
                }
                catch (Exception e)
                {
                    Console.Error.Write("Exception handling client: ");
                    Console.Error.WriteLine(e);
                }
            }
        }
        catch (Exception e)
        {
            Console.Error.Write("Unhandled exception: ");
            Console.Error.WriteLine(e);
            return 1;
        }
    }
}
```

Sockets are described with a file named `<unitname>.socket`. When the `.socket` and `.service` file have the
same unitname, systemd will use them together.

```ini
[Socket]
ListenStream=8080

[Install]
WantedBy=sockets.target
```

`ListenStream` specifies the TCP port for our service.

The `WantedBy` `sockets.target` indicates that, when enabled (i.e. installed), the socket should be started with the system.

The corresponding service file looks like this:
```ini
[Unit]
Requires=%N.socket
After=%N.socket

[Service]
Type=simple
WorkingDirectory=/home/username/app
ExecStart=/usr/bin/dotnet /home/username/app/App.dll

[Install]
WantedBy=multi-user.target
Also=%N.socket
```

The `%N` placeholder here will be substituted by systemd with the unitname.

The socket unit is referenced in a few places. `Requires` indicates the service needs the socket unit to start succesfully.
`After` means that socket must be started before the service. `Also` means when we enable the service, we want to enable the socket too.
