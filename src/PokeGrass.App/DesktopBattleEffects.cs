using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace PokeGrass;

internal enum BattleEffectKind { Impact, Projectile, Defense }

internal sealed record BattleEffectDefinition(
    string Id,
    string FileName,
    int FrameCount,
    int Columns,
    int Rows,
    int FrameMilliseconds,
    BattleEffectKind Kind,
    double SizeMultiplier,
    double Opacity = 1d,
    bool RotateDuringFlight = false,
    int[]? FlightFrames = null,
    int[]? ImpactFrames = null,
    int FlightLoopStart = -1,
    int FlightLoopEnd = -1,
    double ProjectileSpeedPixelsPerMillisecond = 1.25d);

/// <summary>Explicit layouts for every attack sheet. All cells are 192 px, but counts vary by asset.</summary>
internal static class BattleEffectCatalog
{
    private const int CellSize = 192;
    // The median visible extent of Pikachu's electric animation is 120 source pixels.
    // Use it as the shared apparent footprint for every attack and shield frame. The fixed
    // 123-DIP canvas yields a 76.875-DIP visible effect, enlarged from Pikachu's reference.
    internal const double ReferenceAttackExtent = 120d;
    internal const double ReferenceAttackWindowSizeDip = 123d;
    private static readonly Dictionary<string, BattleEffectDefinition> Definitions = BuildDefinitions();
    private static readonly Dictionary<string, BitmapSource[]> FrameCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Rect[]> FrameBoundsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();
    private static readonly IReadOnlyDictionary<string, string> TypeEffectIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Bug"] = "bug", ["Dark"] = "dark", ["Dragon"] = "dragon", ["Electric"] = "electric",
        ["Fairy"] = "fairy", ["Fighting"] = "fighting", ["Fire"] = "fire", ["Flying"] = "flying",
        ["Ghost"] = "ghost", ["Grass"] = "grass", ["Ground"] = "ground", ["Ice"] = "ice",
        ["Normal"] = "normal", ["Poison"] = "poison", ["Psychic"] = "psychic", ["Rock"] = "rock",
        ["Steel"] = "steel", ["Water"] = "water"
    };
    private static readonly IReadOnlyDictionary<string, string[]> MegaTypeOverrides =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["CHARIZARD_1"] = ["fire", "dragon"], ["PINSIR_1"] = ["bug", "flying"],
            ["GYARADOS_1"] = ["water", "dark"], ["MEWTWO_1"] = ["psychic", "fighting"],
            ["AMPHAROS_1"] = ["electric", "dragon"], ["SCEPTILE_1"] = ["grass", "dragon"],
            ["AGGRON_1"] = ["steel"], ["ALTARIA_1"] = ["dragon", "fairy"],
            ["LOPUNNY_1"] = ["normal", "fighting"], ["AUDINO_1"] = ["normal", "fairy"]
        };

    internal static IReadOnlyCollection<BattleEffectDefinition> All => Definitions.Values;

    private static Dictionary<string, BattleEffectDefinition> BuildDefinitions()
    {
        var result = new Dictionary<string, BattleEffectDefinition>(StringComparer.OrdinalIgnoreCase);
        void Add(string id, string file, int frames, int columns, int rows, int frameMs,
            BattleEffectKind kind = BattleEffectKind.Impact, double size = 1d, double opacity = 1d,
            bool rotate = false, int[]? flight = null, int[]? impact = null, int loopStart = -1, int loopEnd = -1,
            double projectileSpeed = 1.25d)
        {
            if (columns * rows != frames || frames <= 0)
                throw new InvalidDataException($"La configuración del spritesheet '{file}' no coincide con su cantidad de frames.");
            result.Add(id, new BattleEffectDefinition(id, file, frames, columns, rows, frameMs,
                kind, size, opacity, rotate, flight, impact, loopStart, loopEnd, projectileSpeed));
        }

        // Type sheets: frames are 192×192, with their measured row/column counts.
        Add("bug", "bug.png", 15, 5, 3, 82); Add("dark", "dark.png", 5, 5, 1, 108);
        Add("dragon", "dragon.png", 10, 5, 2, 92); Add("electric", "electric.png", 15, 5, 3, 82);
        Add("fairy", "fairy.png", 15, 5, 3, 82, size: 1.5d); Add("fighting", "fighting.png", 15, 5, 3, 82);
        Add("fire", "fire.png", 15, 5, 3, 82); Add("flying", "flying.png", 15, 5, 3, 82, size: 1.5d);
        Add("ghost", "ghost.png", 15, 5, 3, 82); Add("grass", "grass.png", 15, 5, 3, 82);
        Add("ground", "ground.png", 15, 5, 3, 82, size: 1.5d); Add("ice", "ire.png", 15, 5, 3, 82, size: 1.5d);
        Add("normal", "normal.png", 4, 4, 1, 112); Add("poison", "poison.png", 15, 5, 3, 82);
        Add("psychic", "psychic.png", 10, 5, 2, 92); Add("rock", "rock.png", 10, 5, 2, 92);
        Add("steel", "steel.png", 5, 5, 1, 108); Add("water", "water.png", 15, 5, 3, 82, size: 1.5d);

        Add("shield", "shield.png", 5, 5, 1, 108, BattleEffectKind.Defense, opacity: .80d);
        Add("bone", "bone.png", 10, 5, 2, 86, BattleEffectKind.Projectile,
            flight: [0, 1, 2, 3, 4, 5], impact: [6, 7, 8, 9], loopStart: 3, loopEnd: 5, projectileSpeed: .90d);
        Add("bone_1", "bone_1.png", 10, 5, 2, 86, BattleEffectKind.Projectile,
            flight: [0, 1, 2, 3, 4, 5], impact: [6, 7, 8, 9], loopStart: 3, loopEnd: 5, projectileSpeed: .90d);
        Add("egg_bomb", "egg_bomb.png", 5, 5, 1, 112, BattleEffectKind.Projectile,
            rotate: true, flight: [0], impact: [1, 2, 3, 4], projectileSpeed: .95d);
        // The supplied Aura Sphere has five single-row frames: first 3 are flight,
        // last 2 provide the arrival/impact finish.
        Add("aura_sphere", "aura_sphere.png", 5, 5, 1, 104, BattleEffectKind.Projectile,
            flight: [0, 1, 2], impact: [3, 4], loopStart: 0, loopEnd: 2, projectileSpeed: 1.10d);
        return result;
    }

    internal static bool TryGet(string id, out BattleEffectDefinition definition) => Definitions.TryGetValue(id, out definition!);

    internal static IReadOnlyList<BitmapSource> GetFrames(BattleEffectDefinition definition)
    {
        lock (CacheLock)
        {
            if (FrameCache.TryGetValue(definition.Id, out var cached)) return cached;
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "pokemon", "fight_effect", definition.FileName);
            var sheet = LoadBitmap(path);
            if (sheet.PixelWidth != definition.Columns * CellSize || sheet.PixelHeight != definition.Rows * CellSize)
                throw new InvalidDataException($"El spritesheet '{definition.FileName}' mide {sheet.PixelWidth}×{sheet.PixelHeight}, incompatible con la cuadrícula configurada {definition.Columns}×{definition.Rows} de 192 px.");
            var frames = new BitmapSource[definition.FrameCount];
            var bounds = new Rect[definition.FrameCount];
            for (var index = 0; index < frames.Length; index++)
            {
                var cell = new CroppedBitmap(sheet, new Int32Rect((index % definition.Columns) * CellSize,
                    (index / definition.Columns) * CellSize, CellSize, CellSize));
                cell.Freeze();
                frames[index] = cell;
                bounds[index] = GetVisibleBounds(cell);
            }
            FrameCache.Add(definition.Id, frames);
            FrameBoundsCache.Add(definition.Id, bounds);
            return frames;
        }
    }

    internal static Rect GetFrameBounds(BattleEffectDefinition definition, int frameIndex)
    {
        _ = GetFrames(definition);
        lock (CacheLock)
            return FrameBoundsCache[definition.Id][Math.Clamp(frameIndex, 0, definition.FrameCount - 1)];
    }

    private static Rect GetVisibleBounds(BitmapSource frame)
    {
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var left = converted.PixelWidth; var top = converted.PixelHeight;
        var right = -1; var bottom = -1;
        for (var y = 0; y < converted.PixelHeight; y++)
        for (var x = 0; x < converted.PixelWidth; x++)
        {
            if (pixels[y * stride + x * 4 + 3] <= 8) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top)
            return new Rect((CellSize - ReferenceAttackExtent) / 2d,
                (CellSize - ReferenceAttackExtent) / 2d, ReferenceAttackExtent, ReferenceAttackExtent);
        return new Rect(left, top, right - left + 1, bottom - top + 1);
    }

    internal static BattleEffectDefinition PickAttack(DesktopCompanionWindow attacker, Random random)
    {
        var species = attacker.BattleSpeciesId;
        var form = attacker.BattleFormId;
        var (specialId, requiredType) = species switch
        {
            "cubone" => ("bone", "ground"),
            "marowak" when form.Equals("MAROWAK_1", StringComparison.OrdinalIgnoreCase) => ("bone_1", "ghost"),
            "marowak" => ("bone", "ground"),
            "happiny" or "chansey" or "blissey" => ("egg_bomb", "normal"),
            "lucario" => ("aura_sphere", "fighting"),
            _ => ((string?)null, (string?)null)
        };

        var typeIds = attacker.BattleTypes.Where(TypeEffectIds.ContainsKey)
            .Select(type => TypeEffectIds[type]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (specialId is not null && requiredType is not null
            && attacker.BattleTypes.Contains(requiredType, StringComparer.OrdinalIgnoreCase)
            && random.Next(100) < 65 && TryGet(specialId, out var special)) return special;
        if (typeIds.Length == 0) return Definitions["normal"];
        return Definitions[typeIds[random.Next(typeIds.Length)]];
    }

    internal static IReadOnlyList<string> GetTypesForForm(string formId, IReadOnlyList<string> baseTypes) =>
        MegaTypeOverrides.TryGetValue(formId, out var megaTypes) ? megaTypes : baseTypes;

    internal static string ValidateAndSummarize()
    {
        var lines = new List<string>();
        foreach (var definition in Definitions.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var frames = GetFrames(definition);
            if (frames.Count != definition.FrameCount || frames.Any(frame => frame.PixelWidth != CellSize || frame.PixelHeight != CellSize))
                throw new InvalidDataException($"El spritesheet '{definition.FileName}' no conserva sus frames de 192×192.");
            lines.Add($"{definition.FileName}: {definition.Columns}×{definition.Rows} = {definition.FrameCount} frames");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static BitmapSource LoadBitmap(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No se encontró el spritesheet de pelea.", path);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
        return image;
    }
}

/// <summary>One click-through overlay used only while an attack or defense animation is active.</summary>
internal sealed class BattleEffectWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static readonly IntPtr HwndTopmost = new(-1);
    private readonly BattleEffectDefinition _definition;
    private readonly IReadOnlyList<BitmapSource> _frames;
    private readonly Func<Point?> _attackerCenter;
    private readonly Func<Point?> _targetCenter;
    private readonly Image _image;
    private readonly RotateTransform _rotation = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double _baseSizePixels;
    private double _displaySizePixels;
    private readonly double _flightMilliseconds;

    internal IntPtr GetWindowHandle() => IsVisible ? new WindowInteropHelper(this).Handle : IntPtr.Zero;

    internal BattleEffectWindow(BattleEffectDefinition definition, DesktopCompanionWindow? attacker,
        DesktopCompanionWindow target, Action<BattleEffectWindow> ended)
    {
        _definition = definition;
        _frames = BattleEffectCatalog.GetFrames(definition);
        _attackerCenter = attacker is null ? target.GetBattleBodyCenter : attacker.GetBattleBodyCenter;
        _targetCenter = target.GetBattleBodyCenter;
        var targetHandle = new WindowInteropHelper(target).Handle;
        var targetDpi = targetHandle == IntPtr.Zero ? 0u : GetDpiForWindow(targetHandle);
        var targetDpiScale = targetDpi == 0 ? 1d : targetDpi / 96d;
        _baseSizePixels = BattleEffectCatalog.ReferenceAttackWindowSizeDip * targetDpiScale;
        _displaySizePixels = _baseSizePixels;
        var source = _attackerCenter(); var destination = _targetCenter();
        var distance = source.HasValue && destination.HasValue ? (destination.Value - source.Value).Length : 0d;
        _flightMilliseconds = Math.Clamp(distance / definition.ProjectileSpeedPixelsPerMillisecond, 320d, 1100d);

        Width = _displaySizePixels; Height = _displaySizePixels;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        IsHitTestVisible = false; UseLayoutRounding = true; SnapsToDevicePixels = true;
        _image = new Image
        {
            Width = Width, Height = Height, Stretch = Stretch.Fill, IsHitTestVisible = false,
            Opacity = definition.Opacity, RenderTransform = _rotation, RenderTransformOrigin = new Point(.5, .5),
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);
        Content = _image;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => TickFrame();
        SourceInitialized += (_, _) => MakeClickThrough();
        Loaded += (_, _) => { _timer.Start(); TickFrame(); };
        Closed += (_, _) => { _timer.Stop(); ended(this); };
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    private void TickFrame()
    {
        if (!IsVisible) return;
        var elapsed = _clock.Elapsed.TotalMilliseconds;
        Point? center;
        int frameIndex;
        var angle = 0d;
        if (_definition.Kind == BattleEffectKind.Projectile && elapsed < _flightMilliseconds)
        {
            var start = _attackerCenter(); var target = _targetCenter();
            if (!start.HasValue || !target.HasValue) { Close(); return; }
            var vector = target.Value - start.Value;
            var direction = vector.Length > .01d ? vector / vector.Length : new Vector(1, 0);
            var relativeScale = DesktopVisualScaleService.Current;
            var from = start.Value + direction * (22d * relativeScale);
            var to = target.Value - direction * (22d * relativeScale);
            var progress = Math.Clamp(elapsed / _flightMilliseconds, 0d, 1d);
            center = from + (to - from) * progress;
            frameIndex = FlightFrame(elapsed);
            if (_definition.RotateDuringFlight)
                angle = Math.Atan2(vector.Y, vector.X) * 180d / Math.PI + progress * 720d;
        }
        else if (_definition.Kind == BattleEffectKind.Projectile)
        {
            center = _targetCenter();
            var impactElapsed = Math.Max(0d, elapsed - _flightMilliseconds);
            var impactFrames = _definition.ImpactFrames ?? [];
            var impactIndex = (int)(impactElapsed / _definition.FrameMilliseconds);
            if (impactIndex >= impactFrames.Length) { Close(); return; }
            frameIndex = impactFrames[impactIndex];
        }
        else
        {
            center = _targetCenter();
            frameIndex = (int)(elapsed / _definition.FrameMilliseconds);
            if (frameIndex >= _definition.FrameCount) { Close(); return; }
        }

        if (!center.HasValue) { Close(); return; }
        frameIndex = Math.Clamp(frameIndex, 0, _frames.Count - 1);
        var frameBounds = BattleEffectCatalog.GetFrameBounds(_definition, frameIndex);
        var extent = Math.Max(frameBounds.Width, frameBounds.Height);
        _displaySizePixels = Math.Max(1d,
            _baseSizePixels * DesktopVisualScaleService.Current * _definition.SizeMultiplier
                * BattleEffectCatalog.ReferenceAttackExtent / extent);
        var frameScale = _displaySizePixels / 192d;
        var visibleOffset = new Vector(
            (frameBounds.Left + frameBounds.Width / 2d - 96d) * frameScale,
            (frameBounds.Top + frameBounds.Height / 2d - 96d) * frameScale);
        var radians = angle * Math.PI / 180d;
        var centeredOffset = new Vector(
            visibleOffset.X * Math.Cos(radians) - visibleOffset.Y * Math.Sin(radians),
            visibleOffset.X * Math.Sin(radians) + visibleOffset.Y * Math.Cos(radians));
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = handle == IntPtr.Zero ? 0u : GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1d : dpi / 96d;
        var dipSize = _displaySizePixels / scale;
        if (Math.Abs(Width - dipSize) > .25d)
        {
            Width = Height = dipSize;
            _image.Width = _image.Height = dipSize;
        }
        _image.Source = _frames[frameIndex];
        _rotation.Angle = angle;
        PlaceAt(center.Value - centeredOffset);
    }

    private int FlightFrame(double elapsedMilliseconds)
    {
        var flightFrames = _definition.FlightFrames ?? [0];
        var step = (int)(elapsedMilliseconds / _definition.FrameMilliseconds);
        if (_definition.FlightLoopStart >= 0 && _definition.FlightLoopEnd >= _definition.FlightLoopStart
            && flightFrames.Length > _definition.FlightLoopEnd)
        {
            var loopLength = _definition.FlightLoopEnd - _definition.FlightLoopStart + 1;
            if (step > _definition.FlightLoopEnd)
                step = _definition.FlightLoopStart + (step - _definition.FlightLoopStart) % loopLength;
        }
        return flightFrames[Math.Min(step, flightFrames.Length - 1)];
    }

    private void PlaceAt(Point center)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var dpi = GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1d : dpi / 96d;
        var width = Math.Max(1, (int)Math.Round(_displaySizePixels));
        var dipSize = _displaySizePixels / scale;
        if (Math.Abs(Width - dipSize) > .25d)
        {
            Width = Height = dipSize;
            _image.Width = _image.Height = dipSize;
        }
        _ = SetWindowPos(handle, App.OverlayInsertAfter, (int)Math.Round(center.X - width / 2d),
            (int)Math.Round(center.Y - width / 2d), width, width, SwpNoActivate | SwpNoOwnerZOrder);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
