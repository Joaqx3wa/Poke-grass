using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FontFamily = System.Windows.Media.FontFamily;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace PokeGrass;

internal static class UiTestMode
{
    internal static bool Enabled => string.Equals(Environment.GetEnvironmentVariable("POKEGRASS_UI_TEST_MODE"), "1", StringComparison.Ordinal);
}

internal static class PixelButtonUi
{
    private sealed class IconButtonVisualState(Grid background, BitmapSource normal, BitmapSource hover,
        BitmapSource pressed, Border pressShade, bool strongPress)
    {
        internal Grid Background { get; } = background;
        internal BitmapSource Normal { get; } = normal;
        internal BitmapSource Hover { get; } = hover;
        internal BitmapSource Pressed { get; } = pressed;
        internal Border PressShade { get; } = pressShade;
        internal bool StrongPress { get; } = strongPress;
        internal bool Selected { get; set; }
        internal bool PointerPressed { get; set; }
    }

    private static readonly Uri FontUri = new("pack://application:,,,/assets/fonts/");
    internal static FontFamily Font => new(FontUri, "#Minecraft");
    // Layered WPF windows pass native mouse input through pixels whose alpha is exactly zero.
    // A nearly invisible backing fill makes the entire useful button rectangle reliable while
    // keeping the sprite's transparent visual corners effectively transparent.
    internal static System.Windows.Media.Brush HitTestBackground { get; } = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));

    internal static BitmapSource Load(string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", relative.Replace('/', Path.DirectorySeparatorChar));
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image;
    }

    internal static Button Button(string label, string color, double width, double height, Action action,
        bool enabled = true, bool selected = false, double fontSize = 13, string? toolTip = null)
    {
        var normal = Load(State(color, enabled ? "normal" : "disabled"));
        var hover = enabled ? Load(State(color, "hover")) : normal;
        var pressed = enabled ? Load(State(color, "pressed")) : normal;
        var background = NineSlice(normal, width, height);
        var text = new TextBlock
        {
            Text = label, FontFamily = Font, FontSize = fontSize,
            Foreground = enabled
                ? color is "green" or "yellow" ? new SolidColorBrush(Color.FromRgb(24, 41, 34)) : Brushes.White
                : new SolidColorBrush(Color.FromRgb(86, 86, 89)),
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 2, 8, 2), IsHitTestVisible = false
        };
        // This grid is the actual hit surface for the bare button template. Its visual layers
        // remain click-through, while the alpha-1 background gives the whole button a native hit area.
        var content = new Grid { Width = width, Height = height, Background = HitTestBackground, IsHitTestVisible = true };
        content.Children.Add(background); content.Children.Add(text);
        var button = new Button
        {
            Content = content, Width = width, Height = height, IsEnabled = enabled, IsHitTestVisible = enabled,
            Background = HitTestBackground, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Cursor = enabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
            ToolTip = toolTip ?? label, Template = BareButtonTemplate()
        };
        if (enabled)
        {
            SetState(background, selected ? pressed : normal);
            button.MouseEnter += (_, _) => SetState(background, selected ? pressed : hover);
            button.MouseLeave += (_, _) => SetState(background, selected ? pressed : normal);
            button.PreviewMouseLeftButtonDown += (_, _) => SetState(background, pressed);
            button.PreviewMouseLeftButtonUp += (_, _) => SetState(background, selected ? pressed : hover);
            button.Click += (_, _) => action();
        }
        return button;
    }

    internal static Button IconButton(string iconPath, string tip, double size, Action action,
        bool enabled = true, bool selected = false, bool strongPress = false)
        => IconButton(iconPath, tip, size, size, action, enabled, selected, strongPress);

    internal static Button IconButton(string iconPath, string tip, double width, double height, Action action,
        bool enabled = true, bool selected = false, bool strongPress = false)
    {
        var color = enabled ? "green" : "blue";
        var normal = Load(State(color, enabled ? "normal" : "disabled"));
        var hover = enabled ? Load(State(color, "hover")) : normal;
        var pressed = enabled ? Load(State(color, "pressed")) : normal;
        var background = NineSlice(normal, width, height, 12, 12);
        var iconSource = Load(iconPath);
        var iconSize = Math.Min(width, height) * .66;
        var icon = new Image { Source = iconSource, Width = iconSize, Height = iconSize, Stretch = Stretch.Uniform, IsHitTestVisible = false, Opacity = enabled ? 1 : .45 };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);
        var content = new Grid { Width = width, Height = height, Background = HitTestBackground, IsHitTestVisible = true };
        content.Children.Add(background); content.Children.Add(icon);
        var pressShade = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(68, 0, 0, 0)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        content.Children.Add(pressShade);
        var button = new Button { Content = content, Width = width, Height = height, IsEnabled = enabled, IsHitTestVisible = enabled,
            ToolTip = tip, Cursor = enabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
            Background = HitTestBackground, BorderThickness = new Thickness(0), Padding = new Thickness(0), Template = BareButtonTemplate() };
        if (enabled)
        {
            var visualState = new IconButtonVisualState(background, normal, hover, pressed, pressShade, strongPress)
                { Selected = selected };
            button.Tag = visualState;
            ApplyIconButtonState(visualState, selected || button.IsMouseOver);
            button.MouseEnter += (_, _) => ApplyIconButtonState(visualState, true);
            button.MouseLeave += (_, _) => ApplyIconButtonState(visualState, false);
            button.PreviewMouseLeftButtonDown += (_, _) =>
            {
                visualState.PointerPressed = true;
                ApplyIconButtonState(visualState, true);
            };
            button.PreviewMouseLeftButtonUp += (_, _) =>
            {
                visualState.PointerPressed = false;
                ApplyIconButtonState(visualState, button.IsMouseOver);
            };
            button.Click += (_, _) => action();
        }
        return button;
    }

    internal static void SetIconButtonSelected(Button button, bool selected)
    {
        if (button.Tag is not IconButtonVisualState state) return;
        state.Selected = selected;
        ApplyIconButtonState(state, selected || button.IsMouseOver);
    }

    private static void ApplyIconButtonState(IconButtonVisualState state, bool hovered)
    {
        var emphasized = state.StrongPress && (state.Selected || state.PointerPressed);
        SetState(state.Background, state.Selected || state.PointerPressed
            ? state.Pressed
            : hovered ? state.Hover : state.Normal);
        state.PressShade.Visibility = emphasized ? Visibility.Visible : Visibility.Collapsed;
    }

    internal static Grid NineSlice(BitmapSource source, double width, double height, int xEdge = 16, int yEdge = 16)
    {
        var grid = new Grid { Width = width, Height = height, IsHitTestVisible = false };
        var left = Math.Min(xEdge, Math.Max(1, source.PixelWidth / 3));
        var right = left; var top = Math.Min(yEdge, Math.Max(1, source.PixelHeight / 3)); var bottom = top;
        var widths = new[] { left, Math.Max(1, source.PixelWidth - left - right), right };
        var heights = new[] { top, Math.Max(1, source.PixelHeight - top - bottom), bottom };
        // Keep corner blocks square and pixel aligned; only the straight center strips expand.
        var integerCornerScale = Math.Max(1, Math.Floor(Math.Min(width / source.PixelWidth, height / source.PixelHeight)));
        var edgeWidth = Math.Min(width / 3, xEdge * integerCornerScale);
        var edgeHeight = Math.Min(height / 3, yEdge * integerCornerScale);
        for (var column = 0; column < 3; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = column == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(edgeWidth) });
        for (var row = 0; row < 3; row++) grid.RowDefinitions.Add(new RowDefinition { Height = row == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(edgeHeight) });
        var y = 0;
        for (var row = 0; row < 3; row++)
        {
            var x = 0;
            for (var column = 0; column < 3; column++)
            {
                var crop = new CroppedBitmap(source, new Int32Rect(x, y, widths[column], heights[row])); crop.Freeze();
                var image = new Image { Source = crop, Stretch = Stretch.Fill, SnapsToDevicePixels = true, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
                Grid.SetRow(image, row); Grid.SetColumn(image, column); grid.Children.Add(image); x += widths[column];
            }
            y += heights[row];
        }
        return grid;
    }

    internal static void SetState(Grid grid, BitmapSource source)
    {
        var replacement = NineSlice(source, grid.Width, grid.Height);
        grid.Children.Clear(); grid.RowDefinitions.Clear(); grid.ColumnDefinitions.Clear();
        foreach (var column in replacement.ColumnDefinitions) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = column.Width });
        foreach (var row in replacement.RowDefinitions) grid.RowDefinitions.Add(new RowDefinition { Height = row.Height });
        foreach (var child in replacement.Children.Cast<UIElement>().ToArray())
        {
            var row = Grid.GetRow(child); var column = Grid.GetColumn(child);
            replacement.Children.Remove(child); Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child);
        }
    }

    private static string State(string color, string state)
    {
        var code = (color, state) switch
        {
            ("green", "normal") => "009", ("green", "hover") => "010", ("green", "pressed") => "011", ("green", _) => "012",
            ("red", "normal") => "017", ("red", "hover") => "018", ("red", "pressed") => "019", ("red", _) => "020",
            ("yellow", "normal") => "041", ("yellow", "hover") => "042", ("yellow", "pressed") => "043", ("yellow", _) => "044",
            ("blue", "normal") => "028", ("blue", "hover") => "029", ("blue", "pressed") => "030", _ => "031"
        };
        return $"ui/buttons/palette/{color}_{state}_{code}.png";
    }

    internal static ControlTemplate BareButtonTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        return new ControlTemplate(typeof(Button)) { VisualTree = presenter };
    }
}

