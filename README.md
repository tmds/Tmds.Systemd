[![Travis](https://api.travis-ci.org/tmds/Tmds.Systemd.svg?branch=master)](https://travis-ci.org/tmds/Tmds.Systemd)
[![NuGet](https://img.shields.io/nuget/v/Tmds.Systemd.svg)](https://www.nuget.org/packages/Tmds.Systemd)

Tmds.Systemd is a .NET Core library for interacting with systemd.

## Api

### Tmds.Systemd package

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
  }
  static class Journal
  {
    // Returns whether the journal service is available.
    bool IsAvailable { get; }
    // The syslog identifier string added to each log message.
    SyslogIdentifier { get; set; } = "dotnet";
    // Obtain a cleared JournalMessage. The Message must be Disposed to return it.
    JournalMessage GetMessage();
    // Submit a log entry to the journal.
    void Log(LogFlags flags, JournalMessage message);
  }
  enum LogFlags
  { None, Emergency, ..., Debug };
  class JournalMessage : IDisposable
  {
    // Returns whether the journal service is available.
    bool IsEnabled { get; }
    // Appends a field to the message.
    JournalMessage Append(string name, object value);
    JournalMessage Append(JournalFieldName name, object value);
  }
  // Represents a valid journal field name.
  struct JournalFieldName
  {
    static readonly JournalFieldName Priority;
    static readonly JournalFieldName SyslogIdentifier;
    static readonly JournalFieldName Message;
    // Creates a JournalFieldNames. Throws when name is not valid.
    public JournalFieldName(string name);
  }
}
```

### Tmds.Systemd.Logging package

This package allows to easly add journal logging to an ASP.NET Core application by adding the following line to the host building step:

This package supports .NET Core 2.1+.

```diff
         public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
             WebHost.CreateDefaultBuilder(args)
+            .ConfigureLogging(_ =>  _ .AddJournal())
             .UseStartup<Startup>();
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

## Example

Create the application:

```
$ dotnet new console -o MyDaemon
$ cd MyDaemon
```

To use a daily build, add the myget NuGet feed to, `NuGet.Config`.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="tmds" value="https://www.myget.org/F/tmds/api/v3/index.json" />
  </packageSources>
</configuration>
```

Add the package reference to `MyDaemon.csproj`.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Tmds.Systemd" Version="0.4.0-*"/>
  </ItemGroup>
</Project>
```

Restore the dependency:

```
$ dotnet restore
```

Implement the daemon in `Program.cs`.

```C#
using System;
using static System.Threading.Thread;
using static System.Console;
using Tmds.Systemd;

namespace MyDaemon
{
    class Program
    {
        static void Main(string[] args)
        {
            Sleep(2000);
            ServiceManager.Notify(ServiceState.Ready);
            while (true)
            {
                foreach (var status in new [] { "Doing great", "Everything fine", "Still running" })
                {
                    ServiceManager.Notify(ServiceState.Status(status));
                    Sleep(5000);
                }
            }
        }
    }
}
```

Publish the application:
```
$ dotnet publish -c Release
```

Create a user and folder for our daemon:
```
# useradd -s /sbin/nologin mydaemon
# mkdir /var/mydaemon
```

Now we copy the application in the folder:
```
# cp -r ./bin/Release/netcoreapp2.0/publish/* /var/mydaemon
# chown -R mydaemon:mydaemon /var/mydaemon
```

Create a systemd service file:

```
# touch /etc/systemd/system/mydaemon.service
# chmod 664 /etc/systemd/system/mydaemon.service
```

Add this content to the file `mydaemon.service`.

```
[Unit]
Description=My .NET Core Daemon

[Service]
Type=notify
WorkingDirectory=/var/mydaemon
ExecStart=/opt/dotnet/dotnet MyDaemon.dll
Restart=always
RestartSec=10
SyslogIdentifier=mydaemon
User=mydaemon

[Install]
WantedBy=multi-user.target
```

The `Type=notify` indicates the application will signal when it is ready.

Start the service:

```
# systemctl start mydaemon
```

Notice the command blocks until it gets the `Ready` notification.

When we query the status of the daemon, we can see the `Status` notifications.

```
$ watch systemctl status mydaemon
```
