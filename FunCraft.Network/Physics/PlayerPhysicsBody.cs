using System.Runtime.InteropServices;

namespace FunCraft.Network.Physics
{
    /// <summary>
    /// One client-reported movement snapshot in the ring buffer.
    /// 32 bytes, 8-byte aligned, zero padding.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct MovementSample  // 32 bytes exactly
    {
        public double X;          // 8
        public double Y;          // 8
        public double Z;          // 8
        public long TimestampMs; // 8
    }

    /// <summary>
    /// Tracks a connected player's movement history for future anti-cheat analysis.
    ///
    /// <para>
    /// Player position is currently client-authoritative — this body does not drive
    /// the player's world position. It exists as a data-collection layer:
    /// <list type="bullet">
    ///   <item>A 20-sample (1-second) ring buffer of recent positions and timestamps.</item>
    ///   <item>The last confirmed server-side position (<see cref="LastX"/>, <see cref="LastY"/>, <see cref="LastZ"/>).</item>
    /// </list>
    /// TODO: Implement <c>GetMovementViolation()</c> to compare reported deltas against
    /// server-simulated expected positions (flying detection, speed hacks, etc.).
    /// </para>
    ///
    /// <para>
    /// All writes happen on the network thread (single connection pipeline); reads for
    /// anti-cheat validation happen on the physics thread. The ring buffer is not
    /// lock-protected by design — stale reads during a concurrent write are acceptable
    /// for statistical violation detection. Position fields use <see cref="Volatile"/>
    /// so the physics thread always sees the latest confirmed coordinates.
    /// </para>
    /// </summary>
    public sealed class PlayerPhysicsBody : IPhysicsBody
    {
        private const int HistorySize = 20; // 1 second of samples at 20 TPS

        private readonly MovementSample[] _history = new MovementSample[HistorySize];
        private int _head;  // next write slot (0–19)
        private int _count; // valid sample count (0–20)

        private double _lastX, _lastY, _lastZ;

        public int EntityId { get; }

        /// <summary>Last client-reported position (volatile reads).</summary>
        public double LastX => Volatile.Read(ref _lastX);
        public double LastY => Volatile.Read(ref _lastY);
        public double LastZ => Volatile.Read(ref _lastZ);

        /// <summary>Number of samples currently held (0–20).</summary>
        public int SampleCount => _count;

        public PlayerPhysicsBody(int entityId, double startX, double startY, double startZ)
        {
            EntityId = entityId;
            _lastX = startX;
            _lastY = startY;
            _lastZ = startZ;
        }

        /// <summary>
        /// Records a movement packet received from the client. Call from the network
        /// thread only — the per-connection pipeline guarantees no concurrent writes.
        /// </summary>
        public void RecordMovement(double x, double y, double z)
        {
            _history[_head] = new MovementSample
            {
                X = x,
                Y = y,
                Z = z,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            _head = (_head + 1) % HistorySize;
            if (_count < HistorySize) _count++;

            Volatile.Write(ref _lastX, x);
            Volatile.Write(ref _lastY, y);
            Volatile.Write(ref _lastZ, z);
        }

        /// <summary>
        /// Copies the most recent <paramref name="count"/> samples into
        /// <paramref name="destination"/>, newest first.
        /// Returns the number of samples actually written.
        /// TODO: Anti-cheat reads this to compute per-tick displacement and compare
        ///       against the maximum legal speed for the player's current state.
        /// </summary>
        public int GetRecentSamples(Span<MovementSample> destination, int count)
        {
            var available = Math.Min(count, _count);
            for (var i = 0; i < available; i++)
            {
                var idx = ((_head - 1 - i) % HistorySize + HistorySize) % HistorySize;
                destination[i] = _history[idx];
            }
            return available;
        }

        // Player position is client-authoritative; no server-side simulation needed.
        public bool Tick(ICollisionProvider collision) => true;

        /// <inheritdoc/>
        public void OnRemoved() { /* nothing to signal for player bodies */ }
    }
}