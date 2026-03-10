using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Connections;
    using Protocol.Packets;
    using Protocol.Packets.Play;

    /// <summary>
    /// Handles the Play connection state.
    /// </summary>
    internal class PlayHandler
    {
        private const int SpawnTeleportId = 1;
        private const int ViewDistance = 2; // radius — (2r+1)^2 = 25 chunks total

        private const double SpawnX = 0.5;
        private const double SpawnY = 65.0;
        private const double SpawnZ = 0.5;

        private bool _spawnAcked = false;

        internal required IPacketSender Sender { get; init; }

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            Console.WriteLine("PlayHandler::OnEnterAsync()");

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

        internal async ValueTask<ConnectionState> HandleAsync(int packetId, CancellationToken ct)
        {
            switch (packetId)
            {
                case ConfirmTeleportationPacket.Id when !_spawnAcked:
                    _spawnAcked = true;
                    Console.WriteLine("PlayHandler: teleport confirmed — sending world");
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

            var r = ViewDistance;
            for (var cx = -r; cx <= r; cx++)
            {
                for (var cz = -r; cz <= r; cz++)
                {
                    await Sender.SendAsync(new ChunkDataPacket {ChunkX = cx, ChunkZ = cz}, ct);
                }
            }

            Console.WriteLine($"PlayHandler: sent {(2 * r + 1) * (2 * r + 1)} chunks");
        }
    }
}
