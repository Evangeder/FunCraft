using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Data.Sessions;
    using FunCraft.Network.Players;
    using Protocol.Packets;
    using Protocol.Packets.Login.Incoming;
    using Protocol.Packets.Login.Outgoing;

    internal class LoginHandler(PlayerContext ctx, ISessionStore sessions) : AsyncHandlerBase
    {
        internal override async ValueTask<ConnectionState> HandleAsync(int packetId, ReadOnlySequence<byte> payload,
            CancellationToken ct)
        {
            switch (packetId)
            {
                case LoginStartPacket.Id:
                    var reader = new SequenceReader<byte>(payload);
                    var login = new LoginStartPacket();
                    login.TryRead(ref reader);

                    // Populate the shared context so PlayHandler can use it.
                    ctx.Username = login.PlayerName;
                    ctx.Uuid = login.PlayerGuid;

                    await Sender.SendAsync(new LoginSuccessPacket
                    {
                        PlayerName = login.PlayerName,
                        PlayerGuid = login.PlayerGuid,
                        PropertyCount = 0
                    }, ct);

                    // Write the live session to Redis.
                    await sessions.SetAsync(new PlayerSession
                    {
                        Uuid = ctx.Uuid,
                        Username = ctx.Username,
                        IpAddress = ctx.IpAddress,
                        ConnectedAt = DateTimeOffset.UtcNow,
                    }, ct);

                    return ConnectionState.Login;
                    
                case LoginAcknowledgePacket.Id:
                    return ConnectionState.Configuration;

                default:
                    return ConnectionState.Login;
            }
        }
    }
}