namespace FunCraft.Network.Connections
{
    using Protocol.Packets;

    public interface IPacketSender
    {
        ValueTask SendAsync(IPacket packet, CancellationToken ct);
    }
}
