using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeGrass;

internal static class AlphaEffectFrames
{
    internal const int EncounterCount = 8;
    internal const int CompanionCount = 9;
    internal const int FrameMilliseconds = 55;
    internal const string EncounterAssetPath = "assets/reference/alfa1.png";
    internal const string CompanionAssetPath = "assets/reference/alfa2.png";

    internal static IReadOnlyList<BitmapSource> Load(string path, int frameCount, bool findTransparentSeams = false)
    {
        if (frameCount is not (EncounterCount or CompanionCount))
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        var source = LoadBitmap(path);
        if (source.PixelWidth != 2172 || source.PixelHeight != 724)
            throw new InvalidDataException($"El spritesheet Alfa debe medir 2172×724; mide {source.PixelWidth}×{source.PixelHeight}.");

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        if (findTransparentSeams)
            return CropFramesAtTransparentSeams(pixels, converted.PixelWidth, converted.PixelHeight, frameCount);

        var minY = converted.PixelHeight;
        var maxY = -1;
        for (var y = 0; y < converted.PixelHeight; y++)
        for (var x = 0; x < converted.PixelWidth; x++)
            if (pixels[(y * converted.PixelWidth + x) * 4 + 3] > 8)
            {
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        if (maxY < minY) throw new InvalidDataException("El spritesheet Alfa no contiene pixeles visibles.");

        var edges = Enumerable.Range(0, frameCount + 1).Select(index => index * converted.PixelWidth / frameCount).ToArray();
        var frameWidth = Enumerable.Range(0, frameCount).Max(index => edges[index + 1] - edges[index]);
        var frameHeight = maxY - minY + 1;
        var frames = new BitmapSource[frameCount];
        for (var index = 0; index < frameCount; index++)
        {
            var width = edges[index + 1] - edges[index];
            var framePixels = new byte[frameWidth * frameHeight * 4];
            for (var y = 0; y < frameHeight; y++)
                Buffer.BlockCopy(pixels, ((minY + y) * converted.PixelWidth + edges[index]) * 4,
                    framePixels, y * frameWidth * 4, width * 4);
            var frame = BitmapSource.Create(frameWidth, frameHeight, 96, 96, PixelFormats.Bgra32,
                null, framePixels, frameWidth * 4);
            frame.Freeze();
            frames[index] = frame;
        }
        return frames;
    }

    internal static string ValidateAndSummarize(string encounterPath, string companionPath)
    {
        var encounter = ValidateSheet(encounterPath, "alfa1.png", EncounterCount, findTransparentSeams: true);
        var companion = ValidateSheet(companionPath, "alfa2.png", CompanionCount);
        return $"Alfa: `alfa1.png` y `alfa2.png` verificados como hojas 2172×724; 8 frames separados por márgenes transparentes y 9 frames horizontales ({encounter.PixelWidth}×{encounter.PixelHeight} y {companion.PixelWidth}×{companion.PixelHeight}), lectura izquierda a derecha a ~18 FPS.";
    }

    private static BitmapSource ValidateSheet(string path, string label, int frameCount, bool findTransparentSeams = false)
    {
        var frames = Load(path, frameCount, findTransparentSeams);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var frame in frames)
        {
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
                throw new InvalidDataException($"Cada frame de {label} debe tener transparencia y pixeles visibles.");
            hashes.Add(Convert.ToHexString(SHA256.HashData(pixels)));
        }
        if (hashes.Count != frameCount)
            throw new InvalidDataException($"{label} no contiene {frameCount} frames distintos.");
        return frames[0];
    }

    private static IReadOnlyList<BitmapSource> CropFramesAtTransparentSeams(byte[] pixels, int sheetWidth, int sheetHeight, int frameCount)
    {
        // alfa1 is not divided into eight equal-width cells. Find each separator
        // near the expected interval, where the full column has no visible alpha.
        const byte visibleAlpha = 32;
        var opaquePixelsPerColumn = new int[sheetWidth];
        for (var y = 0; y < sheetHeight; y++)
        for (var x = 0; x < sheetWidth; x++)
            if (pixels[(y * sheetWidth + x) * 4 + 3] > visibleAlpha)
                opaquePixelsPerColumn[x]++;

        var edges = new int[frameCount + 1];
        edges[0] = 0;
        edges[^1] = sheetWidth;
        var searchRadius = Math.Max(8, sheetWidth / frameCount / 4);
        for (var boundary = 1; boundary < frameCount; boundary++)
        {
            var expected = boundary * sheetWidth / frameCount;
            var searchStart = Math.Max(edges[boundary - 1] + 1, expected - searchRadius);
            var searchEnd = Math.Min(sheetWidth - 1, expected + searchRadius);
            var bestStart = -1;
            var bestEnd = -1;
            for (var x = searchStart; x <= searchEnd; x++)
            {
                if (opaquePixelsPerColumn[x] != 0) continue;
                var runStart = x;
                while (runStart > searchStart && opaquePixelsPerColumn[runStart - 1] == 0) runStart--;
                var runEnd = x;
                while (runEnd < searchEnd && opaquePixelsPerColumn[runEnd + 1] == 0) runEnd++;
                if (bestStart < 0
                    || Math.Abs((runStart + runEnd) / 2d - expected) < Math.Abs((bestStart + bestEnd) / 2d - expected))
                {
                    bestStart = runStart;
                    bestEnd = runEnd;
                }
                x = runEnd;
            }
            if (bestStart < 0)
                throw new InvalidDataException($"No se encontró una separación transparente para el frame {boundary} de alfa1.png.");
            edges[boundary] = (bestStart + bestEnd + 1) / 2;
        }

        var bounds = new Int32Rect[frameCount];
        var maxWidth = 0;
        var maxHeight = 0;
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var minX = sheetWidth;
            var minY = sheetHeight;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < sheetHeight; y++)
            for (var x = edges[frameIndex]; x < edges[frameIndex + 1]; x++)
                if (pixels[(y * sheetWidth + x) * 4 + 3] > visibleAlpha)
                {
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            if (maxX < minX || maxY < minY)
                throw new InvalidDataException($"El frame {frameIndex + 1} de alfa1.png no contiene pixeles visibles.");
            bounds[frameIndex] = new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            maxWidth = Math.Max(maxWidth, bounds[frameIndex].Width);
            maxHeight = Math.Max(maxHeight, bounds[frameIndex].Height);
        }

        var frames = new BitmapSource[frameCount];
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var boundsForFrame = bounds[frameIndex];
            var framePixels = new byte[maxWidth * maxHeight * 4];
            var offsetX = (maxWidth - boundsForFrame.Width) / 2;
            var offsetY = (maxHeight - boundsForFrame.Height) / 2;
            for (var y = 0; y < boundsForFrame.Height; y++)
                Buffer.BlockCopy(pixels,
                    ((boundsForFrame.Y + y) * sheetWidth + boundsForFrame.X) * 4,
                    framePixels, ((offsetY + y) * maxWidth + offsetX) * 4,
                    boundsForFrame.Width * 4);
            var frame = BitmapSource.Create(maxWidth, maxHeight, 96, 96, PixelFormats.Bgra32,
                null, framePixels, maxWidth * 4);
            frame.Freeze();
            frames[frameIndex] = frame;
        }
        return frames;
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
