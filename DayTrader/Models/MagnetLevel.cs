using System.Text.Json.Serialization;

namespace DayTrader.Models;

public class MagnetLevel
{
    [JsonPropertyName("strike")]
    public double Strike { get; set; }

    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }
}
