using System.Text.Json.Serialization;

namespace FunCraft.Protocol.Models
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ServerStatus))]
    [JsonSerializable(typeof(ServerVersion))]
    [JsonSerializable(typeof(ServerPlayers))]
    [JsonSerializable(typeof(ServerDescription))]
    public partial class ServerStatusContext : JsonSerializerContext { }

    public record ServerStatus(
        ServerVersion Version,
        ServerPlayers Players,
        ServerDescription Description
    );

    public record ServerVersion(string Name, int Protocol);
    public record ServerPlayers(int Max, int Online);
    public record ServerDescription(string Text);
}
