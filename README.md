[![Travis](https://api.travis-ci.org/tmds/Tmds.Systemd.svg?branch=master)](https://travis-ci.org/tmds/Tmds.Systemd)

Tmds.Systemd is a netstandard library for interacting with systemd.

## Api

```C#
namespace Tmds.Systemd
{
  class ServiceManager
  {
    // Notify service manager about start-up completion and other service status changes.
    public static bool Notify(ServiceState state, params ServiceState[] states);
    // Instantiate Sockets for the file descriptors passed by the service manager.
    public static Socket[] GetListenSockets();
  }
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
    <PackageReference Include="Tmds.Systemd" Version="0.1.0-*"/>
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
