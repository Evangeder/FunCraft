using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets;
    using Protocol.Packets.Login;
    using Connections;

    internal class LoginHandler
    {
        internal required IPacketSender Sender { get; init; }

        internal async ValueTask<ConnectionState> HandleAsync(int packetId, ReadOnlySequence<byte> payload,
            CancellationToken ct)
        {
            switch (packetId)
            {
                case LoginStartPacket.Id:
                    var login = new LoginStartPacket();
                    var reader = new SequenceReader<byte>(payload);

                    login.TryRead(ref reader);

                    // TODO: decide if success or disconnect (because for example player is banned)
                    await Sender.SendAsync(new LoginSuccessPacket
                    {
                        PlayerName = login.PlayerName,
                        PlayerGuid = login.PlayerGuid,
                        PropertyCount = 0
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