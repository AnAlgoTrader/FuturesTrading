using System.Linq;

namespace FootPrintNT
{
    public readonly struct FootprintLevel
    {
        public FootprintLevel(double price, decimal bidVolume, decimal askVolume)
        {
            Price = price;
            BidVolume = bidVolume;
            AskVolume = askVolume;
        }

        public double Price { get; }
        public decimal BidVolume { get; }
        public decimal AskVolume { get; }

        public decimal Total
        {
            get { return BidVolume + AskVolume; }
        }

        public decimal Delta
        {
            get { return AskVolume - BidVolume; }
        }
    }

    public sealed class FootprintBarData
    {
        public FootprintBarData(int tradeCount, long startTime, long endTime,
            double open, double high, double low, double close, FootprintLevel[] levels)
        {
            TradeCount = tradeCount;
            StartTime = startTime;
            EndTime = endTime;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Levels = levels ?? new FootprintLevel[0];
        }

        public int TradeCount { get; }
        public long StartTime { get; }
        public long EndTime { get; }
        public double Open { get; }
        public double High { get; }
        public double Low { get; }
        public double Close { get; }
        public FootprintLevel[] Levels { get; }

        public decimal Delta
        {
            get { return Levels.Sum(l => l.Delta); }
        }

        public decimal Volume
        {
            get { return Levels.Sum(l => l.Total); }
        }
    }

    public sealed class FootprintSnapshot
    {
        public FootprintSnapshot(FootprintBarData[] completed, FootprintBarData current)
        {
            Completed = completed ?? new FootprintBarData[0];
            Current = current;
        }

        public FootprintBarData[] Completed { get; }
        public FootprintBarData Current { get; }
    }
}
