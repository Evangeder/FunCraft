namespace FunCraft.World.Blocks
{
    public readonly record struct BlockState(ushort Id)
    {
        public static readonly BlockState Air = new(0);

        public bool IsAir => Id == 0;

        public override string ToString() => $"BlockState({Id})";

        public static implicit operator ushort(BlockState block) => block.Id;
        public static implicit operator BlockState(ushort id) => new(id);
    }
}