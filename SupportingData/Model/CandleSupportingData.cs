namespace SupportingData.Model;

public class CandleSupportingData
{
    public long UtcTimestamp { get; set; }
    public DateTime Time { get; set; } 
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }  
    public double Volume { get; set; }
}