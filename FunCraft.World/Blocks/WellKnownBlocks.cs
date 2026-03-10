namespace FunCraft.World.Blocks
{
    /// <summary>
    /// Verified global palette IDs for 1.21.1 (protocol 773).
    /// <br/>Each ID was confirmed against the vanilla data generator output.
    /// <br/>Add new entries only after verification — wrong IDs corrupt the world silently.
    /// </summary>
    public static class WellKnownBlocks
    {
        /// <summary>air — ID 0, always.</summary>
        public static readonly BlockState Air = BlockState.Air;

        /// <summary>stone — single block state, ID 1.</summary>
        public static readonly BlockState Stone = new(1);

        /// <summary>grass_block[snowy=false] — ID 9.</summary>
        public static readonly BlockState GrassBlock = new(9);

        /// <summary>dirt — ID 10.</summary>
        public static readonly BlockState Dirt = new(10);

        /// <summary>bedrock — ID 33.</summary>
        public static readonly BlockState Bedrock = new(33);
    }
}