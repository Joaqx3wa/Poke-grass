using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeGrass;

internal static class DittoTransformFrames
{
    // The exported sheet has uneven transparent gutters, so fixed 9×3 cuts
    // clip or include neighboring pixels. Find the transparent seams first.
    internal const string AssetPath = "assets/reference/transform.png";
    internal const int Columns = 9;
    internal const int Rows = 3;
    internal const int FrameMilliseconds = 83;
    private const byte VisibleAlphaThreshold = 64;

    internal static IReadOnlyList<BitmapSource> Load(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(path));
        bitmap.EndInit();
        bitmap.Freeze();
        if (bitmap.PixelWidth < Columns || bitmap.PixelHeight < Rows)
            throw new InvalidDataException($"La hoja Ditto es demasiado pequeña para extraer 9×3 fotogramas: {bitmap.PixelWidth}×{bitmap.PixelHeight}.");

        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var sourceWidth = converted.PixelWidth;
        var sourceHeight = converted.PixelHeight;
        var stride = sourceWidth * 4;
        var pixels = new byte[stride * sourceHeight];
        converted.CopyPixels(pixels, stride, 0);

        var xCuts = new int[Columns + 1];
        var yCuts = new int[Rows + 1];
        xCuts[0] = 0;
        xCuts[Columns] = sourceWidth;
        yCuts[0] = 0;
        yCuts[Rows] = sourceHeight;
        for (var column = 1; column < Columns; column++)
            xCuts[column] = FindTransparentCut(pixels, stride, sourceWidth, sourceHeight,
                vertical: true, (int)Math.Round(sourceWidth * column / (double)Columns), 16);
        for (var row = 1; row < Rows; row++)
            yCuts[row] = FindTransparentCut(pixels, stride, sourceWidth, sourceHeight,
                vertical: false, (int)Math.Round(sourceHeight * row / (double)Rows), 36);

        var maxWidth = Enumerable.Range(0, Columns).Max(column => xCuts[column + 1] - xCuts[column]);
        var maxHeight = Enumerable.Range(0, Rows).Max(row => yCuts[row + 1] - yCuts[row]);
        var frames = new List<BitmapSource>(Columns * Rows);
        for (var row = 0; row < Rows; row++)
        for (var column = 0; column < Columns; column++)
        {
            var rect = new Int32Rect(xCuts[column], yCuts[row],
                xCuts[column + 1] - xCuts[column], yCuts[row + 1] - yCuts[row]);
            var crop = new CroppedBitmap(converted, rect);
            crop.Freeze();
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawImage(crop, new Rect((maxWidth - rect.Width) / 2d,
                    (maxHeight - rect.Height) / 2d, rect.Width, rect.Height));
            var padded = new RenderTargetBitmap(maxWidth, maxHeight, 96, 96, PixelFormats.Pbgra32);
            padded.Render(visual);
            padded.Freeze();
            frames.Add(padded);
        }
        return frames;
    }

    private static int FindTransparentCut(byte[] pixels, int stride, int width, int height,
        bool vertical, int expected, int searchRadius)
    {
        var limit = vertical ? width : height;
        var start = Math.Max(1, expected - searchRadius);
        var end = Math.Min(limit - 1, expected + searchRadius);
        var bestCut = -1;
        var bestVisiblePixels = int.MaxValue;
        for (var cut = start; cut <= end; cut++)
        {
            var visiblePixels = 0;
            var length = vertical ? height : width;
            for (var index = 0; index < length; index++)
            {
                var offset = vertical ? index * stride + cut * 4 : cut * stride + index * 4;
                if (pixels[offset + 3] >= VisibleAlphaThreshold) visiblePixels++;
            }
            if (visiblePixels < bestVisiblePixels
                || (visiblePixels == bestVisiblePixels && Math.Abs(cut - expected) < Math.Abs(bestCut - expected)))
            {
                bestVisiblePixels = visiblePixels;
                bestCut = cut;
            }
        }
        if (bestVisiblePixels != 0)
            throw new InvalidDataException($"No se encontró un corte transparente cerca de {expected} ({(vertical ? "vertical" : "horizontal")}); hay {bestVisiblePixels} píxeles visibles en el mejor corte.");
        return bestCut;
    }

    internal static double SourceOpacityAt(int frameIndex)
    {
        if (frameIndex < 0) return 1d;
        if (frameIndex >= 9) return 0d;
        return 1d - frameIndex / 9d;
    }

    internal static double TargetOpacityAt(int frameIndex)
    {
        if (frameIndex < 13) return 0d;
        if (frameIndex >= 26) return 1d;
        return .1d + (frameIndex - 13) / 13d * .9d;
    }

    internal static string ValidateAndSummarize(string path)
    {
        var frames = Load(path);
        if (frames.Count != 27 || frames.Select(frame => (frame.PixelWidth, frame.PixelHeight)).Distinct().Count() != 1)
            throw new InvalidDataException("La hoja Ditto no produjo 27 fotogramas de tamaño uniforme.");
        if (SourceOpacityAt(9) != 0d || TargetOpacityAt(13) < .099d || TargetOpacityAt(26) != 1d)
            throw new InvalidDataException("La sincronización de opacidad de Ditto no respeta los fotogramas 10, 14 y 27.");
        return $"Ditto transform: PASS ({frames.Count} celdas uniformes de {frames[0].PixelWidth}×{frames[0].PixelHeight})";
    }
}
