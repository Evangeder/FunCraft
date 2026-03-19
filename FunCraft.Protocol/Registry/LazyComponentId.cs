namespace FunCraft.Protocol.Registry
{
    /// <summary>
    /// Lazily resolves a structured data component type ID from the registry.
    /// Safe as a static field — resolves on first <see cref="Value"/> access
    /// after <see cref="RegistryLookup.Build"/> has run.
    ///
    /// <para>
    /// The registry and entry names are pre-encoded to UTF-8 bytes in the constructor
    /// so that every subsequent <see cref="Value"/> read until resolution is zero-alloc.
    /// Once resolved, <see cref="Value"/> returns the cached integer with no further
    /// work.
    /// </para>
    /// </summary>
    public sealed class LazyComponentId(string registry, string entry)
    {
        // Pre-encoded at construction so no allocation occurs on the Value hot path.
        private readonly byte[] _registryUtf8 = System.Text.Encoding.UTF8.GetBytes(registry);
        private readonly byte[] _entryUtf8 = System.Text.Encoding.UTF8.GetBytes(entry);

        private int _id = int.MinValue; // sentinel: not yet resolved

        public int Value
        {
            get
            {
                if (_id == int.MinValue)
                {
                    _id = RegistryLookup.GetId(_registryUtf8, _entryUtf8);
                }

                return _id;
            }
        }
    }
}