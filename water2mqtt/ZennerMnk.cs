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

    private BufferBlock<Volume> values = new();

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

            CvInvoke.Imwrite("original.jpg", original);

            var rotated = new Mat(new Size(original.Height, original.Width), original.Depth,
                original.NumberOfChannels);

            var rotationMatrix = new Mat(new Size(2, 2), DepthType.Cv32F, 1);
            CvInvoke.GetRotationMatrix2D(new PointF(original.Width / 2, original.Height / 2), rotateAngle, 1,
                rotationMatrix);
            CvInvoke.WarpAffine(original, rotated, rotationMatrix, original.Size);

            CvInvoke.Imwrite("rotated.jpg", rotated);

            var gray = new Mat(new Size(rotated.Width, rotated.Height), rotated.Depth, rotated.NumberOfChannels);
            CvInvoke.CvtColor(rotated, gray, ColorConversion.Rgb2Gray);
            CvInvoke.Imwrite("gray.jpg", gray);

            var param1 = 100; // Upper threshold for the internal Canny edge detector.
            var param2 = 50; // Threshold for center detection.

            var meterCircles = CvInvoke.HoughCircles(gray, HoughModes.Gradient, 1, 10, param1, param2, 50, 100);

            var now = DateTimeOffset.UtcNow;

            var mainCircles = meterCircles.Take(4).OrderBy(c => c.Center.X).ToList();

            if (mainCircles.Count < 4)
            {
                log.LogWarning("Meter circles not found.");
                continue;
            }

            if (CircleUtils.DoAnyCirclesOverlap(mainCircles))
            {
                log.LogWarning("Circles overlap.");
                continue;
            }

            var angles = new List<double>();

            int i = 0;
            foreach (var circle in mainCircles)
            {
                var width = (int)(circle.Radius + 5) * 2;

                var rect = new Rectangle(
                    new Point((int)(circle.Center.X - width / 2), (int)(circle.Center.Y - width / 2)),
                    new Size(width, width));

                using var cropped = new Mat(rotated, rect);
                //CvInvoke.Imwrite($"decCaptures\\{now.ToString("O").Replace(":", ".")}_{i}.jpg", cropped);

                // Calculate the center of the image
                double imageCenterX = cropped.Width / 2.0;
                double imageCenterY = cropped.Height / 2.0;

                var croppedWithBlackBorder = MaskWithBlack(cropped, (int)(0.85 * cropped.Width / 2.0));

                CvInvoke.Imwrite($"withBorder{i}.jpg", croppedWithBlackBorder);

                CvInvoke.Circle(croppedWithBlackBorder, new Point((int)imageCenterX, (int)imageCenterY),
                    (int)(0.6 * cropped.Width / 2.0), new MCvScalar(0, 0, 0), -1);

                //CvInvoke.Circle(image, new System.Drawing.Point(imageCenterX, imageCenterY), radius, new MCvScalar(0, 0, 0), -1);

                CvInvoke.Imwrite($"withCircle{i}.jpg", croppedWithBlackBorder);

                var hsv = new Mat();
                CvInvoke.CvtColor(croppedWithBlackBorder, hsv, ColorConversion.Bgr2Hsv);

                var lowerRed1 = new ScalarArray(new MCvScalar(0, 50, 50));
                var upperRed1 = new ScalarArray(new MCvScalar(10, 255, 255));
                var lowerRed2 = new ScalarArray(new MCvScalar(170, 50, 50));
                var upperRed2 = new ScalarArray(new MCvScalar(180, 255, 255));

                // Create masks for red
                Mat mask1 = new Mat();
                Mat mask2 = new Mat();
                CvInvoke.InRange(hsv, lowerRed1, upperRed1, mask1);
                CvInvoke.InRange(hsv, lowerRed2, upperRed2, mask2);

                // Combine the masks
                Mat redMask = new Mat();
                CvInvoke.BitwiseOr(mask1, mask2, redMask);

                CvInvoke.Imwrite($"redmask{i}.jpg", redMask);

                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                {
                    Mat hierarchy = new Mat();
                    CvInvoke.FindContours(redMask, contours, hierarchy, RetrType.External,
                        ChainApproxMethod.ChainApproxSimple);

                    if (contours.Length > 0)
                    {
                        var contourList = Enumerable.Range(0, contours.Size)
                            .Select(i => new
                                { Index = i, Contour = contours[i], Area = CvInvoke.ContourArea(contours[i]) })
                            .ToList();

                        var largestContour = contourList
                            .OrderByDescending(c => c.Area)
                            .FirstOrDefault();

                        if (largestContour != null)
                        {
                            var moments = CvInvoke.Moments(largestContour.Contour);
                            double contourMidX = moments.M10 / moments.M00;
                            double contourMidY = moments.M01 / moments.M00;

                            // Compute the angle in radians
                            double angleRadians = Math.Atan2(contourMidY - imageCenterY, contourMidX - imageCenterX);

                            // Convert to degrees
                            double angleDegrees = (angleRadians * (180.0 / Math.PI) + 90 + 360) % 360;
                            angles.Add(angleDegrees);

                            log.LogTrace($"{i} Angle {angleDegrees}");

                            // Draw the line from the center of the image to the midpoint of the contour
                            //CvInvoke.Line(cropped, new System.Drawing.Point((int)imageCenterX, (int)imageCenterY),
                            //  new System.Drawing.Point((int)contourMidX, (int)contourMidY), new MCvScalar(255, 0, 0), 2);
                        }
                    }
                    else
                    {
                        log.LogWarning("Red contours not found.");
                    }
                }

                i++;
            }

            foreach (var circle in mainCircles)
            {
                CvInvoke.Circle(rotated, new Point((int)circle.Center.X, (int)circle.Center.Y), (int)circle.Radius,
                    new MCvScalar(0, 0, 255), 2);
            }

            CvInvoke.Imwrite("output.png", rotated);

            decimal? proposedDecimals = AnglesToDecimals(angles);

            if (proposedDecimals != null)
            {
                log.LogTrace($"Proposed decimals: {proposedDecimals}");

                values.Post(Volume.FromCubicMeters(proposedDecimals.Value));
            }

            log.LogTrace($"Analysis time: {DateTime.UtcNow - startOfAnalysis}");

            var sleepDelay = TimeSpan.FromSeconds(1);

            while ((DateTimeOffset.UtcNow - now) < sleepDelay && !cancel.IsCancellationRequested)
            {
                await Task.Yield();
                // Read and ignore frames while we wait.
                videoCapture.Read(original);
            }
        }
    }

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

    Task<Volume> IWaterMeterRaw.GetNextValue(CancellationToken cancel)
    {
        return values.ReceiveAsync(cancel);
    }
}