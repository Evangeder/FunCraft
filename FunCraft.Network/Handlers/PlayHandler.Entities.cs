namespace FunCraft.Network.Handlers
{
    using Connections;
    using Entities;
    using FunCraft.Network.Physics;
    using Players;
    using Protocol.IO;
    using Protocol.Packets;
    using Protocol.Packets.Play.Outgoing;
    using Protocol.Types;

    internal sealed partial class PlayHandler
    {
        private static async ValueTask SendSpawnPlayerAsync(
            IPacketSender target, ConnectedPlayer player, CancellationToken ct)
        {
            await target.SendRawAsync(BundleDelimiterPacket.PreFramed, ct);

            await target.SendAsync(new SpawnEntityPacket
            {
                EntityId = player.EntityId,
                EntityUuid = player.Uuid,
                EntityType = EntityTypeIds.Player,
                X = player.Context.X,
                Y = player.Context.Y,
                Z = player.Context.Z,
                Yaw = player.Context.Yaw,
                Pitch = player.Context.Pitch,
                HeadYaw = player.Context.Yaw,
            }, ct);

            await target.SendAsync(new SetHeadRotationPacket
            {
                EntityId = player.EntityId,
                HeadYaw = player.Context.Yaw,
            }, ct);

            await target.SendRawAsync(BundleDelimiterPacket.PreFramed, ct);
        }

        /// <summary>
        /// Broadcasts <paramref name="self"/>'s spawn bundle to every connection in
        /// <paramref name="targets"/> with exactly one serialization pass.
        /// <para>
        /// The previous pattern of <c>await SendSpawnPlayerAsync(other.Sender, self, ct)</c>
        /// inside a loop serialized the same 4 packets once per recipient — O(N) at 1k bots.
        /// This method serializes once (2 frames) and fans out via direct <see cref="IPacketSender.EnqueueRaw"/>
        /// calls — O(1) serialization regardless of population.
        /// </para>
        /// </summary>
        private static void BroadcastPlayerSpawnToAll(
            IReadOnlyList<ConnectedPlayer> targets, ConnectedPlayer self)
        {
            if (targets.Count == 0)
            {
                return;
            }

            var spawnFrame = SerializePacketToFrame(new SpawnEntityPacket
            {
                EntityId = self.EntityId,
                EntityUuid = self.Uuid,
                EntityType = EntityTypeIds.Player,
                X = self.Context.X,
                Y = self.Context.Y,
                Z = self.Context.Z,
                Yaw = self.Context.Yaw,
                Pitch = self.Context.Pitch,
                HeadYaw = self.Context.Yaw,
            });

            var headFrame = SerializePacketToFrame(new SetHeadRotationPacket
            {
                EntityId = self.EntityId,
                HeadYaw = self.Context.Yaw,
            });

            foreach (var target in targets)
            {
                try
                {
                    target.Sender.EnqueueRaw(BundleDelimiterPacket.PreFramed);
                    target.Sender.EnqueueRaw(spawnFrame);
                    target.Sender.EnqueueRaw(headFrame);
                    target.Sender.EnqueueRaw(BundleDelimiterPacket.PreFramed);
                }
                catch
                {
                    // disconnected during broadcast
                }
            }
        }

        // Used only during player join (OnEnterAsync) — one player, one item.
        // Staying async is fine here; this path is not on the hot broadcast path.
        private static async ValueTask SendSpawnItemAsync(
            IPacketSender target, ItemEntity item, CancellationToken ct)
        {
            await target.SendRawAsync(BundleDelimiterPacket.PreFramed, ct);

            await target.SendAsync(new SpawnEntityPacket
            {
                EntityId = item.EntityId,
                EntityUuid = item.Uuid,
                EntityType = EntityTypeIds.Item,
                X = item.X,
                Y = item.Y,
                Z = item.Z,
                VelocityX = item.VelocityX,
                VelocityY = item.VelocityY,
                VelocityZ = item.VelocityZ,
                Yaw = 0,
                Pitch = 0,
                HeadYaw = 0,
            }, ct);

            await target.SendAsync(new SetEntityMetadataPacket
            {
                EntityId = item.EntityId,
                ItemId = item.ItemId,
                Count = item.Count,
            }, ct);

            await target.SendRawAsync(BundleDelimiterPacket.PreFramed, ct);
        }

        /// <summary>
        /// Broadcasts spawn of a new item entity synchronously and schedules the
        /// settle-and-merge background task.
        /// <para>
        /// Callers invoke this as a plain synchronous call — it does not need to be
        /// awaited because <see cref="BroadcastSpawnItem"/> only enqueues pre-serialized
        /// frames into each connection's send queue (no I/O, no blocking).
        /// </para>
        /// </summary>
        private void SpawnAndScheduleMerge(
            ItemEntity dropped, ItemPhysicsBody body, CancellationToken ct)
        {
            BroadcastSpawnItem(dropped);
            _ = TryMergeOnSettleAsync(dropped, body, ct);
        }

        /// <summary>
        /// Awaits <paramref name="body"/>'s settle signal, then looks for a neighbour
        /// item of the same type within 0.5 blocks. If found, merges them into one entity.
        /// Runs as fire-and-forget from <see cref="SpawnAndScheduleMerge"/>.
        /// </summary>
        private async Task TryMergeOnSettleAsync(
            ItemEntity dropped, ItemPhysicsBody body, CancellationToken ct)
        {
            try
            {
                // Wait for physics to settle OR for the item to be picked up / removed
                // (OnRemoved completes the task either way so this never leaks).
                await body.SettledTask.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Item may have been picked up while falling — verify it still exists.
            if (!entities.TryRemove(dropped.EntityId, out _))
            {
                return;
            }

            const double MergeRadius = 0.5;
            var neighbour = entities.FindMergeable(
                dropped.ItemId, dropped.X, dropped.Z, MergeRadius, dropped.EntityId);

            if (neighbour != null && entities.TryRemove(neighbour.EntityId, out _))
            {
                physics.Unregister(neighbour.EntityId);

                await registry.BroadcastRawAsync(new RemoveEntitiesPacket
                {
                    EntityIds = [neighbour.EntityId]
                }, Guid.Empty, ct);

                // Also remove the dropped entity visually — we'll replace with merged.
                await registry.BroadcastRawAsync(new RemoveEntitiesPacket
                {
                    EntityIds = [dropped.EntityId]
                }, Guid.Empty, ct);

                var merged = entities.SpawnItem(
                    dropped.ItemId,
                    neighbour.Count + dropped.Count,
                    neighbour.X, neighbour.Y, neighbour.Z);

                // Merged item is already at rest — no physics needed.
                BroadcastSpawnItem(merged);
            }
            else
            {
                // No neighbour — re-register the item (we removed it above) so pickup
                // detection keeps working, and broadcast removal+respawn at settled pos.
                entities.ReAdd(dropped);

                // Broadcast a teleport to snap the client-side entity to the final
                // physics-settled position (the client may still show it mid-flight).
                await registry.BroadcastRawAsync(new TeleportEntityPacket
                {
                    EntityId = dropped.EntityId,
                    X = dropped.X,
                    Y = dropped.Y,
                    Z = dropped.Z,
                    Yaw = 0,
                    Pitch = 0,
                }, Guid.Empty, ct);
            }
        }

        /// <summary>
        /// Fans out an item spawn bundle to all connected players.
        /// <para>
        /// The <see cref="SpawnEntityPacket"/> and <see cref="SetEntityMetadataPacket"/>
        /// frames are serialized exactly once outside the player loop — 2 allocations
        /// regardless of player count. Each player receives 4 <see cref="IPacketSender.EnqueueRaw"/>
        /// calls (no async overhead, no per-player serialization).
        /// </para>
        /// </summary>
        private void BroadcastSpawnItem(ItemEntity item)
        {
            // Serialize the variable-content frames once for all recipients.
            var spawnFrame = SerializePacketToFrame(new SpawnEntityPacket
            {
                EntityId = item.EntityId,
                EntityUuid = item.Uuid,
                EntityType = EntityTypeIds.Item,
                X = item.X,
                Y = item.Y,
                Z = item.Z,
                VelocityX = item.VelocityX,
                VelocityY = item.VelocityY,
                VelocityZ = item.VelocityZ,
                Yaw = 0,
                Pitch = 0,
                HeadYaw = 0,
            });

            var metaFrame = SerializePacketToFrame(new SetEntityMetadataPacket
            {
                EntityId = item.EntityId,
                ItemId = item.ItemId,
                Count = item.Count,
            });

            foreach (var player in registry.GetAll())
            {
                try
                {
                    player.Sender.EnqueueRaw(BundleDelimiterPacket.PreFramed);
                    player.Sender.EnqueueRaw(spawnFrame);
                    player.Sender.EnqueueRaw(metaFrame);
                    player.Sender.EnqueueRaw(BundleDelimiterPacket.PreFramed);
                }
                catch
                {
                    // disconnected mid-broadcast
                }
            }
        }

        /// <summary>
        /// Serializes a packet into a complete wire frame (length prefix + packet ID + payload).
        /// The result is suitable for <see cref="IPacketSender.EnqueueRaw"/>.
        /// </summary>
        private static ReadOnlyMemory<byte> SerializePacketToFrame(IPacket packet)
        {
            var payloadLength = packet.GetLength();
            var idLength = VarInt.GetSize(packet.PacketId);
            var totalLength = payloadLength + idLength;
            var frameLength = VarInt.GetSize(totalLength) + totalLength;

            var buf = new byte[frameLength];
            var writer = new PacketWriter(buf);
            writer.WriteVarInt(totalLength);
            writer.WriteVarInt(packet.PacketId);
            packet.Write(buf.AsSpan(writer.BytesWritten), out _);
            return buf.AsMemory();
        }
    }
}