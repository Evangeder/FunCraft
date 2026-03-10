using System.Buffers;

namespace FunCraft.Protocol.Types
{
    public interface IDataType<TSelf, TValue>
        where TSelf : IDataType<TSelf, TValue>
    {
        static abstract int Write(Span<byte> destination, TValue value);
        static abstract bool TryRead(ref SequenceReader<byte> reader, out TValue value);
        static abstract int GetSize(TValue value);
    }
}
