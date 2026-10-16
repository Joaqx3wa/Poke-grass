using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeGrass;

internal sealed class PokemonEvolutionDefinition
{
    public required string FromId { get; init; }
    public required string ToId { get; init; }
    public string? RequiredSex { get; init; }
}

internal sealed class PokemonEvolutionData
{
    public string Source { get; init; } = string.Empty;
    public string SourceRetrieved { get; init; } = string.Empty;
    public List<PokemonEvolutionDefinition> Evolutions { get; init; } = [];
}

internal sealed class PokemonEvolutionCatalog
{
    internal const int VictoryChancePercent = 5;
    private readonly Dictionary<string, IReadOnlyList<PokemonEvolutionDefinition>> _bySource;
    private readonly PokemonCatalog _pokemon;
    private readonly PokemonSpeciesMetadataCatalog _metadata;
    private readonly MegaEvolutionCatalog _mega;

    private PokemonEvolutionCatalog(PokemonEvolutionData data, PokemonCatalog pokemon,
        PokemonSpeciesMetadataCatalog metadata, MegaEvolutionCatalog mega)
    {
        _pokemon = pokemon;
        _metadata = metadata;
        _mega = mega;
        var rows = data.Evolutions.Where(row =>
                pokemon.ById.ContainsKey(row.FromId)
                && pokemon.ById.ContainsKey(row.ToId)
                && !_mega.TryGetForm(row.FromId, out _)
                && !_mega.TryGetForm(row.ToId, out _)
                && !metadata.Get(row.FromId).SpeciesId.Equals("ditto", StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.FromId, StringComparer.OrdinalIgnoreCase);
        _bySource = rows.ToDictionary(group => group.Key, group => (IReadOnlyList<PokemonEvolutionDefinition>)group
            .DistinctBy(row => $"{row.ToId}|{row.RequiredSex}", StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static PokemonEvolutionCatalog Load(string path, PokemonCatalog pokemon,
        PokemonSpeciesMetadataCatalog metadata, MegaEvolutionCatalog mega)
    {
        var data = JsonSerializer.Deserialize<PokemonEvolutionData>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("El catálogo de evoluciones está vacío o no es válido.");
        if (data.Evolutions.Count == 0)
            throw new InvalidDataException("El catálogo de evoluciones no contiene relaciones válidas.");
        return new PokemonEvolutionCatalog(data, pokemon, metadata, mega);
    }

    internal IReadOnlyList<PokemonSpeciesMetadata> GetAvailableTargets(CapturedPokemon source,
        IReadOnlyCollection<CapturedPokemon> collection)
    {
        var sourceSpecies = _metadata.Get(source.Id);
        var sourceSex = PokemonSex.Get(sourceSpecies);
        if (sourceSex is not ("male" or "female" or "genderless")
            || source.SpeciesId.Equals("ditto", StringComparison.OrdinalIgnoreCase)
            || source.MegaFormId is not null)
            return Array.Empty<PokemonSpeciesMetadata>();

        var sourceId = source.Id.EndsWith("_female", StringComparison.OrdinalIgnoreCase)
            ? source.Id[..^"_female".Length]
            : source.Id;
        if (!_bySource.TryGetValue(sourceId, out var definitions))
            return Array.Empty<PokemonSpeciesMetadata>();

        return definitions
            .Where(definition => definition.RequiredSex is null
                || definition.RequiredSex.Equals(sourceSex, StringComparison.OrdinalIgnoreCase))
            .Select(definition => ResolveTarget(definition.ToId, sourceSex))
            .Where(target => target is not null)
            .Cast<PokemonSpeciesMetadata>()
            .Where(target => !IsOwned(collection, source.RecordId, target))
            .DistinctBy(target => EvolutionIdentity.Key(target, _metadata), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool RollForEvolution(Func<int, int> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return next(100) < VictoryChancePercent;
    }

    private PokemonSpeciesMetadata? ResolveTarget(string targetId, string sourceSex)
    {
        if (!_pokemon.ById.ContainsKey(targetId)) return null;
        var baseTarget = _metadata.Get(targetId);
        var targetSex = PokemonSex.Get(baseTarget);

        if (targetSex.Equals("genderless", StringComparison.OrdinalIgnoreCase))
            return sourceSex.Equals("genderless", StringComparison.OrdinalIgnoreCase) ? baseTarget : null;
        if (targetSex.Equals("female", StringComparison.OrdinalIgnoreCase))
            return sourceSex.Equals("female", StringComparison.OrdinalIgnoreCase) ? baseTarget : null;
        if (sourceSex.Equals("male", StringComparison.OrdinalIgnoreCase)) return baseTarget;
        if (sourceSex.Equals("female", StringComparison.OrdinalIgnoreCase))
        {
            var femaleId = $"{targetId}_female";
            return _metadata.Entries.FirstOrDefault(entry => entry.Id.Equals(femaleId, StringComparison.OrdinalIgnoreCase));
        }

        return baseTarget;
    }

    private bool IsOwned(IEnumerable<CapturedPokemon> collection, string currentRecordId,
        PokemonSpeciesMetadata target) => collection.Any(capture => capture.RecordId != currentRecordId
        && EvolutionIdentity.Key(capture, _metadata).Equals(EvolutionIdentity.Key(target, _metadata),
            StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

internal static class EvolutionIdentity
{
    internal static string Key(CapturedPokemon capture, PokemonSpeciesMetadataCatalog metadata)
    {
        var sex = PokemonSex.Get(metadata.GetForCapture(capture.Id));
        return Key(capture.SpeciesId, RegionalForm(capture.Form), sex);
    }

    internal static string Key(PokemonSpeciesMetadata species, PokemonSpeciesMetadataCatalog metadata) =>
        Key(species.SpeciesId, RegionalForm(species.Form), PokemonSex.Get(species));

    private static string Key(string species, string form, string sex) => $"{species}|{form}|{sex}";

    private static string RegionalForm(string form)
    {
        if (form.Equals("female", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (form.StartsWith("female_", StringComparison.OrdinalIgnoreCase)) return form["female_".Length..];
        if (form.Equals("male", StringComparison.OrdinalIgnoreCase) || form.Equals("genderless", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return form;
    }

    private static string SexFromForm(string form) => form.Equals("genderless", StringComparison.OrdinalIgnoreCase)
        ? "genderless" : form.Equals("female", StringComparison.OrdinalIgnoreCase)
            || form.StartsWith("female_", StringComparison.OrdinalIgnoreCase) ? "female" : "male";
}

internal static class EvolutionAnimationFrames
{
    internal const int Columns = 9;
    internal const int Rows = 3;
    internal const int Count = Columns * Rows;
    internal const int FrameMilliseconds = 78;
    internal const string AssetPath = "reference/evolution.png";

    internal static IReadOnlyList<BitmapSource> Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Falta el spritesheet de evolución.", path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        var width = bitmap.PixelWidth / Columns;
        var rowHeight = bitmap.PixelHeight / Rows;
        var extraRight = bitmap.PixelWidth - width * Columns;
        var extraBottom = bitmap.PixelHeight - rowHeight * Rows;
        if (width <= 0 || rowHeight <= 0 || extraRight > Columns || extraBottom > 1)
            throw new InvalidDataException($"El spritesheet evolution no encaja en una cuadrícula 9×3: {bitmap.PixelWidth}×{bitmap.PixelHeight}.");

        // The supplied sheet is 2172×724: the last three columns are transparent
        // export padding, while the final row is one pixel taller than the first
        // two. Normalize each cell in memory, retaining every pixel of frame 19–27
        // and adding transparent padding to frames 1–18.
        if (extraRight > 0 && !IsTransparent(bitmap, width * Columns, 0, extraRight, bitmap.PixelHeight))
            throw new InvalidDataException("El borde derecho extra del spritesheet evolution contiene píxeles; no se puede normalizar sin cortarlos.");

        var height = rowHeight + extraBottom;
        var frames = new BitmapSource[Count];
        for (var index = 0; index < Count; index++)
        {
            var column = index % Columns;
            var row = index / Columns;
            var sourceHeight = rowHeight + (row == Rows - 1 ? extraBottom : 0);
            var crop = new CroppedBitmap(bitmap, new Int32Rect(column * width, row * rowHeight, width, sourceHeight));
            crop.Freeze();
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (var context = visual.RenderOpen())
                context.DrawImage(crop, new Rect(0, 0, width, sourceHeight));
            var frame = new RenderTargetBitmap(width, height, bitmap.DpiX, bitmap.DpiY, PixelFormats.Pbgra32);
            frame.Render(visual);
            frame.Freeze();
            frames[index] = frame;
        }
        return frames;
    }

    private static bool IsTransparent(BitmapSource bitmap, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return true;
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var row = y; row < y + height; row++)
        for (var column = x; column < x + width; column++)
            if (pixels[row * stride + column * 4 + 3] != 0) return false;
        return true;
    }

    internal static string ValidateAndSummarize(string path)
    {
        var frames = Load(path);
        var first = (BitmapSource)frames[0];
        if (frames.Any(frame => frame.PixelWidth != first.PixelWidth || frame.PixelHeight != first.PixelHeight))
            throw new InvalidDataException("Las celdas del spritesheet evolution no tienen el mismo tamaño.");
        return $"evolution: {Count} frames, {first.PixelWidth}×{first.PixelHeight}px each";
    }
}

internal sealed class EvolutionPromptWindow : Window
{
    private bool _answered;

    internal EvolutionPromptWindow(Window owner, string currentName, string targetName, Action<bool> answer)
    {
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 500;
        Height = 244;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        var root = new Grid();
        root.Children.Add(PixelButtonUi.NineSlice(PixelUi.Load("ui/frames/decorative_1_large_001.png"), Width, Height, 32, 32));
        var layout = new Grid { Margin = new Thickness(42, 28, 42, 26) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        layout.Children.Add(new TextBlock
        {
            Text = "¡Evolución disponible!", FontFamily = PixelUi.PixelFont, FontSize = 21,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 44, 31)),
            TextAlignment = TextAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        });
        var body = new TextBlock
        {
            Text = $"¡Tu {currentName} quiere evolucionar a {targetName}!", FontFamily = PixelUi.PixelFont, FontSize = 15,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 44, 31)), TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 3, 8, 3)
        };
        Grid.SetRow(body, 1);
        layout.Children.Add(body);
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        buttons.Children.Add(PixelButtonUi.Button("Cancelar", "blue", 156, 54, () => Respond(false), fontSize: 15));
        buttons.Children.Add(new Border { Width = 14, IsHitTestVisible = false });
        buttons.Children.Add(PixelButtonUi.Button("Evolucionar", "green", 156, 54, () => Respond(true), fontSize: 15));
        Grid.SetRow(buttons, 2);
        layout.Children.Add(buttons);
        root.Children.Add(layout);
        Content = root;
        RenderOptions.SetBitmapScalingMode(root, BitmapScalingMode.NearestNeighbor);
        Closed += (_, _) => { if (!_answered) answer(false); };

        void Respond(bool accepted)
        {
            if (_answered) return;
            _answered = true;
            answer(accepted);
            Close();
        }
    }
}
