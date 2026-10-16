using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace PokeGrass;

/// <summary>Click-through pixel-art arena shown behind the two desktop battle participants.</summary>
internal sealed class BattleArenaWindow : Window
{
    private const double PreferredScale = .255d;
    private const double MaximumWidthDip = 450d;
    private const double MaximumHeightDip = 540d;
    private const double HorizontalOffsetDip = 0d;
    private const double VerticalOffsetDip = 0d;
    private const int FadeInMilliseconds = 650;
    private const int FadeOutMilliseconds = 750;
    private const int WmExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly Dictionary<string, BitmapSource> Backgrounds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BackgroundLoadWarnings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> BackgroundFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["bug"] = "bug.png", ["dark"] = "dark.png", ["dragon"] = "dragon.png", ["electric"] = "electric.png",
        ["fairy"] = "fairy.png", ["fighting"] = "fighting.png", ["fire"] = "fuego.png", ["flying"] = "flying.png",
        ["ghost"] = "ghost.png", ["god"] = "god.png", ["grass"] = "grass.png", ["ground"] = "ground.png",
        ["ice"] = "ice.png", ["legendary"] = "legendary.png", ["mega"] = "mega.png", ["normal"] = "normal.png",
        ["poison"] = "poison.png", ["poke1"] = "poke1.png", ["poke2"] = "poke2.png", ["poke3"] = "poke3.png",
        ["poke4"] = "poke4.png", ["psychic"] = "psychic.png", ["retro"] = "retro.png", ["rock"] = "rock.png",
        ["steel"] = "steel.png", ["water"] = "water.png"
    };

    private readonly Action<BattleArenaWindow> _closedCallback;
    private readonly BitmapSource _background;
    private readonly Image _image;
    private readonly DispatcherTimer _timer;
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private readonly Point _center;
    private readonly Forms.Screen _screen;
    private readonly double _baseWidthDip;
    private readonly double _baseHeightDip;
    private Func<bool>? _waitUntil;
    private DateTime _fadeOutStartedAt;
    private double _fadeOutStartOpacity;
    private bool _disposed;

    internal string BackgroundId { get; }

    internal System.Drawing.Rectangle GetPhysicalBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        return handle != IntPtr.Zero && GetWindowRect(handle, out var bounds)
            ? System.Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)
            : System.Drawing.Rectangle.Empty;
    }

    internal BattleArenaWindow(string backgroundId, Point center, Action<BattleArenaWindow> closedCallback)
    {
        BackgroundId = backgroundId;
        _center = center;
        _screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(center.X), (int)Math.Round(center.Y)));
        _closedCallback = closedCallback;
        _background = LoadBackground(backgroundId);

        var scale = Math.Min(PreferredScale, Math.Min(MaximumWidthDip / _background.PixelWidth,
            MaximumHeightDip / _background.PixelHeight));
        _baseWidthDip = _background.PixelWidth * scale;
        _baseHeightDip = _background.PixelHeight * scale;
        var visualScale = DesktopVisualScaleService.Current;
        var width = _baseWidthDip * visualScale;
        var height = _baseHeightDip * visualScale;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = width;
        Height = height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Opacity = 0d;
        UseLayoutRounding = false;
        SnapsToDevicePixels = true;

        _image = new Image
        {
            Source = _background,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);
        Content = _image;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => TickFade();
        SourceInitialized += (_, _) => MakeClickThrough();
        DesktopVisualScaleService.Changed += OnDesktopVisualScaleChanged;
        Closed += (_, _) =>
        {
            DesktopVisualScaleService.Changed -= OnDesktopVisualScaleChanged;
            _timer.Stop();
            _closedCallback(this);
        };
        Show();
        PlaceOnScreen();
        _timer.Start();
    }

    internal IntPtr GetWindowHandle()
    {
        return IsVisible ? new WindowInteropHelper(this).Handle : IntPtr.Zero;
    }

    internal void FadeOutWhen(Func<bool> condition)
    {
        _waitUntil = condition;
        _fadeOutStartedAt = DateTime.MinValue;
    }

    internal void FadeOutNow()
    {
        if (!IsVisible) return;
        _waitUntil = null;
        if (_fadeOutStartedAt != DateTime.MinValue) return;
        _fadeOutStartOpacity = Opacity;
        _fadeOutStartedAt = DateTime.UtcNow;
    }

    internal void CloseImmediately()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Close();
    }

    internal static bool IsBackgroundAvailable(string id)
    {
        try
        {
            _ = LoadBackground(id);
            return true;
        }
        catch (Exception exception)
        {
            if (BackgroundLoadWarnings.Add(id))
                EncounterDiagnostics.WriteException($"battle-background-load-{id}", exception);
            return false;
        }
    }

    internal static int ValidateBackgroundAssets()
    {
        foreach (var id in BackgroundFiles.Keys)
            _ = LoadBackground(id);
        return BackgroundFiles.Count;
    }

    private static BitmapSource LoadBackground(string id)
    {
        if (!BackgroundFiles.TryGetValue(id, out var fileName))
            throw new ArgumentOutOfRangeException(nameof(id), id, "El identificador de fondo no está registrado.");
        if (Backgrounds.TryGetValue(id, out var cached)) return cached;

        var path = Path.Combine(AppContext.BaseDirectory, "assets", "ui", "fondo_combate", fileName);
        if (!File.Exists(path)) throw new FileNotFoundException("No se encontró el fondo de combate.", path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        Backgrounds[id] = bitmap;
        return bitmap;
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, WmExStyle).ToInt64();
        SetWindowLongPtr(handle, WmExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    private void PlaceOnScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var dpi = GetDpiForWindow(handle);
        var dpiScale = dpi == 0 ? 1d : dpi / 96d;
        var pixelWidth = Math.Max(1, (int)Math.Round(Width * dpiScale));
        var pixelHeight = Math.Max(1, (int)Math.Round(Height * dpiScale));
        var work = _screen.WorkingArea;
        var centerX = _center.X + HorizontalOffsetDip * dpiScale;
        var centerY = _center.Y + VerticalOffsetDip * dpiScale;
        centerX = work.Width <= pixelWidth ? work.Left + work.Width / 2d
            : Math.Clamp(centerX, work.Left + pixelWidth / 2d, work.Right - pixelWidth / 2d);
        centerY = work.Height <= pixelHeight ? work.Top + work.Height / 2d
            : Math.Clamp(centerY, work.Top + pixelHeight / 2d, work.Bottom - pixelHeight / 2d);
        _ = SetWindowPos(handle, App.OverlayInsertAfter,
            (int)Math.Round(centerX - pixelWidth / 2d), (int)Math.Round(centerY - pixelHeight / 2d),
            pixelWidth, pixelHeight, SwpNoActivate | SwpNoOwnerZOrder);
        App.PositionBattleArenaBehindOverlayStack(handle);
    }

    private void OnDesktopVisualScaleChanged(double scale)
    {
        if (_disposed || !IsVisible) return;
        Width = _baseWidthDip * scale;
        Height = _baseHeightDip * scale;
        _image.Width = Width;
        _image.Height = Height;
        PlaceOnScreen();
    }

    private void TickFade()
    {
        if (!IsVisible) return;
        var now = DateTime.UtcNow;
        if (_waitUntil is not null)
        {
            if (_waitUntil()) return;
            _waitUntil = null;
            _fadeOutStartOpacity = Opacity;
            _fadeOutStartedAt = now;
        }

        if (_fadeOutStartedAt != DateTime.MinValue)
        {
            var progress = Math.Clamp((now - _fadeOutStartedAt).TotalMilliseconds / FadeOutMilliseconds, 0d, 1d);
            Opacity = _fadeOutStartOpacity * (1d - SmoothStep(progress));
            if (progress >= 1d) Close();
            return;
        }

        var fadeInProgress = Math.Clamp((now - _shownAt).TotalMilliseconds / FadeInMilliseconds, 0d, 1d);
        Opacity = SmoothStep(fadeInProgress);
    }

    private static double SmoothStep(double value) => value * value * (3d - 2d * value);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
}
