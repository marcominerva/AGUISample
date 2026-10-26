using System.Text.Json.Serialization;

namespace AGUIServer.Models.OpenWeatherMap;

public class Clouds
{
    [JsonPropertyName("all")]
    public int Cloudiness { get; set; }
}