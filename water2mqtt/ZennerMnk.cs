using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Emgu.CV;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace water2mqtt;

public class ZennerMnk : BackgroundService, IWaterMeterRaw
{
    private readonly ILogger<ZennerMnk> log;
    private readonly IConfiguration config;

    private Task? task;

    public string SerialNumber => "8ZRI1714922058";
    public string Manufacturer => "Zenner";
    public string Model => "MNK";

    private BufferBlock<RawMeterReading> values = new();

    public static decimal? AnglesToDecimals(IList<double> angles)
    {
        var decimalEstimates = angles.Select(a => a / (360 / 10)).ToArray();
        // 0 = 0
        // 36 = 1
        // 72 = 2
        // ...
        // 288 = 8
        // 324 = 9
        // 

        if (decimalEstimates.Length == 4)
        {
            var u = new[] { 0.0001m, 0.001m, 0.01m, 0.1m };

            if (Math.Ceiling(decimalEstimates[0]) - decimalEstimates[0] < 0.02)
            {
                decimalEstimates[0] = Math.Ceiling(decimalEstimates[0]);
            }

            for (int i = 1; i <= 3; i++)
            {
                var prev = decimalEstimates[i - 1];

                var curr = decimalEstimates[i];

                var ceil = Math.Ceiling(curr);

                if (ceil - curr < 0.15 && prev < 4)
                {
                    decimalEstimates[i] = Math.Ceiling(curr);
                }
                else if (curr - Math.Floor(curr) < 0.1 && prev > 8)
                {
                    decimalEstimates[i] = curr - 1;
                }

            }

            return decimalEstimates.Select(p => (int)p).Select((d, ix) => d * u[ix]).Sum();
        }

        return null;
    }

    public static decimal? ReadDecimalsFromImage(string imagePath, double rotateAngle,
        ILogger<ZennerMnk>? logger = null)
    {
        using var image = CvInvoke.Imread(imagePath);
        if (image.IsEmpty)
        {
            throw new InvalidDataException($"Could not read meter image: {imagePath}");
        }

        return AnalyzeImage(image, rotateAngle, logger, false)?.Decimals;
    }

    public ZennerMnk(ILogger<ZennerMnk> log, IConfiguration config)
    {
        this.log = log;
        this.config = config;
    }

    async Task Run(Volume? initialKnownGood, /*string[] args, */CancellationToken cancel)
    {
        Volume? knownInteger = null;
        Volume? knownGoodValue = initialKnownGood;
        DateTimeOffset knownGoodValueTimestamp = default(DateTimeOffset);

        /*else if (args.Length == 1 && decimal.TryParse(args[0], out var parsedResult))
            {
                knownGoodValue = parsedResult;
            }*/

/*        if (knownGoodValue == null && File.Exists("knowngood.txt"))
        {
            var knownTxt = File.ReadAllText("knowngood.txt");
            var parts = knownTxt.Split();
            var parsed = decimal.Parse(parts[0]);

            if (parsed > 1)
            {
                knownInteger = Volume.FromCubicMeters(Math.Floor(parsed));
            }

            knownGoodValue = Volume.FromCubicMeters(parsed - Math.Floor(parsed));
            knownGoodValueTimestamp = DateTimeOffset.Parse(parts[1]);
        }*/

        var initialTotal = knownInteger + knownGoodValue;
        log.LogInformation($"Initial known good {initialTotal}");

        /*if (initialTotal > Volume.FromCubicMeters(1))
        {
            values.Post(initialTotal);
        }*/

        string? source = config["VideoSource"];
        string? rotate = config["RotateAngle"];
        if (rotate == null)
        {
            throw new Exception("Rotate angle not configured.");
        }

        var rotateAngle = double.Parse(rotate, CultureInfo.InvariantCulture);

        if (source == null)
        {
            throw new Exception("Video source not configured.");
        }

        var videoCapture = new VideoCapture(source);

        if (!videoCapture.IsOpened)
        {
            throw new Exception("Camera not opened");
        }

        Volume? tooLarge = null;
        DateTime? tooLargeTimeStamp = null;

        while (!cancel.IsCancellationRequested)
        {
            using Mat original = new Mat();

            var readStart = DateTimeOffset.UtcNow;
            var retryTime = TimeSpan.FromMinutes(1);

            while (!videoCapture.Read(original) && !cancel.IsCancellationRequested)
            {
                /*if (fps > 0)
                    {
                        Console.WriteLine("Read failed.");
                        return;
                    }*/
                Console.WriteLine("Read failed. Retrying...");
                await Task.Delay(TimeSpan.FromSeconds(0.1), cancel);

                if (DateTimeOffset.UtcNow - readStart > retryTime)
                {
                    Console.WriteLine("Restart capture device");
                    videoCapture = new VideoCapture(source);
                    readStart = DateTimeOffset.UtcNow;
                }
            }

            var startOfAnalysis = DateTime.UtcNow;

            var analysis = AnalyzeImage(original, rotateAngle, log, true);

            if (analysis?.Decimals is decimal proposedDecimals)
            {
                log.LogTrace($"Proposed decimals: {proposedDecimals}");

                values.Post(new RawMeterReading(
                    Volume.FromCubicMeters(proposedDecimals),
                    analysis.ImageJpeg));
            }

            log.LogTrace($"Analysis time: {DateTime.UtcNow - startOfAnalysis}");

            var sleepDelay = TimeSpan.FromSeconds(1);

            while (DateTime.UtcNow - startOfAnalysis < sleepDelay && !cancel.IsCancellationRequested)
            {
                await Task.Yield();
                // Read and ignore frames while we wait.
                videoCapture.Read(original);
            }
        }
    }

