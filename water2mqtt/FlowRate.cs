namespace water2mqtt;

public class FlowRate(Volume volume, TimeSpan timeSpan)
{
    private TimeSpan Timespan { get; } = ValidTimespan(timeSpan) ? timeSpan : throw new ArgumentException("Invalid timespan");
     
    static bool ValidTimespan(TimeSpan timeSpan) => timeSpan > TimeSpan.Zero;

    public decimal ToLitersPerMinute()
    {
        var liters = volume.ToLiters();
        var minutes = (decimal)Timespan.TotalMinutes;

        return liters / minutes;
    }

    public static FlowRate Zero = new(Volume.Zero, TimeSpan.FromMinutes(1));

    public static FlowRate FromLitersPerMinute(decimal value) => new(Volume.FromLiters(value), TimeSpan.FromMinutes(1));

    public static bool operator <(FlowRate a, FlowRate b)
    {
        var lpma = a.ToLitersPerMinute();
        var lpmb = b.ToLitersPerMinute();
        return lpma < lpmb;
    }

    public static bool operator >(FlowRate a, FlowRate b)
    {
        var lpma = a.ToLitersPerMinute();
        var lpmb = b.ToLitersPerMinute();
        return lpma > lpmb;
    }
}
