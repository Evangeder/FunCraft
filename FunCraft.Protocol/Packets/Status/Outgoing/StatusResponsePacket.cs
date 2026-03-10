namespace FunCraft.Protocol.Packets.Status.Outgoing
{
    using Types;

    public class StatusResponsePacket : IPacket
    {
        public const int Id = 0x00;
        public int PacketId => Id;

        public int GetLength() => McString.GetSize(JsonResponse);

        public required string JsonResponse { private get; init; }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = McString.Write(destination, JsonResponse);
        }
    }
}
