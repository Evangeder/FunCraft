using FunCraft.Protocol.Registry;
using Microsoft.Extensions.Logging;
using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Commands;
    using Data.Inventory;
    using Data.Players;
    using Entities;
    using FunCraft.Network.Connections;
    using FunCraft.World;
    using Physics;
    using Players;
    using Protocol.Packets;
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;
    using Protocol.Types;
    using World;

    // I'm leaving this here for future logging (to do not forget how to do it optimally)
    public static partial class Log
    {
        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Debug,
            Message = "Payload: {Payload}")]
        public static partial void Payload(this ILogger logger, string payload);
    }

    internal sealed partial class PlayHandler(
        IWorldSource world,
        PlayerContext ctx,
        IPlayerRepository players,
        IInventoryRepository inventory,
        IPlayerRegistry registry,
        CommandDispatcher commands,
        ReadOnlyMemory<byte> welcomeMessage,
        IEntityManager entities,
        IPhysicsEngine physics) : AsyncHandlerBase
    {
        private const int ViewDistance = 2;

        // Maximum number of chunks that can be in view at once: (ViewDistance * 2 + 1)^2
        private const int MaxChunksInView = (ViewDistance * 2 + 1) * (ViewDistance * 2 + 1);

        private const int InitialTeleportId = 1;
        private const long InitialWorldAge = 0;
        private const long NoonTimeOfDay = 6000;
        private const int RespawnGameEvent = 13;
        private const float RespawnGameEventValue = 0f;

        private const double DefaultSpawnX = 0.5;
        private const double DefaultSpawnY = 65.0;
        private const double DefaultSpawnZ = 0.5;

        private const double PickupRadius = 1.5;
        private const double TeleportThreshold = 8.0;
        private const long PickupCooldownMs = 500;

        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

        private static readonly byte[] MsgWelcome =
            "Hello, welcome to the FunC#raft server!"u8.ToArray();
        private static readonly byte[] MsgInDev =
            "This server is heavily in development."u8.ToArray();

        private readonly CommandDispatcher _localCommands = new();

        private bool _spawnAcknowledged;
        private long _lastKeepAliveId;

        // All keep-alive IDs sent but not yet acknowledged. The client may
        // respond to several in a burst after any processing pause.
        private readonly HashSet<long> _pendingKeepAliveIds = [];

        private int _lastChunkX = int.MinValue;
        private int _lastChunkZ = int.MinValue;
        private readonly HashSet<(int, int)> _loadedChunks = [];
        private readonly SemaphoreSlim _chunkLock = new(1, 1);

        // Set in OnEnterAsync; used to feed the movement history for anti-cheat.
        private PlayerPhysicsBody? _playerBody;

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            var record = await players.GetByUuidAsync(ctx.Uuid, ct);

            if (record is not null)
            {
                ctx.X = record.X;
                ctx.Y = record.Y;
                ctx.Z = record.Z;
                ctx.Yaw = record.Yaw;
                ctx.Pitch = record.Pitch;
            }
            else
            {
                ctx.X = DefaultSpawnX;
                ctx.Y = DefaultSpawnY;
                ctx.Z = DefaultSpawnZ;
            }

            // Register player tracking body. Records movement history each tick
            // for future anti-cheat validation.
            _playerBody = physics.RegisterPlayer(ctx.EntityId, ctx.X, ctx.Y, ctx.Z);

            await Sender.SendAsync(new LoginPlayPacket
            {
                EntityId = ctx.EntityId,
                DimensionType = "minecraft:overworld",
                DimensionName = "minecraft:overworld"
            }, ct);

            await Sender.SendAsync(new SynchronizePlayerPositionPacket
            {
                TeleportId = InitialTeleportId,
                X = ctx.X,
                Y = ctx.Y,
                Z = ctx.Z,
                VelocityX = 0,
                VelocityY = 0,
                VelocityZ = 0,
                Yaw = ctx.Yaw,
                Pitch = ctx.Pitch,
                Flags = 0
            }, ct);

            await Sender.SendAsync(new SetCenterChunkPacket
            {
                ChunkX = WorldToChunk(ctx.X),
                ChunkZ = WorldToChunk(ctx.Z)
            }, ct);

            await Sender.SendAsync(new SetChunkCacheRadiusPacket { ViewDistance = ViewDistance }, ct);

            await Sender.SendAsync(new UpdateTimePacket
            {
                WorldAge = InitialWorldAge,
                TimeOfDay = NoonTimeOfDay,
                TimeOfDayIncreasing = false
            }, ct);

            var existing = registry.GetAll();

            if (existing.Count > 0)
            {
                await Sender.SendAsync(BuildInfoUpdate(existing), ct);

                foreach (var other in existing)
                {
                    await SendSpawnPlayerAsync(Sender, other, ct);
                }
            }

            var self = new ConnectedPlayer(ctx.Uuid, ctx.Username, Sender, ctx);
            registry.Register(self);

            await Sender.SendAsync(BuildInfoUpdate([self]), ct);
            await registry.BroadcastRawAsync(BuildInfoUpdate([self]), excludeUuid: ctx.Uuid, ct);

            // Serialize self's spawn frames exactly once, then enqueue to all
            // existing connections synchronously. This is O(1) serializations
            // instead of the previous O(N) — critical for 1k-bot join performance.
            BroadcastPlayerSpawnToAll(existing, self);

            _localCommands.Register(new TestCommand(ctx));
            _localCommands.Register(new RespawnCommand(ctx));
            _localCommands.Register(new GiveCommand(ctx, entities, physics));
            _localCommands.Register(new HelpCommand(_localCommands, commands));

            var rentedInv = ArrayPool<InventorySlot>.Shared.Rent(InventorySlot.InventorySize);

            try
            {
                rentedInv.AsSpan(0, InventorySlot.InventorySize).Clear();

                var hadSaved = await inventory.TryGetInventoryAsync(
                    ctx.Uuid, rentedInv.AsMemory(0, InventorySlot.InventorySize), ct);

                if (hadSaved)
                {
                    rentedInv.AsSpan(0, InventorySlot.InventorySize).CopyTo(ctx.Inventory);
                }
            }
            finally
            {
                ArrayPool<InventorySlot>.Shared.Return(rentedInv);
            }

            var slots = new (int ItemId, int Count, int Damage)[InventorySlot.InventorySize];

            for (var i = 0; i < InventorySlot.InventorySize; i++)
            {
                slots[i] = (ctx.Inventory[i].ItemId, ctx.Inventory[i].Count, SlotDamage(ctx.Inventory[i]));
            }

            await Sender.SendAsync(new SetContainerContentPacket
            {
                WindowId = 0,
                StateId = ctx.NextStateId(),
                Slots = slots,
            }, ct);

            foreach (var item in entities.GetAllItems())
            {
                await SendSpawnItemAsync(Sender, item, ct);
            }

            _ = KeepAliveLoopAsync(ct);

            await registry.BroadcastAsync(new SystemChatMessagePacket
            {
                Content = ConcatBytes("\u00a7e"u8, ctx.Username.Span, "\u00a7f joined the game."u8)
            }, ct);

            await Sender.SendAsync(new SystemChatMessagePacket { Content = MsgWelcome }, ct);
            await Sender.SendAsync(new SystemChatMessagePacket { Content = MsgInDev }, ct);

            var remaining = welcomeMessage;

            while (!remaining.IsEmpty)
            {
                var nl = remaining.Span.IndexOf((byte)'\n');
                ReadOnlyMemory<byte> line;

                if (nl < 0)
                {
                    line = remaining;
                    remaining = default;
                }
                else
                {
                    line = remaining[..nl];
                    remaining = remaining[(nl + 1)..];
                }

                if (!line.IsEmpty)
                {
                    await Sender.SendAsync(new SystemChatMessagePacket { Content = line }, ct);
                }
            }
        }

        internal override async ValueTask<ConnectionState> HandleAsync(
            int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            switch (packetId)
            {
                case ConfirmTeleportationPacket.Id:
                    if (!_spawnAcknowledged)
                    {
                        _spawnAcknowledged = true;
                        await SendInitialChunksAsync(ct);
                    }
                    break;

                case ServerboundKeepAlivePacket.Id:
                    HandleKeepAlive(payload);
                    break;

                case ChunkBatchReceivedPacket.Id:
                    HandleChunkBatchReceived(payload);
                    break;

                case SetPlayerPositionPacket.Id:
                    await HandleSetPlayerPositionAsync(payload, ct);
                    break;

                case SetPlayerPositionAndRotationPacket.Id:
                    await HandleSetPlayerPositionAndRotationAsync(payload, ct);
                    break;

                case SetPlayerRotationPacket.Id:
                    HandleSetPlayerRotation(payload);
                    break;

                case ChatCommandPacket.Id:
                    await HandleChatCommandAsync(payload, ct);
                    break;

                case ChatMessagePacket.Id:
                    await HandleChatAsync(payload, ct);
                    break;

                case PlayerActionPacket.Id:
                    await HandlePlayerActionAsync(payload, ct);
                    break;

                case ClickContainerPacket.Id:
                    await HandleClickContainerAsync(payload, ct);
                    break;

                case SetHeldItemPacket.Id:
                    HandleSetHeldItem(payload);
                    break;

                case UseItemOnPacket.Id:
                    await HandleUseItemOnAsync(payload, ct);
                    break;

                case 0x0C:
                case 0x20:
                case 0x27:
                case 0x29:
                case 0x2A:
                case 0x2B:
                case 0x3C:
                    break;

                case 0x12:
                    HandleCloseContainer();
                    break;

                default:
                    Console.WriteLine($"[PlayHandler] unhandled 0x{packetId:X2}");
                    break;
            }

            return ConnectionState.Play;
        }
    }
}