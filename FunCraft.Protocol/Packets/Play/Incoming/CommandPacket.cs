using System.Buffers;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    using Types;

    /// <summary>
    /// 0x06 — Chat Command (C→S)<br/>
    /// Sent when the player runs a command. The Command field is the full command
    /// text <b>without</b> the leading slash.
    /// </summary>
    public sealed class ChatCommandPacket : IIncomingPacket
    {
        public const int Id = 0x06;

        public string Command { get; private set; } = string.Empty;

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!McString.TryRead(ref reader, out var command)) return false;
            Command = command;
            return true;
        }
    }
}