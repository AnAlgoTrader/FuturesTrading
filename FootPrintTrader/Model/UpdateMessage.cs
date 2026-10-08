using System.Text.Json;

namespace FootPrintTrader.Model;

public class UpdateMessage
{
    public string Type { get; set; } = "";
    public JsonElement Data { get; set; }
}

public class UpdateMessageType
{
    public const string FOOTPRINT_CURRENT = "FOOTPRINT_CURRENT";
}
