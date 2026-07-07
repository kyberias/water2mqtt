using System.Diagnostics;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace water2mqtt.Test;

public class ErrorCorrectingProxyTest
{
    private readonly ErrorCorrectingProxy proxy;
    private Volume initial = Volume.FromCubicMeters(1024.2355m);
    private DateTimeOffset initialTime = new (2025, 2, 9, 1, 2, 3, TimeSpan.Zero);
    private Mock<IReadingStorage> storage = new();
    private Mock<TimeProvider> time = new();
    private BufferBlock<Volume> meterReadings = new();
    private ITestOutputHelper output;

    public ErrorCorrectingProxyTest(ITestOutputHelper output)
    {
        this.output = output;
        var meter = new Mock<IWaterMeterRaw>();

        SetupInitial(initial);

        meter.Setup(m => m.GetNextValue(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken cancel) => meterReadings.ReceiveAsync(cancel));

        time.Setup(t => t.GetUtcNow()).Returns(initialTime);

        var log = output.BuildLoggerFor<ErrorCorrectingProxy>();

        proxy = new ErrorCorrectingProxy(meter.Object, time.Object, log, storage.Object);
    }

    private TimeSpan DefaultTimeout => Debugger.IsAttached ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(5);

    void SetupInitial(Volume init)
    {
        storage.Setup(s => s.LatestGood).Returns(init);
        storage.Setup(s => s.LatestGoodTimestamp).Returns(initialTime);
    }

    [Fact]
    public async Task CanBeStoppedAndStarted()
    {
        using var cancel = new CancellationTokenSource(DefaultTimeout);

        await proxy.StartAsync(cancel.Token);
        await proxy.StopAsync(cancel.Token);
    }

    [Fact]
    public async Task ShouldGiveInitialValue()
    {
        using var cancel = new CancellationTokenSource(DefaultTimeout);

        await proxy.StartAsync(cancel.Token);

        var val = await proxy.GetNextValue(cancel.Token);

        Assert.Equal(initial, val.Volume);

        await proxy.StopAsync(cancel.Token);
    }

    [Theory]
    [InlineData(1.0, 0.001)]
    public async Task PositiveChangeReported(double start, double next)
    {
        using var cancel = new CancellationTokenSource(DefaultTimeout);

        storage.Setup(s => s.LatestGood).Returns(Volume.FromCubicMeters((decimal)start));

        await proxy.StartAsync(cancel.Token);

        Assert.Equal((decimal)start, (await proxy.GetNextValue(cancel.Token)).Volume.ToCubicMeters());

        AdvanceTime(TimeSpan.FromMinutes(1));
        meterReadings.Post(Volume.FromCubicMeters((decimal)next));

        Assert.Equal((decimal)(start+next), (await proxy.GetNextValue(cancel.Token)).Volume.ToCubicMeters());

        await proxy.StopAsync(cancel.Token);
    }

    [Theory]
    [InlineData(1.0, 0.000, 0)]
    public async Task ShouldReportFlowrate(double start, double next, double LperMin)
    {
        using var cancel = new CancellationTokenSource(DefaultTimeout);

        storage.Setup(s => s.LatestGood).Returns(Volume.FromCubicMeters((decimal)start));

        await proxy.StartAsync(cancel.Token);

        Assert.Equal((decimal)start, (await proxy.GetNextValue(cancel.Token)).Volume.ToCubicMeters());
        AdvanceTime(TimeSpan.FromMinutes(1));
        meterReadings.Post(Volume.FromCubicMeters((decimal)start));

        Assert.Equal((decimal)start, (await proxy.GetNextValue(cancel.Token)).Volume.ToCubicMeters());
        AdvanceTime(TimeSpan.FromMinutes(1));
        meterReadings.Post(Volume.FromCubicMeters((decimal)start));
        Assert.Equal((decimal)start, (await proxy.GetNextValue(cancel.Token)).Volume.ToCubicMeters());
        AdvanceTime(TimeSpan.FromMinutes(1));
        meterReadings.Post(Volume.FromCubicMeters((decimal)start));
        Assert.Equal((decimal)start, (await proxy.GetNextValue(cancel.Token)).Volume.ToCubicMeters());

        AdvanceTime(TimeSpan.FromMinutes(1));
        meterReadings.Post(Volume.FromCubicMeters((decimal)next));

        var nextValue = await proxy.GetNextValue(cancel.Token);

        Assert.Equal((decimal)(start+next), nextValue.Volume.ToCubicMeters());
        Assert.Equal((decimal)LperMin, nextValue.FlowRate!.ToLitersPerMinute());

        await proxy.StopAsync(cancel.Token);
    }

