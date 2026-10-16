namespace PokeGrass;

internal readonly record struct SpriteSupport(
    double X,
    double Y,
    int AlphaLeft,
    int AlphaTop,
    int AlphaRight,
    int AlphaBottom,
    int LowerBodyLeft,
    int LowerBodyRight)
{
    public double VisibleHeight => AlphaBottom - AlphaTop + 1;
    public double VisibleCenterX => (AlphaLeft + AlphaRight + 1) / 2d;
    public int LowerBodyWidth => LowerBodyRight - LowerBodyLeft + 1;
}

internal readonly record struct PokemonLayout(double Left, double Top, double Width, double Height, double FootY);

internal static class PokemonPlacement
{
    public const double GreenLeft = 18;
    public const double GreenRight = 112;
    public const double GreenTop = 4;
    public const double GreenBottom = 64;

    // Pikachu occupied about 35 garden pixels in height in the approved reference.
    // Its 192px canvas has a 92px visible height, so all canvases use this same factor.
    public const int EncounterCanvasSize = 192;
    public const double Scale = 35d / 92d;

    public static PokemonLayout Calculate(int canvasWidth, int canvasHeight, SpriteSupport support,
        double anchorX, double footY, double sizeMultiplier = 1d)
    {
        if (canvasWidth <= 0 || canvasHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(canvasWidth), "El lienzo del sprite debe tener dimensiones positivas.");
        if (support.X < 0 || support.X > canvasWidth || support.Y < 0 || support.Y > canvasHeight)
            throw new ArgumentOutOfRangeException(nameof(support), "El punto de apoyo debe pertenecer al lienzo completo.");

        sizeMultiplier = Math.Clamp(sizeMultiplier, .5d, 2d);
        var scale = Scale * sizeMultiplier;
        var width = canvasWidth * scale;
        var height = canvasHeight * scale;
        var left = anchorX - support.X * scale;
        var top = footY - support.Y * scale;
        return new PokemonLayout(left, top, width, height, footY);
    }

    public static double ChooseFootY(SpriteSupport support, IEnumerable<double> candidateFootYs, double sizeMultiplier = 1d)
    {
        var requiredTop = support.VisibleHeight * Scale * Math.Clamp(sizeMultiplier, .5d, 2d);
        var candidates = candidateFootYs
            .Where(footY => footY >= GreenTop && footY <= GreenBottom)
            .Distinct()
            .ToArray();
        var containedBodyFootY = candidates
            .Where(footY => footY >= GreenTop + requiredTop)
            .OrderBy(footY => footY)
            .FirstOrDefault(double.NaN);
        if (double.IsFinite(containedBodyFootY)) return containedBodyFootY;

        // Oversized species keep the shared sprite scale. When no grass row can
        // contain their whole visible body, anchor their feet to the lowest valid
        // row and let the body extend upward over the garden.
        return candidates.OrderByDescending(footY => footY).FirstOrDefault(double.NaN);
    }

    public static int SelectShadowVariant(int supportWidth, IReadOnlyList<int> shadowWidths)
    {
        if (supportWidth <= 0 || shadowWidths.Count == 0 || shadowWidths.Any(width => width <= 0))
            throw new ArgumentOutOfRangeException(nameof(supportWidth), "El ancho de apoyo y las sombras deben ser positivos.");
        return Enumerable.Range(0, shadowWidths.Count)
            .OrderBy(index => Math.Abs(shadowWidths[index] - supportWidth))
            .ThenBy(index => index)
            .First();
    }

    public static PokemonLayout CalculateShadow(int shadowWidth, int shadowHeight, double anchorX, double footY,
        double sizeMultiplier = 1d)
    {
        if (shadowWidth <= 0 || shadowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(shadowWidth), "La sombra debe tener dimensiones positivas.");
        if (anchorX < GreenLeft || anchorX > GreenRight || footY < GreenTop || footY > GreenBottom)
            throw new ArgumentOutOfRangeException(nameof(anchorX), "El apoyo del Pokémon debe permanecer dentro del área verde.");

        var scale = Math.Min(Scale * Math.Clamp(sizeMultiplier, .5d, 2d), Math.Min(
            (anchorX - GreenLeft) * 2 / shadowWidth,
            (GreenRight - anchorX) * 2 / shadowWidth));
        scale = Math.Min(scale, (GreenBottom - GreenTop) / (double)shadowHeight);
        var width = shadowWidth * scale;
        var height = shadowHeight * scale;
        var left = anchorX - width / 2;
        var top = Math.Clamp(footY - height / 2, GreenTop, GreenBottom - height);
        return new PokemonLayout(left, top, width, height, footY);
    }
}