internal sealed class SoundSettings
{
    public bool Enabled { get; set; } = true;
    public int Volume { get; set; } = 70;
}

internal static class GameSoundSettings
{
    private static readonly object Gate = new();
    private static SoundSettings _state = Load();
    private static string FilePath => Environment.GetEnvironmentVariable("POKEGRASS_AUDIO_SETTINGS_PATH") is { Length: > 0 } path
        ? Path.GetFullPath(path) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "audio-settings.json");
    internal static event Action? Changed;
    internal static bool Enabled { get { lock (Gate) return _state.Enabled; } }
    internal static int Volume { get { lock (Gate) return _state.Volume; } }
    internal static double EffectiveVolume { get { lock (Gate) return _state.Enabled ? Math.Clamp(_state.Volume, 0, 100) / 100d : 0d; } }

    internal static void Update(bool? enabled = null, int? volume = null)
    {
        lock (Gate)
        {
            if (enabled.HasValue) _state.Enabled = enabled.Value;
            if (volume.HasValue) _state.Volume = Math.Clamp(volume.Value, 0, 100);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        Changed?.Invoke();
    }

    private static SoundSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<SoundSettings>(File.ReadAllText(FilePath)) ?? new SoundSettings() : new SoundSettings(); }
        catch { return new SoundSettings(); }
    }
}
