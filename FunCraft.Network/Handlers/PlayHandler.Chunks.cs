using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using FunCraft.Network.World;
    using Protocol.Packets.Play.Outgoing;

    internal sealed partial class PlayHandler
    {
        private async ValueTask SendInitialChunksAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new GameEventPacket
            {
                Event = RespawnGameEvent,
                Value = RespawnGameEventValue
            }, ct);

            var cx = WorldToChunk(ctx.X);
            var cz = WorldToChunk(ctx.Z);
            _lastChunkX = cx;
            _lastChunkZ = cz;

            await Sender.SendRawAsync(ChunkBatchStartPacket.PreFramed, ct);

            for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
            {
                for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
                {
                    var chunkX = cx + dx;
                    var chunkZ = cz + dz;
                    await SendChunkAsync(chunkX, chunkZ, ct);
                    _loadedChunks.Add((chunkX, chunkZ));
                }
            }

            await Sender.SendAsync(new ChunkBatchFinishedPacket { BatchSize = MaxChunksInView }, ct);
        }

        /// <summary>TODO: Remove LINQ</summary>
        private async Task UpdateChunksAsync(int newCx, int newCz, CancellationToken ct)
        {
            if (!await _chunkLock.WaitAsync(0, ct))
            {
                return;
            }

            try
            {
                _lastChunkX = newCx;
                _lastChunkZ = newCz;

                await Sender.SendAsync(new SetCenterChunkPacket { ChunkX = newCx, ChunkZ = newCz }, ct);

                // Rent temp buffers; MaxChunksInView is the tightest possible bound
                // for how many chunks can be in either list at once.
                var toUnloadBuf = ArrayPool<(int, int)>.Shared.Rent(MaxChunksInView);
                var toLoadBuf = ArrayPool<(int, int)>.Shared.Rent(MaxChunksInView);
                var unloadCount = 0;
                var loadCount = 0;

                try
                {
                    // Collect chunks that have left the new view.
                    foreach (var chunk in _loadedChunks)
                    {
                        if (!IsChunkInView(chunk.Item1, chunk.Item2, newCx, newCz))
                        {
                            toUnloadBuf[unloadCount++] = chunk;
                        }
                    }

                    // Collect chunks that have entered the new view.
                    for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
                    {
                        for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
                        {
                            var chunk = (newCx + dx, newCz + dz);

                            if (!_loadedChunks.Contains(chunk))
                            {
                                toLoadBuf[loadCount++] = chunk;
                            }
                        }
                    }

                    // Unload out-of-view chunks first.
                    for (var i = 0; i < unloadCount; i++)
                    {
                        var (x, z) = toUnloadBuf[i];
                        await Sender.SendAsync(new UnloadChunkPacket { ChunkX = x, ChunkZ = z }, ct);
                        _loadedChunks.Remove((x, z));
                    }

                    // Load newly visible chunks in a single batch.
                    if (loadCount > 0)
                    {
                        await Sender.SendRawAsync(ChunkBatchStartPacket.PreFramed, ct);

                        for (var i = 0; i < loadCount; i++)
                        {
                            var (x, z) = toLoadBuf[i];
                            await SendChunkAsync(x, z, ct);
                            _loadedChunks.Add((x, z));
                        }

                        await Sender.SendAsync(new ChunkBatchFinishedPacket { BatchSize = loadCount }, ct);
                    }
                }
                finally
                {
                    ArrayPool<(int, int)>.Shared.Return(toUnloadBuf);
                    ArrayPool<(int, int)>.Shared.Return(toLoadBuf);
                }
            }
            finally
            {
                _chunkLock.Release();
            }
        }

        private async ValueTask SendChunkAsync(int chunkX, int chunkZ, CancellationToken ct)
        {
            var column = world.GetChunk(chunkX, chunkZ);
            var data = ChunkSerializer.Serialize(column);
            await Sender.SendAsync(new ChunkDataPacket { ChunkX = chunkX, ChunkZ = chunkZ, ChunkData = data }, ct);
        }

        // Returns true if the chunk at (cx, cz) falls within ViewDistance of the
        // centre at (viewCx, viewCz). Eliminates the per-update HashSet allocation
        // that the old ChunksInView helper required.
        private static bool IsChunkInView(int cx, int cz, int viewCx, int viewCz)
        {
            return Math.Abs(cx - viewCx) <= ViewDistance
                && Math.Abs(cz - viewCz) <= ViewDistance;
        }
    }
}