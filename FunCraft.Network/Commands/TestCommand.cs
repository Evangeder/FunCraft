using FunCraft.Protocol.Registry;

namespace FunCraft.Network.Commands
{
    using Connections;
    using Data.Inventory;
    using Players;
    using Protocol.Packets.Play.Outgoing;

    /// <summary>
    /// /test — puts 64× oak_log in hotbar slot 0 and tracks it server-side.
    /// </summary>
    public sealed class TestCommand(PlayerContext ctx) : ICommand
    {
        public ReadOnlySpan<byte> Name => "test"u8;
        public ReadOnlySpan<byte> Description => "Gives you 64x minecraft:oak_log."u8;

        private const int HotbarSlot0 = 36;
        private const int StackSize = 64;

        private static readonly byte[] MsgGiven =
            "§aGiven 64x minecraft:oak_log."u8.ToArray();

        public async Task ExecuteAsync(ReadOnlyMemory<byte> args, Func<ReadOnlyMemory<byte>, Task> respond,
            IPacketSender sender, CancellationToken ct)
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

            await respond(MsgGiven);
        }
    }
}