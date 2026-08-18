# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WatsonTcp is a C# TCP client/server library with integrated framing for reliable message-level delivery. It's part of the .NET Foundation and targets multiple frameworks: .NET Standard 2.0/2.1, .NET Framework 4.62/4.8, and .NET 6.0/8.0.

**Critical constraint**: WatsonTcp uses a custom framing protocol. Both client and server must use WatsonTcp, OR non-WatsonTcp endpoints must implement compatible framing (see FRAMING.md).

## Build and Test Commands

### Building
```bash
# Build the main library (multi-targeting)
dotnet build src/WatsonTcp/WatsonTcp.csproj

# Build all projects including tests
dotnet build

# Create NuGet package (happens automatically on build)
dotnet pack src/WatsonTcp/WatsonTcp.csproj
```

### Running Tests
The repository uses manual test projects rather than unit tests. To run a test:

```bash
# Run a specific test project (examples)
dotnet run --project src/Test.Client/Test.Client.csproj --framework net8.0
dotnet run --project src/Test.Server/Test.Server.csproj --framework net8.0

# Run with specific framework
dotnet run --project src/Test.Client/Test.Client.csproj --framework net462
```

Common test pairs to run together:
- `Test.Client` + `Test.Server` - Basic message exchange
- `Test.ClientStream` + `Test.ServerStream` - Stream-based message exchange
- `Test.SyncMessages` - Request/response pattern testing
- `Test.Metadata` - Messages with metadata
- `Test.FastDisconnect`, `Test.Reconnect` - Connection handling
- `Test.Throughput`, `Test.LargeMessages`, `Test.Parallel` - Performance testing

## Architecture

### Core Message Flow

1. **Message Framing**: WatsonTcp implements HTTP-like framing with JSON headers
   - Header: `{"len":1234,"status":"Normal",...}\r\n\r\n`
   - Followed by: raw data bytes
   - See FRAMING.md for integration with non-WatsonTcp endpoints

2. **Message Processing Pipeline**:
   - `WatsonMessageBuilder` - Constructs messages with headers
   - `WatsonMessage` - Core message structure with metadata, content length, sync flags, timestamps, GUIDs
   - `WatsonStream` - Handles reading/writing framed messages from TCP streams

3. **Client/Server Architecture**:
   - `WatsonTcpClient` - TCP client with connect/disconnect, send, and event handling
   - `WatsonTcpServer` - TCP server managing multiple clients via `ClientMetadataManager`
   - Both support SSL/TLS via optional certificate parameters

### Key Components

- **Settings Classes**: `WatsonTcpClientSettings`, `WatsonTcpServerSettings` - Configuration including debug logging, idle timeouts, buffer sizes
- **Events Classes**: `WatsonTcpClientEvents`, `WatsonTcpServerEvents` - Connection, disconnection, message received events
- **Callbacks Classes**: `WatsonTcpClientCallbacks`, `WatsonTcpServerCallbacks` - Async handlers for sync requests
- **SSL Configuration**: `WatsonTcpClientSslConfiguration`, `WatsonTcpServerSslConfiguration` - TLS settings, certificate validation
- **Keepalive**: `WatsonTcpKeepaliveSettings` - TCP keepalive configuration (not available in .NET Standard)

### Message Patterns

1. **Fire-and-forget**: `SendAsync(data)` or `SendAsync(data, metadata)`
2. **Synchronous request/response**: `SendAndWaitAsync(timeout, data)` returns `SyncResponse`
3. **Stream vs Byte Array**:
   - Set `Events.MessageReceived` for byte array consumption (async)
   - Set `Events.StreamReceived` for stream consumption (async for small messages, sync for large >MaxProxiedStreamSize)

### SSL/TLS Support

Both client and server support SSL via constructor overloads:
```csharp
new WatsonTcpServer(ip, port, pfxCertFile, pfxPassword);
new WatsonTcpClient(ip, port, pfxCertFile, pfxPassword);
```

