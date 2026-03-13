using System.Buffers;
using System.Text;

namespace FunCraft.Network.Handlers
{
    using Data.Sessions;
    using Players;
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

                    // Populate the shared context — both fields stay as bytes.
                    ctx.Username = login.PlayerName;
                    ctx.Uuid = login.PlayerGuid;

                    await Sender.SendAsync(new LoginSuccessPacket
                    {
                        PlayerName = login.PlayerName,
                        PlayerGuid = login.PlayerGuid,
                        PropertyCount = 0
                    }, ct);

                    // Redis expects a string — decode exactly once at this persistence boundary.
                    await sessions.SetAsync(new PlayerSession
                    {
                        Uuid = ctx.Uuid,
                        Username = Encoding.UTF8.GetString(ctx.Username.Span),
                        IpAddress = ctx.IpAddress?.ToString() ?? "unknown",
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