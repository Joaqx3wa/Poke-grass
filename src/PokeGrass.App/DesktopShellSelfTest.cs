using System.Drawing;
using System.IO;

namespace PokeGrass;

internal static class DesktopShellSelfTest
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PokeGrass-instance-test", Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = SingleInstanceGate.TryAcquire(directory))
            {
                if (first is null) throw new InvalidDataException("First instance must acquire the lock.");
                using var second = SingleInstanceGate.TryAcquire(directory);
                if (second is not null) throw new InvalidDataException("Second instance must be rejected.");
            }
            using var restarted = SingleInstanceGate.TryAcquire(directory);
            if (restarted is null) throw new InvalidDataException("Restart must acquire the released lock.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        foreach (var area in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1280, -200, 1280, 984),
                     new Rectangle(1920, 0, 800, 560) })
        foreach (var scale in new[] { 1, 2, 3, 4 })
        foreach (var ratio in new[] { 1d, 1.25d, 1.5d, 2d })
        foreach (var atTop in new[] { false, true })
        {
            var garden = new Rectangle(area.Right - 128 * scale, atTop ? area.Top : area.Bottom - 80 * scale,
                128 * scale, 80 * scale);
            // Physical size is invariant under Windows DPI scaling.
            var width = (32 * scale / ratio) * ratio;
            var height = (16 * scale / ratio) * ratio;
            var point = GardenWindow.TimedIndicatorPosition(garden, area, scale, width, height);
            if (point.X < area.Left || point.X + width > area.Right || point.Y < area.Top || point.Y + height > area.Bottom
                || point.Y < garden.Top - GardenWindow.EffectBleedY * scale
                || point.Y + height > garden.Bottom + GardenWindow.EffectBleedY * scale)
                throw new InvalidDataException("Timed indicator must fit the monitor and garden canvas.");
            if (atTop && point.Y < garden.Bottom) throw new InvalidDataException("Top edge must move the timer below.");
            if (!atTop && point.Y >= garden.Top) throw new InvalidDataException("Bottom edge must keep the timer above.");
        }
    }
}
