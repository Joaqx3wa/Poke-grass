using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Image = System.Windows.Controls.Image;
using Drawing = System.Drawing;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Cursors = System.Windows.Input.Cursors;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace PokeGrass;

internal sealed class GardenActionButtonsWindow : Window
{
    private readonly GardenWindow _garden;
    private readonly Canvas _canvas = new();
    private readonly Button[] _icons = new Button[3];
    private readonly bool[] _active = new bool[3];
    private readonly (string File, string Tip, Action Invoke)[] _actions;
    private double _lastButtonWidth = double.NaN;
    private double _lastButtonHeight = double.NaN;
    private double _lastGap = double.NaN;
    // The action art uses the whole native icon canvas; 72 logical px at garden 3×
    // restores a large, easy target without changing its aspect ratio.
    private const int ItemUnitAt3x = 72;
    private const int GapUnitAt3x = 8;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;

    internal GardenActionButtonsWindow(GardenWindow garden, Action settings, Action bag, Action pc)
    {
        _garden = garden;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Title = "PokéGrass · Accesos del jardín";
        Background = Brushes.Transparent; ShowInTaskbar = UiTestMode.Enabled; ShowActivated = false; Topmost = true;
        Content = _canvas;
        _actions = [("settings_054.png", "Configuración", settings), ("bag_053.png", "Bolsa", bag), ("pc_052.png", "PC", pc)];
        for (var i = 0; i < _actions.Length; i++)
        {
            var (file, tip, invoke) = _actions[i];
            var button = PixelButtonUi.IconButton($"ui/buttons/actions/{file}", tip, ItemUnitAt3x, ItemUnitAt3x, invoke, strongPress: true);
            Canvas.SetLeft(button, 0);
            Canvas.SetTop(button, i * (ItemUnitAt3x + GapUnitAt3x));
            _canvas.Children.Add(button);
            _icons[i] = button;
        }
        _lastButtonWidth = ItemUnitAt3x;
        _lastButtonHeight = ItemUnitAt3x;
        _lastGap = GapUnitAt3x;
        SourceInitialized += (_, _) => Reposition();
        Loaded += (_, _) => Reposition();
    }

    internal void Reposition()
    {
        if (!IsLoaded) return;
        var garden = _garden.GetPhysicalBounds();
        var dpi = _garden.GetDpiScale();
        var scale = _garden.Scale / 3d;
        // The garden is sized in physical pixels with SetWindowPos. Convert those same
        // pixel dimensions back to WPF DIPs for the toolbar content; otherwise WPF
        // applies the monitor DPI a second time and clips the fixed-size button content.
        var physicalUnit = ItemUnitAt3x * scale;
        var physicalGap = GapUnitAt3x * scale;
        var width = Math.Max(1, (int)Math.Ceiling(physicalUnit));
        var height = Math.Max(1, (int)Math.Ceiling(physicalUnit * 3 + physicalGap * 2));
        var buttonWidthDip = physicalUnit / dpi.X;
        var buttonHeightDip = physicalUnit / dpi.Y;
        var gapDip = physicalGap / dpi.Y;
        Width = width / dpi.X; Height = height / dpi.Y;
        _canvas.Width = Width; _canvas.Height = Height;
        if (Math.Abs(_lastButtonWidth - buttonWidthDip) > 0.01 || Math.Abs(_lastButtonHeight - buttonHeightDip) > 0.01 || Math.Abs(_lastGap - gapDip) > 0.01)
        {
            for (var i = 0; i < _icons.Length; i++)
            {
                if (_icons[i] is not null) _canvas.Children.Remove(_icons[i]);
                var (file, tip, invoke) = _actions[i];
                var button = PixelButtonUi.IconButton($"ui/buttons/actions/{file}", tip, buttonWidthDip, buttonHeightDip,
                    invoke, selected: _active[i], strongPress: true);
                Canvas.SetLeft(button, 0);
                _canvas.Children.Add(button);
                _icons[i] = button;
            }
            _lastButtonWidth = buttonWidthDip; _lastButtonHeight = buttonHeightDip; _lastGap = gapDip;
        }
        for (var i = 0; i < _icons.Length; i++) Canvas.SetTop(_icons[i], i * (buttonHeightDip + gapDip));
        var area = Forms.Screen.FromRectangle(garden).WorkingArea;
        var gap = Math.Max(1, (int)Math.Ceiling(4 * scale));
        var leftX = garden.Left - gap - width;
        var rightX = garden.Right + gap;
        var canLeft = leftX >= area.Left && leftX + width <= area.Right;
        var canRight = rightX >= area.Left && rightX + width <= area.Right;
        var x = canLeft ? leftX : canRight ? rightX : garden.Left - area.Left >= area.Right - garden.Right ? leftX : rightX;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
        var y = Math.Clamp(garden.Top + (garden.Height - height) / 2, area.Top, Math.Max(area.Top, area.Bottom - height));
        _ = SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, width, height, SwpNoActivate | SwpNoZOrder);
    }

    internal Drawing.Rectangle GetPhysicalBounds()
    {
        if (GetWindowRect(new WindowInteropHelper(this).Handle, out var rect))
            return Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var garden = _garden.GetPhysicalBounds(); var scale = _garden.Scale / 3d;
        return new Drawing.Rectangle(garden.Left - (int)Math.Ceiling(ItemUnitAt3x * scale) - (int)Math.Ceiling(4 * scale), garden.Top,
            (int)Math.Ceiling(ItemUnitAt3x * scale), (int)Math.Ceiling((ItemUnitAt3x * 3 + GapUnitAt3x * 2) * scale));
    }

    internal void SetWindowActive(int index, bool active)
    {
        if (index is < 0 or >= 3) throw new ArgumentOutOfRangeException(nameof(index));
        _active[index] = active;
        PixelButtonUi.SetIconButtonSelected(_icons[index], active);
    }

