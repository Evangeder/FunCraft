This document describes the protocol layer as it exists in the current codebase.

It is **not** a full Minecraft protocol specification. It is the implemented subset used by FunCraft right now.

## Protocol layer responsibilities

`FunCraft.Protocol` contains:

- packet contracts and packet classes
- primitive data types (`VarInt`, `VarLong`, `McString`)
- `PacketReader` and `PacketWriter`
- status response models
- registry resource loading

## Packet framing model

Packets are written in the usual framed layout used by the Java protocol:

```text
[VarInt total_length][VarInt packet_id][payload bytes]
```

`ClientConnection.SendAsync` constructs this frame manually before writing to the socket.

## Low-level types
### `VarInt`
Used for packet lengths, packet IDs, counts, and many protocol fields.

### `VarLong`
Available for protocol fields that need longer variable-length integers.

### `McString`
Handles Minecraft string encoding on top of UTF-8 with a VarInt length prefix.

### `PacketReader` / `PacketWriter`
These are the main helpers used by packet classes and handlers for big-endian integer and protocol-type access.

## Implemented packet inventory
### Handshaking

|Packet|Direction|ID|Implemented|
|---|---|--:|---|
|`HandshakePacket`|client -> server|`0x00`|Yes|

### Status

|Packet|Direction|ID|Implemented|
|---|---|--:|---|
|`StatusRequestPacket`|client -> server|`0x00`|Yes|
|`PingRequestPacket`|client -> server|`0x01`|Yes|
|`StatusResponsePacket`|server -> client|`0x00`|Yes|
|`PongResponsePacket`|server -> client|`0x01`|Yes|
### Login

|Packet|Direction|ID|Implemented|
|---|---|--:|---|
|`LoginStartPacket`|client -> server|`0x00`|Yes|
|`LoginAcknowledgePacket`|client -> server|`0x03`|Yes|
|`LoginSuccessPacket`|server -> client|`0x02`|Yes|
|`LoginDisconnect`|server -> client|`0x00`|Class exists|
Not implemented in practice:
- encryption request / response flow
- login plugin requests
- compression negotiation
- online-mode profile resolution

### Configuration

|Packet|Direction|ID|Implemented|
|---|---|--:|---|
|`ServerboundKnownPacksPacket`|client -> server|`0x07`|Yes|
|`AcknowledgeConfigurationPacket`|client -> server|`0x03`|Yes|
|`KnownPacksPacket`|server -> client|`0x0E`|Yes|
|`RegistryDataPacket`|server -> client|`0x07`|Yes|
|`FinishConfigurationPacket`|server -> client|`0x03`|Yes|
### Play

|Packet|Direction|ID|Implemented|
|---|---|--:|---|
|`ConfirmTeleportationPacket`|client -> server|`0x00`|Yes|
|`ChunkBatchReceivedPacket`|client -> server|`0x0A`|Yes|
|`ServerboundKeepAlivePacket`|client -> server|`0x1B`|Yes|
|`ChunkBatchFinishedPacket`|server -> client|`0x0B`|Yes|
|`ChunkBatchStartPacket`|server -> client|`0x0C`|Yes|
|`GameEventPacket`|server -> client|`0x26`|Yes|
|`ChunkDataPacket`|server -> client|`0x2C`|Yes|
|`LoginPlayPacket`|server -> client|`0x30`|Yes|
|`SynchronizePlayerPositionPacket`|server -> client|`0x46`|Yes|
|`SetCenterChunkPacket`|server -> client|`0x5C`|Yes|
|`SetChunkCacheRadiusPacket`|server -> client|`0x5D`|Yes|
|`UpdateTimePacket`|server -> client|`0x6F`|Yes|
|`ClientboundKeepAlivePacket`|server -> client|`0x2B`|Yes|
## Packet handling philosophy
The project currently mixes two styles of packet support:
- **fully parsed and acted on** packets, such as handshake, status ping, teleport confirmation, and keep-alive
- **known but intentionally ignored** high-frequency packets in Play state

That is reasonable for an early server bootstrap, because the goal is to get the client into the world before expanding interaction logic.

## Known hardcoded protocol values
A few values are currently embedded directly in packet construction:
- server version string `1.21.10`
- protocol number `773`
- known pack version `1.21.10`
- view distance and simulation distance values in play bootstrap
- dimension names and IDs in `LoginPlayPacket`
- sea level, game mode, and other login/play metadata

As the server matures, these should move into a version-aware configuration or protocol profile layer.