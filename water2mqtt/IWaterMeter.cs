namespace water2mqtt;

public interface IWaterMeter
{
    //Task Start(Volume? knownGood = null);
    //Task Stop();

    string SerialNumber { get; }
    string Manufacturer { get; }
    string Model { get; }

    Task<MeterReading> GetNextValue(CancellationToken cancel);
}

public interface IWaterMeterValueSink
{
    void PostReading(MeterReading value);
}

public interface IWaterMeterRaw
{
    string SerialNumber { get; }
    string Manufacturer { get; }
    string Model { get; }

    Task<RawMeterReading> GetNextValue(CancellationToken cancel);
}

public interface IWaterMeterRawValueSink
{
    void PostValue(Volume value);
}

public class RawMeterReading
{
    public RawMeterReading(Volume volume, byte[]? imageJpeg = null, byte[]? rawImageJpeg = null)
    {
        Volume = volume;
        ImageJpeg = imageJpeg;
        RawImageJpeg = rawImageJpeg;
    }

    public Volume Volume { get; }
    public byte[]? ImageJpeg { get; }
    public byte[]? RawImageJpeg { get; }
}

public class MeterReading
{
    public MeterReading(Volume volume, FlowRate? flowRate, byte[]? imageJpeg = null,
        WaterUsage? completedUsage = null, byte[]? rawImageJpeg = null)
    {
        Volume = volume;
        FlowRate = flowRate;
        ImageJpeg = imageJpeg;
        CompletedUsage = completedUsage;
        RawImageJpeg = rawImageJpeg;
    }

    public Volume Volume { get; set; }
    public FlowRate? FlowRate { get; set; }
    public byte[]? ImageJpeg { get; }
    public byte[]? RawImageJpeg { get; }
    public WaterUsage? CompletedUsage { get; }
}

public class WaterUsage
{
    public WaterUsage(Volume volume, DateTimeOffset start, DateTimeOffset end)
    {
        Volume = volume;
        Start = start;
        End = end;
        Duration = end - start;
        FlowRate = new FlowRate(volume, Duration);
    }

    public Volume Volume { get; }
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public TimeSpan Duration { get; }
    public FlowRate FlowRate { get; }
}