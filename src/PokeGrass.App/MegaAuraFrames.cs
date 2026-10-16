using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeGrass;

internal static class MegaAuraFrames
{
    internal const int Count = 9;
    internal const int FrameMilliseconds = 83; // ~12 FPS
    internal const string AssetPath = "assets/reference/mega2.png";

    internal static IReadOnlyList<BitmapSource> Load(string path)
    {
        var source = LoadBitmap(path);
        if (source.PixelWidth != 2172 || source.PixelHeight != 724)
            throw new InvalidDataException($"La secuencia Mega debe medir 2172×724; mide {source.PixelWidth}×{source.PixelHeight}.");

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        var minY = converted.PixelHeight;
        var maxY = -1;
        for (var y = 0; y < converted.PixelHeight; y++)
        for (var x = 0; x < converted.PixelWidth; x++)
            if (pixels[(y * converted.PixelWidth + x) * 4 + 3] > 8)
            {
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

        if (maxY < minY) throw new InvalidDataException("mega2.png no contiene pixeles visibles.");
        var edges = Enumerable.Range(0, Count + 1)
            .Select(index => index * converted.PixelWidth / Count)
            .ToArray();
        var uniformWidth = edges.Zip(edges.Skip(1), (left, right) => right - left).Max();
        var frameHeight = maxY - minY + 1;
        var frames = new BitmapSource[Count];
        for (var index = 0; index < Count; index++)
        {
            var sourceX = edges[index];
            var sourceWidth = edges[index + 1] - sourceX;
            var framePixels = new byte[uniformWidth * frameHeight * 4];
            for (var y = 0; y < frameHeight; y++)
                Buffer.BlockCopy(pixels, ((minY + y) * converted.PixelWidth + sourceX) * 4,
                    framePixels, y * uniformWidth * 4, sourceWidth * 4);

            var frame = BitmapSource.Create(uniformWidth, frameHeight, 96, 96, PixelFormats.Bgra32,
                null, framePixels, uniformWidth * 4);
            frame.Freeze();
            frames[index] = frame;
        }

        return frames;
    }

    internal static string ValidateAndSummarize(string path)
    {
        var original = LoadBitmap(path);
        if (original.PixelWidth != 2172 || original.PixelHeight != 724)
            throw new InvalidDataException($"mega2.png debe medir 2172×724; mide {original.PixelWidth}×{original.PixelHeight}.");

        var frames = Load(path);
        if (frames.Count != Count) throw new InvalidDataException($"Se esperaban 9 fotogramas Mega; se encontraron {frames.Count}.");
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var frame in frames)
        {
            if (frame.PixelWidth != 242 || frame.PixelHeight < 200)
                throw new InvalidDataException($"Fotograma Mega inesperado: {frame.PixelWidth}×{frame.PixelHeight}.");
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            var hasTransparent = false;
            var hasVisible = false;
            for (var offset = 3; offset < pixels.Length; offset += 4)
            {
                hasTransparent |= pixels[offset] == 0;
                hasVisible |= pixels[offset] > 0;
            }
            if (!hasTransparent || !hasVisible)
                throw new InvalidDataException("Cada fotograma de mega2.png debe conservar transparencia y pixeles visibles.");
            if (!hashes.Add(Convert.ToHexString(SHA256.HashData(pixels))))
                throw new InvalidDataException("Hay fotogramas Mega duplicados.");
        }

        return $"Aura Mega: 9 fotogramas horizontales distintos, alfa verificado, recorte común {frames[0].PixelWidth}×{frames[0].PixelHeight}, 12 FPS.";
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
