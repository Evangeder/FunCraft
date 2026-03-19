using FunCraft.Network.Entities;

namespace FunCraft.Network.Physics
{
    /// <summary>
    /// Server-side physics simulation running at 20 Hz (one tick per 50 ms).
    /// </summary>
    public interface IPhysicsEngine
    {
        /// <summary>
        /// Registers a dropped item for simulation. The item's position will be
        /// updated each tick via <see cref="ItemEntity.UpdatePosition"/> until it
        /// settles on a solid surface, after which it is auto-removed from the engine.
        /// </summary>
        ItemPhysicsBody RegisterItem(ItemEntity item);

        /// <summary>
        /// Registers a connected player's physics tracking body. The body persists
        /// until <see cref="Unregister"/> is called on player disconnect.
        /// </summary>
        PlayerPhysicsBody RegisterPlayer(int entityId, double startX, double startY, double startZ);

        /// <summary>
        /// Removes the body with <paramref name="entityId"/> from the simulation.
        /// Safe to call even if the body has already auto-settled and been removed.
        /// </summary>
        void Unregister(int entityId);

        /// <summary>
        /// Pauses the tick loop. All registered bodies stop being simulated until
        /// <see cref="Resume"/> is called. Useful for in-game admin commands.
        /// </summary>
        void Pause();

        /// <summary>Resumes a previously paused simulation.</summary>
        void Resume();

        /// <summary>Whether the engine is currently paused.</summary>
        bool IsPaused { get; }
    }
}