Configure via `Settings.AcceptInvalidCertificates` and `Settings.MutuallyAuthenticate`.

## Important Design Patterns

### Async-First API
v6.0+ moved to async-first design. Synchronous APIs are marked obsolete. Always use:
- `SendAsync()` instead of `Send()`
- `ConnectAsync()` instead of `Connect()`
- Async event handlers and callbacks

### Cancellation Token Support
Background tasks honor cancellation tokens. Server/client disposal triggers cancellation.

### Client Identification
- Server tracks clients using GUID-based `ClientMetadata`
- Clients can specify GUID before connecting: `client.Settings.Guid = myGuid;`
- Server methods take client GUID: `server.SendAsync(clientGuid, data)`

### Metadata Handling
Metadata is CPU-intensive due to header parsing. Keep metadata small (<1KB) to avoid performance degradation.

### Disconnection Detection
- Event-based: `Events.ClientDisconnected` / `Events.ServerDisconnected` with `DisconnectReason` enum
- TCP keepalives available (.NET Core/.NET Framework only, not .NET Standard)
- Idle client timeout: `server.IdleClientTimeoutSeconds` for automatic disconnection

## Development Notes

### Multi-Targeting
The library targets 6 frameworks. When adding features:
- Check framework-specific capabilities (e.g., TCP keepalives unavailable in .NET Standard)
- Use conditional compilation if needed
- Test on multiple frameworks when behavior differs

### Logging and Debugging
```csharp
client.Settings.DebugMessages = true;
client.Settings.Logger = MyLoggerMethod;
client.Events.ExceptionEncountered += MyExceptionHandler;

void MyLoggerMethod(Severity sev, string msg)
{
    Console.WriteLine($"{sev}: {msg}");
}
```

### Common Pitfalls
- Only set ONE of `Events.MessageReceived` or `Events.StreamReceived` (MessageReceived takes precedence)
- Listener IP `127.0.0.1` only accepts local connections; use `null`, `*`, `+`, or `0.0.0.0` for all interfaces (requires admin)
- Ports <1024 require admin privileges
- Mono deployments: use AOT compilation and may need TLS downgrade from TLS 1.2

### Testing Disconnection Scenarios
The TcpTest project (https://github.com/jchristn/TcpTest) provides reference testing for:
- Graceful dispose (client/server)
- Abrupt termination (CTRL-C, process kill)
- Network interface down/cable unplugged (requires keepalives)

## File Organization

```
src/WatsonTcp/          # Main library source
  WatsonTcpClient.cs    # Client implementation
  WatsonTcpServer.cs    # Server implementation
  WatsonMessage.cs      # Message structure
  WatsonMessageBuilder.cs # Message construction
  WatsonStream.cs       # Stream reading/writing
  ClientMetadata*.cs    # Client tracking for server
  WatsonTcpMetrics.cs   # Public telemetry names/units/tag keys (Meter + ActivitySource "WatsonTcp")
  WatsonTcpInstrumentation.cs # Internal per-instance metrics/spans recorder
  *Events.cs           # Event definitions
  *Callbacks.cs        # Callback definitions
  *Settings.cs         # Configuration classes

src/Test.*/            # Manual test projects (not unit tests)
```

## NuGet Package

Version is defined in `src/WatsonTcp/WatsonTcp.csproj` (currently 6.4.0). Package builds automatically with `GeneratePackageOnBuild`.

## Telemetry

WatsonTcp emits vendor-neutral telemetry via `System.Diagnostics.Metrics` (a `Meter` named `WatsonTcp`) and `System.Diagnostics.ActivitySource` (an `ActivitySource` named `WatsonTcp`). Hosts (Radiant, OpenTelemetry, Prometheus) subscribe by name; WatsonTcp takes no telemetry-backend dependency. Public string contract lives in `WatsonTcpMetrics`. See `TELEMETRY.md`. Telemetry is on by default and can be disabled per instance via `Settings.EnableMetrics` / `Settings.EnableTracing`.
