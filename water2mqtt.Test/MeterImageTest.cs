using System.Globalization;
using Neovolve.Logging.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace water2mqtt.Test;

public class MeterImageTest
{
    private readonly Microsoft.Extensions.Logging.ILogger<ZennerMnk> logger;

    public MeterImageTest(ITestOutputHelper output)
    {
        logger = output.BuildLoggerFor<ZennerMnk>();
    }

    [Fact]
    public void ReadsDecimalDigitsFromImagesNamedWithExpectedReading()
    {
        var imageDirectory = Path.Combine(AppContext.BaseDirectory, "testimages");
        var imagePaths = Directory.GetFiles(imageDirectory, "*.jpg");

        Assert.NotEmpty(imagePaths);

        foreach (var imagePath in imagePaths)
        {
            var expectedReading = decimal.Parse(
                Path.GetFileNameWithoutExtension(imagePath).Replace(',', '.'), CultureInfo.InvariantCulture);
            var expectedDecimals = expectedReading - decimal.Truncate(expectedReading);

            var actualDecimals = ZennerMnk.ReadDecimalsFromImage(imagePath, 270, logger);

            Assert.True(actualDecimals == expectedDecimals,
                $"{Path.GetFileName(imagePath)}: expected {expectedDecimals}, got {actualDecimals}");
        }
    }
}