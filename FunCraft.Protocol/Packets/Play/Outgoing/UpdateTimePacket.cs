namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;

    /// <summary>
    /// 0x6F — Update Time (S→C)
    /// <br/>Sets the world age and time of day.
    /// <br/>Time of day: 0 = sunrise, 6000 = noon, 12000 = sunset, 18000 = midnight.
    /// <br/>Sending 6000 gives full daylight with no skylight arrays needed.
    /// </summary>
    public class UpdateTimePacket : IPacket
    {
        public const int Id = 0x6F;
        public int PacketId => Id;

        public required long WorldAge { get; init; }
        public required long TimeOfDay { get; init; }

        /// <summary>
        /// If true the client automatically advances time each tick.
        /// <br/>Set false for a frozen time of day.
        /// </summary>
        public required bool TimeOfDayIncreasing { get; init; }

        public int GetLength() => sizeof(long) + sizeof(long) + sizeof(bool);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteLong(WorldAge);
            writer.WriteLong(TimeOfDay);
            writer.WriteBoolean(TimeOfDayIncreasing);
            bytesWritten = writer.BytesWritten;
        }
    }
}