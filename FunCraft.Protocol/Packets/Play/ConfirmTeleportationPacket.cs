namespace FunCraft.Protocol.Packets.Play
{
    using System.Buffers;
    using Types;

    /// <summary>
    /// 0x00 — Confirm Teleportation (C→S, serverbound)
    /// </summary>
    public class ConfirmTeleportationPacket : IIncomingPacket
    {
        public const int Id = 0x00;

        public int TeleportId { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!VarInt.TryRead(ref reader, out var teleportId))
                return false;

            TeleportId = teleportId;
            return true;
        }
    }
}