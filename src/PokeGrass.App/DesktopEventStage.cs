using System.Drawing;
using System.Windows;
using Point = System.Windows.Point;

namespace PokeGrass;

/// <summary>Finds a nearby event area that does not overlap the garden window.</summary>
internal static class DesktopEventStage
{
    internal static Point? FindClearCenter(Point preferred, double width, double height,
        Rectangle garden, Rectangle workArea, double margin = 24d)
    {
        if (garden.IsEmpty || width <= 0d || height <= 0d) return preferred;
        var preferredBounds = CenteredBounds(preferred, width, height);
        if (!preferredBounds.IntersectsWith(garden)) return preferred;

        var obstacle = RectangleF.FromLTRB(garden.Left - (float)margin, garden.Top - (float)margin,
            garden.Right + (float)margin, garden.Bottom + (float)margin);
        var minX = workArea.Left + width / 2d;
        var maxX = workArea.Right - width / 2d;
        var minY = workArea.Top + height / 2d;
        var maxY = workArea.Bottom - height / 2d;
        if (minX > maxX || minY > maxY) return null;

        var candidates = new[]
        {
            new Point(Math.Clamp(preferred.X, minX, maxX), obstacle.Top - height / 2d),
            new Point(Math.Clamp(preferred.X, minX, maxX), obstacle.Bottom + height / 2d),
            new Point(obstacle.Left - width / 2d, Math.Clamp(preferred.Y, minY, maxY)),
            new Point(obstacle.Right + width / 2d, Math.Clamp(preferred.Y, minY, maxY)),
            new Point(obstacle.Left - width / 2d, obstacle.Top - height / 2d),
            new Point(obstacle.Right + width / 2d, obstacle.Top - height / 2d),
            new Point(obstacle.Left - width / 2d, obstacle.Bottom + height / 2d),
            new Point(obstacle.Right + width / 2d, obstacle.Bottom + height / 2d)
        };

        return candidates
            .Where(center => IsInsideWorkArea(center, width, height, workArea))
            .Where(center => !CenteredBounds(center, width, height).IntersectsWith(garden))
            .OrderBy(center => (center - preferred).LengthSquared)
            .Select(center => (Point?)center)
            .FirstOrDefault();
    }

    private static RectangleF CenteredBounds(Point center, double width, double height) =>
        new((float)(center.X - width / 2d), (float)(center.Y - height / 2d), (float)width, (float)height);

    private static bool IsInsideWorkArea(Point center, double width, double height, Rectangle area) =>
        center.X - width / 2d >= area.Left && center.X + width / 2d <= area.Right
        && center.Y - height / 2d >= area.Top && center.Y + height / 2d <= area.Bottom;
}
