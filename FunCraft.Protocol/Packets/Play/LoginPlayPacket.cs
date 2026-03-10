namespace FunCraft.Protocol.Packets.Play
{
    using IO;
    using Types;

    /// <summary>
    /// Temporarily without settings
    /// <br/>TODO settings, make this dynamic
    /// </summary>
    public class LoginPlayPacket : IPacket
    {
        public const int Id = 0x30;
        public int PacketId => Id;

        public required int EntityId { get; init; }
        public required string DimensionType { get; init; }
        public required string DimensionName { get; init; }

        public int GetLength()
        {
            var size = 0;
            size += sizeof(int);                                // EntityId
            size += 1;                                          // IsHardcore
            size += VarInt.GetSize(1);                          // dimension count
            size += McString.GetSize(DimensionType);            // dimension name in list
            size += VarInt.GetSize(0);                          // MaxPlayers
            size += VarInt.GetSize(10);                         // ViewDistance
            size += VarInt.GetSize(10);                         // SimulationDistance
            size += 1;                                          // ReducedDebugInfo
            size += 1;                                          // EnableRespawnScreen
            size += 1;                                          // DoLimitedCrafting
            size += VarInt.GetSize(0);                          // DimensionType (registry id)
            size += McString.GetSize(DimensionName);            // DimensionName
            size += sizeof(long);                               // HashedSeed
            size += 1;                                          // GameMode
            size += 1;                                          // PreviousGameMode
            size += 1;                                          // IsDebug
            size += 1;                                          // IsFlat
            size += 1;                                          // HasDeathLocation (false)
            size += VarInt.GetSize(0);                          // PortalCooldown
            size += 1;                                          // EnforceSecureChat
            size += VarInt.GetSize(63);                         // SeaLevel
            return size;
        }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteInt(EntityId);
            writer.WriteBoolean(false);                         // IsHardcore
            writer.WriteVarInt(1);                              // 1 dimension
            writer.WriteString(DimensionType);                  // dimension list entry
            writer.WriteVarInt(0);                              // MaxPlayers
            writer.WriteVarInt(10);                             // ViewDistance
            writer.WriteVarInt(10);                             // SimulationDistance
            writer.WriteBoolean(false);                         // ReducedDebugInfo
            writer.WriteBoolean(true);                          // EnableRespawnScreen
            writer.WriteBoolean(false);                         // DoLimitedCrafting
            writer.WriteVarInt(0);                              // DimensionType id (index into registry)
            writer.WriteString(DimensionName);                  // DimensionName
            writer.WriteLong(0);                                // HashedSeed
            writer.WriteByte(0);                                // GameMode (survival)
            writer.WriteByte(255);                              // PreviousGameMode (-1 = none)
            writer.WriteBoolean(false);                         // IsDebug
            writer.WriteBoolean(false);                         // IsFlat
            writer.WriteBoolean(false);                         // HasDeathLocation
            writer.WriteVarInt(0);                              // PortalCooldown
            writer.WriteBoolean(false);                         // EnforceSecureChat
            writer.WriteVarInt(63);                             // SeaLevel
            bytesWritten = writer.BytesWritten;
        }
    }
}
