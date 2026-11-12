using System.Drawing;
using Emgu.CV.Structure;
using Xunit;

namespace water2mqtt.Test;

public class CircleUtilsTest
{
    [Fact]
    public void SelectsCirclesThatFitOrderedMeterGeometryAndIgnoresShiftedDetection()
    {
        var modelPoints = new[]
        {
            new PointF(1503, 480),
            new PointF(1647, 601),
            new PointF(1699, 781),
            new PointF(1593, 1014)
        };
        const double scale = 0.3;
        const double rotation = 0.4;
        var expectedInModelOrder = modelPoints.Select(point =>
        {
            var x = point.X * scale;
            var y = point.Y * scale;
            return new CircleF(new PointF(
                (float)(500 + x * Math.Cos(rotation) - y * Math.Sin(rotation)),
                (float)(300 + x * Math.Sin(rotation) + y * Math.Cos(rotation))), 10);
        }).ToArray();
        var detections = expectedInModelOrder
            .Concat(new[]
            {
                new CircleF(new PointF(800, 100), 10),
                new CircleF(new PointF(expectedInModelOrder[0].Center.X + 4,
                    expectedInModelOrder[0].Center.Y - 3), 22)
            })
            .ToList();

        var selected = CircleUtils.GetCirclesMatchingGeometry(detections, modelPoints, 1);

        Assert.Equal(4, selected.Count);
        for (var index = 0; index < selected.Count; index++)
        {
            Assert.Equal(expectedInModelOrder[^(index + 1)].Center, selected[index].Center);
        }
    }

    [Fact]
    public void RejectsGeometryMatchWithOneMuchSmallerCircle()
    {
        var modelPoints = new[]
        {
            new PointF(1503, 480),
            new PointF(1647, 601),
            new PointF(1699, 781),
            new PointF(1593, 1014)
        };
        var circles = modelPoints.Select((point, index) =>
            new CircleF(new PointF(point.X * 0.3f + 50, point.Y * 0.3f + 30), index == 0 ? 22 : 30)).ToList();

        var selected = CircleUtils.GetCirclesMatchingGeometry(circles, modelPoints, 1);

        Assert.Empty(selected);
    }

    [Fact]
    public void SelectsFourSimilarCirclesAndIgnoresSmallerDetection()
    {
        var circles = new List<CircleF>
        {
            new(new PointF(10, 10), 30),
            new(new PointF(80, 10), 30),
            new(new PointF(150, 10), 30),
            new(new PointF(220, 10), 30),
            new(new PointF(290, 10), 15)
        };

        var selected = CircleUtils.GetLargestConsistentCircles(circles, 4);

        Assert.Equal(4, selected.Count);
        Assert.All(selected, circle => Assert.Equal(30, circle.Radius));
    }

    [Fact]
    public void RejectsFourCirclesWhenOneIsTooSmall()
    {
        var circles = new List<CircleF>
        {
            new(new PointF(10, 10), 30),
            new(new PointF(80, 10), 30),
            new(new PointF(150, 10), 30),
            new(new PointF(220, 10), 15)
        };

        var selected = CircleUtils.GetLargestConsistentCircles(circles, 4);

        Assert.Empty(selected);
    }
}