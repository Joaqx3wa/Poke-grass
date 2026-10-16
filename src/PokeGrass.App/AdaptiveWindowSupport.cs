using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;
using Forms = System.Windows.Forms;

namespace PokeGrass;

/// <summary>Borderless resize affordances and per-window bounds persistence.</summary>
internal static class AdaptiveWindowSupport
{
    private sealed record Bounds(double Left, double Top, double Width, double Height);
    private sealed record Position(double Left, double Top);

    internal static void ConfigureMovable(Window window, string positionPath)
    {
        window.Loaded += (_, _) => RestorePosition(window, positionPath);
        window.LocationChanged += (_, _) => SavePosition(window, positionPath);
        window.Closed += (_, _) => SavePosition(window, positionPath);
    }

    internal static void Configure(Window window, string settingsPath, double minWidth, double minHeight)
    {
        window.MinWidth = minWidth;
        window.MinHeight = minHeight;
        window.ResizeMode = ResizeMode.CanResize;
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(9),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        window.Loaded += (_, _) => Restore(window, settingsPath);
        window.LocationChanged += (_, _) => Save(window, settingsPath);
        window.SizeChanged += (_, _) => Save(window, settingsPath);
        window.Closed += (_, _) => Save(window, settingsPath);
    }

    internal static void DragFrom(Window window, UIElement freeHeader)
    {
        freeHeader.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            if (IsInteractiveSource(e.OriginalSource as DependencyObject, freeHeader)) return;
            try { window.DragMove(); } catch (InvalidOperationException) { }
            e.Handled = true;
        };
    }

    private static bool IsInteractiveSource(DependencyObject? source, DependencyObject boundary)
    {
        while (source is not null && !ReferenceEquals(source, boundary))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or Selector
                or System.Windows.Controls.Primitives.ScrollBar
                or Thumb)
                return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private static void Restore(Window window, string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var state = JsonSerializer.Deserialize<Bounds>(File.ReadAllText(path));
            if (state is null || state.Width < window.MinWidth || state.Height < window.MinHeight) return;
            window.Width = state.Width;
            window.Height = state.Height;
            window.Left = state.Left;
            window.Top = state.Top;
            KeepAccessible(window);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { EncounterDiagnostics.WriteException($"window-restore:{window.Title}", e); }
    }

    private static void RestorePosition(Window window, string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var position = JsonSerializer.Deserialize<Position>(File.ReadAllText(path));
            if (position is null || !double.IsFinite(position.Left) || !double.IsFinite(position.Top)) return;
            window.Left = position.Left;
            window.Top = position.Top;
            KeepAccessible(window);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { EncounterDiagnostics.WriteException($"window-position-restore:{window.Title}", e); }
    }

    private static void SavePosition(Window window, string path)
    {
        if (!window.IsLoaded || window.WindowState != WindowState.Normal
            || !double.IsFinite(window.Left) || !double.IsFinite(window.Top)) return;
        try
        {
            KeepAccessible(window);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Position(window.Left, window.Top)));
            File.Move(temporary, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { EncounterDiagnostics.WriteException($"window-position-save:{window.Title}", e); }
    }

    private static void Save(Window window, string path)
    {
        if (!window.IsLoaded || window.WindowState != WindowState.Normal) return;
        try
        {
            KeepAccessible(window);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Bounds(window.Left, window.Top, window.Width, window.Height)));
            File.Move(temporary, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { EncounterDiagnostics.WriteException($"window-save:{window.Title}", e); }
    }

    internal static void KeepAccessible(Window window)
    {
        var screens = Forms.Screen.AllScreens;
        if (screens.Length == 0) return;
        var dpi = 1d;
        if (PresentationSource.FromVisual(window) is { CompositionTarget: not null } source)
            dpi = source.CompositionTarget.TransformToDevice.M11;
        var physicalX = (int)Math.Round(window.Left * dpi);
        var physicalY = (int)Math.Round(window.Top * dpi);
        var bounds = new System.Drawing.Rectangle(physicalX, physicalY,
            (int)Math.Ceiling(window.Width * dpi), (int)Math.Ceiling(window.Height * dpi));
        if (screens.Any(screen =>
        {
            var visible = System.Drawing.Rectangle.Intersect(bounds, screen.WorkingArea);
            return visible.Width >= Math.Min(180 * dpi, bounds.Width)
                && visible.Height >= Math.Min(72 * dpi, bounds.Height);
        })) return;

        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(physicalX, physicalY));
        var area = screen.WorkingArea;
        var visibleWidth = Math.Min(180 * dpi, bounds.Width);
        var visibleHeight = Math.Min(72 * dpi, bounds.Height);
        window.Left = Math.Clamp(window.Left, area.Left / dpi - window.Width + visibleWidth / dpi,
            (area.Right - visibleWidth) / dpi);
        window.Top = Math.Clamp(window.Top, area.Top / dpi - window.Height + visibleHeight / dpi,
            (area.Bottom - visibleHeight) / dpi);
    }
}
