using System.Text.Json;

namespace FootPrintTrader.UI.Models;

public class UpdateMessage
{
    public string Type { get; set; } = "";
    public JsonElement Data { get; set; }
}

public static class UpdateMessageType
{
    public const string FOOTPRINT_SNAPSHOT = "FOOTPRINT_SNAPSHOT";
    public const string MARKET_DATA = "MARKET_DATA";
    public const string MARKET_INFO = "MARKET_INFO";
    public const string POSITION_UPDATE = "POSITION_UPDATE";
    public const string OPTIONS_CHAIN = "OPTIONS_CHAIN";
    public const string OPTIONS_ERROR = "OPTIONS_ERROR";
}
