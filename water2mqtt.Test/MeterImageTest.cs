using System.Globalization;
using Xunit;

namespace water2mqtt.Test;

public class MeterImageTest
{
    public static IEnumerable<object[]> ImageCases
    {
        get
        {
            var imageDirectory = Path.Combine(AppContext.BaseDirectory, "testimages");
            return Directory.GetFiles(imageDirectory, "*.jpg")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(imagePath =>
                {
                    var imageFileName = Path.GetFileName(imagePath);
                    var expectedReading = decimal.Parse(
                        Path.GetFileNameWithoutExtension(imageFileName).Replace(',', '.'), CultureInfo.InvariantCulture);
                    var expectedDecimals = expectedReading - decimal.Truncate(expectedReading);
                    return new object[] { imageFileName, expectedDecimals };
                });
        }
    }

    [Theory]
    [MemberData(nameof(ImageCases))]
    public void ReadsDecimalDigitsFromImage(string imageFileName, decimal expectedDecimals)
    {
        var imagePath = Path.Combine(AppContext.BaseDirectory, "testimages", imageFileName);
        var actualDecimals = ZennerMnk.ReadDecimalsFromImage(imagePath, 270);

        Assert.True(actualDecimals == expectedDecimals,
            $"{imageFileName}: expected {expectedDecimals}, got {actualDecimals}");
    }
}