using System.Buffers;
using System.Buffers.Binary;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    /// <summary>
    /// 0x0A — Chunk Batch Received (C→S)
    /// <br/>Sent by the client after receiving <see cref="Outgoing.ChunkBatchFinishedPacket"/>.
    /// Reports the client's desired chunk throughput. The vanilla server uses this to
    /// throttle how many chunks it sends per tick.
    /// <br/>We log the value but don't throttle yet.
    /// </summary>
    public class ChunkBatchReceivedPacket : IIncomingPacket
    {
        public const int Id = 0x0A;

        /// <summary>
        /// Desired chunks per tick as reported by the client.
        /// </summary>
        public float DesiredChunksPerTick { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (reader.Remaining < sizeof(float))
                return false;

            Span<byte> buf = stackalloc byte[sizeof(float)];
            reader.TryCopyTo(buf);
            reader.Advance(sizeof(float));

            DesiredChunksPerTick = BinaryPrimitives.ReadSingleBigEndian(buf);
            return true;
        }
    }
}