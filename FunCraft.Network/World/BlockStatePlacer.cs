using System.Collections.Frozen;
using System.Runtime.CompilerServices;

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
    /// <para>
    /// Hot-path allocation budget per <see cref="Resolve"/> call: zero heap.
    /// Key/value pair staging uses a stack-allocated <see cref="PairBuffer"/> (InlineArray).
    /// State key assembly writes into a stackalloc'd byte buffer.
    /// WallVariants lookup uses a FNV-1a hash key — no string decode.
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

        // Maximum state key size in bytes.
        // Longest block name ~50 bytes + up to 12 properties ~200 bytes = 256 comfortably.
        private const int MaxStateKeyBytes = 512;

        // Maximum number of property pairs added per placement.
        private const int MaxPairs = 12;

        // ── Property key bytes — static ReadOnlyMemory<byte> allocated once at startup.
        // ReadOnlyMemory<byte> is used (not ReadOnlySpan<byte>) so they can be stored
        // as fields inside the Pair value struct without ref-struct restrictions.

        private static readonly ReadOnlyMemory<byte> KFacing = "facing"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KAxis = "axis"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KHalf = "half"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KType = "type"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KFace = "face"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KHinge = "hinge"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KShape = "shape"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KOpen = "open"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KWaterlogged = "waterlogged"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KPowered = "powered"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KSnowy = "snowy"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> KLit = "lit"u8.ToArray();

        // ── Value bytes

        private static readonly ReadOnlyMemory<byte> VNorth = "north"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VSouth = "south"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VEast = "east"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VWest = "west"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VUp = "up"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VDown = "down"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VX = "x"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VY = "y"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VZ = "z"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VTop = "top"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VBottom = "bottom"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VLower = "lower"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VLeft = "left"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VFloor = "floor"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VWall = "wall"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VCeiling = "ceiling"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VFalse = "false"u8.ToArray();
        private static readonly ReadOnlyMemory<byte> VStraight = "straight"u8.ToArray();

        // ── Key/value pair stored on the stack via InlineArray ───────────────────

        private readonly struct Pair(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value)
        {
            public readonly ReadOnlyMemory<byte> Key = key;
            public readonly ReadOnlyMemory<byte> Value = value;
        }

        // InlineArray(12) means a stack-allocated buffer of exactly 12 Pair elements.
        // No heap allocation — replaces the per-placement List<(byte[], byte[])>.
        [InlineArray(MaxPairs)]
        private struct PairBuffer
        {
            private Pair _element0;
        }

        // ── Items that place different blocks depending on face ───────────────────
        // Keyed by FNV-1a hash of the item name so Resolve() never needs to decode
        // the name to a managed string (eliminates one allocation per placement).
        private static readonly FrozenDictionary<uint, (ReadOnlyMemory<byte> Floor, ReadOnlyMemory<byte> Wall)>
            WallVariants = BuildWallVariants();

        private static FrozenDictionary<uint, (ReadOnlyMemory<byte>, ReadOnlyMemory<byte>)> BuildWallVariants()
        {
            var t = new Dictionary<uint, (ReadOnlyMemory<byte>, ReadOnlyMemory<byte>)>(4);

            Add("minecraft:torch", "minecraft:torch", "minecraft:wall_torch");
            Add("minecraft:soul_torch", "minecraft:soul_torch", "minecraft:soul_wall_torch");
            Add("minecraft:redstone_torch", "minecraft:redstone_torch", "minecraft:redstone_wall_torch");
            Add("minecraft:copper_torch", "minecraft:copper_torch", "minecraft:copper_wall_torch");

            return t.ToFrozenDictionary();

            void Add(string item, string floor, string wall)
            {
                var itemBytes = System.Text.Encoding.UTF8.GetBytes(item);
                t[Hash(itemBytes)] = (
                    System.Text.Encoding.UTF8.GetBytes(floor).AsMemory(),
                    System.Text.Encoding.UTF8.GetBytes(wall).AsMemory());
            }
        }

        // Wall-facing: the direction a wall-mounted block faces AWAY from the wall.
        private static readonly ReadOnlyMemory<byte>[] WallFacingForSideFace =
        [
            ReadOnlyMemory<byte>.Empty,  // 0 bottom — unused
            ReadOnlyMemory<byte>.Empty,  // 1 top    — unused
            VNorth,   // 2 north face clicked -> torch faces south
            VSouth,   // 3 south face clicked -> torch faces north
            VWest,    // 4 west  face clicked -> torch faces east
            VEast,    // 5 east  face clicked -> torch faces west
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

            if (itemNameMem.IsEmpty)
            {
                return 0;
            }

            var nameSp = itemNameMem.Span;

            // Wall-variant items (torches etc.): place a different block depending on
            // which face was clicked. Lookup by FNV-1a hash — no string decode.
            if (WallVariants.TryGetValue(Hash(nameSp), out var variant))
            {
                if (face == FaceTop)
                {
                    return RegistryLookup.GetBlockId(variant.Floor.Span);
                }

                if (face == FaceBottom)
                {
                    return 0;
                }

                var wallFacing = WallFacingForSideFace[face];
                return ResolveWallBlock(variant.Wall.Span, wallFacing.Span);
            }

            // Single-state block — nothing to compute.
            var props = RegistryLookup.GetBlockProperties(nameSp);

            if (props == null || props.Count == 0)
            {
                return RegistryLookup.GetBlockId(nameSp);
            }

            // Accumulate property key/value pairs into a stack-allocated InlineArray.
            // No heap allocation on this path.
            var pairsBuffer = new PairBuffer();
            var pairs = (Span<Pair>)pairsBuffer;
            var pairCount = 0;

            // facing + face (buttons, levers)
            if (props.Contains("facing"))
            {
                if (props.Contains("face"))
                {
                    var (bFace, bFacing) = ButtonFaceAndFacing(face, yaw);
                    pairs[pairCount++] = new Pair(KFace, bFace);
                    pairs[pairCount++] = new Pair(KFacing, bFacing);
                }
                else if (Supports6DirFacing(nameSp))
                {
                    pairs[pairCount++] = new Pair(KFacing, Facing6Dir(face, yaw));
                }
                else
                {
                    pairs[pairCount++] = new Pair(KFacing, Facing4Dir(yaw));
                }
            }

            // axis (logs, pillars, chains, basalt, bone blocks...)
            if (props.Contains("axis"))
            {
                pairs[pairCount++] = new Pair(KAxis, AxisFromFace(face));
            }

            // half — doors always "lower"; stairs and trapdoors use top/bottom.
            if (props.Contains("half"))
            {
                var halfVal = props.Contains("hinge")
                    ? VLower
                    : HalfFromFaceAndCursor(face, cursorY);
                pairs[pairCount++] = new Pair(KHalf, halfVal);
            }

            // shape (stairs only)
            if (props.Contains("shape") && IsStairShape(nameSp))
            {
                pairs[pairCount++] = new Pair(KShape, VStraight);
            }

            // type (slabs)
            if (props.Contains("type") && IsSlabType(nameSp))
            {
                pairs[pairCount++] = new Pair(KType, SlabTypeFromFaceAndCursor(face, cursorY));
            }

            // hinge (doors)
            if (props.Contains("hinge"))
            {
                pairs[pairCount++] = new Pair(KHinge, VLeft);
            }

            // boolean defaults
            if (props.Contains("open")) { pairs[pairCount++] = new Pair(KOpen, VFalse); }
            if (props.Contains("waterlogged")) { pairs[pairCount++] = new Pair(KWaterlogged, VFalse); }
            if (props.Contains("powered")) { pairs[pairCount++] = new Pair(KPowered, VFalse); }
            if (props.Contains("snowy")) { pairs[pairCount++] = new Pair(KSnowy, VFalse); }
            if (props.Contains("lit")) { pairs[pairCount++] = new Pair(KLit, VFalse); }

            // Sort pairs A-Z by key (required by the state key format).
            // Insertion sort — max 12 elements, branch-predictable, no allocation.
            InsertionSort(pairs[..pairCount]);

            // Build the full state key directly into a stackalloc'd buffer.
            Span<byte> keyBuf = stackalloc byte[MaxStateKeyBytes];
            var keyLen = BuildStateKey(nameSp, pairs, pairCount, keyBuf);

            var stateId = RegistryLookup.GetBlockStateId(keyBuf[..keyLen]);

            if (stateId >= 0)
            {
                return (ushort)stateId;
            }

            // Fall back to default state if the computed key wasn't found.
            return RegistryLookup.GetBlockId(nameSp);
        }

        // ── Property value computation ────────────────────────────────────────────

        /// <summary>
        /// 4-directional facing from player yaw.
        /// Blocks face TOWARD the player (opposite of view direction).
        /// Yaw: 0/360=south, 90=west, 180=north, 270=east.
        /// </summary>
        private static ReadOnlyMemory<byte> Facing4Dir(float yaw)
        {
            var norm = ((yaw % 360f) + 360f) % 360f;

            if (norm >= 315f || norm < 45f)
            {
                return VSouth;
            }

            if (norm < 135f)
            {
                return VWest;
            }

            if (norm < 225f)
            {
                return VNorth;
            }

            return VEast;
        }

        /// <summary>6-directional facing — adds up/down for top/bottom face clicks.</summary>
        private static ReadOnlyMemory<byte> Facing6Dir(int face, float yaw) => face switch
        {
            FaceBottom => VDown,
            FaceTop => VUp,
            _ => Facing4Dir(yaw),
        };

        /// <summary>Axis from placement face: top/bottom → y, north/south → z, east/west → x.</summary>
        private static ReadOnlyMemory<byte> AxisFromFace(int face) => face switch
        {
            FaceTop or FaceBottom => VY,
            FaceNorth or FaceSouth => VZ,
            _ => VX,
        };

        /// <summary>
        /// top/bottom half for stairs and trapdoors.
        /// Clicking the top face places the block starting at the floor (bottom half).
        /// Clicking the bottom face places it at the ceiling (top half).
        /// Clicking a side face: cursor Y >= 0.5 → top, otherwise bottom.
        /// </summary>
        private static ReadOnlyMemory<byte> HalfFromFaceAndCursor(int face, float cursorY) => face switch
        {
            FaceTop => VBottom,
            FaceBottom => VTop,
            _ => cursorY >= 0.5f ? VTop : VBottom,
        };

        /// <summary>Slab type (top/bottom) from face and cursor Y.</summary>
        private static ReadOnlyMemory<byte> SlabTypeFromFaceAndCursor(int face, float cursorY) => face switch
        {
            FaceTop => VBottom,
            FaceBottom => VTop,
            _ => cursorY >= 0.5f ? VTop : VBottom,
        };

        /// <summary>
        /// Buttons and levers use a "face" property (floor/wall/ceiling) and a
        /// "facing" property (horizontal direction, ignored for floor/ceiling).
        /// </summary>
        private static (ReadOnlyMemory<byte> Face, ReadOnlyMemory<byte> Facing) ButtonFaceAndFacing(
            int face, float yaw)
        {
            if (face == FaceTop)
            {
                return (VFloor, Facing4Dir(yaw));
            }

            if (face == FaceBottom)
            {
                return (VCeiling, Facing4Dir(yaw));
            }

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
        /// Returns true if the block uses stair-style shape values.
        /// Probes for a full known stair state key.
        /// </summary>
        private static bool IsStairShape(ReadOnlySpan<byte> blockName)
        {
            const string suffix = "[facing=north,half=bottom,shape=straight,waterlogged=false]";
            Span<byte> key = stackalloc byte[blockName.Length + suffix.Length];
            blockName.CopyTo(key);

            for (var i = 0; i < suffix.Length; i++)
            {
                key[blockName.Length + i] = (byte)suffix[i];
            }

            return RegistryLookup.GetBlockStateId(key) >= 0;
        }

        /// <summary>
        /// Returns true if the block uses slab-style type values (top / bottom / double).
        /// Probes for "name[type=bottom,waterlogged=false]".
        /// </summary>
        private static bool IsSlabType(ReadOnlySpan<byte> blockName)
        {
            const string suffix = "[type=bottom,waterlogged=false]";
            Span<byte> key = stackalloc byte[blockName.Length + suffix.Length];
            blockName.CopyTo(key);

            for (var i = 0; i < suffix.Length; i++)
            {
                key[blockName.Length + i] = (byte)suffix[i];
            }

            return RegistryLookup.GetBlockStateId(key) >= 0;
        }

        // ── Wall-block helper ─────────────────────────────────────────────────────

        /// <summary>
        /// Looks up a wall block state by name and a single facing value.
        /// Falls back to the block's default state if the minimal key misses.
        /// All work is done in stackalloc'd buffers — zero heap allocation.
        /// </summary>
        private static ushort ResolveWallBlock(ReadOnlySpan<byte> wallName, ReadOnlySpan<byte> facing)
        {
            // "facing=" = 7 bytes, "[" + "]" = 2 bytes
            Span<byte> key = stackalloc byte[wallName.Length + 2 + 7 + facing.Length];
            var pos = 0;

            wallName.CopyTo(key);
            pos += wallName.Length;

            key[pos++] = (byte)'[';
            "facing="u8.CopyTo(key[pos..]);
            pos += 7;
            facing.CopyTo(key[pos..]);
            pos += facing.Length;
            key[pos] = (byte)']';

            var id = RegistryLookup.GetBlockStateId(key);

            if (id >= 0)
            {
                return (ushort)id;
            }

            // Fall back to default state (handles extra props like lit=true).
            return RegistryLookup.GetBlockId(wallName);
        }

        // ── State key assembly ────────────────────────────────────────────────────

        /// <summary>
        /// Writes the block state key (e.g. "minecraft:oak_log[axis=y]") into
        /// <paramref name="output"/> and returns the number of bytes written.
        /// No heap allocation.
        /// </summary>
        private static int BuildStateKey(
            ReadOnlySpan<byte> blockName, Span<Pair> pairs, int count, Span<byte> output)
        {
            if (count == 0)
            {
                blockName.CopyTo(output);
                return blockName.Length;
            }

            var pos = 0;
            blockName.CopyTo(output[pos..]);
            pos += blockName.Length;
            output[pos++] = (byte)'[';

            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    output[pos++] = (byte)',';
                }

                var k = pairs[i].Key.Span;
                var v = pairs[i].Value.Span;

                k.CopyTo(output[pos..]);
                pos += k.Length;
                output[pos++] = (byte)'=';
                v.CopyTo(output[pos..]);
                pos += v.Length;
            }

            output[pos++] = (byte)']';
            return pos;
        }

        // ── Utilities ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Insertion sort on a small span of <see cref="Pair"/> by key bytes (A-Z).
        /// O(n²) but n ≤ 12; faster than any comparison-based allocating sort at this size.
        /// </summary>
        private static void InsertionSort(Span<Pair> pairs)
        {
            for (var i = 1; i < pairs.Length; i++)
            {
                var current = pairs[i];
                var j = i - 1;

                while (j >= 0 && CompareBytes(pairs[j].Key.Span, current.Key.Span) > 0)
                {
                    pairs[j + 1] = pairs[j];
                    j--;
                }

                pairs[j + 1] = current;
            }
        }

        private static int CompareBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            var len = Math.Min(a.Length, b.Length);

            for (var i = 0; i < len; i++)
            {
                var diff = a[i] - b[i];

                if (diff != 0)
                {
                    return diff;
                }
            }

            return a.Length - b.Length;
        }

        private static uint Hash(ReadOnlySpan<byte> data)
        {
            const uint fnvPrime = 16777619;
            var hash = 2166136261u;

            foreach (var b in data)
            {
                hash ^= b;
                hash *= fnvPrime;
            }

            return hash;
        }
    }
}