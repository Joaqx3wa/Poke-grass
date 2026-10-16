using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;

namespace PokeGrass;

/// <summary>A click-through pixel-art sprite that follows the system cursor during a drag.</summary>
internal sealed class DragPreviewWindow : Window
{
    private static readonly HashSet<DragPreviewWindow> ActivePreviews = [];
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static readonly IntPtr HwndTopmost = new(-1);
    private readonly double _cursorOffsetX;
    private readonly double _cursorOffsetY;
    private readonly BitmapSource _sprite;
    private readonly Image _image;
    private readonly double _baseMaxWidth;
    private readonly double _baseMaxHeight;
    private readonly bool _scaleWithDesktopVisuals;
    private HwndSource? _source;
    private bool _hasPositioned;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;

    internal static event Action? ActivePreviewsChanged;

    internal static IReadOnlyList<IntPtr> GetActiveWindowHandles() => ActivePreviews
        .Where(preview => preview.IsVisible)
        .Select(preview => preview.GetWindowHandle())
        .Where(handle => handle != IntPtr.Zero)
        .ToArray();

    internal IntPtr GetWindowHandle() => IsVisible ? new WindowInteropHelper(this).Handle : IntPtr.Zero;

    internal DragPreviewWindow(BitmapSource source, double maxWidth, double maxHeight, double cursorOffset = 0,
        bool scaleWithDesktopVisuals = false)
    {
        _sprite = source;
        _baseMaxWidth = maxWidth;
        _baseMaxHeight = maxHeight;
        _scaleWithDesktopVisuals = scaleWithDesktopVisuals;
        _cursorOffsetX = cursorOffset;
        _cursorOffsetY = cursorOffset;
        var visualScale = scaleWithDesktopVisuals ? DesktopVisualScaleService.Current : 1d;
        var scale = Math.Min(maxWidth * visualScale / source.PixelWidth, maxHeight * visualScale / source.PixelHeight);
        Width = Math.Max(1, Math.Round(source.PixelWidth * scale));
        Height = Math.Max(1, Math.Round(source.PixelHeight * scale));
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _image = new Image
        {
            Source = source,
            Width = Width,
            Height = Height,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);
        Content = _image;

        SourceInitialized += (_, _) => MakeMouseTransparent();
        if (_scaleWithDesktopVisuals) DesktopVisualScaleService.Changed += OnDesktopVisualScaleChanged;
        Loaded += (_, _) =>
        {
            CompositionTarget.Rendering += OnRendering;
            UpdatePosition();
            ActivePreviews.Add(this);
            ActivePreviewsChanged?.Invoke();
        };
        Closed += (_, _) =>
        {
            if (_scaleWithDesktopVisuals) DesktopVisualScaleService.Changed -= OnDesktopVisualScaleChanged;
            CompositionTarget.Rendering -= OnRendering;
            ActivePreviews.Remove(this);
            ActivePreviewsChanged?.Invoke();
        };
    }

    private void OnDesktopVisualScaleChanged(double scale)
    {
        if (!_scaleWithDesktopVisuals || !IsVisible) return;
        var fit = Math.Min(_baseMaxWidth * scale / _sprite.PixelWidth, _baseMaxHeight * scale / _sprite.PixelHeight);
        Width = Math.Max(1d, Math.Round(_sprite.PixelWidth * fit));
        Height = Math.Max(1d, Math.Round(_sprite.PixelHeight * fit));
        _image.Width = Width;
        _image.Height = Height;
        UpdatePosition();
    }

    private void MakeMouseTransparent()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    private void OnRendering(object? sender, EventArgs e) => UpdatePosition();

    private void UpdatePosition()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetCursorPos(out var cursor)) return;
        var dpi = GetDpiForWindow(handle);
        var scale = dpi == 0 ? VisualTreeHelper.GetDpi(this).DpiScaleX : dpi / 96d;
        // Keep the sprite centered on the pointer hotspot. This avoids the old
        // lower-right bias while the layered window remains click-through.
        var x = (int)Math.Round(cursor.X - Width * scale / 2 + _cursorOffsetX * scale);
        var y = (int)Math.Round(cursor.Y - Height * scale / 2 + _cursorOffsetY * scale);
        if (_hasPositioned && x == _lastX && y == _lastY) return;
        // Establish topmost once. Reordering this layered HWND on every pointer
        // frame is expensive and makes the drag preview appear to move at low FPS.
        // The app's shared Z-order stack keeps it above the PC while dragging.
        var flags = SwpNoActivate | SwpNoOwnerZOrder;
        var insertAfter = HwndTopmost;
        if (_hasPositioned)
        {
            flags |= SwpNoZOrder;
            insertAfter = IntPtr.Zero;
        }
        else
        {
            _hasPositioned = true;
        }
        SetWindowPos(handle, insertAfter, x, y,
            (int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale),
            flags);
        _lastX = x;
        _lastY = y;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
}
