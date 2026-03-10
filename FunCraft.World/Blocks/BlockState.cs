namespace FunCraft.World.Blocks
{
    public readonly record struct BlockState(ushort Id)
    {
        public static readonly BlockState Air = new(0);

        public bool IsAir => Id == 0;

        public override string ToString() => $"BlockState({Id})";
    }
}