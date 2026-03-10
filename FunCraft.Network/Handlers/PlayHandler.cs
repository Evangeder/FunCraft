using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets;
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;

    /// <summary>
    /// Handles the Play connection state.
    /// <br/>TODO: Expand properties to allow sending personalized data
    /// </summary>
    internal class PlayHandler : AsyncHandlerBase
    {
        private const int SpawnTeleportId = 1;

        /// <summary>
        /// Radius — (2r+1)^2 = 25 chunks total, for now
        /// </summary>
        private const int ViewDistance = 2;

        private const double SpawnX = 0.5;
        private const double SpawnY = 65.0;
        private const double SpawnZ = 0.5;

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new LoginPlayPacket
            {
                EntityId = 1,
                DimensionType = "minecraft:overworld",
                DimensionName = "minecraft:overworld"
            }, ct);

            await Sender.SendAsync(new SynchronizePlayerPositionPacket
            {
                TeleportId = SpawnTeleportId,
                X = SpawnX,
                Y = SpawnY,
                Z = SpawnZ,
                VelocityX = 0,
                VelocityY = 0,
                VelocityZ = 0,
                Yaw = 0f,
                Pitch = 0f,
                Flags = 0
            }, ct);

            await Sender.SendAsync(new SetCenterChunkPacket { ChunkX = 0, ChunkZ = 0 }, ct);
            await Sender.SendAsync(new SetChunkCacheRadiusPacket { ViewDistance = ViewDistance }, ct);
        }

        internal override async ValueTask<ConnectionState> HandleAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            switch (packetId)
            {
                case ConfirmTeleportationPacket.Id:
                    await SendWorldAsync(ct);
                    break;

                default:
                    Console.WriteLine($"PlayHandler: unhandled packet 0x{packetId:X2}");
                    break;
            }

            return ConnectionState.Play;
        }

        private async ValueTask SendWorldAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new GameEventPacket { Event = 13, Value = 0f }, ct);

            for (var chunkX = -ViewDistance; chunkX <= ViewDistance; chunkX++)
            {
                for (var chunkZ = -ViewDistance; chunkZ <= ViewDistance; chunkZ++)
                {
                    await Sender.SendAsync(new ChunkDataPacket {ChunkX = chunkX, ChunkZ = chunkZ}, ct);
                }
            }
        }
    }
}
