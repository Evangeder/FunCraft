using FunCraft.Protocol.Registry;

namespace FunCraft.Network.Commands
{
    using Connections;
    using Data.Inventory;
    using Players;
    using Protocol.Packets.Play.Outgoing;

    /// <summary>
    /// /give &lt;item&gt; — puts 64x of the named item in hotbar slot 0.
    /// </summary>
    public sealed class GiveCommand(PlayerContext ctx) : ICommand
    {
        public ReadOnlySpan<byte> Name => "give"u8;
        public ReadOnlySpan<byte> Description => "Gives you a stack of the specified item."u8;

        private const int HotbarSlot0 = 36;
        private const int StackSize = 64;

        private static readonly byte[] MsgNoArgs = "§cUsage: /give <item>"u8.ToArray();
        private static readonly byte[] MsgInvalidItem = "§cUnknown item: "u8.ToArray();
        private static readonly byte[] MsgGivenPrefix = "§aGiven 64x "u8.ToArray();

        // Namespace prefix applied when the argument contains no ':'.
        private static readonly byte[] DefaultNamespace = "minecraft:"u8.ToArray();

        public async Task ExecuteAsync(ReadOnlyMemory<byte> args, Func<ReadOnlyMemory<byte>, Task> respond,
            IPacketSender sender, CancellationToken ct)
        {
            var argSpan = args.Span;
            var spaceIdx = argSpan.IndexOf((byte)' ');
            if (spaceIdx >= 0) argSpan = argSpan[..spaceIdx];

            if (argSpan.IsEmpty)
            {
                await respond(MsgNoArgs);
                return;
            }

            byte[] fullName;
            if (argSpan.IndexOf((byte)':') >= 0)
            {
                fullName = argSpan.ToArray();
            }
            else
            {
                fullName = new byte[DefaultNamespace.Length + argSpan.Length];
                DefaultNamespace.CopyTo(fullName.AsSpan());
                argSpan.CopyTo(fullName.AsSpan(DefaultNamespace.Length));
            }

            var itemId = RegistryLookup.GetItemId(fullName);
            if (itemId < 0)
            {
                await respond(Concat(MsgInvalidItem, fullName));
                return;
            }

            ctx.Inventory[HotbarSlot0] = new InventorySlot(itemId, StackSize);

            await sender.SendAsync(new SetContainerSlotPacket
            {
                WindowId = 0,
                StateId = 0,
                Slot = HotbarSlot0,
                ItemId = itemId,
                Count = StackSize,
            }, ct);

            await respond(Concat(MsgGivenPrefix, fullName));
        }

        private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            var result = new byte[a.Length + b.Length];
            a.CopyTo(result);
            b.CopyTo(result.AsSpan(a.Length));
            return result;
        }
    }
}