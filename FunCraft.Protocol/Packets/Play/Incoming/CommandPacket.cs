using System.Buffers;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    using Types;

    /// <summary>
    /// 0x06 — Chat Command (C→S)<br/>
    /// The <see cref="Command"/> field is the full command text
    /// <b>without</b> the leading slash, as raw UTF-8 bytes.
    /// </summary>
    public sealed class ChatCommandPacket : IIncomingPacket
    {
        public const int Id = 0x06;

        public ReadOnlyMemory<byte> Command { get; private set; } = ReadOnlyMemory<byte>.Empty;

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!McString.TryReadRaw(ref reader, out var command)) return false;
            Command = command;
            return true;
        }
    }
}