namespace FunCraft.Network.Physics
{
    /// <summary>
    /// A single entity participating in the server-side physics simulation.
    /// </summary>
    public interface IPhysicsBody
    {
        int EntityId { get; }

        /// <summary>
        /// Advances this body by one tick (50 ms at 20 TPS).
        /// Returns <see langword="true"/> while the body is still active.
        /// Returning <see langword="false"/> signals to the engine that this body
        /// has settled and should be removed automatically.
        /// </summary>
        bool Tick(ICollisionProvider collision);

        /// <summary>
        /// Called by the engine immediately before this body is removed — whether by
        /// natural settle, explicit <c>Unregister</c>, or shutdown.
        /// Implementations use this to complete any pending signals.
        /// </summary>
        void OnRemoved();
    }
}