#if DEBUG
    internal void ClickActionDebug(int index)
    {
        if (index is < 0 or >= 3) throw new ArgumentOutOfRangeException(nameof(index));
        _icons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
#endif

    private static BitmapSource Load(string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", relative.Replace('/', Path.DirectorySeparatorChar));
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
internal sealed class GardenCornerButtonsWindow : Window
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private readonly GardenWindow _garden;
    private readonly Action _closeApplication;
    private readonly Canvas _canvas = new();
    private Button? _pinButton;
    private Button? _closeButton;
    private int _lastPinWidth = -1;
    private int _lastCloseWidth = -1;
    private int _lastHeight = -1;
    private int _lastGap = -1;
    private double _lastDpiX = double.NaN;
    private double _lastDpiY = double.NaN;

    internal GardenCornerButtonsWindow(GardenWindow garden, Action closeApplication)
    {
        _garden = garden;
        _closeApplication = closeApplication;
        Title = "PokéGrass · Controles del jardín";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _canvas;

        Loaded += (_, _) => Reposition();
        _garden.LocationChanged += (_, _) => Reposition();
        _garden.ScaleChanged += _ => Reposition();
        _garden.IsVisibleChanged += (_, _) =>
        {
            if (_garden.IsVisible) { Show(); Reposition(); }
            else Hide();
        };
    }

    internal void Reposition()
    {
        if (!IsLoaded || !_garden.IsVisible) return;
        var gardenBounds = _garden.GetPhysicalBounds();
        var dpi = _garden.GetDpiScale();
        var scale = _garden.Scale;
        var pinWidth = Math.Max(48, (int)Math.Round(58 * scale / 3d));
        var closeWidth = Math.Max(40, (int)Math.Round(46 * scale / 3d));
        var height = Math.Max(38, (int)Math.Round(44 * scale / 3d));
        var gap = Math.Max(3, (int)Math.Round(4 * scale / 3d));
        var area = Forms.Screen.FromRectangle(gardenBounds).WorkingArea;
        var fit = Math.Min(1d, Math.Min(area.Width / (double)(pinWidth + gap + closeWidth), area.Height / (double)height));
        if (fit < 1d)
        {
            pinWidth = Math.Max(1, (int)Math.Floor(pinWidth * fit));
            closeWidth = Math.Max(1, (int)Math.Floor(closeWidth * fit));
            gap = Math.Max(0, (int)Math.Floor(gap * fit));
            height = Math.Max(1, (int)Math.Floor(height * fit));
            while (pinWidth + gap + closeWidth > area.Width)
            {
                if (pinWidth >= closeWidth && pinWidth > 1) pinWidth--;
                else if (closeWidth > 1) closeWidth--;
                else if (gap > 0) gap--;
                else break;
            }
        }

        if (_lastPinWidth != pinWidth || _lastCloseWidth != closeWidth || _lastHeight != height || _lastGap != gap
            || Math.Abs(_lastDpiX - dpi.X) > 0.001 || Math.Abs(_lastDpiY - dpi.Y) > 0.001
            || _pinButton is null || _closeButton is null)
            RebuildButtons(pinWidth, closeWidth, height, gap, dpi.X, dpi.Y);

        var windowWidth = Math.Max(1, pinWidth + gap + closeWidth);
        var windowHeight = Math.Max(1, height);
        var x = gardenBounds.Right - windowWidth - Math.Max(2, scale * 2);
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - windowWidth));
        var preferredY = gardenBounds.Top - windowHeight - Math.Max(3, scale * 3);
        var belowY = gardenBounds.Bottom + Math.Max(3, scale * 3);
        var y = preferredY >= area.Top
            ? preferredY
            : belowY + windowHeight <= area.Bottom ? belowY : area.Top;
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - windowHeight));
        _ = SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y,
            windowWidth, windowHeight, SwpNoActivate | SwpNoZOrder);
    }

    private void RebuildButtons(int pinWidth, int closeWidth, int height, int gap, double dpiX, double dpiY)
    {
        _canvas.Children.Clear();
        _canvas.Width = (pinWidth + gap + closeWidth) / dpiX;
        _canvas.Height = height / dpiY;
        Width = _canvas.Width;
        Height = _canvas.Height;

        _pinButton = PixelButtonUi.Button(_garden.IsLocked ? "Soltar" : "Fijar", "green",
            pinWidth / dpiX, height / dpiY,
            () => { _garden.SetLocked(!_garden.IsLocked); _lastPinWidth = -1; Reposition(); }, fontSize: 12,
            toolTip: _garden.IsLocked ? "Permitir mover el jardín" : "Fijar el jardín en su posición");
        _closeButton = PixelButtonUi.Button("×", "red", closeWidth / dpiX, height / dpiY,
            ConfirmClose, fontSize: 18, toolTip: "Cerrar PokéGrass");
        Canvas.SetLeft(_pinButton, 0);
        Canvas.SetTop(_pinButton, 0);
        Canvas.SetLeft(_closeButton, (pinWidth + gap) / dpiX);
        Canvas.SetTop(_closeButton, 0);
        _canvas.Children.Add(_pinButton);
        _canvas.Children.Add(_closeButton);
        _lastPinWidth = pinWidth;
        _lastCloseWidth = closeWidth;
        _lastHeight = height;
        _lastGap = gap;
        _lastDpiX = dpiX;
        _lastDpiY = dpiY;
    }

    private void ConfirmClose()
    {
        if (PixelConfirmWindow.Confirm(this, "Cerrar PokéGrass",
            "¿Quieres cerrar PokéGrass y guardar el estado actual?"))
            _closeApplication();
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

internal sealed class SettingsWindow : Window
{
    private sealed record SavedPlacement(string Monitor, int X, int Y, double Width = 0, double Height = 0);
    private const double DesignWidth = 680;
    private const double DesignHeight = 530;
    private const uint MonitorDefaultToNearest = 2;
    private readonly Grid _outer = new();
    private Grid? _frame;
    private readonly GardenWindow _garden;
    private readonly Grid _page = new();
    private readonly EncounterAudio _testAudio = new();
    private readonly GameSaveStore _save;
    private readonly Action<string> _restartAfterImport;
    private readonly Button[] _tabs = new Button[3];
    private Grid? _tabPanel;
    private TextBlock? _volumeReadout;
    private TextBlock? _updateStatus;
    private bool _checkingUpdates;
    private bool _settingPosition;
    private readonly string _placementPath = Environment.GetEnvironmentVariable("POKEGRASS_SETTINGS_WINDOW_PATH") is { Length: > 0 } placementPath
        ? Path.GetFullPath(placementPath)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "settings-window.json");
    private int _tab;

    internal SettingsWindow(GardenWindow garden, GameSaveStore save, Action<string> restartAfterImport)
    {
        _garden = garden;
        _save = save;
        _restartAfterImport = restartAfterImport;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; AllowsTransparency = false;
        Title = "PokéGrass · Configuración";
        Background = Brushes.Transparent; ShowInTaskbar = UiTestMode.Enabled; Topmost = true; Width = DesignWidth; Height = DesignHeight;
        MinWidth = 520; MinHeight = 370;
        _outer.Margin = new Thickness(0); _outer.Background = new SolidColorBrush(Color.FromRgb(230, 238, 222));
        _frame = PixelButtonUi.NineSlice(PixelButtonUi.Load("ui/frames/decorative_1_large_001.png"), DesignWidth, DesignHeight, 32, 32);
        _outer.Children.Add(_frame);
        var contentLayer = new Grid { Width = DesignWidth, Height = DesignHeight };
        var layout = new Grid { Margin = new Thickness(36, 42, 36, 26) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        header.Background = PixelButtonUi.HitTestBackground;
        var title = new TextBlock { Text = "CONFIGURACIÓN", FontFamily = PixelUi.PixelFont, FontSize = 30,
            Foreground = new SolidColorBrush(Color.FromRgb(47, 44, 31)), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
        Grid.SetColumnSpan(title, 2);
        header.Children.Add(title);
        AdaptiveWindowSupport.DragFrom(this, header);
        var close = PixelButtonUi.Button("×", "red", 48, 46, Close, fontSize: 21, toolTip: "Cerrar configuración");
        close.HorizontalAlignment = System.Windows.HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Center;
        close.Margin = new Thickness(0, 0, 3, 0); Grid.SetColumn(close, 1); header.Children.Add(close);
        Grid.SetRow(header, 0); layout.Children.Add(header);
        _tabPanel = new Grid { Width = 476, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        for (var i = 0; i < 5; i++) _tabPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i % 2 == 0 ? 144 : 22) });
        var tabLabels = new[] { "General", "Jardín", "Sonido" };
        for (var i = 0; i < tabLabels.Length; i++)
        {
            var index = i;
            _tabs[i] = PixelButtonUi.Button(tabLabels[i], "blue", 144, 68, () => { _tab = index; RefreshPage(); }, fontSize: 19);
            _tabs[i].HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            Grid.SetColumn(_tabs[i], i * 2); _tabPanel.Children.Add(_tabs[i]);
        }
        Grid.SetRow(_tabPanel, 1); layout.Children.Add(_tabPanel);
        var pageFrame = new Border { Background = new SolidColorBrush(Color.FromRgb(251, 248, 226)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(77, 116, 92)), BorderThickness = new Thickness(3),
            Padding = new Thickness(24, 10, 24, 10), Margin = new Thickness(4, 0, 4, 4) };
        pageFrame.Child = _page;
        Grid.SetRow(pageFrame, 2); layout.Children.Add(pageFrame);
        contentLayer.Children.Add(layout);
        var contentView = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = contentLayer };
        RenderOptions.SetBitmapScalingMode(contentView, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(contentView, EdgeMode.Aliased);
        _outer.Children.Add(contentView);
        Content = _outer;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        { CaptionHeight = 0, ResizeBorderThickness = new Thickness(9), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        SizeChanged += (_, _) =>
        {
            if (_frame is null) return;
            _outer.Children.Remove(_frame);
            _frame = PixelButtonUi.NineSlice(PixelButtonUi.Load("ui/frames/decorative_1_large_001.png"), ActualWidth, ActualHeight, 32, 32);
            _outer.Children.Insert(0, _frame);
            KeepWindowAccessibleAndSave();
        };
        SourceInitialized += (_, _) => Position();
        Loaded += (_, _) => { Position(); RefreshPage(); };
        LocationChanged += (_, _) => KeepWindowAccessibleAndSave();
        _garden.ScaleChanged += _ => { if (_tab == 1) RefreshPage(); };
        Closed += (_, _) => { SavePlacement(); _testAudio.Dispose(); };
    }

    private void RefreshPage()
    {
        if (_tabPanel is not null)
        for (var i = 0; i < _tabs.Length; i++)
        {
            var index = i; var replacement = PixelButtonUi.Button(new[] { "General", "Jardín", "Sonido" }[i], i == _tab ? "green" : "blue", 144, 68,
                () => { _tab = index; RefreshPage(); }, selected: i == _tab, fontSize: 19);
            replacement.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            Grid.SetColumn(replacement, i * 2); var old = _tabs[i]; var at = _tabPanel.Children.IndexOf(old); _tabPanel.Children.Remove(old); _tabPanel.Children.Insert(at, replacement); _tabs[i] = replacement;
        }
        _page.Children.Clear();
        _page.RowDefinitions.Clear(); _page.ColumnDefinitions.Clear();
        var rows = new StackPanel { Margin = new Thickness(10, 3, 10, 2), VerticalAlignment = VerticalAlignment.Top };
        if (_tab == 0)
        {
            rows.VerticalAlignment = VerticalAlignment.Center;
            rows.Children.Add(SectionTitle("General"));
            var startupRow = new Grid { Margin = new Thickness(8, 4, 8, 8) };
            startupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            startupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            startupRow.Children.Add(new TextBlock { Text = "Inicio automático con Windows", FontFamily = PixelUi.PixelFont, FontSize = 19,
                Foreground = new SolidColorBrush(Color.FromRgb(47, 44, 31)), VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) });
            var startupEnabled = WindowsStartupRegistration.IsEnabled;
            var startupButton = PixelButtonUi.Button(startupEnabled ? "Activado" : "Desactivado", startupEnabled ? "green" : "blue", 156, 60,
                ToggleWindowsStartup, selected: !startupEnabled, fontSize: 15, toolTip: "Iniciar PokéGrass al entrar en Windows");
            startupButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            Grid.SetColumn(startupButton, 1); startupRow.Children.Add(startupButton);
            rows.Children.Add(startupRow);
            rows.Children.Add(SectionTitle("Datos de la partida"));
            rows.Children.Add(new TextBlock { Text = "Pokémon, cajas, equipo, inventario y efectos", FontFamily = PixelUi.PixelFont,
                FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(71, 64, 43)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(4, 0, 4, 5) });
            var dataButtons = new UniformGrid { Columns = 3, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            var exportButton = PixelButtonUi.Button("Exportar", "blue", 136, 60, ExportSave, fontSize: 16, toolTip: "Exportar la partida a un archivo");
            var importButton = PixelButtonUi.Button("Importar", "green", 136, 60, ImportSave, fontSize: 16, toolTip: "Cargar una partida desde un archivo");
            var backupsButton = PixelButtonUi.Button("Copias", "blue", 124, 60, OpenBackupManager, fontSize: 16, toolTip: "Ver, crear y restaurar copias de seguridad");
            exportButton.Margin = importButton.Margin = backupsButton.Margin = new Thickness(3, 0, 3, 0);
            dataButtons.Children.Add(exportButton); dataButtons.Children.Add(importButton); dataButtons.Children.Add(backupsButton);
            rows.Children.Add(dataButtons);
            rows.Children.Add(SectionTitle("Actualizaciones"));
            rows.Children.Add(new TextBlock { Text = $"Versión instalada: {AppUpdateService.InstalledVersion}", FontFamily = PixelUi.PixelFont,
                FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(71, 64, 43)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(4, 6, 4, 2) });
            _updateStatus = new TextBlock { Text = "Consulta GitHub para buscar una versión nueva.", FontFamily = PixelUi.PixelFont,
                FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(71, 64, 43)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 4) };
            rows.Children.Add(_updateStatus);
            var updateButton = PixelButtonUi.Button("Buscar actualizaciones", "green", 230, 54, CheckForUpdates,
                fontSize: 15, toolTip: "Consultar GitHub Releases y ofrecer la actualización disponible");
            updateButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            rows.Children.Add(updateButton);
        }
        else if (_tab == 1)
        {
            rows.Children.Add(SectionTitle("Escala del jardín"));
            var scaleRow = new UniformGrid { Columns = 4, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 10) };
            for (var scale = 1; scale <= 4; scale++)
            {
                var chosen = scale; var button = PixelButtonUi.Button($"{scale}×", scale == _garden.Scale ? "green" : "blue", 120, 60,
                    () => _garden.SetScale(chosen), selected: scale == _garden.Scale, fontSize: 22, toolTip: $"Escala {scale}×");
                button.Margin = new Thickness(3, 0, 3, 0); scaleRow.Children.Add(button);
            }
            rows.Children.Add(scaleRow);
            rows.Children.Add(SectionTitle("Tamaño de Pokémon y efectos"));
            var visualScaleRow = new UniformGrid { Columns = 3, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 8) };
            var visualScale = DesktopVisualScaleService.Current;
            foreach (var option in DesktopVisualScaleService.AllowedScales)
            {
                var chosen = option;
                var label = option switch { 1d => "Normal (x1)", 1.5d => "Grande (x1.5)", _ => "Muy grande (x2)" };
                var button = PixelButtonUi.Button(label, Math.Abs(option - visualScale) < .001d ? "green" : "blue", 132, 56,
                    () => { DesktopVisualScaleService.Set(chosen); RefreshPage(); },
                    selected: Math.Abs(option - visualScale) < .001d, fontSize: 14,
                    toolTip: $"Tamaño visual del escritorio: {label}");
                button.Margin = new Thickness(3, 0, 3, 0);
                visualScaleRow.Children.Add(button);
            }
            rows.Children.Add(visualScaleRow);
            rows.Children.Add(new TextBlock { Text = $"Escala actual: {_garden.Scale}×",
                FontFamily = PixelUi.PixelFont, FontSize = 18, Foreground = new SolidColorBrush(Color.FromRgb(47, 44, 31)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        }
        else
        {
            var soundRow = new Grid { Margin = new Thickness(8, 1, 8, 2) };
            soundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            soundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            soundRow.Children.Add(MakeSettingLabel("Efectos de sonido"));
            var soundButton = PixelButtonUi.Button(GameSoundSettings.Enabled ? "Activado" : "Desactivado", GameSoundSettings.Enabled ? "green" : "blue", 156, 60,
                () => { GameSoundSettings.Update(enabled: !GameSoundSettings.Enabled); RefreshPage(); }, selected: !GameSoundSettings.Enabled, fontSize: 16);
            soundButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Center; Grid.SetColumn(soundButton, 1); soundRow.Children.Add(soundButton);
            rows.Children.Add(soundRow);

            var volumeLabelRow = new Grid { Margin = new Thickness(8, 1, 8, 0) };
            volumeLabelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            volumeLabelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            volumeLabelRow.Children.Add(MakeSettingLabel("Volumen general"));
            _volumeReadout = new TextBlock { Text = $"{GameSoundSettings.Volume}%", FontFamily = PixelUi.PixelFont, FontSize = 22,
                Foreground = new SolidColorBrush(Color.FromRgb(47, 44, 31)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            Grid.SetColumn(_volumeReadout, 1); volumeLabelRow.Children.Add(_volumeReadout); rows.Children.Add(volumeLabelRow);

            rows.Children.Add(CreateVolumeSlider());
            var testButton = PixelButtonUi.Button("Probar sonido", "green", 156, 60,
                () =>
                {
                    EncounterDiagnostics.Write("audio-test", "Test sound button clicked.");
                    _testAudio.PlayEffect("sounds/effects/ui_click_back.mp3");
                }, fontSize: 18);
            testButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Center; testButton.Margin = new Thickness(0, 1, 0, 0);
            rows.Children.Add(testButton);
        }
        _page.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = rows
        });
    }

    private void ExportSave()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar partida de PokéGrass",
            Filter = "Guardado de PokéGrass (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"PokéGrass-{DateTime.Now:yyyy-MM-dd}.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _save.ExportToFile(dialog.FileName);
            System.Windows.MessageBox.Show(this, "La partida se exportó correctamente.", "PokéGrass",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"No se pudo exportar la partida.\n{exception.Message}", "PokéGrass",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenBackupManager()
    {
        new BackupManagerWindow(this, _save.SavePath, _restartAfterImport).ShowDialog();
    }

    private async void CheckForUpdates()
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        if (_updateStatus is not null) _updateStatus.Text = "Comprobando GitHub Releases…";
        try
        {
            var result = await AppUpdateService.CheckAsync();
            if (!result.IsInstalled)
            {
                if (_updateStatus is not null) _updateStatus.Text = "Las actualizaciones se activan al instalar PokéGrass con el Setup de Windows.";
                return;
            }
            if (result.Update is null)
            {
                if (_updateStatus is not null) _updateStatus.Text = "PokéGrass ya está actualizado.";
                return;
            }

            var version = result.Update.TargetFullRelease.Version.ToString();
            if (!PixelConfirmWindow.Confirm(this, "Actualización disponible",
                    $"Está disponible PokéGrass {version}. ¿Descargar e instalar ahora? Puedes posponerlo y volver a comprobar después."))
            {
                if (_updateStatus is not null) _updateStatus.Text = $"PokéGrass {version} está disponible; actualización pospuesta.";
                return;
            }

            if (_updateStatus is not null) _updateStatus.Text = "Creando una copia de seguridad antes de actualizar…";
            SaveBackupService.CreateAutomatic(_save.SavePath, force: true);
            await AppUpdateService.DownloadAsync(result.Manager, result.Update, progress =>
                Dispatcher.BeginInvoke(() => { if (_updateStatus is not null) _updateStatus.Text = $"Descargando actualización: {progress}%"; }));
            if (_updateStatus is not null) _updateStatus.Text = "Descarga verificada; reiniciando para instalar…";
            AppUpdateService.ApplyAndRestart(result.Manager, result.Update);
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException("update-check-or-install", exception);
            if (_updateStatus is not null) _updateStatus.Text = $"No se pudo completar la actualización: {exception.Message}";
        }
        finally { _checkingUpdates = false; }
    }

    private void ToggleWindowsStartup()
    {
        try
        {
            WindowsStartupRegistration.SetEnabled(!WindowsStartupRegistration.IsEnabled);
            RefreshPage();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"No se pudo actualizar el inicio automático.\n{exception.Message}",
                "PokéGrass", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportSave()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar partida de PokéGrass",
            Filter = "Guardado de PokéGrass (*.json)|*.json|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!PixelConfirmWindow.Confirm(this, "Importar partida",
                "La partida actual será reemplazada. Se guardará una copia de seguridad y PokéGrass se reiniciará para aplicar los datos. ¿Continuar?")) return;
        try
        {
            var backupPath = _save.ImportFromFile(dialog.FileName);
            _restartAfterImport(backupPath);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"No se pudo importar la partida.\n{exception.Message}", "PokéGrass",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static TextBlock SectionTitle(string text) => new() { Text = text, FontFamily = PixelUi.PixelFont, FontSize = 23,
        Foreground = new SolidColorBrush(Color.FromRgb(47, 44, 31)), Margin = new Thickness(2, 0, 2, 6) };
    private static TextBlock MakeSettingLabel(string text) => new() { Text = text, FontFamily = PixelUi.PixelFont, FontSize = 21,
        Foreground = new SolidColorBrush(Color.FromRgb(47, 44, 31)), VerticalAlignment = VerticalAlignment.Center };

    private UIElement CreateVolumeSlider()
    {
        const double width = 440;
        const double height = 64;
        const double trackLeft = 24;
        const double trackWidth = width - 2 * trackLeft;
        const double trackTop = 21;
        const double trackHeight = 20;
        const double thumbWidth = 18;
        const double thumbHeight = 34;
        var shell = new Grid { Width = width, Height = height, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4), ClipToBounds = false, Background = PixelButtonUi.HitTestBackground,
            Cursor = Cursors.SizeWE };
        var track = new Border { Width = trackWidth, Height = trackHeight, BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Color.FromRgb(37, 61, 55)), Background = new SolidColorBrush(Color.FromRgb(188, 204, 191)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(trackLeft, trackTop, 0, 0), IsHitTestVisible = false };
        shell.Children.Add(track);
        var visual = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
        var fill = new Border { Height = 12, Background = new SolidColorBrush(Color.FromRgb(53, 157, 91)) };
        Canvas.SetLeft(fill, trackLeft + 4); Canvas.SetTop(fill, trackTop + 5); visual.Children.Add(fill);
        var thumb = new Border { Width = thumbWidth, Height = thumbHeight, BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(Color.FromRgb(32, 53, 50)), Background = new SolidColorBrush(Color.FromRgb(239, 225, 163)) };
        Canvas.SetTop(thumb, (height - thumbHeight) / 2); visual.Children.Add(thumb);
        shell.Children.Add(visual);
        const double sliderInset = trackLeft + 2;
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = GameSoundSettings.Volume, Opacity = 0,
            VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(sliderInset, 0, sliderInset, 0), IsMoveToPointEnabled = true,
            TickFrequency = 1, IsSnapToTickEnabled = false, Focusable = true, IsHitTestVisible = false };
        System.Windows.Automation.AutomationProperties.SetName(slider, "Volumen general");
        System.Windows.Automation.AutomationProperties.SetHelpText(slider, "Arrastra para ajustar el volumen entre 0 y 100 por ciento");
        void RenderSliderValue(double rawValue)
        {
            var value = (int)Math.Round(rawValue);
            if (value != GameSoundSettings.Volume) GameSoundSettings.Update(volume: value);
            if (_volumeReadout is not null) _volumeReadout.Text = $"{value}%";
            var thumbX = Math.Round(trackLeft + 2 + (trackWidth - 4 - thumbWidth) * value / 100d);
            Canvas.SetLeft(thumb, thumbX);
            fill.Width = Math.Max(0, Math.Round(thumbX + thumbWidth / 2 - trackLeft - 4));
        }
        slider.ValueChanged += (_, _) => RenderSliderValue(slider.Value);
        shell.Children.Add(slider);
        var dragging = false;
        void SetValueAt(System.Windows.Point point)
        {
            var usableWidth = trackWidth - thumbWidth;
            slider.Value = Math.Clamp((point.X - trackLeft - thumbWidth / 2) / usableWidth * 100d, 0d, 100d);
        }
        shell.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var point = e.GetPosition(shell);
            if (point.Y < trackTop - 16 || point.Y > trackTop + trackHeight + 16) return;
            dragging = true;
            shell.CaptureMouse();
            SetValueAt(point);
            e.Handled = true;
        };
        shell.PreviewMouseMove += (_, e) =>
        {
            if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
            SetValueAt(e.GetPosition(shell));
            e.Handled = true;
        };
        shell.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            SetValueAt(e.GetPosition(shell));
            dragging = false;
            shell.ReleaseMouseCapture();
            e.Handled = true;
        };
        shell.LostMouseCapture += (_, _) => dragging = false;
        slider.Value = GameSoundSettings.Volume;
        RenderSliderValue(GameSoundSettings.Volume);
        return shell;
    }

    internal void SelectTabDebug(int index) { _tab = Math.Clamp(index, 0, 2); RefreshPage(); }

    private void Position()
    {
        if (!IsLoaded) return;
        var saved = LoadPlacement();
        var screens = Forms.Screen.AllScreens;
        var monitor = saved is null ? null : screens.FirstOrDefault(screen => string.Equals(screen.DeviceName, saved.Monitor, StringComparison.OrdinalIgnoreCase));
        var missingSavedMonitor = saved is not null && monitor is null;
        var gardenBounds = _garden.GetPhysicalBounds();
        monitor ??= saved is null ? Forms.Screen.FromRectangle(gardenBounds) : Forms.Screen.PrimaryScreen;
        monitor ??= screens.FirstOrDefault();
        if (monitor is null) return;

        var area = monitor.WorkingArea;
        var (dpiX, dpiY) = GetMonitorDpi(area);
        if (saved is { } placement && placement.Width >= MinWidth && placement.Height >= MinHeight)
        { Width = placement.Width; Height = placement.Height; }
        var width = (int)Math.Ceiling(Width * dpiX / 96d);
        var height = (int)Math.Ceiling(Height * dpiY / 96d);
        int x, y;
        if (saved is not null && !missingSavedMonitor)
        {
            x = saved.X; y = saved.Y;
            ClampToWorkingAreaWhenPossible(ref x, ref y, width, height, area, screens);
        }
        else if (missingSavedMonitor)
        {
            x = area.Left + (area.Width - width) / 2;
            y = area.Top + (area.Height - height) / 2;
        }
        else
        {
            var gap = (int)Math.Ceiling(10 * dpiX / 96d);
            var left = gardenBounds.Left - gap - width;
            var right = gardenBounds.Right + gap;
            x = left >= area.Left ? left : right;
            y = gardenBounds.Top + (gardenBounds.Height - height) / 2;
        }

        ClampPartiallyVisible(ref x, ref y, width, height, area);
        _settingPosition = true;
        _ = SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, width, height, 0x0010 | 0x0004);
        _settingPosition = false;
        SavePlacement();
    }

    private static (double X, double Y) GetMonitorDpi(Drawing.Rectangle area)
    {
        var point = new Drawing.Point(area.Left + Math.Max(1, area.Width / 2), area.Top + Math.Max(1, area.Height / 2));
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var x, out var y) == 0 && x > 0 && y > 0)
            return (x, y);
        return (96, 96);
    }

    private static void ClampPartiallyVisible(ref int x, ref int y, int width, int height, Drawing.Rectangle area)
    {
        if (width <= area.Width) x = Math.Clamp(x, area.Left, area.Right - width);
        else
        {
            var visible = Math.Min(180, area.Width);
            x = Math.Clamp(x, area.Left - width + visible, area.Right - visible);
        }

        if (height <= area.Height) y = Math.Clamp(y, area.Top, area.Bottom - height);
        else
        {
            var visible = Math.Min(64, area.Height);
            y = Math.Clamp(y, area.Top, area.Bottom - visible);
        }
    }

    private static bool HasVisibleWindowRegion(int x, int y, int width, int height, IEnumerable<Forms.Screen> screens)
    {
        var bounds = new Drawing.Rectangle(x, y, width, height);
        var minimumWidth = Math.Min(180, width);
        var minimumHeight = Math.Min(64, height);
        return screens.Any(screen =>
        {
            var visible = Drawing.Rectangle.Intersect(bounds, screen.WorkingArea);
            return visible.Width >= minimumWidth && visible.Height >= minimumHeight;
        });
    }

    private static void ClampToWorkingAreaWhenPossible(ref int x, ref int y, int width, int height, Drawing.Rectangle preferredArea, IEnumerable<Forms.Screen> screens)
    {
        var requestedX = x; var requestedY = y;
        var target = screens
            .Select(screen => (screen.WorkingArea, Intersection: Drawing.Rectangle.Intersect(new Drawing.Rectangle(requestedX, requestedY, width, height), screen.WorkingArea)))
            .OrderByDescending(item => item.Intersection.Width * item.Intersection.Height)
            .FirstOrDefault();
        var area = target.WorkingArea.Width > 0 ? target.WorkingArea : preferredArea;
        if (width <= area.Width && height <= area.Height)
        {
            x = Math.Clamp(x, area.Left, area.Right - width);
            y = Math.Clamp(y, area.Top, area.Bottom - height);
        }
        else if (!HasVisibleWindowRegion(x, y, width, height, screens)) ClampPartiallyVisible(ref x, ref y, width, height, area);
    }

    private SavedPlacement? LoadPlacement()
    {
        try
        {
            return File.Exists(_placementPath)
                ? System.Text.Json.JsonSerializer.Deserialize<SavedPlacement>(File.ReadAllText(_placementPath))
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private void SavePlacement()
    {
        if (_settingPosition || !IsLoaded) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return;
        var bounds = Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var screen = Forms.Screen.FromRectangle(bounds);
        try
        {
            var directory = Path.GetDirectoryName(_placementPath);
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            var temporary = _placementPath + ".tmp";
            File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(new SavedPlacement(screen.DeviceName, rect.Left, rect.Top, Width, Height)));
            File.Move(temporary, _placementPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void KeepWindowAccessibleAndSave()
    {
        if (!_settingPosition && IsLoaded)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero && GetWindowRect(handle, out var rect))
            {
                var bounds = Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                var screens = Forms.Screen.AllScreens;
                var visibleOnAnyScreen = screens.Any(screen =>
                {
                    var visible = Drawing.Rectangle.Intersect(bounds, screen.WorkingArea);
                    return visible.Width >= Math.Min(180, bounds.Width)
                        && visible.Height >= Math.Min(64, bounds.Height);
                });
                var x = rect.Left; var y = rect.Top;
                if (!visibleOnAnyScreen)
                {
                    var preferred = Forms.Screen.FromRectangle(bounds).WorkingArea;
                    ClampPartiallyVisible(ref x, ref y, bounds.Width, bounds.Height, preferred);
                }
                if (x != rect.Left || y != rect.Top)
                {
                    _settingPosition = true;
                    try { _ = SetWindowPos(handle, IntPtr.Zero, x, y, bounds.Width, bounds.Height, 0x0010 | 0x0004); }
                    finally { _settingPosition = false; }
                }
            }
        }
        SavePlacement();
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Drawing.Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
}
