# FunCraft
FunCraft is a custom Minecraft Java Edition server written from scratch in C# on .NET 10.

At the moment, the codebase is focused on the early protocol and world bootstrap path:

- accept raw TCP connections
- handle the handshake and status ping
- perform a basic offline-mode login
- send configuration registries
- enter Play state
- spawn the player into a generated flat world
- stream an initial 5x5 chunk area

This repository is not a full game server yet. It is a compact protocol-first implementation that is useful for learning, experimentation, and incremental protocol work.

## Current target version
The code currently points most strongly to **Minecraft Java 1.21.10 / protocol 773**.

That target appears in several places in the codebase, including:
- `StatusHandler` server ping response
- `KnownPacksPacket`
- packet comments and chunk serializer notes
- registry generation notes and bundled reference material


## What is implemented today

| Area                         | Status                | Notes                                                       |
| ---------------------------- | --------------------- | ----------------------------------------------------------- |
| TCP listener                 | Implemented           | Raw socket server with IPv4/IPv6 dual mode                  |
| Packet framing               | Implemented           | Length-prefixed frames with VarInt packet IDs               |
| Handshake                    | Implemented           | Routes to Status or Login state                             |
| Status ping                  | Implemented           | Returns hardcoded MOTD, version, and player counts          |
| Login                        | Partially implemented | Offline-style login success, no auth/session integration    |
| Configuration                | Implemented           | Sends known packs and bundled registry data                 |
| Play bootstrap               | Partially implemented | Sends login/play data, teleport, chunk radius, time, chunks |
| World generation             | Implemented           | Cached flat world generator                                 |
| Chunk serialization          | Implemented           | Heightmaps, block states, biomes, light masks               |
| Persistence                  | Partially implemented | Only player position and rotation is saved                  |
| Multiplayer gameplay systems | Not implemented       | No entities, inventory, combat, commands, etc.              |
| Production readiness         | Not implemented       | Educational/prototype stage                                 |
| PostgreSQL DB Backend        | Partially implemented | Docker based                                                |

## Roadmap
The project is currently in the early protocol-first stage. Core networking, packet framing, connection state handling, and the first playable bootstrap path are already in place.

### Planned phases
- [x] **Phase 1 — Network foundation**
  - TCP server
  - `System.IO.Pipelines`-based packet processing
  - VarInt framing
  - Handshake / Status / Login packet groundwork

- [x] **Phase 2 — Connection state machine**
  - protocol state transitions
  - packet routing by connection state
  - server list ping
  - initial Configuration and Play bootstrap flow

- [x] **Phase 3 — Session and player lifecycle**
  - Redis-backed sessions
  - player registry
  - more complete Login → Play handshake
  - stronger connection/session validation

- [x] **Phase 4 — World and persistence**
  - expand the game world model
  - evolve the chunk system beyond the current bootstrap implementation
  - PostgreSQL-backed persistence
  - player/world save data

- [ ] **Phase 5 — Infrastructure and storage**
  - containerized deployment improvements
  - DynamoDB integration where it makes sense
  - S3-backed asset or snapshot storage

- [ ] **Phase 6 — Profiling and optimization**
  - BenchmarkDotNet benchmarks
  - PerfView profiling
  - dotTrace analysis
  - hot-path optimization for packet handling, chunk serialization, and memory usage

- [ ] **Phase 7 — Web platform (`FunCraft.Web`)**
  - [ ] **Authentication**
    - Microsoft OAuth
    - Minecraft account linking via Mojang APIs
    - offline-mode username/password flow
    
  - [ ] **Admin panel**
    - player management
    - bans and moderation tools
    - live server stats
    
  - [ ] **Trading post**
    - player-to-player item marketplace
    - PostgreSQL-backed listings and transactions
    
  - [ ] **Live map**
    - real-time world rendering
    - chunk data streamed from the server
    - SignalR-based updates

### Notes
Some groundwork for later phases already exists in the current codebase:
- early Play-state bootstrap
- flat-world generation
- initial chunk streaming
- Docker scaffolding

That means Phases 4 and 5 are not starting from zero, but they are still far from complete.

## Getting started
### Prerequisites
- .NET 10 SDK
- a local Minecraft Java client compatible with protocol 773 / version 1.21.10
- optional: Docker, if you want to use the server container setup

### Run locally
```shell
dotnet run --project FunCraft.Server/FunCraft.Server.csproj
```

By default, the server listens on port `25565`.

Configuration lives in `FunCraft.Server/appsettings.json`:

```json
{  
  "Server": {  
    "Port": 25565  
  }  
}
```

### What to expect when you connect
When the current implementation works as intended, a client should:
1. complete the handshake and login path
2. receive configuration registries
3. enter Play state
4. be teleported to the spawn position
5. receive a fixed chunk radius around spawn

This is still a narrow bootstrap flow, not a feature-complete Minecraft experience.
