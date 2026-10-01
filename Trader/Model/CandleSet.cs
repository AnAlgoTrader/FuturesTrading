using System.Collections.Generic;

namespace Trader.Model;

public class CandleSet
{
    public List<CandleUpdate> Candles { get; set; } = new();
}
