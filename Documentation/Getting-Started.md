This document explains how to run and work on FunCraft locally.

## Prerequisites

From the project files, the intended development stack is:

- .NET 10 SDK
- Visual Studio or Rider
- optional Docker support
- a Minecraft Java client compatible with protocol 773 / version 1.21.10

## Solution structure

The solution file is `FunCraft.slnx` and includes:

- `FunCraft.Server`
- `FunCraft.Network`
- `FunCraft.Protocol`
- `FunCraft.World`
- `FunCraft.WorldGen`
- `FunCraft.RegistryBuilder`
- `FunCraft.Tests`

## Run the server

From the repository root:

```bash
dotnet run --project FunCraft.Server/FunCraft.Server.csproj
```

The server uses `Host.CreateDefaultBuilder`, so standard .NET host configuration rules apply.

## Configuration

The shipped `appsettings.json` currently contains:

```json
{  
  "Server": {  
    "Port": 25565  
  }  
}
```

The network server reads `Server:Port` and defaults to `25565` if no value is provided.

## What the server currently does

When a compatible client connects, the current code attempts to:

1. accept the TCP socket
2. parse length-prefixed packets
3. route the connection to Status or Login based on the handshake
4. complete a basic login flow
5. send known packs and registry data during Configuration
6. enter Play state
7. send a spawn teleport, time update, view radius, and initial chunks

## Practical limitations

This is still a prototype. Expect hardcoded behavior such as:

- hardcoded server status text and player counts
- hardcoded entity ID and spawn location
- hardcoded view distance in the play bootstrap
- offline-style login success with zero profile properties
- no persistence, command system, or game rules layer

## Docker notes

`FunCraft.Server` includes a Dockerfile oriented around .NET 10 and native publishing.

The file currently supports:

- restore/build/publish stages
- optional Visual Studio debug container behavior
- final runtime image creation

That said, Docker support should be treated as infrastructure scaffolding rather than proof of production readiness.

## Recommended first checks when resuming development

### 1. Verify the protocol target

Before adding features, confirm the intended Minecraft patch/protocol pair and update these together:

- status ping version
- known pack version
- registry source data
- block ID assumptions
- documentation

### 2. Verify `registries.bin`

The server loads registry packets from embedded resources during startup. If those resources are stale or missing, the configuration phase will drift out of sync with the client.

See `Registry-Pipeline.md`.

### 3. Verify local build and tests

Typical commands:

```shell
dotnet build FunCraft.slnx  
dotnet test FunCraft.Tests/FunCraft.Tests.csproj
```

## Expected connection target

The code most strongly points to **Minecraft Java 1.21.10 / protocol 773**.

If a client on a different patch level fails during handshake, configuration, or play bootstrap, version drift is the first suspect. In Minecraft protocol work, the first suspect is often also the second suspect wearing a fake moustache.