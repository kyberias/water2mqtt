using System.Drawing;
using Emgu.CV.Structure;

namespace water2mqtt;

public class CircleUtils
{
    public static List<CircleF> GetCirclesMatchingGeometry(List<CircleF> circles,
        IReadOnlyList<PointF> modelPoints, float maximumRmsError, float minimumRadiusRatio = 0.75f)
    {
        if (modelPoints.Count == 0)
        {
            return [];
        }

        var candidates = circles
            .Where(circle => circle.Radius > 0)
            .OrderByDescending(circle => circle.Radius)
            .Take(20)
            .ToList();
        if (candidates.Count < modelPoints.Count)
        {
            return [];
        }

        var modelCenterX = modelPoints.Average(point => point.X);
        var modelCenterY = modelPoints.Average(point => point.Y);
        var modelVariance = modelPoints.Sum(point =>
            Math.Pow(point.X - modelCenterX, 2) + Math.Pow(point.Y - modelCenterY, 2));
        if (modelVariance == 0)
        {
            return [];
        }

        var assigned = new CircleF[modelPoints.Count];
        var used = new bool[candidates.Count];
        CircleF[]? bestMatch = null;
        var bestError = double.PositiveInfinity;
        var bestRadiusRatio = 0.0;

        void Search(int modelIndex)
        {
            if (modelIndex == modelPoints.Count)
            {
                var minRadius = assigned.Min(circle => circle.Radius);
                var maxRadius = assigned.Max(circle => circle.Radius);
                var radiusRatio = minRadius / maxRadius;
                if (radiusRatio < minimumRadiusRatio || DoAnyCirclesOverlap(assigned.ToList()))
                {
                    return;
                }

                var observedCenterX = assigned.Average(circle => circle.Center.X);
                var observedCenterY = assigned.Average(circle => circle.Center.Y);
                var coefficientA = 0.0;
                var coefficientB = 0.0;

                for (var index = 0; index < modelPoints.Count; index++)
                {
                    var modelX = modelPoints[index].X - modelCenterX;
                    var modelY = modelPoints[index].Y - modelCenterY;
                    var observedX = assigned[index].Center.X - observedCenterX;
                    var observedY = assigned[index].Center.Y - observedCenterY;
                    coefficientA += modelX * observedX + modelY * observedY;
                    coefficientB += modelX * observedY - modelY * observedX;
                }

                coefficientA /= modelVariance;
                coefficientB /= modelVariance;

                var squaredError = 0.0;
                for (var index = 0; index < modelPoints.Count; index++)
                {
                    var modelX = modelPoints[index].X - modelCenterX;
                    var modelY = modelPoints[index].Y - modelCenterY;
                    var fittedX = observedCenterX + coefficientA * modelX - coefficientB * modelY;
                    var fittedY = observedCenterY + coefficientB * modelX + coefficientA * modelY;
                    squaredError += Math.Pow(assigned[index].Center.X - fittedX, 2) +
                                    Math.Pow(assigned[index].Center.Y - fittedY, 2);
                }

                var rmsError = Math.Sqrt(squaredError / modelPoints.Count);
                if (rmsError <= maximumRmsError &&
                    (radiusRatio > bestRadiusRatio || radiusRatio == bestRadiusRatio && rmsError < bestError))
                {
                    bestError = rmsError;
                    bestRadiusRatio = radiusRatio;
                    bestMatch = assigned.ToArray();
                }

                return;
            }

            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                if (used[candidateIndex])
                {
                    continue;
                }

                assigned[modelIndex] = candidates[candidateIndex];
                used[candidateIndex] = true;
                Search(modelIndex + 1);
                used[candidateIndex] = false;
            }
        }

        Search(0);
        // The model is ordered from tenths to ten-thousandths; angle conversion expects the reverse.
        return bestMatch?.Reverse().ToList() ?? [];
    }

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