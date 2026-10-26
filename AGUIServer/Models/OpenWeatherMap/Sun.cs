using System.Text.Json.Serialization;
using AGUIServer.Models.OpenWeatherMap.Converters;

namespace AGUIServer.Models.OpenWeatherMap;

public class Sun
{
    [JsonPropertyName("country")]
    public required string Country { get; set; }

    [JsonConverter(typeof(UnixToDateTimeConverter))]
    [JsonPropertyName("sunrise")]
    public DateTime Sunrise { get; set; }

    [JsonConverter(typeof(UnixToDateTimeConverter))]
    [JsonPropertyName("sunset")]
    public DateTime Sunset { get; set; }
}