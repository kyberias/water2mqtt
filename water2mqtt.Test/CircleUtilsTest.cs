using System.Drawing;
using Emgu.CV.Structure;
using Xunit;

namespace water2mqtt.Test;

public class CircleUtilsTest
{
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