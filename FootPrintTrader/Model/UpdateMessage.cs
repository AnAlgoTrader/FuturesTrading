using System.Text.Json;

namespace FootPrintTrader.Model;

public class UpdateMessage
{
    public string Type { get; set; } = "";
    public JsonElement Data { get; set; }
}

public class UpdateMessageType
{
    public const string FOOTPRINT_SNAPSHOT = "FOOTPRINT_SNAPSHOT";
    public const string CAPTURE_CHART = "CAPTURE_CHART";
    public const string CHART_CAPTURE = "CHART_CAPTURE";
}
