namespace FunCraft.Data.Sessions
{
    public sealed class PlayerSession
    {
        public Guid Uuid { get; init; }
        public string Username { get; init; } = "";
        public string IpAddress { get; init; } = "";
        public DateTimeOffset ConnectedAt { get; init; }
    }
}