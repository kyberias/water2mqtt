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
    public RawMeterReading(Volume volume, byte[]? imageJpeg = null)
    {
        Volume = volume;
        ImageJpeg = imageJpeg;
    }

    public Volume Volume { get; }
    public byte[]? ImageJpeg { get; }
}

public class MeterReading
{
    public MeterReading(Volume volume, FlowRate? flowRate, byte[]? imageJpeg = null)
    {
        Volume = volume;
        FlowRate = flowRate;
        ImageJpeg = imageJpeg;
    }

    public Volume Volume { get; set; }
    public FlowRate? FlowRate { get; set; }
    public byte[]? ImageJpeg { get; }
}