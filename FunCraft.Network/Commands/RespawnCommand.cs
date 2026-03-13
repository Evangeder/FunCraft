namespace FunCraft.Network.Commands
{
    using Connections;
    using Players;
    using Protocol.Packets.Play.Outgoing;

    /// <summary>/respawn — teleports the player back to spawn (0, 128, 0).</summary>
    public sealed class RespawnCommand(PlayerContext ctx) : ICommand
    {
        public ReadOnlySpan<byte> Name => "respawn"u8;
        public ReadOnlySpan<byte> Description => "Respawns the player at spawn."u8;

        private static readonly byte[] MsgRespawned = "§aRespawned."u8.ToArray();

        public async Task ExecuteAsync(
            ReadOnlyMemory<byte> args,
            Func<ReadOnlyMemory<byte>, Task> respond,
            IPacketSender sender,
            CancellationToken ct)
        {
            ctx.X = 0;
            ctx.Y = 128;
            ctx.Z = 0;

            await sender.SendAsync(new SynchronizePlayerPositionPacket
            {
                TeleportId = 1,
                X = ctx.X,
                Y = ctx.Y,
                Z = ctx.Z,
                VelocityX = 0,
                VelocityY = 0,
                VelocityZ = 0,
                Yaw = ctx.Yaw,
                Pitch = ctx.Pitch,
                Flags = 0
            }, ct);

            await respond(MsgRespawned);
        }
    }
}