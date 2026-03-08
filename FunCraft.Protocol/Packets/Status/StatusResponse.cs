namespace FunCraft.Protocol.Packets.Status
{
    using Types;

    public class StatusResponse : IPacket
    {
        public int PacketId => 0x00;
        public int GetLength() => McString.GetSize(JsonResponse);


        public required string JsonResponse { private get; init; }


        public void Write(Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = McString.Write(destination, JsonResponse);
        }
    }
}
