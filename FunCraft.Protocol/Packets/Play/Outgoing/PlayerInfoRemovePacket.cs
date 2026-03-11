namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x43 — Player Info Remove (S→C)<br/>
    /// Removes one or more players from the TAB list.
    /// </summary>
    public sealed class PlayerInfoRemovePacket : IPacket
    {
        public const int Id = 0x43;
        public int PacketId => Id;

        public required IReadOnlyList<Guid> Uuids { get; init; }

        private const int GuidSize = 16;

        public int GetLength() =>
            VarInt.GetSize(Uuids.Count) + Uuids.Count * GuidSize;

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(Uuids.Count);
            foreach (var uuid in Uuids)
                writer.WriteGuid(uuid);
            bytesWritten = writer.BytesWritten;
        }
    }
}