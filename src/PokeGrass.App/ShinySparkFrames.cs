using System.Windows.Media.Imaging;

namespace PokeGrass;

internal static class ShinySparkFrames
{
    internal const int Count = 16;
    internal const int FrameMilliseconds = 40;

    internal static IReadOnlyList<BitmapSource> Load(Func<string, BitmapSource> load)
    {
        // The source sheet is 2172×724: 8 columns × 2 rows, read left-to-right
        // and top-to-bottom. Source columns alternate between 271 and 272px;
        // extracted frames are padded to 272×362 without resampling.
        var frames = new BitmapSource[Count];
        for (var index = 0; index < frames.Length; index++)
            frames[index] = load($"ui/effects/shiny_sparks/frame-{index:D2}.png");
        return frames;
    }
}
