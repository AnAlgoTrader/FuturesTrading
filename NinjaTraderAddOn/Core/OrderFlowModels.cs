using System.Collections.Generic;

namespace FootPrintNT
{
    public class OrderFlowSignal
    {
        public string Type { get; set; } = ""; // "stacked_imbalance", "absorption", "trapped_traders", "divergence", "unfinished_auction", "exhaustion"
        public string Title { get; set; } = "";
        public string Tone { get; set; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
        public double Price { get; set; }          // actionable level the signal refers to
        public int Priority { get; set; } = 5;     // lower = more important (see FootprintAnalyzer.Priority*)
        public string Description { get; set; } = ""; // one-line "why" shown under the title
        public string Time { get; set; } = "";     // HH:mm of the bar the signal came from
    }

    public class OrderFlowZone
    {
        public long BarStartTime { get; set; }
        public bool IsBuy { get; set; } // true = Buy Imbalance, false = Sell Imbalance
        public double LowPrice { get; set; }
        public double HighPrice { get; set; }
        public bool IsExhaustionAtBirth { get; set; } // Formed at bar extreme (top for buy, bottom for sell)
        public int AgeBars { get; set; }
        public bool IsTrapped { get; set; }
    }

    public class OrderFlowContext
    {
        public string Bias { get; set; } = "Neutral";
        public string BiasTone { get; set; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
        public string Action { get; set; } = "";
        public double ActionPrice { get; set; } // single source of truth for the actionable level

        // Live Bar Metrics
        public double PocPrice { get; set; }
        public double CurrentDelta { get; set; }
        public double DeltaPercent { get; set; }
        public decimal CurrentVolume { get; set; }
        public string PocPosition { get; set; } = "-";
        public string PocMigration { get; set; } = "-";

        // Multi-bar / Session Structure
        public double CumulativeDelta { get; set; }
        public string DeltaDivergence { get; set; } = "None";

        // Active Signals
        public List<OrderFlowSignal> Signals { get; set; } = new List<OrderFlowSignal>();
    }

    public class MarketInterpretation
    {
        public string Title { get; set; } = "";
        public string Tone { get; set; } = "neutral"; // "bullish", "bearish", "warning", "neutral"
        public string Action { get; set; } = "";
        public string Details { get; set; } = "";
    }
}
