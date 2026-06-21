using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PalworldServerManager.REST
{
    public class ServerInfoResponse
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("servername")] public string ServerName { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("worldguid")] public string WorldGuid { get; set; } = "";
    }

    public class PlayerInfo
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("accountName")] public string AccountName { get; set; } = "";
        [JsonPropertyName("playerId")] public string PlayerId { get; set; } = "";
        [JsonPropertyName("userId")] public string UserId { get; set; } = "";
        [JsonPropertyName("ip")] public string Ip { get; set; } = "";
        [JsonPropertyName("ping")] public double Ping { get; set; }
        [JsonPropertyName("location_x")] public double LocationX { get; set; }
        [JsonPropertyName("location_y")] public double LocationY { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("building_count")] public int BuildingCount { get; set; }
    }

    public class PlayerListResponse
    {
        [JsonPropertyName("players")] public List<PlayerInfo> Players { get; set; } = new();
    }

    public class ServerMetricsResponse
    {
        [JsonPropertyName("serverfps")] public int ServerFps { get; set; }
        [JsonPropertyName("currentplayernum")] public int CurrentPlayerNum { get; set; }
        [JsonPropertyName("serverframetime")] public double ServerFrameTime { get; set; }
        [JsonPropertyName("maxplayernum")] public int MaxPlayerNum { get; set; }
        [JsonPropertyName("uptime")] public int UpTime { get; set; }
        [JsonPropertyName("basecampnum")] public int BaseCampNum { get; set; }
        [JsonPropertyName("days")] public int Days { get; set; }
    }

    public class AnnounceRequest
    {
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }

    public class KickBanRequest
    {
        [JsonPropertyName("userid")] public string UserId { get; set; } = "";
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    public class UnbanRequest
    {
        [JsonPropertyName("userid")] public string UserId { get; set; } = "";
    }

    public class ShutdownRequest
    {
        [JsonPropertyName("waittime")] public int WaitTime { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
