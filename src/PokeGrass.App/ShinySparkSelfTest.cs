using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeGrass;

internal static class ShinySparkSelfTest
{
    internal const string Summary = "Shiny sparks: source alpha and 16 extracted 272x362 frames verified; unique frames and 8x2 row-major resources checked.";

    internal static void Run()
    {
        var originalPath = Path.Combine(AppContext.BaseDirectory, "assets", "reference", "shiny_sparks.png");
        var original = Load(originalPath);
        if (original.PixelWidth != 2172 || original.PixelHeight != 724)
            throw new InvalidDataException($"Unexpected shiny sparks source dimensions: {original.PixelWidth}x{original.PixelHeight}.");
        ValidateAlpha(original, "original shiny_sparks.png");

        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var frames = ShinySparkFrames.Load(relative => Load(Path.Combine(AppContext.BaseDirectory, "assets", relative)));
        if (frames.Count != 16) throw new InvalidDataException($"Expected 16 shiny spark frames; found {frames.Count}.");
        foreach (var frame in frames)
        {
            if (frame.PixelWidth != 272 || frame.PixelHeight != 362)
                throw new InvalidDataException($"Shiny spark frames must be 272x362; found {frame.PixelWidth}x{frame.PixelHeight}.");
            ValidateAlpha(frame, "extracted shiny spark frame");
            var pixels = GetBgraPixels(frame);
            var hash = Convert.ToHexString(SHA256.HashData(pixels));
            if (!hashes.Add(hash)) throw new InvalidDataException("Two extracted shiny spark frames are identical.");
        }
    }

    private static BitmapSource Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void ValidateAlpha(BitmapSource source, string name)
    {
        var pixels = GetBgraPixels(source);
        var hasTransparent = false;
        var hasVisible = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            hasTransparent |= pixels[i] == 0;
            hasVisible |= pixels[i] > 0;
            if (hasTransparent && hasVisible) break;
        }
        if (!hasTransparent || !hasVisible)
            throw new InvalidDataException($"{name} must contain both transparent and visible pixels.");
    }

    private static byte[] GetBgraPixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
}
