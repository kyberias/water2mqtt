using Emgu.CV.Structure;

namespace water2mqtt;

public class CircleUtils
{
    public static List<CircleF> GetLargestConsistentCircles(List<CircleF> circles, int count,
        float minimumRadiusRatio = 0.85f)
    {
        var ordered = circles
            .Where(circle => circle.Radius > 0)
            .OrderByDescending(circle => circle.Radius)
            .Take(Math.Max(count, 20))
            .ToList();

        List<CircleF>? bestGroup = null;
        var bestRadiusRatio = 0.0;
        var bestHorizontalSpan = 0.0;
        var bestAverageRadius = 0.0;
        var candidateGroup = new List<CircleF>(count);

        void SearchGroups(int start)
        {
            if (candidateGroup.Count == count)
            {
                var minimumRadius = candidateGroup.Min(circle => circle.Radius);
                var maximumRadius = candidateGroup.Max(circle => circle.Radius);
                var radiusRatio = minimumRadius / maximumRadius;
                if (radiusRatio < minimumRadiusRatio || DoAnyCirclesOverlap(candidateGroup))
                {
                    return;
                }

                var horizontalSpan = candidateGroup.Max(circle => circle.Center.X) -
                                     candidateGroup.Min(circle => circle.Center.X);
                var averageRadius = candidateGroup.Average(circle => circle.Radius);
                if (radiusRatio > bestRadiusRatio ||
                    radiusRatio == bestRadiusRatio && averageRadius > bestAverageRadius ||
                    radiusRatio == bestRadiusRatio && averageRadius == bestAverageRadius &&
                    horizontalSpan > bestHorizontalSpan)
                {
                    bestRadiusRatio = radiusRatio;
                    bestAverageRadius = averageRadius;
                    bestHorizontalSpan = horizontalSpan;
                    bestGroup = candidateGroup.ToList();
                }

                return;
            }

            for (var index = start; index <= ordered.Count - (count - candidateGroup.Count); index++)
            {
                candidateGroup.Add(ordered[index]);
                SearchGroups(index + 1);
                candidateGroup.RemoveAt(candidateGroup.Count - 1);
            }
        }

        SearchGroups(0);
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