namespace FunCraft.Protocol.Registry
{
    public sealed class LazyComponentId(string registry, string entry)
    {
        private int _id = int.MinValue;

        public int Value
        {
            get
            {
                if (_id == int.MinValue)
                {
                    _id = RegistryLookup.GetId(
                        System.Text.Encoding.UTF8.GetBytes(registry),
                        System.Text.Encoding.UTF8.GetBytes(entry));
                }

                return _id;
            }
        }
    }
}