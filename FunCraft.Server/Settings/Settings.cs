namespace FunCraft.Server.Settings
{
    public sealed class ServerSettings
    {
        public const string Section = "Server";

        public int Port { get; init; } = 25565;
        public string Name { get; init; } = "FunCraft";
        public int MaxPlayers { get; init; } = 20;

        /// <summary>
        /// Lines shown in the server list description, joined with a newline.
        /// <br/>Supports § colour codes.
        /// </summary>
        public string[] Motd { get; init; } = ["A FunC#raft Server"];
    }
}