using System.Collections.Concurrent;
using FunCraft.Network.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FunCraft.Network.Physics
{
    /// <summary>
    /// Dedicated server-side physics engine running at 20 Hz (50 ms tick interval).
    /// Implemented as a <see cref="BackgroundService"/> so the DI host owns its
    /// lifetime, and as <see cref="IPhysicsEngine"/> so consumers don't take a
    /// direct dependency on the concrete type.
    ///
    /// <para>
    /// All bodies are stored in a <see cref="ConcurrentDictionary{TKey,TValue}"/>:
    /// the network thread registers/unregisters bodies while the physics thread
    /// iterates and ticks them. ConcurrentDictionary's enumerator takes a weak
    /// snapshot so concurrent removals during iteration are safe.
    /// </para>
    /// </summary>
    public sealed class PhysicsEngine(ICollisionProvider collision, ILogger<PhysicsEngine> logger)
        : BackgroundService, IPhysicsEngine
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50); // 20 TPS

        private readonly ConcurrentDictionary<int, IPhysicsBody> _bodies = new();

        // Pause state. Volatile so Pause()/Resume() on the command thread are
        // immediately visible to the physics tick thread without a lock.
        private volatile bool _paused;

        public bool IsPaused => _paused;

        // ─── BackgroundService ────────────────────────────────────────────────────

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            logger.LogInformation("Physics engine started (20 Hz, {Interval} ms tick).",
                TickInterval.TotalMilliseconds);

            using var timer = new PeriodicTimer(TickInterval);

            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_paused) continue;

                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    // Log and continue — a single tick failure must never kill the loop.
                    logger.LogError(ex,
                        "Unhandled exception during physics tick. Bodies: {Count}.",
                        _bodies.Count);
                }
            }

            logger.LogInformation("Physics engine stopped.");
        }

        private void Tick()
        {
            foreach (var (id, body) in _bodies)
            {
                try
                {
                    if (!body.Tick(collision))
                    {
                        if (_bodies.TryRemove(id, out _))
                            body.OnRemoved();
                    }
                }
                catch (Exception ex)
                {
                    if (_bodies.TryRemove(id, out _))
                        body.OnRemoved();
                    logger.LogWarning(ex,
                        "Exception ticking body {EntityId}. Body removed from simulation.", id);
                }
            }
        }

        // ─── IPhysicsEngine ───────────────────────────────────────────────────────

        public ItemPhysicsBody RegisterItem(ItemEntity item)
        {
            var body = new ItemPhysicsBody(item);
            _bodies[item.EntityId] = body;
            return body;
        }

        public PlayerPhysicsBody RegisterPlayer(int entityId,
            double startX, double startY, double startZ)
        {
            var body = new PlayerPhysicsBody(entityId, startX, startY, startZ);
            _bodies[entityId] = body;
            return body;
        }

        public void Unregister(int entityId)
        {
            if (_bodies.TryRemove(entityId, out var body))
                body.OnRemoved();
        }

        public void Pause()
        {
            if (_paused) return;
            _paused = true;
            logger.LogWarning("Physics engine paused. {Count} bodies suspended.", _bodies.Count);
        }

        public void Resume()
        {
            if (!_paused) return;
            _paused = false;
            logger.LogInformation("Physics engine resumed. {Count} bodies active.", _bodies.Count);
        }
    }
}