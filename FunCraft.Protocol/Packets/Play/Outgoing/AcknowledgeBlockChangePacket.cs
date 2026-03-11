namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using Types;
    using IO;

    /// <summary>
    /// 0x04 — Acknowledge Block Change (S→C)
    /// <br/>Must be sent after every serverbound Player Action / Use Item On
    /// to confirm the sequence ID. Without it the client desynchronises.
    /// </summary>
    public sealed class AcknowledgeBlockChangePacket : IPacket
    {
        public const int Id = 0x04;
        public int PacketId => Id;

        public required int SequenceId { get; init; }

        public int GetLength() => VarInt.GetSize(SequenceId);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(SequenceId);
            bytesWritten = writer.BytesWritten;
        }
    }
}