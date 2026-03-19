namespace FunCraft.Network.Entities
{
    /// <summary>
    /// A dropped-item entity in the world.
    /// Position and velocity are updated each physics tick via volatile writes.
    /// <see cref="IsSettled"/> is set and velocity is zeroed atomically (from the
    /// physics thread) when the item lands on a solid surface.
    /// </summary>
    public sealed class ItemEntity(int itemId, int count,
        double x, double y, double z,
        double vx = 0, double vy = 0, double vz = 0,
        bool instantPickup = false)
    {
        public int EntityId { get; } = EntityIdSource.Next();
        public Guid Uuid { get; } = Guid.NewGuid();
        public int ItemId { get; } = itemId;
        public int Count { get; } = count;

        /// <summary>Initial velocity used in the SpawnEntity packet for client visuals.</summary>
        public double VelocityX { get; } = vx;
        public double VelocityY { get; } = vy;
        public double VelocityZ { get; } = vz;

        /// <summary>
        /// Monotonic ms timestamp at which this entity was spawned.
        /// 0 means instant pickup (e.g. /give — bypasses the 500 ms cooldown).
        /// </summary>
        public long SpawnedAtMs { get; } = instantPickup ? 0L : Environment.TickCount64;

        /// <summary>True once the physics body has come to rest on a solid surface.</summary>
        public bool IsSettled => Volatile.Read(ref _settled) != 0;

        private int _settled;

        // Position — volatile so pickup detection on the network thread sees
        // current physics-thread updates without a lock.
        private double _x = x, _y = y, _z = z;
        private double _liveVx = vx, _liveVy, _liveVz = vz;

        public double X => Volatile.Read(ref _x);
        public double Y => Volatile.Read(ref _y);
        public double Z => Volatile.Read(ref _z);

        /// <summary>Live velocity as updated by the physics engine each tick.</summary>
        public double LiveVx => Volatile.Read(ref _liveVx);
        public double LiveVy => Volatile.Read(ref _liveVy);
        public double LiveVz => Volatile.Read(ref _liveVz);

        /// <summary>Called to mark this entity as at rest.</summary>
        public void MarkSettled() => Volatile.Write(ref _settled, 1);

        /// <summary>Called by <see cref="Physics.ItemPhysicsBody"/> each tick.</summary>
        internal void UpdatePosition(double x, double y, double z)
        {
            Volatile.Write(ref _x, x);
            Volatile.Write(ref _y, y);
            Volatile.Write(ref _z, z);
        }

        /// <summary>Called by <see cref="Physics.ItemPhysicsBody"/> each tick.</summary>
        internal void UpdateVelocity(double vx, double vy, double vz)
        {
            Volatile.Write(ref _liveVx, vx);
            Volatile.Write(ref _liveVy, vy);
            Volatile.Write(ref _liveVz, vz);
        }
    }
}