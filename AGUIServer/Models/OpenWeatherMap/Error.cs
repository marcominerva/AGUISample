using System.Text.Json.Serialization;

namespace AGUIServer.Models.OpenWeatherMap;

public class Error
{
    [JsonPropertyName("cod")]
    public int Code { get; set; }

    public string? Message { get; set; }
}
