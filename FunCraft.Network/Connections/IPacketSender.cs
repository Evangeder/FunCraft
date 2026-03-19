namespace FunCraft.Network.Connections
{
    using Protocol.Packets;

    public interface IPacketSender
    {
        ValueTask SendAsync(IPacket packet, CancellationToken ct);
        ValueTask SendRawAsync(ReadOnlyMemory<byte> framed, CancellationToken ct);
        void EnqueueRaw(ReadOnlyMemory<byte> framed);
        void EnqueueMovementFrame(int movementKey, byte[] frame);
    }
}