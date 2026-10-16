using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeGrass;

internal static class MegaBubbleFrames
{
    internal const int Count = 27;
    internal const int FrameMilliseconds = 83; // ~12 FPS
    internal const string AssetPath = "assets/reference/mega-bubble.png";

    // The artwork is arranged in three rows of nine, but the horizontal gaps
    // are not evenly spaced. These cuts follow the transparent gutters found
    // by inspecting the original 1881x836 sheet.
    private static readonly int[] XEdges = [0, 215, 423, 636, 844, 1051, 1257, 1472, 1676, 1881];
    private static readonly int[] YEdges = [0, 227, 464, 836];

    internal static bool MegaIsVisibleAt(int zeroBasedFrame) => zeroBasedFrame >= 8;

    internal static double MegaOpacityAt(int zeroBasedFrame) => zeroBasedFrame switch
    {
        < 8 => 0d,
        < 17 => .1d + (zeroBasedFrame - 8) / 9d * .9d,
        _ => 1d
    };

    internal static IReadOnlyList<BitmapSource> Load(string path)
    {
        var source = LoadBitmap(path);
        if (source.PixelWidth != 1881 || source.PixelHeight != 836)
            throw new InvalidDataException($"mega-bubble.png debe medir 1881×836; mide {source.PixelWidth}×{source.PixelHeight}.");

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        // The inspected divider columns contain only alpha=1 export noise at
        // a few points; no visible pixel (alpha > 1) crosses a cut. Row cuts
        // are fully transparent.
        foreach (var x in XEdges.Skip(1).SkipLast(1))
        for (var y = 0; y < converted.PixelHeight; y++)
            if (pixels[y * stride + x * 4 + 3] > 1)
                throw new InvalidDataException($"El corte vertical x={x} de mega-bubble.png atraviesa un píxel visible.");
        foreach (var y in YEdges.Skip(1).SkipLast(1))
        for (var x = 0; x < converted.PixelWidth; x++)
            if (pixels[y * stride + x * 4 + 3] != 0)
                throw new InvalidDataException($"El corte horizontal y={y} de mega-bubble.png no es transparente.");

        var visibleBounds = new Int32Rect[Count];
        var maxWidth = 0;
        var maxHeight = 0;
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 9; column++)
        {
            var index = row * 9 + column;
            var left = XEdges[column];
            var right = XEdges[column + 1];
            var top = YEdges[row];
            var bottom = YEdges[row + 1];
            var minX = right; var minY = bottom; var maxX = -1; var maxY = -1;
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
                if (pixels[y * stride + x * 4 + 3] > 1)
                {
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }

            if (maxX < 0 || maxY < 0)
                throw new InvalidDataException($"El fotograma {index + 1} de mega-bubble.png está vacío.");
            visibleBounds[index] = new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            maxWidth = Math.Max(maxWidth, visibleBounds[index].Width);
            maxHeight = Math.Max(maxHeight, visibleBounds[index].Height);
        }

        var frames = new BitmapSource[Count];
        for (var index = 0; index < Count; index++)
        {
            var bounds = visibleBounds[index];
            var framePixels = new byte[maxWidth * maxHeight * 4];
            var offsetX = (maxWidth - bounds.Width) / 2;
            var offsetY = (maxHeight - bounds.Height) / 2;
            for (var y = 0; y < bounds.Height; y++)
                Buffer.BlockCopy(pixels, (bounds.Y + y) * stride + bounds.X * 4,
                    framePixels, (offsetY + y) * maxWidth * 4 + offsetX * 4, bounds.Width * 4);

            var frame = BitmapSource.Create(maxWidth, maxHeight, 96, 96, PixelFormats.Bgra32,
                null, framePixels, maxWidth * 4);
            frame.Freeze();
            frames[index] = frame;
        }
        return frames;
    }

    internal static string ValidateAndSummarize(string path)
    {
        if (MegaIsVisibleAt(7) || !MegaIsVisibleAt(8)
            || Math.Abs(MegaOpacityAt(8) - .1d) > .00001d
            || Math.Abs(MegaOpacityAt(17) - 1d) > .00001d
            || MegaOpacityAt(26) != 1d
            || Enumerable.Range(8, 10).Zip(Enumerable.Range(9, 10),
                (current, next) => MegaOpacityAt(next) >= MegaOpacityAt(current)).Any(isMonotonic => !isMonotonic))
            throw new InvalidDataException("La transición Mega debe cambiar en el frame 9, iniciar al 10% y alcanzar el 100% en el frame 18.");
        var frames = Load(path);
        if (frames.Count != Count) throw new InvalidDataException($"Se esperaban 27 fotogramas mega-bubble; se encontraron {frames.Count}.");
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var frame in frames)
        {
            var frameStride = frame.PixelWidth * 4;
            var framePixels = new byte[frameStride * frame.PixelHeight];
            frame.CopyPixels(framePixels, frameStride, 0);
            var hasVisible = false;
            var hasTransparent = false;
            for (var offset = 3; offset < framePixels.Length; offset += 4)
            {
                hasVisible |= framePixels[offset] > 1;
                hasTransparent |= framePixels[offset] == 0;
            }
            if (!hasVisible || !hasTransparent)
                throw new InvalidDataException("Todos los fotogramas de mega-bubble deben conservar contenido y transparencia.");
            if (!hashes.Add(Convert.ToHexString(SHA256.HashData(framePixels))))
                throw new InvalidDataException("mega-bubble.png contiene fotogramas vacíos o duplicados tras el recorte.");
        }
        return $"Mega-bubble: 27 fotogramas únicos, cortes por canal alpha transparente, lienzo uniforme {frames[0].PixelWidth}×{frames[0].PixelHeight}, ~12 FPS.";
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
