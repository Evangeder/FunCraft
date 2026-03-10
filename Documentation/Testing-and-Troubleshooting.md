This document covers the current test surface and the most likely failure points.

## Test project scope

`FunCraft.Tests` is currently focused mostly on registry and NBT correctness.

The existing tests inspect things like:

- whether NBT-backed registries begin with a compound tag header
- whether registries expected to include NBT actually contain it
- whether `RegistryDataPacket.GetLength()` matches the actual write length
- whether framed packet bytes decode cleanly
- dumping or inspecting registry field structure for debugging

This is useful because registry synchronization is one of the trickier parts of the current implementation.

## Typical commands

```bash
dotnet build FunCraft.slnx
dotnet test FunCraft.Tests/FunCraft.Tests.csproj
```

## Important documentation caveat
For this documentation refresh, those commands were **not run in the doc-generation environment**, because the container used for the refresh did not have the .NET SDK installed.

So while the docs reflect the current source tree, they are not backed by a fresh successful build log from this environment.

# Common failure points

## 1. Client and server version mismatch
Symptoms:
- disconnect during login or configuration
- malformed packet parsing
- strange client-side behavior after entering the world

Check:
- status version string
- known pack version string
- registry source version
- packet IDs and payload assumptions

## 2. Stale or missing `registries.bin`
Symptoms:
- failure during Configuration state
- client disconnect after known packs / registry exchange
- mismatched registry interpretation on the client side

Check:
- `FunCraft.Protocol/Resources/registries.bin` exists
- it matches the target Minecraft version
- `Resources.resx` still embeds it correctly
- `RegistryLoader.Load()` succeeds before startup

## 3. Block ID drift
Symptoms:
- wrong blocks appear in the world
- chunks load but terrain looks corrupted or nonsensical

Check:
- `WellKnownBlocks` IDs still match the targeted version
- registry pipeline and version strings were updated together

## 4. World layer confusion
Symptoms:
- spawn height feels wrong
- documented terrain shape does not match reality

Check:
- the constants in `FlatWorldGenerator`
- not the older XML summary comment above it

Right now, the code generates ground up to Y=0 even though the comment still describes an older shallow layout.

## 5. Partial Play-state support
Symptoms:
- client enters the world but many interactions do nothing
- movement and gameplay systems feel incomplete

Cause:

The current Play handler intentionally implements only a narrow bootstrap subset. That is expected behavior, not a mysterious cosmic punishment.

## Recommended next tests to add
A useful next wave of tests would include:
- connection-state transition tests
- packet roundtrip tests for handshake/login/play packets
- chunk serializer golden-file tests
- validation of world generator output at representative coordinates
- regression tests for version-specific constants

## Suggested debugging approach
When debugging connection issues, inspect them in this order:
1. protocol/version agreement
2. registry synchronization
3. login/configuration state transitions
4. chunk serialization output
5. clientbound play packet ordering

That order usually saves time because it starts with the brittle parts first.