    private static ImageAnalysis? AnalyzeImage(Mat original, double rotateAngle, ILogger<ZennerMnk>? logger,
        bool writeDebugImages)
    {
        if (writeDebugImages)
        {
            CvInvoke.Imwrite("original.jpg", original);
        }

        using var rotated = new Mat();
        using var rotationMatrix = new Mat(new Size(2, 3), DepthType.Cv32F, 1);
        CvInvoke.GetRotationMatrix2D(new PointF(original.Width / 2, original.Height / 2), rotateAngle, 1,
            rotationMatrix);
        CvInvoke.WarpAffine(original, rotated, rotationMatrix, original.Size);

        if (writeDebugImages)
        {
            CvInvoke.Imwrite("rotated.jpg", rotated);
        }

        using var gray = new Mat();
        CvInvoke.CvtColor(rotated, gray, ColorConversion.Bgr2Gray);
        if (writeDebugImages)
        {
            CvInvoke.Imwrite("gray.jpg", gray);
        }

        var meterCircles = CvInvoke.HoughCircles(gray, HoughModes.Gradient, 1, 10, 100, 50, 25, 100);
        var mainCircles = CircleUtils.GetLargestConsistentCircles(meterCircles.ToList(), 4);
        logger?.LogTrace("Hough circles: {Circles}; selected {SelectedCircles}.",
            string.Join(";", meterCircles.Select(circle =>
                $"{circle.Center.X:F1},{circle.Center.Y:F1} r{circle.Radius:F1}")),
            string.Join(";", mainCircles.Select(circle =>
                $"{circle.Center.X:F1},{circle.Center.Y:F1} r{circle.Radius:F1}")));

        if (mainCircles.Count < 4 || CircleUtils.DoAnyCirclesOverlap(mainCircles))
        {
            logger?.LogWarning("Meter circles not found or overlap.");
            return null;
        }

        var angles = new List<double>();
        for (var index = 0; index < mainCircles.Count; index++)
        {
            var circle = mainCircles[index];
            var width = (int)(circle.Radius + 5) * 2;
            var rect = new Rectangle(
                new Point((int)(circle.Center.X - width / 2), (int)(circle.Center.Y - width / 2)),
                new Size(width, width));

            using var cropped = new Mat(rotated, rect);
            var imageCenterX = cropped.Width / 2.0;
            var imageCenterY = cropped.Height / 2.0;
            using var croppedWithBlackBorder = MaskWithBlack(cropped, (int)(0.85 * cropped.Width / 2.0));

            if (writeDebugImages)
            {
                CvInvoke.Imwrite($"withBorder{index}.jpg", croppedWithBlackBorder);
            }

            CvInvoke.Circle(croppedWithBlackBorder, new Point((int)imageCenterX, (int)imageCenterY),
                (int)(0.6 * cropped.Width / 2.0), new MCvScalar(0, 0, 0), -1);

            if (writeDebugImages)
            {
                CvInvoke.Imwrite($"withCircle{index}.jpg", croppedWithBlackBorder);
            }

            using var hsv = new Mat();
            CvInvoke.CvtColor(croppedWithBlackBorder, hsv, ColorConversion.Bgr2Hsv);
            using var mask1 = new Mat();
            using var mask2 = new Mat();
            CvInvoke.InRange(hsv, new ScalarArray(new MCvScalar(0, 50, 50)),
                new ScalarArray(new MCvScalar(10, 255, 255)), mask1);
            CvInvoke.InRange(hsv, new ScalarArray(new MCvScalar(170, 50, 50)),
                new ScalarArray(new MCvScalar(180, 255, 255)), mask2);
            using var redMask = new Mat();
            CvInvoke.BitwiseOr(mask1, mask2, redMask);

            if (writeDebugImages)
            {
                CvInvoke.Imwrite($"redmask{index}.jpg", redMask);
            }

            using var contours = new VectorOfVectorOfPoint();
            using var hierarchy = new Mat();
            CvInvoke.FindContours(redMask, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);

            if (contours.Length == 0)
            {
                logger?.LogWarning("Red contours not found.");
                continue;
            }

            var largestContour = Enumerable.Range(0, contours.Size)
                .Select(contourIndex => contours[contourIndex])
                .OrderByDescending(contour => CvInvoke.ContourArea(contour))
                .First();
            var moments = CvInvoke.Moments(largestContour);
            if (moments.M00 == 0)
            {
                continue;
            }

            var contourMidX = moments.M10 / moments.M00;
            var contourMidY = moments.M01 / moments.M00;
            var angleRadians = Math.Atan2(contourMidY - imageCenterY, contourMidX - imageCenterX);
            var angleDegrees = (angleRadians * (180.0 / Math.PI) + 90 + 360) % 360;
            angles.Add(angleDegrees);
            logger?.LogTrace("{Index} Angle {AngleDegrees}", index, angleDegrees);
        }

        foreach (var circle in mainCircles)
        {
            CvInvoke.Circle(rotated, new Point((int)circle.Center.X, (int)circle.Center.Y), (int)circle.Radius,
                new MCvScalar(0, 0, 255), 2);
        }

        logger?.LogTrace("Found {AngleCount} needle angles.", angles.Count);

        if (writeDebugImages)
        {
            CvInvoke.Imwrite("output.png", rotated);
        }

        return new ImageAnalysis(AnglesToDecimals(angles), CvInvoke.Imencode(".jpg", rotated));
    }

    private sealed record ImageAnalysis(decimal? Decimals, byte[] ImageJpeg);

    static Mat MaskWithBlack(Mat originalImage, int radius)
    {
        // Create a black image (background) for the output
        Mat outputImage = new Mat(originalImage.Size, DepthType.Cv8U, 3);
        outputImage.SetTo(new MCvScalar(255, 255, 255)); // White background

        // Create a mask with the same size as the original image
        Mat mask = new Mat(originalImage.Size, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0)); // Set mask to black (0)

        // Define the circle parameters
        Point center = new Point(originalImage.Width / 2, originalImage.Height / 2); // Center of the image

        // Draw a white filled circle on the mask
        CvInvoke.Circle(mask, center, radius, new MCvScalar(255, 255, 255), -1);

        // Use the mask to copy the circular region from the original image
        originalImage.CopyTo(outputImage, mask);

        return outputImage;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Run(null, stoppingToken);
    }

    Task<RawMeterReading> IWaterMeterRaw.GetNextValue(CancellationToken cancel)
    {
        return values.ReceiveAsync(cancel);
    }
}