namespace FunCraft.Network.Connections
{
    using Protocol.Packets;

    internal interface IPacketSender
    {
        ValueTask SendAsync(IPacket packet, CancellationToken ct);
    }
}
