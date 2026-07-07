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

    Task<Volume> GetNextValue(CancellationToken cancel);
}

public interface IWaterMeterRawValueSink
{
    void PostValue(Volume value);
}

public class MeterReading
{
    public MeterReading(Volume volume, FlowRate? flowRate)
    {
        Volume = volume;
        FlowRate = flowRate;
    }

    public Volume Volume { get; set; }
    public FlowRate? FlowRate { get; set; }
}