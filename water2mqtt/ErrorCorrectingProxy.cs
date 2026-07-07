using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace water2mqtt;

public class ErrorCorrectingProxy : BackgroundService, IWaterMeter
{
    private readonly ILogger<ErrorCorrectingProxy> log;
    private readonly IWaterMeterRaw meter;
    private readonly TimeProvider time;
    private readonly IReadingStorage storage;

    public ErrorCorrectingProxy(IWaterMeterRaw meter, TimeProvider time, ILogger<ErrorCorrectingProxy> log, IReadingStorage storage)
    {
        this.meter = meter;
        this.time = time;
        this.log = log;
        this.storage = storage;
        cancellationTokenSource = new CancellationTokenSource();
    }

    public override void Dispose()
    {
        cancellationTokenSource.Dispose();
        base.Dispose();
    }

    private Volume? knownInteger;
    private Volume? knownGoodDecimals;
    private DateTimeOffset? knownGoodValueTimestamp;
    private Task task;
    private readonly CancellationTokenSource cancellationTokenSource;

    private Volume? FullValue => knownInteger + knownGoodDecimals;

/*    public async Task Start(Volume? knownGood = null)
    {
        this.knownGood = knownGood;

        if (knownGood != null)
        {
            knownGoodTimestamp = time.GetUtcNow();
        }

        await meter.Start();

        task = Process(cancellationTokenSource.Token);
    }*/

    private async Task Process(CancellationToken cancel)
    {
        if (FullValue != null)
        {
            goodValues.Post(new MeterReading(FullValue, FlowRate.Zero));
        }

        FixedSizeList<(DateTimeOffset, Volume)> previouslyReported = new FixedSizeList<(DateTimeOffset, Volume)>(5);

        while (!cancel.IsCancellationRequested)
        {
            var proposedDecimals = await meter.GetNextValue(cancel);

            bool goodValueChanged = false;

            if (knownGoodDecimals == null)
            {
                knownGoodDecimals = proposedDecimals;
                knownGoodValueTimestamp = time.GetUtcNow();
                goodValueChanged = true;
            }
            else if (knownGoodDecimals != null)
            {
                //var newReading = proposedDecimals;

                var detectedDecimalChange = proposedDecimals - knownGoodDecimals;
                //var detectedIntegerChange = 0;

                if (detectedDecimalChange < -Volume.FromCubicMeters(0.9m) && proposedDecimals < Volume.FromCubicMeters(0.050m) && knownGoodDecimals > Volume.FromCubicMeters(0.9m))
                {
                    // This may indicate a cubic-meter rollover
                    // E.g. 123,999 to 124,001
                    //newReading += Volume.FromCubicMeters(1);
                    detectedDecimalChange = (proposedDecimals + Volume.FromCubicMeters(1)) - knownGoodDecimals;
                    //detectedIntegerChange = 1;
                    log.LogWarning("Detected potential cubit-meter rollover.");
                }

                var timeDiff = time.GetUtcNow() - knownGoodValueTimestamp.Value;
                var maxFlow = FlowRate.FromLitersPerMinute(50);

                if (timeDiff <= TimeSpan.Zero)
                {
                    log.LogWarning("Time difference is lower or equal to zero.");
                    continue;
                }

                var change = new FlowRate(detectedDecimalChange, timeDiff);

                if (change > maxFlow)
                {
                    log.LogWarning(
                        $"Too large detected flow: {change.ToLitersPerMinute()} l / minute (change: {detectedDecimalChange} time: {timeDiff})");

                    //tooLarge = newReading;
                    //tooLargeTimeStamp = DateTime.UtcNow;

                    //return;
                }
                else if (detectedDecimalChange < Volume.Zero)
                {
                    log.LogTrace($"Negative change: {detectedDecimalChange}");
                }
                else
                {
                    if (detectedDecimalChange > Volume.Zero)
                    {
                        goodValueChanged = true;
                    }

                    knownGoodDecimals += detectedDecimalChange;

                    if (knownGoodDecimals > Volume.FromCubicMeters(1))
                    {
                        knownGoodDecimals -= Volume.FromCubicMeters(1);
                        knownInteger += Volume.FromCubicMeters(1);
                    }

                    //knownInteger += Volume.FromCubicMeters(detectedIntegerChange);
                    knownGoodValueTimestamp = time.GetUtcNow();

                    /*if (newReading == tooLarge && (knownGoodValueTimestamp - tooLargeTimeStamp) > TimeSpan.FromMinutes(1))
                    {
                        var tooLargeCopy = $"toolarge_output_{tooLargeTimeStamp}.png";
                        log.LogWarning($"New value was too large over a minute a ago! {tooLargeCopy}");
                        File.Copy("output.png", tooLargeCopy);
                    }*/
                }
            }

            if (/*goodValueChanged && */knownInteger != null && knownGoodDecimals != null)
            {
                var total = knownInteger + knownGoodDecimals;

                log.LogInformation($"Known good {total}");
                storage.LatestGood = total;
                //await File.WriteAllTextAsync("knowngood.txt", $"{total.ToCubicMeters()} {knownGoodValueTimestamp:O}");

                FlowRate? flowRate = null;
                var now = time.GetUtcNow();
                if (previouslyReported.Count > 2)
                {
                    var timeDiff = now - previouslyReported.Items[0].Item1;
                    if (timeDiff > TimeSpan.Zero)
                    {
                        flowRate = (total - previouslyReported.Items[0].Item2) / timeDiff;
                    }
                }

                goodValues.Post(new MeterReading(total, flowRate));

                previouslyReported.Add((now, total));
            }
        }
    }

/*    private bool IsGoodValue(Volume val)
    {

    }

    public async Task Stop()
    {
        await cancellationTokenSource.CancelAsync();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }

        await meter.Stop();
    }*/

    public string SerialNumber => meter.SerialNumber;
    public string Manufacturer => meter.Manufacturer;
    public string Model => meter.Model;

    private readonly BufferBlock<MeterReading> goodValues = new();

    public Task<MeterReading> GetNextValue(CancellationToken cancel)
    {
        return goodValues.ReceiveAsync(cancel);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var latest = storage.LatestGood;
        if (latest != null)
        {
            knownGoodValueTimestamp = storage.LatestGoodTimestamp;
            var floor = Math.Floor(latest.ToCubicMeters());
            knownInteger = Volume.FromCubicMeters(floor);
            knownGoodDecimals = latest - knownInteger;
        }

        return Process(stoppingToken);
    }
}
