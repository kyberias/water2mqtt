using Emgu.CV.Structure;

namespace water2mqtt;

public class CircleUtils
{
    public static List<CircleF> GetLargestConsistentCircles(List<CircleF> circles, int count,
        float minimumRadiusRatio = 0.9f)
    {
        var ordered = circles
            .Where(circle => circle.Radius > 0)
            .OrderByDescending(circle => circle.Radius)
            .ToList();

        List<CircleF>? bestGroup = null;
        var bestHorizontalSpan = 0.0;
        var bestAverageRadius = 0.0;

        for (var start = 0; start <= ordered.Count - count; start++)
        {
            var group = ordered.Skip(start).Take(count).ToList();
            if (group[^1].Radius < group[0].Radius * minimumRadiusRatio || DoAnyCirclesOverlap(group))
            {
                continue;
            }

            var horizontalSpan = group.Max(circle => circle.Center.X) - group.Min(circle => circle.Center.X);
            var averageRadius = group.Average(circle => circle.Radius);
            if (horizontalSpan > bestHorizontalSpan ||
                horizontalSpan == bestHorizontalSpan && averageRadius > bestAverageRadius)
            {
                bestHorizontalSpan = horizontalSpan;
                bestAverageRadius = averageRadius;
                bestGroup = group;
            }
        }

        return bestGroup?.OrderBy(circle => circle.Center.X).ToList() ?? [];
    }

    public static bool DoAnyCirclesOverlap(List<CircleF> circles)
    {
        for (int i = 0; i < circles.Count; i++)
        {
            for (int j = i + 1; j < circles.Count; j++)
            {
                if (IsOverlapping(circles[i], circles[j]))
                {
                    return true; // Found overlapping circles
                }
            }
        }
        return false; // No overlapping circles found
    }

    private static bool IsOverlapping(CircleF c1, CircleF c2)
    {
        float dx = c1.Center.X - c2.Center.X;
        float dy = c1.Center.Y - c2.Center.Y;
        float distanceSquared = dx * dx + dy * dy;
        float radiusSum = c1.Radius + c2.Radius;

        return distanceSquared < (radiusSum * radiusSum);
    }
}