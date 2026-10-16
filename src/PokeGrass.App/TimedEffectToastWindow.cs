using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace PokeGrass;

internal sealed class TimedEffectToastWindow : Window
{
    private readonly DispatcherTimer _timer;

    internal TimedEffectToastWindow(string message, int left, int top)
    {
        Width = 300; Height = 82; Left = left; Top = top;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
        var root = new Grid();
        root.Children.Add(PixelButtonUi.NineSlice(PixelButtonUi.Load("ui/frames/decorative_2_large_057.png"), 300, 82, 18, 18));
        var text = new TextBlock
        {
            Text = message, FontFamily = PixelUi.PixelFont, FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 53, 42)),
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(35, 12, 35, 12)
        };
        TextOptions.SetTextRenderingMode(text, TextRenderingMode.Aliased);
        root.Children.Add(text); Content = root;
        var positionPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "timed-notice-position.json");
        AdaptiveWindowSupport.ConfigureMovable(this, positionPath);
        AdaptiveWindowSupport.DragFrom(this, root);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }
}
