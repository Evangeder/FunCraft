using FunCraft.Network.Entities;
using FunCraft.Network.Physics;
using FunCraft.Protocol.Registry;

namespace FunCraft.Network.Commands
{
    using Connections;
    using Data.Inventory;
    using Players;
    using Protocol.Packets.Play.Outgoing;

    /// <summary>
    /// /give &lt;item&gt; — spawns a max-stack of the named item at the player's feet
    /// with zero velocity and no pickup cooldown, so it's collected instantly on
    /// the next movement tick.
    /// </summary>
    public sealed class GiveCommand(
        PlayerContext ctx,
        IEntityManager entities,
        IPhysicsEngine physics) : ICommand
    {
        public ReadOnlySpan<byte> Name => "give"u8;
        public ReadOnlySpan<byte> Description => "Gives you a stack of the specified item."u8;

        private static readonly byte[] MsgNoArgs = "§cUsage: /give <item>"u8.ToArray();
        private static readonly byte[] MsgInvalidItem = "§cUnknown item: "u8.ToArray();
        private static readonly byte[] MsgGivenPrefix = "§aGiven "u8.ToArray();
        private static readonly byte[] MsgGivenSuffix = "x "u8.ToArray();
        private static readonly byte[] DefaultNamespace = "minecraft:"u8.ToArray();

        public async Task ExecuteAsync(ReadOnlyMemory<byte> args,
            Func<ReadOnlyMemory<byte>, Task> respond,
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

            var stackSize = ItemStackTable.GetMaxStack(fullName);

            // Spawn at the player's feet. Zero velocity, instantPickup=true so
            // there's no 500 ms cooldown — the next movement packet picks it up.
            var item = entities.SpawnItem(itemId, stackSize,
                ctx.X, ctx.Y, ctx.Z,
                instantPickup: true);

            // No physics — item is already on the ground at the player's feet.
            item.MarkSettled();

            // Broadcast the spawn to all connected clients so they see the entity.
            // We don't have direct access to the registry here, so we send directly
            // to the caller and let pickup handling clean up the entity.
            await sender.SendRawAsync(BundleDelimiterPacket.PreFramed, ct);
            await sender.SendAsync(new SpawnEntityPacket
            {
                EntityId = item.EntityId,
                EntityUuid = item.Uuid,
                EntityType = EntityTypeIds.Item,
                X = item.X,
                Y = item.Y,
                Z = item.Z,
                VelocityX = 0,
                VelocityY = 0,
                VelocityZ = 0,
                Yaw = 0,
                Pitch = 0,
                HeadYaw = 0,
            }, ct);
            await sender.SendAsync(new SetEntityMetadataPacket
            {
                EntityId = item.EntityId,
                ItemId = itemId,
                Count = stackSize,
            }, ct);
            await sender.SendRawAsync(BundleDelimiterPacket.PreFramed, ct);

            Span<byte> countBytes = stackalloc byte[3];
            var countLen = WriteAsciiInt(countBytes, stackSize);
            await respond(Concat4(MsgGivenPrefix, countBytes[..countLen], MsgGivenSuffix, fullName));
        }

        private static int WriteAsciiInt(Span<byte> buf, int value)
        {
            if (value == 0) { buf[0] = (byte)'0'; return 1; }
            var len = 0;
            while (value > 0) { buf[len++] = (byte)('0' + value % 10); value /= 10; }
            buf[..len].Reverse();
            return len;
        }

        private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            var result = new byte[a.Length + b.Length];
            a.CopyTo(result);
            b.CopyTo(result.AsSpan(a.Length));
            return result;
        }

        private static byte[] Concat4(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b,
            ReadOnlySpan<byte> c, ReadOnlySpan<byte> d)
        {
            var result = new byte[a.Length + b.Length + c.Length + d.Length];
            a.CopyTo(result);
            b.CopyTo(result.AsSpan(a.Length));
            c.CopyTo(result.AsSpan(a.Length + b.Length));
            d.CopyTo(result.AsSpan(a.Length + b.Length + c.Length));
            return result;
        }
    }
}