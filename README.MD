# FunCraft

A Minecraft Java Edition 1.20.1 server built from scratch in C#/.NET 10.

This project was made over the weekend in order to prepare myself for a Job Interview for Senior Backend Dev.
Please do NOT contribute unless in separate branch.

Also, i've used resharper, so there's a lot of vars, even on primitives.

## Project Structure

```
FunCraft.sln
├── src/
│   ├── FunCraft.Protocol
│   │   # Packet definitions, types, primitives
│   ├── FunCraft.Network
│   │   # TCP listener, pipeline, connection handling
│   └── FunCraft.Server
│       # Entry point, DI wiring
└── tests/
    └── FunCraft.Tests
        # xUnit tests
```

## Prerequisites

- .NET 10 SDK
- Visual Studio 2026 or Rider
- Docker Desktop
- Minecraft Java Edition 1.20.1

## Running

```bash
dotnet run --project src/FunCraft.Server
```

Then connect with Minecraft Java 1.21.10 to `localhost:25565`.

## Roadmap

- [x] **Phase 1** — Network foundation (TCP, Pipelines, VarInt, Handshake/Status/Login packets)
- [x] **Phase 2** — Connection state machine, packet routing, server list ping
- [ ] **Phase 3** — Redis sessions, player registry, Login → Play handshake
- [ ] **Phase 4** — Game world, chunk system, PostgreSQL persistence
- [ ] **Phase 5** — Docker, DynamoDB, S3
- [ ] **Phase 6** — Profiling and optimization (BenchmarkDotNet, PerfView, dotTrace)
- [ ] **Phase 7** — ASP.NET Core web app (`FunCraft.Web`)
  - [ ] Authentication — Microsoft OAuth (links to Minecraft account via Mojang API) + offline mode username/password
  - [ ] Admin panel — player management, bans, server stats
  - [ ] Trading post — player-to-player item marketplace backed by PostgreSQL
  - [ ] Live map — real-time world render using chunk data from the server, streamed via SignalR

## Protocol Reference

[Minecraft Java Edition Protocol 1.20.1](https://minecraft.wiki/w/Java_Edition_protocol/Packets)