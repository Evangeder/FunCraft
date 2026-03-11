namespace FunCraft.Network.Commands
{
    using Connections;
    using Players;
    using Protocol.Packets.Play.Outgoing;

    /// <summary>/test — puts 64× dirt in hotbar slot 0 and tracks it server-side.</summary>
    public sealed class RespawnCommand(PlayerContext ctx) : ICommand
    {
        public string Name => "respawn";
        public string Description => "Respawns the player.";

        public async Task ExecuteAsync(
            string[] args, Func<string, Task> respond, IPacketSender sender, CancellationToken ct)
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

            await respond("§aRespawned.");
        }
    }
}