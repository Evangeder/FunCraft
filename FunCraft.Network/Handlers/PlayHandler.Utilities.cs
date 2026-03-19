namespace FunCraft.Network.Handlers
{
    using Data.Inventory;
    using Protocol.Registry;

    internal sealed partial class PlayHandler
    {
        private static int WorldToChunk(double worldCoord) => (int)Math.Floor(worldCoord) >> 4;

        /// <summary>
        /// Converts an <see cref="InventorySlot"/> to the <c>minecraft:damage</c>
        /// component value the client expects: damage = maxDurability - remaining.
        /// Returns 0 (pristine) for non-tool items or uninitialised slots.
        /// </summary>
        private static int SlotDamage(InventorySlot slot)
        {
            if (slot.IsEmpty || slot.Durability <= 0)
            {
                return 0;
            }

            var info = ToolSpeedTable.Get(slot.ItemId);

            if (!info.HasValue || info.Value.Durability <= 0)
            {
                return 0;
            }

            return info.Value.Durability - slot.Durability;
        }

        /// <summary>
        /// Computes the initial velocity for a player-thrown item.
        /// <para>
        /// Projects forward along the look vector at 0.3 blocks/tick (vanilla throw
        /// speed), then adds ±0.02 random jitter per axis so consecutive drops don't
        /// stack into a single entity before the cooldown expires.
        /// </para>
        /// <para>
        /// Minecraft yaw convention: 0 = south (+Z), 90 = west (-X), 180 = north (-Z),
        /// 270 = east (+X). Pitch: 0 = horizontal, -90 = straight up, +90 = straight down.
        /// Forward vector: dx = -sin(yaw)·cos(pitch), dy = -sin(pitch), dz = cos(yaw)·cos(pitch).
        /// </para>
        /// </summary>
        private static (double vx, double vy, double vz) ThrowVelocity(float yawDeg, float pitchDeg)
        {
            // Vanilla single-item throw: 0.4 blocks/tick along the look vector.
            const double Speed = 0.4;
            const double Jitter = 0.02;

            var yaw = yawDeg * Math.PI / 180.0;
            var pitch = pitchDeg * Math.PI / 180.0;
            var cosPitch = Math.Cos(pitch);

            var dx = -Math.Sin(yaw) * cosPitch;
            var dy = -Math.Sin(pitch);
            var dz = Math.Cos(yaw) * cosPitch;

            var jx = (Random.Shared.NextDouble() - 0.5) * Jitter;
            var jy = (Random.Shared.NextDouble() - 0.5) * Jitter;
            var jz = (Random.Shared.NextDouble() - 0.5) * Jitter;

            return (dx * Speed + jx, dy * Speed + jy, dz * Speed + jz);
        }

        private static byte[] ConcatBytes(
            ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c)
        {
            var result = new byte[a.Length + b.Length + c.Length];
            a.CopyTo(result);
            b.CopyTo(result.AsSpan(a.Length));
            c.CopyTo(result.AsSpan(a.Length + b.Length));
            return result;
        }

        private static byte[] ConcatBytes(
            ReadOnlySpan<byte> a, ReadOnlySpan<byte> b,
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