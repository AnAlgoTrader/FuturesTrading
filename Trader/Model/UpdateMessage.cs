using System.Text.Json;

namespace Trader.Model;

public class UpdateMessage
{
    public string Type { get; set; }
    public JsonElement Data { get; set; }
}

public class UpdateMessageType
{
    public const string ACCOUNT_UPDATE = "ACCOUNT_UPDATE";
}
