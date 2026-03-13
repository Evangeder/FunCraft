namespace FunCraft.Network.World
{
    using Protocol.Registry;

    /// <summary>
    /// Resolves the correct block state ID for a block placement event.
    /// <para>
    /// Given the held item's protocol ID, the face clicked, the player's yaw, and the
    /// cursor Y offset, computes all placement-relevant state properties and performs a
    /// direct key lookup against the full state registry (minecraft:block_state).
    /// Falls back to the block's default state if the computed key is not found.
    /// </para>
    /// </summary>
    public static class BlockStatePlacer
    {
        // UseItemOnPacket.Face constants.
        private const int FaceBottom = 0; // -Y
        private const int FaceTop = 1; // +Y
        private const int FaceNorth = 2; // -Z
        private const int FaceSouth = 3; // +Z
        private const int FaceWest = 4; // -X
        private const int FaceEast = 5; // +X

        // ── Property key bytes ────────────────────────────────────────────────────
        private static readonly byte[] KFacing = "facing"u8.ToArray();
        private static readonly byte[] KAxis = "axis"u8.ToArray();
        private static readonly byte[] KHalf = "half"u8.ToArray();
        private static readonly byte[] KType = "type"u8.ToArray();
        private static readonly byte[] KFace = "face"u8.ToArray();
        private static readonly byte[] KHinge = "hinge"u8.ToArray();
        private static readonly byte[] KShape = "shape"u8.ToArray();
        private static readonly byte[] KOpen = "open"u8.ToArray();
        private static readonly byte[] KWaterlogged = "waterlogged"u8.ToArray();
        private static readonly byte[] KPowered = "powered"u8.ToArray();
        private static readonly byte[] KSnowy = "snowy"u8.ToArray();
        private static readonly byte[] KLit = "lit"u8.ToArray();

        // ── Value bytes ───────────────────────────────────────────────────────────
        private static readonly byte[] VNorth = "north"u8.ToArray();
        private static readonly byte[] VSouth = "south"u8.ToArray();
        private static readonly byte[] VEast = "east"u8.ToArray();
        private static readonly byte[] VWest = "west"u8.ToArray();
        private static readonly byte[] VUp = "up"u8.ToArray();
        private static readonly byte[] VDown = "down"u8.ToArray();
        private static readonly byte[] VX = "x"u8.ToArray();
        private static readonly byte[] VY = "y"u8.ToArray();
        private static readonly byte[] VZ = "z"u8.ToArray();
        private static readonly byte[] VTop = "top"u8.ToArray();
        private static readonly byte[] VBottom = "bottom"u8.ToArray();
        private static readonly byte[] VLower = "lower"u8.ToArray();
        private static readonly byte[] VLeft = "left"u8.ToArray();
        private static readonly byte[] VFloor = "floor"u8.ToArray();
        private static readonly byte[] VWall = "wall"u8.ToArray();
        private static readonly byte[] VCeiling = "ceiling"u8.ToArray();
        private static readonly byte[] VFalse = "false"u8.ToArray();
        private static readonly byte[] VStraight = "straight"u8.ToArray();

        // ── Items that place different blocks depending on face ───────────────────
        // Key   = item name (UTF-8)
        // Value = (floor block name, wall block name)
        // Floor block is used when clicking the top face; wall block for side faces.
        // Clicking the bottom face returns 0 (cannot attach to ceiling).
        private static readonly Dictionary<string, (byte[] Floor, byte[] Wall)> WallVariants = new()
        {
            ["minecraft:torch"] = ("minecraft:torch"u8.ToArray(), "minecraft:wall_torch"u8.ToArray()),
            ["minecraft:soul_torch"] = ("minecraft:soul_torch"u8.ToArray(), "minecraft:soul_wall_torch"u8.ToArray()),
            ["minecraft:redstone_torch"] = ("minecraft:redstone_torch"u8.ToArray(), "minecraft:redstone_wall_torch"u8.ToArray()),
            ["minecraft:copper_torch"] = ("minecraft:copper_torch"u8.ToArray(), "minecraft:copper_wall_torch"u8.ToArray()),
        };

        // Wall-facing: the direction a wall-mounted block faces AWAY from the wall.
        // Index = UseItemOnPacket.Face value for side faces (2=north, 3=south, 4=west, 5=east).
        private static readonly byte[][] WallFacingForSideFace =
        [
            null!,    // 0 bottom — unused
            null!,    // 1 top    — unused
            VNorth,   // 2 north face clicked → torch faces south
            VSouth,   // 3 south face clicked → torch faces north
            VWest,    // 4 west  face clicked → torch faces east
            VEast,    // 5 east  face clicked → torch faces west
        ];

        /// <summary>
        /// Returns the global-palette block state ID to place, or 0 (air) if the item
        /// cannot be resolved to a block.
        /// </summary>
        /// <param name="itemId">Protocol item ID of the held item.</param>
        /// <param name="face">Clicked block face (0–5, UseItemOnPacket.Face).</param>
        /// <param name="yaw">Player yaw in degrees (0/360=south, 90=west, 180=north, 270=east).</param>
        /// <param name="cursorY">Cursor Y within the clicked face (0.0–1.0).</param>
        public static ushort Resolve(int itemId, int face, float yaw, float cursorY)
        {
            var itemNameMem = RegistryLookup.GetItemName(itemId);
            if (itemNameMem.IsEmpty) return 0;

            var nameSp = itemNameMem.Span;

            // ── Wall-variant items (torches etc.) ─────────────────────────────────
            // These place a different block depending on which face was clicked.
            var itemNameStr = System.Text.Encoding.UTF8.GetString(nameSp);
            if (WallVariants.TryGetValue(itemNameStr, out var variant))
            {
                if (face == FaceTop)
                    return RegistryLookup.GetBlockId(variant.Floor);   // floor — single state

                if (face == FaceBottom)
                    return 0;   // cannot attach to ceiling

                // Side face — wall block with facing away from wall.
                var wallFacing = WallFacingForSideFace[face];
                return ResolveWallBlock(variant.Wall, wallFacing);
            }

            // Single-state block — nothing to compute.
            var props = RegistryLookup.GetBlockProperties(nameSp);
            if (props == null || props.Count == 0)
                return RegistryLookup.GetBlockId(nameSp);

            // Accumulate property key/value pairs relevant to this placement.
            var pairs = new List<(byte[] Key, byte[] Val)>(10);

            // ── facing + face (buttons, levers) ──────────────────────────────────
            if (props.Contains("facing"))
            {
                if (props.Contains("face"))
                {
                    // Buttons / levers: separate "face" (floor/wall/ceiling) and "facing"
                    // (horizontal direction, relevant only when face=wall).
                    var (bFace, bFacing) = ButtonFaceAndFacing(face, yaw);
                    pairs.Add((KFace, bFace));
                    pairs.Add((KFacing, bFacing));
                }
                else if (Supports6DirFacing(nameSp))
                {
                    // Barrels, shulker boxes, dispensers — can face up or down.
                    pairs.Add((KFacing, Facing6Dir(face, yaw)));
                }
                else
                {
                    // Most directional blocks: north/south/east/west only.
                    pairs.Add((KFacing, Facing4Dir(yaw)));
                }
            }

            // ── axis (logs, pillars, chains, basalt, bone blocks…) ───────────────
            if (props.Contains("axis"))
                pairs.Add((KAxis, AxisFromFace(face)));

            // ── half ─────────────────────────────────────────────────────────────
            // Doors always start as "lower"; stairs and trapdoors use top/bottom.
            if (props.Contains("half"))
                pairs.Add((KHalf, props.Contains("hinge") ? VLower : HalfFromFaceAndCursor(face, cursorY)));

            // ── shape (stairs only — rails are handled by default state) ─────────
            // Stair shape is "straight" on initial placement; neighbours reshape it
            // post-placement (not implemented here — that requires world queries).
            if (props.Contains("shape") && IsStairShape(nameSp))
                pairs.Add((KShape, VStraight));

            // ── type (slabs) ─────────────────────────────────────────────────────
            // type=top / bottom / double; only set for slab-type blocks.
            if (props.Contains("type") && IsSlabType(nameSp))
                pairs.Add((KType, SlabTypeFromFaceAndCursor(face, cursorY)));

            // ── hinge (doors) ────────────────────────────────────────────────────
            // Left hinge on initial placement (right-hinge determination requires
            // knowing what is in the adjacent block — not implemented).
            if (props.Contains("hinge"))
                pairs.Add((KHinge, VLeft));

            // ── boolean defaults ─────────────────────────────────────────────────
            if (props.Contains("open")) pairs.Add((KOpen, VFalse));
            if (props.Contains("waterlogged")) pairs.Add((KWaterlogged, VFalse));
            if (props.Contains("powered")) pairs.Add((KPowered, VFalse));
            if (props.Contains("snowy")) pairs.Add((KSnowy, VFalse));
            if (props.Contains("lit")) pairs.Add((KLit, VFalse));

            // ── Sort pairs A-Z by key (required by the state key format) ─────────
            pairs.Sort(static (a, b) => CompareBytes(a.Key, b.Key));

            // ── Build the full key and look it up ─────────────────────────────────
            var keyBytes = BuildStateKey(nameSp, pairs);
            var stateId = RegistryLookup.GetBlockStateId(keyBytes);
            if (stateId >= 0) return (ushort)stateId;

            // Fall back to default state (e.g. if we skipped a required property).
            return RegistryLookup.GetBlockId(nameSp);
        }

        // ── Property value computation ────────────────────────────────────────────

        /// <summary>
        /// 4-directional facing from player yaw.
        /// Blocks face TOWARD the player (opposite of view direction).
        /// Yaw: 0/360=south, 90=west, 180=north, 270=east.
        /// </summary>
        private static byte[] Facing4Dir(float yaw)
        {
            var norm = ((yaw % 360f) + 360f) % 360f;
            if (norm >= 315f || norm < 45f) return VSouth;
            if (norm < 135f) return VWest;
            if (norm < 225f) return VNorth;
            return VEast;
        }

        /// <summary>6-directional facing — adds up/down for top/bottom face clicks.</summary>
        private static byte[] Facing6Dir(int face, float yaw) => face switch
        {
            FaceBottom => VDown,
            FaceTop => VUp,
            _ => Facing4Dir(yaw),
        };

        /// <summary>Axis from placement face: top/bottom → y, north/south → z, east/west → x.</summary>
        private static byte[] AxisFromFace(int face) => face switch
        {
            FaceTop or FaceBottom => VY,
            FaceNorth or FaceSouth => VZ,
            _ => VX,
        };

        /// <summary>
        /// top/bottom half for stairs and trapdoors.
        /// Clicking the top face places the block starting at the floor (bottom half).
        /// Clicking the bottom face places it at the ceiling (top half).
        /// Clicking a side face: cursor Y ≥ 0.5 → top, otherwise bottom.
        /// </summary>
        private static byte[] HalfFromFaceAndCursor(int face, float cursorY) => face switch
        {
            FaceTop => VBottom,
            FaceBottom => VTop,
            _ => cursorY >= 0.5f ? VTop : VBottom,
        };

        /// <summary>Slab type (top/bottom) from face and cursor Y.</summary>
        private static byte[] SlabTypeFromFaceAndCursor(int face, float cursorY) => face switch
        {
            FaceTop => VBottom,
            FaceBottom => VTop,
            _ => cursorY >= 0.5f ? VTop : VBottom,
        };

        /// <summary>
        /// Buttons and levers use a "face" property (floor/wall/ceiling) and a
        /// "facing" property (horizontal direction, ignored for floor/ceiling).
        /// </summary>
        private static (byte[] Face, byte[] Facing) ButtonFaceAndFacing(int face, float yaw)
        {
            if (face == FaceTop) return (VFloor, Facing4Dir(yaw));
            if (face == FaceBottom) return (VCeiling, Facing4Dir(yaw));
            // Placed on a vertical face → button faces out from that wall.
            var wallFacing = face switch
            {
                FaceNorth => VSouth,
                FaceSouth => VNorth,
                FaceWest => VEast,
                FaceEast => VWest,
                _ => Facing4Dir(yaw),
            };
            return (VWall, wallFacing);
        }

        // ── Registry probes ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the block supports 6-directional facing (up/down included).
        /// Determined by probing for a "name[facing=up]" key in the state registry.
        /// </summary>
        private static bool Supports6DirFacing(ReadOnlySpan<byte> blockName)
        {
            Span<byte> key = stackalloc byte[blockName.Length + 11]; // "[facing=up]"
            blockName.CopyTo(key);
            "[facing=up]"u8.CopyTo(key[blockName.Length..]);
            return RegistryLookup.GetBlockStateId(key) >= 0;
        }

        /// <summary>
        /// Returns true if the block uses stair-style shape values
        /// (straight / inner_left / inner_right / outer_left / outer_right).
        /// Probes for "name[...,shape=straight,...]" — we use the full default state
        /// key for a known stair property combination to avoid false positives.
        /// </summary>
        private static bool IsStairShape(ReadOnlySpan<byte> blockName)
        {
            // All stairs have facing + half + shape + waterlogged, so we probe for
            // a key that only stairs would produce. The simplest probe is shape=straight
            // not existing on rails. We use a partial key that would only match stairs.
            // Easiest: check if "name[facing=north,half=bottom,shape=straight,waterlogged=false]" exists.
            const string suffix = "[facing=north,half=bottom,shape=straight,waterlogged=false]";
            Span<byte> key = stackalloc byte[blockName.Length + suffix.Length];
            blockName.CopyTo(key);
            for (var i = 0; i < suffix.Length; i++) key[blockName.Length + i] = (byte)suffix[i];
            return RegistryLookup.GetBlockStateId(key) >= 0;
        }

        /// <summary>
        /// Returns true if the block uses slab-style type values (top / bottom / double).
        /// Probes for "name[type=bottom,waterlogged=false]" — all slabs have exactly
        /// these two properties.
        /// </summary>
        private static bool IsSlabType(ReadOnlySpan<byte> blockName)
        {
            const string suffix = "[type=bottom,waterlogged=false]";
            Span<byte> key = stackalloc byte[blockName.Length + suffix.Length];
            blockName.CopyTo(key);
            for (var i = 0; i < suffix.Length; i++) key[blockName.Length + i] = (byte)suffix[i];
            return RegistryLookup.GetBlockStateId(key) >= 0;
        }

        // ── Wall-block helper ─────────────────────────────────────────────────────

        /// <summary>
        /// Looks up a wall block state by name and a single facing value,
        /// e.g. "minecraft:wall_torch" + facing=south → state ID.
        /// Handles optional extra properties (like lit=true on redstone_wall_torch)
        /// by falling back to the block's default state if the minimal key misses.
        /// </summary>
        private static ushort ResolveWallBlock(byte[] wallName, byte[] facing)
        {
            // Build "name[facing=X]" — sufficient for plain torches.
            var nameSp = wallName.AsSpan();
            var facingSp = facing.AsSpan();

            // "facing=" = 7 bytes, "[" + "]" = 2 bytes
            Span<byte> key = stackalloc byte[nameSp.Length + 2 + 7 + facingSp.Length];
            var pos = 0;
            nameSp.CopyTo(key); pos += nameSp.Length;
            key[pos++] = (byte)'[';
            "facing="u8.CopyTo(key[pos..]); pos += 7;
            facingSp.CopyTo(key[pos..]); pos += facingSp.Length;
            key[pos] = (byte)']';

            var id = RegistryLookup.GetBlockStateId(key);
            if (id >= 0) return (ushort)id;

            // Fall back to default state (handles extra props like lit=true).
            return RegistryLookup.GetBlockId(wallName);
        }

        // ── State key assembly ────────────────────────────────────────────────────

        private static ReadOnlySpan<byte> BuildStateKey(
            ReadOnlySpan<byte> blockName,
            List<(byte[] Key, byte[] Val)> pairs)
        {
            if (pairs.Count == 0) return blockName;

            var len = blockName.Length + 1; // '['
            for (var i = 0; i < pairs.Count; i++)
            {
                if (i > 0) len++;
                len += pairs[i].Key.Length + 1 + pairs[i].Val.Length; // "k=v"
            }
            len++; // ']'

            var buf = new byte[len];
            var pos = 0;
            blockName.CopyTo(buf.AsSpan(pos)); pos += blockName.Length;
            buf[pos++] = (byte)'[';
            for (var i = 0; i < pairs.Count; i++)
            {
                if (i > 0) buf[pos++] = (byte)',';
                pairs[i].Key.CopyTo(buf.AsSpan(pos)); pos += pairs[i].Key.Length;
                buf[pos++] = (byte)'=';
                pairs[i].Val.CopyTo(buf.AsSpan(pos)); pos += pairs[i].Val.Length;
            }
            buf[pos] = (byte)']';
            return buf;
        }

        // ── Utility ───────────────────────────────────────────────────────────────

        private static int CompareBytes(byte[] a, byte[] b)
        {
            var len = Math.Min(a.Length, b.Length);
            for (var i = 0; i < len; i++)
            {
                var diff = a[i] - b[i];
                if (diff != 0) return diff;
            }
            return a.Length - b.Length;
        }
    }
}