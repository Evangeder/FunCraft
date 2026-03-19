using FunCraft.Network.Entities;

namespace FunCraft.Network.Physics
{
    /// <summary>
    /// Simulates Minecraft item-entity physics on the server tick thread.
    ///
    /// <para>Vanilla tick order (must match client exactly so settle positions agree):</para>
    /// <list type="number">
    ///   <item>Apply gravity to Vy: <c>vy -= 0.04</c></item>
    ///   <item>Integrate: <c>pos += vel</c></item>
    ///   <item>Collision: snap Y to surface, zero Vy only — Vx/Vz are NOT touched.</item>
    ///   <item>Drag (applied after movement, not before):
    ///         on ground: <c>vx *= 0.588, vz *= 0.588</c>;
    ///         in air:    <c>vel *= 0.98</c> (all axes).</item>
    /// </list>
    ///
    /// <para>
    /// The item keeps sliding until <c>|Vx| + |Vz| &lt; 0.003</c> while on the ground,
    /// which is the effective float-precision limit for the vanilla client's simulation.
    /// Only at that point is <see cref="ItemEntity.MarkSettled"/> called and
    /// <see cref="SettledTask"/> completed so merge logic can fire.
    /// </para>
    /// </summary>
    public sealed class ItemPhysicsBody(ItemEntity entity) : IPhysicsBody
    {
        // Vanilla item-entity constants.
        private const double Gravity = 0.04;   // blocks/tick² downward
        private const double AirDrag = 0.98;   // applied to all axes in air
        private const double GroundFriction = 0.6 * AirDrag; // slipperiness × drag = 0.588

        // Item is considered at rest when combined horizontal speed drops below this.
        private const double SettleThreshold = 0.003;

        private readonly ItemEntity _entity = entity;

        private readonly TaskCompletionSource _settled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SettledTask => _settled.Task;

        private double _x = entity.X, _y = entity.Y, _z = entity.Z;
        private double _vx = entity.VelocityX, _vy = entity.VelocityY, _vz = entity.VelocityZ;

        public int EntityId => _entity.EntityId;

        public bool Tick(ICollisionProvider collision)
        {
            _vy -= Gravity;

            var nextX = _x + _vx;
            var nextY = _y + _vy;
            var nextZ = _z + _vz;

            var onGround = false;
            if (_vy <= 0)
            {
                var by = (int)Math.Floor(nextY);
                if (collision.IsSolid((int)Math.Floor(nextX), by, (int)Math.Floor(nextZ)))
                {
                    nextY = by + 1.0;
                    _vy = 0;
                    onGround = true;
                }
            }

            if (onGround)
            {
                _vx *= GroundFriction;
                _vz *= GroundFriction;
            }
            else
            {
                _vx *= AirDrag;
                _vy *= AirDrag;
                _vz *= AirDrag;
            }

            _x = nextX; _y = nextY; _z = nextZ;
            _entity.UpdatePosition(_x, _y, _z);
            _entity.UpdateVelocity(_vx, _vy, _vz);

            if (onGround && Math.Abs(_vx) + Math.Abs(_vz) < SettleThreshold)
            {
                return false;
            }

            return true;
        }

        public void OnRemoved()
        {
            _entity.MarkSettled();
            _settled.TrySetResult();
        }
    }
}