    void AdvanceTime(TimeSpan bySpan)
    {
        var now = time.Object.GetUtcNow();
        time.Setup(t => t.GetUtcNow()).Returns(now + bySpan);
    }

    [Theory]
    [InlineData(1.0, 0.9, 0.001)]
    public async Task TooLargeChangeNotReported(double start, double ignored, double nextGood)
    {
        using var cancel = new CancellationTokenSource(DefaultTimeout);

        storage.Setup(s => s.LatestGood).Returns(Volume.FromCubicMeters((decimal)start));

        await proxy.StartAsync(cancel.Token);

        Assert.Equal((decimal)start, (await proxy.GetNextValue(cancel.Token)).Volume.ToCubicMeters());

        AdvanceTime(TimeSpan.FromMinutes(10));

        meterReadings.Post(Volume.FromCubicMeters((decimal)ignored));
        meterReadings.Post(Volume.FromCubicMeters((decimal)nextGood));

        Assert.Equal((decimal)start, (await proxy.GetNextValue(cancel.Token)).Volume.ToCubicMeters());
        Assert.Equal((decimal)1.001, (await proxy.GetNextValue(cancel.Token)).Volume.ToCubicMeters());

        await proxy.StopAsync(cancel.Token);
    }

    async Task AssertNext(double expected, CancellationToken cancel)
    {
        Assert.Equal((decimal)expected, (await proxy.GetNextValue(cancel)).Volume.ToCubicMeters());
    }

    [Theory]
    [InlineData(0.9990, 0.0001, 1.0001 )]
    public async Task ShouldRollOverCubic(double start, double nextGood, double expected)
    {
        using var cancel = new CancellationTokenSource(DefaultTimeout);

        storage.Setup(s => s.LatestGood).Returns(Volume.FromCubicMeters((decimal)start));

        await proxy.StartAsync(cancel.Token);

        await AssertNext(start, cancel.Token);

        AdvanceTime(TimeSpan.FromMinutes(10));

        meterReadings.Post(Volume.FromCubicMeters((decimal)nextGood));

        await AssertNext(expected, cancel.Token);

        await proxy.StopAsync(cancel.Token);
    }

    [Theory]
    [InlineData(0.9990,
        new[] { 0.010, 0.020, 0.1000, 0.2000 },
        new[] { 1.010, 1.020, 1.1000, 1.2000 })]
    public async Task SequenceOfValues(double start, double[] nextGoods, double[] expecteds)
    {
        using var cancel = new CancellationTokenSource(DefaultTimeout);

        storage.Setup(s => s.LatestGood).Returns(Volume.FromCubicMeters((decimal)start));

        await proxy.StartAsync(cancel.Token);

        await AssertNext(start, cancel.Token);

        for (var i = 0; i < nextGoods.Length; i++)
        {
            AdvanceTime(TimeSpan.FromMinutes(60));

            var nextGood = nextGoods[i];
            var expected = expecteds[i];
            
            meterReadings.Post(Volume.FromCubicMeters((decimal)nextGood));
            await AssertNext(expected, cancel.Token);
        }

        await proxy.StopAsync(cancel.Token);
    }
}