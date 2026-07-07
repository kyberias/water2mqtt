using Emgu.CV.Structure;

namespace water2mqtt;

public class CircleUtils
{
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