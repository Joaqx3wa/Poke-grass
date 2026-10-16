using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace PokeGrass;

/// <summary>Clickable only on the opaque toy pixels; stays transparent to other apps elsewhere.</summary>
internal sealed class DesktopToyWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int HtClient = 1;
    private const int MaNoActivate = 3;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const int SourceScale = 2;
    internal const double PushSpeedPixelsPerSecond = 100d;
    internal const double SoloPlayPushSpeedPixelsPerSecond = 520d;
    internal const double CollisionDamping = .12d;
    private const double RollingFrictionPerSecond = 4.8d;
    internal const double SoloPlayFrictionPerSecond = 1.9d;
    private const double StopSpeedPixelsPerSecond = 2d;
    private const double MinimumThrowSpeedPixelsPerSecond = 70d;
    private const double MaximumThrowSpeedPixelsPerSecond = 2600d;
    private const double MaximumReleasedThrowSpeedPixelsPerSecond = 3800d;
    private const double ThrowSpeedMultiplier = 2.1d;
    private const double ThrowVelocitySmoothing = .85d;
    private const double ThrowVelocityForgetSeconds = .28d;
    private const double ManualThrowFrictionPerSecond = 2.4d;
    private const double WallRestitution = .58d;
    private const double WallTangentialDamping = .94d;
    private readonly Image _image;
    private readonly BitmapSource _scaledSource;
    private readonly bool[] _opaquePixels;
    private readonly double _opaqueRadiusSourcePixels;
    private readonly Action? _dragStarted;
    private readonly Action? _dragEnded;
    private readonly Func<Rect, string?, string?> _findCompanionCollision;
    private readonly Action<string, string>? _passReceived;
    private Canvas? _canvas;
    private double _visualScale = 1d;
    private readonly DispatcherTimer _physicsTimer;
    private HwndSource? _source;
    private Point _dragStartCursor;
    private Point _dragStartCenter;
    private Point _lastDragCursor;
    private long _lastDragTimestamp;
    private double _throwVelocityX;
    private double _throwVelocityY;
    private bool _dragPending;
    private bool _isDragging;
    private double _velocityX;
    private double _velocityY;
    private double _rotation;
    private double _rollingFrictionPerSecond = RollingFrictionPerSecond;
    private string? _ignoreCollisionWithRecordId;
    private DateTime _ignorePusherUntil;
    private DateTime _lastPhysicsTick;
    private Point? _passTarget;
    private string? _passPusherRecordId;
    private string? _passReceiverRecordId;
    private bool _pausedForTeamRally;

    internal bool IsBeingDragged => _isDragging;
    internal bool IsMoving => Math.Abs(_velocityX) + Math.Abs(_velocityY) > 12d;

    internal void PauseForTeamRally()
    {
        if (_pausedForTeamRally) return;
        _pausedForTeamRally = true;
        _physicsTimer.Stop();
    }

    internal void ResumeAfterTeamRally()
    {
        if (!_pausedForTeamRally) return;
        _pausedForTeamRally = false;
        _lastPhysicsTick = DateTime.UtcNow;
        if (IsVisible) _physicsTimer.Start();
    }

    internal DesktopToyWindow(Action? dragStarted = null, Action? dragEnded = null,
        Func<Rect, string?, string?>? findCompanionCollision = null, Action<string, string>? passReceived = null)
    {
        _dragStarted = dragStarted;
        _dragEnded = dragEnded;
        _findCompanionCollision = findCompanionCollision ?? ((_, _) => null);
        _passReceived = passReceived;
        var source = LoadImage(Path.Combine(AppContext.BaseDirectory, "assets", "items", "toy.png"));
        _opaquePixels = BuildOpaqueMask(source);
        _opaqueRadiusSourcePixels = FindOpaqueRadius(_opaquePixels, source.PixelWidth, source.PixelHeight);
        var frame = new TransformedBitmap(source, new ScaleTransform(SourceScale, SourceScale));
        frame.Freeze();
        _scaledSource = frame;
        _image = new Image
        {
            Source = frame,
            Width = frame.PixelWidth,
            Height = frame.PixelHeight,
            Stretch = Stretch.None,
            IsHitTestVisible = true,
            RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = new RotateTransform()
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);
        _canvas = new Canvas { Width = frame.PixelWidth, Height = frame.PixelHeight };
        _canvas.Children.Add(_image);
        Content = _canvas;
        DesktopVisualScaleService.Changed += OnDesktopVisualScaleChanged;
        Width = _canvas.Width;
        Height = _canvas.Height;
        UpdateVisualScale(DesktopVisualScaleService.Current, preserveCenter: false);
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _image.PreviewMouseLeftButtonDown += OnMouseDown;
        _image.PreviewMouseMove += OnMouseMove;
        _image.PreviewMouseLeftButtonUp += OnMouseUp;
        _lastPhysicsTick = DateTime.UtcNow;
        _physicsTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _physicsTimer.Tick += OnPhysicsTick;
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WindowHook);
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate));
        };
        Closed += (_, _) =>
        {
            DesktopVisualScaleService.Changed -= OnDesktopVisualScaleChanged;
            if (_image.IsMouseCaptured) _image.ReleaseMouseCapture();
            _physicsTimer.Stop();
            _source?.RemoveHook(WindowHook);
        };
        _physicsTimer.Start();
    }

    internal void MoveCenter(Point center)
    {
        var actual = PlaceCenter(center);
        if (actual.X != center.X) _velocityX = 0;
        if (actual.Y != center.Y) _velocityY = 0;
    }

    private void OnDesktopVisualScaleChanged(double scale) => UpdateVisualScale(scale, preserveCenter: true);

    private void UpdateVisualScale(double scale, bool preserveCenter)
    {
        var center = preserveCenter ? GetScreenCenter() : default;
        _visualScale = scale;
        if (_canvas is not null)
        {
            _canvas.LayoutTransform = Math.Abs(scale - 1d) < .001d ? null : new ScaleTransform(scale, scale);
            RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(_canvas, EdgeMode.Aliased);
            Width = Math.Max(1d, _canvas.Width * scale);
            Height = Math.Max(1d, _canvas.Height * scale);
        }
        if (preserveCenter && IsVisible) MoveCenter(center);
    }

    internal void Push(Point direction, string pusherRecordId, bool soloPlay = false)
    {
        if (_isDragging) return;
        ClearPass();
        _ignoreCollisionWithRecordId = pusherRecordId;
        _ignorePusherUntil = DateTime.UtcNow.AddMilliseconds(550);
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < .001d) return;
        // A nudge should roll the toy only a short distance. Replacing the
        // velocity also prevents repeated contacts from accumulating into a launch.
        var speed = soloPlay ? SoloPlayPushSpeedPixelsPerSecond : PushSpeedPixelsPerSecond;
        _rollingFrictionPerSecond = soloPlay ? SoloPlayFrictionPerSecond : RollingFrictionPerSecond;
        _velocityX = direction.X / length * speed;
        _velocityY = direction.Y / length * speed;
    }

    internal bool PassTo(Point target, string pusherRecordId, string receiverRecordId)
    {
        if (_isDragging) return false;
        var center = GetScreenCenter();
        var dx = target.X - center.X;
        var dy = target.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var bounds = GetScreenBounds();
        if (distance < Math.Max(48d, bounds.Width * 1.5d) || bounds.IsEmpty) return false;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
        var targetScreen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)target.X, (int)target.Y));
        if (screen.DeviceName != targetScreen.DeviceName) return false;
        var area = screen.WorkingArea;
        var halfWidth = bounds.Width / 2d;
        var halfHeight = bounds.Height / 2d;
        if (target.X < area.Left + halfWidth || target.X > area.Right - halfWidth
            || target.Y < area.Top + halfHeight || target.Y > area.Bottom - halfHeight) return false;
        _passTarget = target;
        _passPusherRecordId = pusherRecordId;
        _passReceiverRecordId = receiverRecordId;
        _ignoreCollisionWithRecordId = pusherRecordId;
        _ignorePusherUntil = DateTime.UtcNow.AddMilliseconds(450);
        var speed = Math.Clamp(distance * 1.05d, 180d, 320d);
        _velocityX = dx / distance * speed;
        _velocityY = dy / distance * speed;
        _lastPhysicsTick = DateTime.UtcNow;
        return true;
    }

    internal Point GetScreenCenter()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return new Point(0, 0);
        return new Point(bounds.Left + (bounds.Right - bounds.Left) / 2d, bounds.Top + (bounds.Bottom - bounds.Top) / 2d);
    }

    internal Rect GetScreenBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return Rect.Empty;
        return new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    internal Rect GetCollisionBounds()
    {
        var center = GetScreenCenter();
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = handle == IntPtr.Zero ? 96u : GetDpiForWindow(handle);
        var radius = _opaqueRadiusSourcePixels * SourceScale * _visualScale * (dpi == 0 ? VisualTreeHelper.GetDpi(this).DpiScaleX : dpi / 96d);
        return new Rect(center.X - radius, center.Y - radius, radius * 2d, radius * 2d);
    }

    internal double GetAvailableRollDistance(Point direction)
    {
        var center = GetScreenCenter();
        var radius = GetCollisionBounds().Width / 2d;
        var screens = System.Windows.Forms.Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
        var current = screens.FirstOrDefault(area => center.X >= area.Left && center.X < area.Right
            && center.Y >= area.Top && center.Y < area.Bottom);
        if (current.IsEmpty) return 0d;

        if (direction.X > 0)
        {
            var edge = (double)current.Right;
            for (var i = 0; i < screens.Length; i++)
            {
                var next = screens.Where(area => center.Y >= area.Top && center.Y < area.Bottom
                        && area.Left <= edge + 1d && area.Right > edge + 1d)
                    .OrderByDescending(area => area.Right).FirstOrDefault();
                if (next.IsEmpty) break;
                edge = next.Right;
            }
            return Math.Max(0d, edge - center.X - radius);
        }
        if (direction.X < 0)
        {
            var edge = (double)current.Left;
            for (var i = 0; i < screens.Length; i++)
            {
                var next = screens.Where(area => center.Y >= area.Top && center.Y < area.Bottom
                        && area.Right >= edge - 1d && area.Left < edge - 1d)
                    .OrderBy(area => area.Left).FirstOrDefault();
                if (next.IsEmpty) break;
                edge = next.Left;
            }
            return Math.Max(0d, center.X - edge - radius);
        }
        if (direction.Y > 0)
        {
            var edge = (double)current.Bottom;
            for (var i = 0; i < screens.Length; i++)
            {
                var next = screens.Where(area => center.X >= area.Left && center.X < area.Right
                        && area.Top <= edge + 1d && area.Bottom > edge + 1d)
                    .OrderByDescending(area => area.Bottom).FirstOrDefault();
                if (next.IsEmpty) break;
                edge = next.Bottom;
            }
            return Math.Max(0d, edge - center.Y - radius);
        }
        if (direction.Y < 0)
        {
            var edge = (double)current.Top;
            for (var i = 0; i < screens.Length; i++)
            {
                var next = screens.Where(area => center.X >= area.Left && center.X < area.Right
                        && area.Bottom >= edge - 1d && area.Top < edge - 1d)
                    .OrderBy(area => area.Top).FirstOrDefault();
                if (next.IsEmpty) break;
                edge = next.Top;
            }
            return Math.Max(0d, center.Y - edge - radius);
        }
        return 0d;
    }

    private void OnPhysicsTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Clamp((now - _lastPhysicsTick).TotalSeconds, 0, .05);
        _lastPhysicsTick = now;
        if (_isDragging || !IsVisible) return;

        if (_passTarget is { } passTarget)
        {
            TickPass(passTarget, elapsed, now);
            return;
        }

        var speed = Math.Sqrt(_velocityX * _velocityX + _velocityY * _velocityY);
        if (speed < StopSpeedPixelsPerSecond) { _velocityX = 0; _velocityY = 0; return; }

        var center = GetScreenCenter();
        var intended = new Point(center.X + _velocityX * elapsed, center.Y + _velocityY * elapsed);
        var radius = GetCollisionBounds().Width / 2d;
        var proposedBounds = new Rect(intended.X - radius, intended.Y - radius, radius * 2d, radius * 2d);
        var ignoredPusher = now < _ignorePusherUntil ? _ignoreCollisionWithRecordId : null;
        if (_findCompanionCollision(proposedBounds, ignoredPusher) is not null)
        {
            // Settle at contact instead of reflecting repeatedly between the
            // same companion and toy, which looked like vibration.
            _velocityX *= CollisionDamping;
            _velocityY *= CollisionDamping;
            _lastPhysicsTick = now;
            return;
        }
        var placed = PlaceCenter(intended);
        var hitHorizontalWall = Math.Abs(placed.X - intended.X) > .5d;
        var hitVerticalWall = Math.Abs(placed.Y - intended.Y) > .5d;
        if (hitHorizontalWall)
        {
            _velocityX = -_velocityX * WallRestitution;
            _velocityY *= WallTangentialDamping;
        }
        if (hitVerticalWall)
        {
            _velocityY = -_velocityY * WallRestitution;
            _velocityX *= WallTangentialDamping;
        }

        var distance = speed * elapsed;
        _rotation += distance * 360d / (Math.PI * 19d);
        ((RotateTransform)_image.RenderTransform).Angle = _rotation % 360d;
        var friction = GetRollingFriction(elapsed, _rollingFrictionPerSecond);
        _velocityX *= friction;
        _velocityY *= friction;
        if (Math.Sqrt(_velocityX * _velocityX + _velocityY * _velocityY) < StopSpeedPixelsPerSecond)
            _velocityX = _velocityY = 0;
    }

    internal static double GetRollingFriction(double elapsedSeconds, double frictionPerSecond = RollingFrictionPerSecond)
        => Math.Exp(-frictionPerSecond * Math.Max(0d, elapsedSeconds));

    private void TickPass(Point target, double elapsed, DateTime now)
    {
        var center = GetScreenCenter();
        var dx = target.X - center.X;
        var dy = target.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var speed = Math.Sqrt(_velocityX * _velocityX + _velocityY * _velocityY);
        var travel = speed * elapsed;
        if (distance <= travel + 1d)
        {
            PlaceCenter(target);
            FinishPass();
            return;
        }

        var intended = new Point(center.X + dx / distance * travel, center.Y + dy / distance * travel);
        var placed = PlaceCenter(intended);
        if (Math.Abs(placed.X - intended.X) > .5 || Math.Abs(placed.Y - intended.Y) > .5)
        {
            FinishPass();
            return;
        }
        _rotation += travel * 360d / (Math.PI * 19d);
        ((RotateTransform)_image.RenderTransform).Angle = _rotation % 360d;
    }

    private void FinishPass(string? interceptedBy = null)
    {
        var pusher = _passPusherRecordId;
        var receiver = interceptedBy ?? _passReceiverRecordId;
        ClearPass();
        _velocityX = _velocityY = 0;
        if (pusher is not null && receiver is not null) _passReceived?.Invoke(pusher, receiver);
    }

    private void ClearPass()
    {
        _passTarget = null;
        _passPusherRecordId = null;
        _passReceiverRecordId = null;
    }

    private Point PlaceCenter(Point center)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return center;
        if (!GetWindowRect(handle, out var currentBounds)) return center;
        var width = currentBounds.Right - currentBounds.Left;
        var height = currentBounds.Bottom - currentBounds.Top;
        var availableScreens = System.Windows.Forms.Screen.AllScreens;
        if (availableScreens.Length == 0) return center;

        // Keep the toy center inside the union of the monitors, rather than
        // clamping its whole window to the monitor it started on. That lets it
        // roll continuously across a shared screen edge.
        var centerX = center.X;
        var centerY = center.Y;
        var containingScreen = availableScreens.FirstOrDefault(screen =>
            centerX >= screen.WorkingArea.Left && centerX < screen.WorkingArea.Right
            && centerY >= screen.WorkingArea.Top && centerY < screen.WorkingArea.Bottom);
        if (containingScreen is null)
        {
            var nearest = availableScreens
                .Select(screen =>
                {
                    var area = screen.WorkingArea;
                    var x = Math.Clamp(centerX, area.Left, area.Right - 1d);
                    var y = Math.Clamp(centerY, area.Top, area.Bottom - 1d);
                    var dx = centerX - x;
                    var dy = centerY - y;
                    return (X: x, Y: y, DistanceSquared: dx * dx + dy * dy);
                })
                .MinBy(point => point.DistanceSquared);
            centerX = nearest.X;
            centerY = nearest.Y;
        }
        var x = (int)Math.Round(centerX - width / 2d);
        var y = (int)Math.Round(centerY - height / 2d);
        // Preserve the z-order established when shown. Companions can then be
        // raised above the toy without every physics tick placing it back on top.
        SetWindowPos(handle, IntPtr.Zero, x, y, width, height,
            SwpNoActivate | SwpNoSize | SwpNoOwnerZOrder | SwpNoZOrder);
        return new Point(x + width / 2d, y + height / 2d);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !GetCursorPos(out var cursor)) return;
        ClearPass();
        _velocityX = 0;
        _velocityY = 0;
        _dragPending = true;
        _isDragging = false;
        _dragStartCursor = _lastDragCursor = new Point(cursor.X, cursor.Y);
        _lastDragTimestamp = Stopwatch.GetTimestamp();
        _throwVelocityX = _throwVelocityY = 0;
        _dragStartCenter = GetScreenCenter();
        _image.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragPending || e.LeftButton != MouseButtonState.Pressed || !GetCursorPos(out var cursor)) return;
        var current = new Point(cursor.X, cursor.Y);
        var dx = current.X - _dragStartCursor.X;
        var dy = current.Y - _dragStartCursor.Y;
        var nowTimestamp = Stopwatch.GetTimestamp();
        var sampleSeconds = Math.Max(.001d, Stopwatch.GetElapsedTime(_lastDragTimestamp, nowTimestamp).TotalSeconds);
        if (!_isDragging && Math.Abs(dx) + Math.Abs(dy) >= 5)
        {
            _isDragging = true;
            _dragStarted?.Invoke();
        }
        if (!_isDragging) return;
        var stepX = current.X - _lastDragCursor.X;
        var stepY = current.Y - _lastDragCursor.Y;
        _rotation += Math.Sqrt(stepX * stepX + stepY * stepY) * 360d / (Math.PI * 19d);
        ((RotateTransform)_image.RenderTransform).Angle = _rotation % 360d;
        var sampledVelocityX = Math.Clamp(stepX / sampleSeconds, -MaximumThrowSpeedPixelsPerSecond, MaximumThrowSpeedPixelsPerSecond);
        var sampledVelocityY = Math.Clamp(stepY / sampleSeconds, -MaximumThrowSpeedPixelsPerSecond, MaximumThrowSpeedPixelsPerSecond);
        _throwVelocityX = _throwVelocityX * (1d - ThrowVelocitySmoothing) + sampledVelocityX * ThrowVelocitySmoothing;
        _throwVelocityY = _throwVelocityY * (1d - ThrowVelocitySmoothing) + sampledVelocityY * ThrowVelocitySmoothing;
        PlaceCenter(new Point(_dragStartCenter.X + dx, _dragStartCenter.Y + dy));
        _lastDragCursor = current;
        _lastDragTimestamp = nowTimestamp;
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragPending || e.ChangedButton != MouseButton.Left) return;
        _dragPending = false;
        if (_image.IsMouseCaptured) _image.ReleaseMouseCapture();
        if (_isDragging)
        {
            _isDragging = false;
            var timeSinceLastMove = Stopwatch.GetElapsedTime(_lastDragTimestamp).TotalSeconds;
            var velocityDecay = timeSinceLastMove > ThrowVelocityForgetSeconds
                ? 0d
                : Math.Exp(-4d * timeSinceLastMove);
            var throwX = _throwVelocityX * velocityDecay * ThrowSpeedMultiplier;
            var throwY = _throwVelocityY * velocityDecay * ThrowSpeedMultiplier;
            var throwSpeed = Math.Sqrt(throwX * throwX + throwY * throwY);
            if (throwSpeed >= MinimumThrowSpeedPixelsPerSecond)
            {
                var releaseScale = Math.Min(1d, MaximumReleasedThrowSpeedPixelsPerSecond / throwSpeed);
                throwX *= releaseScale;
                throwY *= releaseScale;
                _velocityX = throwX;
                _velocityY = throwY;
                _rollingFrictionPerSecond = ManualThrowFrictionPerSecond;
                _ignoreCollisionWithRecordId = null;
                _ignorePusherUntil = DateTime.MinValue;
                _lastPhysicsTick = DateTime.UtcNow;
            }
            _dragEnded?.Invoke();
        }
        e.Handled = true;
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest && GetWindowRect(hwnd, out var bounds))
        {
            var packed = lParam.ToInt64();
            var screenX = unchecked((short)(packed & 0xffff));
            var screenY = unchecked((short)((packed >> 16) & 0xffff));
            var width = Math.Max(1, bounds.Right - bounds.Left);
            var height = Math.Max(1, bounds.Bottom - bounds.Top);
            var pixelX = (int)((screenX - bounds.Left) * _scaledSource.PixelWidth / (double)width);
            var pixelY = (int)((screenY - bounds.Top) * _scaledSource.PixelHeight / (double)height);
            var sourceX = pixelX / SourceScale;
            var sourceY = pixelY / SourceScale;
            var sourceWidth = _scaledSource.PixelWidth / SourceScale;
            var sourceHeight = _scaledSource.PixelHeight / SourceScale;
            if (sourceX < 0 || sourceY < 0 || sourceX >= sourceWidth
                || sourceY >= sourceHeight
                || !_opaquePixels[sourceY * sourceWidth + sourceX])
            {
                handled = true;
                return new IntPtr(HtTransparent);
            }
            handled = true;
            return new IntPtr(HtClient);
        }
        if (message == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }
        return IntPtr.Zero;
    }

    private static bool[] BuildOpaqueMask(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var mask = new bool[converted.PixelWidth * converted.PixelHeight];
        for (var y = 0; y < converted.PixelHeight; y++)
        for (var x = 0; x < converted.PixelWidth; x++)
            mask[y * converted.PixelWidth + x] = pixels[y * stride + x * 4 + 3] > 12;
        return mask;
    }

    private static double FindOpaqueRadius(bool[] mask, int width, int height)
    {
        var centerX = width / 2d;
        var centerY = height / 2d;
        var radius = 0d;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (!mask[y * width + x]) continue;
            var dx = x + .5d - centerX;
            var dy = y + .5d - centerY;
            radius = Math.Max(radius, Math.Sqrt(dx * dx + dy * dy));
        }
        return Math.Max(1d, radius);
    }

    private static BitmapSource LoadImage(string path)
    {
        using var input = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = input;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out NativePoint point);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
}
