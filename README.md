# VIEApps.Components.WebSockets.Fleck

This is a hard fork of [Fleck](https://github.com/statianzo/Fleck) (a WebSocket server implementation in C#) with some modifications to run well on .NET Standard 2.0

## NuGet
- Package ID: VIEApps.Components.WebSockets.Fleck
- Details: https://www.nuget.org/packages/VIEApps.Components.WebSockets.Fleck

## Modifications (compare to original Fleck)
- Allow to SetKeepAlive method only run on Windows (SocketWrapper class)
- Change to use Microsoft.Extensions.Logging instead of FleckLog
- Listen to 1000 pending connections

## Documentation
Please see the document of original Fleck at https://github.com/statianzo/Fleck
