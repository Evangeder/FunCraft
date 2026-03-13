using FunCraft.Protocol.Registry;

namespace FunCraft.Network.Commands
{
    using Connections;
    using Data.Inventory;
    using Players;
    using Protocol.Packets.Play.Outgoing;

    /// <summary>
    /// /test — puts 64× dirt in hotbar slot 0 and tracks it server-side.
    /// </summary>
    public sealed class TestCommand(PlayerContext ctx) : ICommand
    {
        public string Name => "test";
        public string Description => "Gives you 64x minecraft:oak_log (hotbar slot 1).";

        private const int DirtItemId = 28;
        private const int HotbarSlot0 = 36; // inventory wire index for hotbar slot 0
        private const int StackSize = 64;

        public async Task ExecuteAsync(
            string[] args, Func<string, Task> respond, IPacketSender sender, CancellationToken ct)
        {
            var itemId = RegistryLookup.GetItemId("minecraft:oak_log"u8);
            ctx.Inventory[HotbarSlot0] = new InventorySlot(itemId, StackSize);

            await sender.SendAsync(new SetContainerSlotPacket
            {
                WindowId = 0,
                StateId = 0,
                Slot = HotbarSlot0,
                ItemId = itemId,
                Count = StackSize,
            }, ct);

            await respond("§aGiven 64x minecraft:oak_log — check hotbar slot 1.");
        }
    }
}