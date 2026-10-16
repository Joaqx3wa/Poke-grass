using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using Color = System.Windows.Media.Color;

namespace PokeGrass;

public partial class CapturePanelWindow : Window
{
    internal const string ItemDragFormat = "PokéGrass.CaptureItem";
    private const int CapturePanelWidth = 560;
    private const int CapturePanelHeight = 400;
    private const int BallsPanelHeight = 310;
    private const int ResultPanelWidth = 480;
    private const int ResultPanelHeight = 220;
    private int _designPanelWidth = CapturePanelWidth;
    private int _designPanelHeight = CapturePanelHeight;
    private double _actualUiScale = 1d;
    private Drawing.Rectangle _lastPhysicalBounds;
    private Drawing.Rectangle _lastWorkArea;
    internal Drawing.Rectangle LastPhysicalBounds => _lastPhysicalBounds;
    internal Drawing.Rectangle LastWorkArea => _lastWorkArea;
    internal double ActualUiScale => _actualUiScale;
    private readonly GardenWindow _garden;
    private readonly GardenActionButtonsWindow? _actionButtons;
    private readonly GameSaveStore _save;
    private readonly RewardBalance? _rewardBalance;
    private readonly PokemonEntry _pokemon;
    private readonly PokemonSpeciesMetadata _metadata;
    private readonly bool _shiny;
    private readonly bool _alpha;
    private readonly IReadOnlyList<BitmapSource> _shinySparkFrames;
    private readonly DispatcherTimer _resultSparkTimer;
    private Image? _resultSparkImage;
    private int _resultSparkFrame = -1;
    private readonly List<ItemCard> _ballCards = [];
    private readonly List<ItemCard> _berryCards = [];
    private CaptureBall? _selectedBall;
    private CaptureBerry? _selectedBerry;
    private readonly EncounterBerrySlot _berrySlot = new();
    private bool _busy;
    private int _activeTab;
    internal CaptureResolution? LastResolution { get; private set; }
    private ItemCard? _dragCandidate;
    private Point _dragStart;
    private readonly BitmapSource _frame;
    private readonly BitmapSource _resultFrame;
    private readonly BitmapSource _shinyMark;
    private readonly BitmapSource _alphaMark;
    private readonly BitmapSource _greenNormal;
    private readonly BitmapSource _greenHover;
    private readonly BitmapSource _greenPressed;
    private readonly BitmapSource _greenDisabled;
    private readonly BitmapSource _redNormal;
    private readonly BitmapSource _redHover;
    private readonly BitmapSource _redPressed;
    private readonly BitmapSource _redDisabled;
    private ActionButton? _launchButton;
    private ActionButton? _fleeButton;

    internal CapturePanelWindow(GardenWindow garden, GameSaveStore save, PokemonEntry pokemon,
        PokemonSpeciesMetadata metadata, bool shiny, GardenActionButtonsWindow? actionButtons = null,
        RewardBalance? rewardBalance = null, bool alpha = false)
    {
        InitializeComponent();
        ShowInTaskbar = UiTestMode.Enabled;
        _garden = garden;
        _actionButtons = actionButtons;
        _save = save;
        _rewardBalance = rewardBalance;
        _pokemon = pokemon;
        _metadata = metadata;
        _shiny = shiny;
        _alpha = alpha;
        _shinySparkFrames = ShinySparkFrames.Load(LoadImage);
        _resultSparkTimer = new DispatcherTimer(DispatcherPriority.Render)
            { Interval = TimeSpan.FromMilliseconds(ShinySparkFrames.FrameMilliseconds) };
        _resultSparkTimer.Tick += OnResultSparkTick;
        TextOptions.SetTextRenderingMode(RootCanvas, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(RootCanvas, TextFormattingMode.Display);
        _frame = LoadImage("ui/frames/decorative_1_large_001.png");
        _resultFrame = LoadImage("ui/frames/decorative_1_large_036.png");
        _shinyMark = LoadImage("ui/special_attribute/shiny-stars.png");
        _alphaMark = LoadImage("ui/special_attribute/alpha-icon.png");
        _greenNormal = LoadImage("ui/buttons/palette/green_normal_009.png");
        _greenHover = LoadImage("ui/buttons/palette/green_hover_010.png");
        _greenPressed = LoadImage("ui/buttons/palette/green_pressed_011.png");
        _greenDisabled = LoadImage("ui/buttons/palette/green_disabled_012.png");
        _redNormal = LoadImage("ui/buttons/palette/red_normal_017.png");
        _redHover = LoadImage("ui/buttons/palette/red_hover_018.png");
        _redPressed = LoadImage("ui/buttons/palette/red_pressed_019.png");
        _redDisabled = LoadImage("ui/buttons/palette/red_disabled_020.png");
        SourceInitialized += (_, _) => PositionBesideGarden();
        Loaded += (_, _) => BuildCaptureView();
        Closed += (_, _) => { _busy = true; _berrySlot.Clear(); _resultSparkTimer.Stop(); _resultSparkImage = null; };
    }

    internal async Task<bool> UseSelectedItemAsync()
    {
        if (_busy) return false;
        if (_selectedBall is { } ball && _save.Count(ball) > 0)
            return await ThrowBallAsync(ball);
        if (_selectedBerry is { } berry)
            return UseBerry(berry);
        return false;
    }

    internal async Task UseDroppedItemAsync(CaptureItemDrag item)
    {
        if (_busy) return;
        if (item.Ball is { } ball) { await ThrowBallAsync(ball, item.OriginScreen); return; }
        if (item.Berry is { } berry) UseBerry(berry);
    }

    private bool UseBerry(CaptureBerry berry)
    {
        if (_busy || _berrySlot.Used || _save.Count(berry) <= 0 || !_berrySlot.TryUse(_save, berry)) return false;
        _selectedBerry = null;
        _selectedBall ??= Enum.GetValues<CaptureBall>().FirstOrDefault(ball => _save.Count(ball) > 0);
        RefreshCards();
        return true;
    }

    internal async Task<bool> ThrowBallAsync(CaptureBall ball)
        => await ThrowBallAsync(ball, GetBallOriginScreen(ball));

    internal async Task<bool> ThrowBallAsync(CaptureBall ball, Drawing.Point originScreen, Random? randomOverride = null)
    {
        if (_busy || _save.Count(ball) <= 0) return false;
        _selectedBall = ball;
        _selectedBerry = null;
        _busy = true;
        RefreshCards();
        CaptureAttempt completed;
        var ballCountBeforeAttempt = _save.Count(ball);
        try
        {
            var berryForThisThrow = _berrySlot.ActiveBerry;
            completed = await _garden.ThrowCaptureBallAsync(ball, originScreen, berryForThisThrow, _save, _metadata, _shiny, randomOverride);
            if (completed.Result is not null)
                _berrySlot.ResolveAttempt();
        }
        catch
        {
            // If the Ball was consumed, its valid throw began and the pending berry
            // must not leak into a later attempt. Precondition failures consume no Ball.
            if (_save.Count(ball) < ballCountBeforeAttempt) _berrySlot.ResolveAttempt();
            throw;
        }
        finally
        {
            _busy = false;
        }

        if (completed.Result is null)
        {
            RefreshCards();
            return false;
        }

        LastResolution = completed.Result;

        if (completed.Result.Success)
        {
            _busy = false;
            ShowCaptureResult(completed.Result);
            return true;
        }

        if (completed.Result.Fled)
        {
            Close();
            return true;
        }

        RefreshCards();
        return true;
    }

    private void BuildCaptureView()
    {
        RootCanvas.Children.Clear();
        _ballCards.Clear(); _berryCards.Clear();
        var panelHeight = _activeTab == 0 ? BallsPanelHeight : CapturePanelHeight;
        SetPanelDimensions(CapturePanelWidth, panelHeight);
        AddNineSliceFrame(_frame, CapturePanelWidth, panelHeight, 20, 20);

        var dataRows = _activeTab == 0 ? 1 : 2;
        const double tabsHeight = 66;
        const double itemRowHeight = 82;
        var contentHeight = tabsHeight + (dataRows + 1) * itemRowHeight;
        var layout = new Grid
        {
            Width = CapturePanelWidth - 44,
            Height = contentHeight,
            IsHitTestVisible = true
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(tabsHeight) });
        // Una fila por grupo de objetos y una para acciones. Evita filas vacías
        // que antes elevaban visualmente los botones dentro del marco.
        for (var rowIndex = 0; rowIndex < dataRows + 1; rowIndex++)
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(itemRowHeight) });
        Canvas.SetLeft(layout, 22);
        // Centrar verticalmente las pestañas, filas de objetos y acciones juntas.
        Canvas.SetTop(layout, (panelHeight - contentHeight) / 2);
        RootCanvas.Children.Add(layout);

        var tabs = new Grid { Width = 260, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        var ballsTab = CreateCaptureTab("Poké Balls", 0);
        var berriesTab = CreateCaptureTab("Bayas", 1);
        Grid.SetColumn(ballsTab, 0); Grid.SetColumn(berriesTab, 2); tabs.Children.Add(ballsTab); tabs.Children.Add(berriesTab);
        Grid.SetRow(tabs, 0); layout.Children.Add(tabs);

        if (_activeTab == 0)
        {
            var balls = new (CaptureBall Ball, string Label, string Image)[]
            {
                (CaptureBall.Poke, "Poké Ball", "items/balls/poke.png"),
                (CaptureBall.Great, "Super Ball", "items/balls/great.png"),
                (CaptureBall.Ultra, "Ultra Ball", "items/balls/ultra.png"),
                (CaptureBall.Master, "Master Ball", "items/balls/master.png")
            };
            var ballRow = MakeUniformRow(balls.Length, 120); Grid.SetRow(ballRow, 1); layout.Children.Add(ballRow);
            for (var i = 0; i < balls.Length; i++)
            {
                var item = balls[i]; var card = CreateItemCard(item.Label, _save.Count(item.Ball), item.Image,
                    _save.Count(item.Ball) > 0, selected: _selectedBall == item.Ball, berry: false, width: 120);
                Grid.SetColumn(card.Root, i * 2); ballRow.Children.Add(card.Root);
                card.Root.MouseLeftButtonDown += (_, e) =>
                {
                    if (_busy || _save.Count(item.Ball) <= 0) return;
                    _selectedBall = item.Ball; _selectedBerry = null; _dragCandidate = card; _dragStart = e.GetPosition(card.Root); RefreshCards(); e.Handled = true;
                };
                card.Root.MouseMove += (_, e) => BeginItemDrag(card, new CaptureItemDrag(item.Ball, null, GetBallOriginScreen(item.Ball)), e);
                _ballCards.Add(card);
            }
        }
        else
        {
            var razzBerries = new (CaptureBerry Berry, string Label, string Image)[]
            {
                (CaptureBerry.Razz, "Frambu", "items/berries/razz-gen7.png"),
                (CaptureBerry.SilverRazz, "Frambu Plateada", "items/berries/silver-razz.png"),
                (CaptureBerry.GoldenRazz, "Frambu Dorada", "items/berries/golden-razz.png")
            };
            AddBerryRow(layout, razzBerries, 1);
            var nanabBerries = new (CaptureBerry Berry, string Label, string Image)[]
            {
                (CaptureBerry.Nanab, "Latano", "items/berries/nanab-gen7.png"),
                (CaptureBerry.SilverNanab, "Latano Plateada", "items/berries/silver-nanab.png"),
                (CaptureBerry.GoldenNanab, "Latano Dorada", "items/berries/golden-nanab.png")
            };
            AddBerryRow(layout, nanabBerries, 2);
        }

        var actionRow = MakeUniformRow(2);
        Grid.SetRow(actionRow, _activeTab == 0 ? 2 : 3);
        layout.Children.Add(actionRow);
        _launchButton = CreateImageButton("Usar", _greenNormal, _greenHover, _greenPressed,
            _greenDisabled, async () => await UseSelectedItemAsync(), fontSize: 14);
        Grid.SetColumn(_launchButton.Root, 0);
        actionRow.Children.Add(_launchButton.Root);
        _fleeButton = CreateImageButton("Huir", _redNormal, _redHover, _redPressed,
            _redDisabled, FleeAsync, fontSize: 14);
        Grid.SetColumn(_fleeButton.Root, 2);
        actionRow.Children.Add(_fleeButton.Root);
        if (_selectedBall is null || _save.Count(_selectedBall.Value) == 0)
            _selectedBall = Enum.GetValues<CaptureBall>().FirstOrDefault(ball => _save.Count(ball) > 0);
        RefreshCards();
    }

    internal void SelectTabDebug(int index)
    {
        if (_busy) return;
        _activeTab = Math.Clamp(index, 0, 1); BuildCaptureView();
    }

    private void AddBerryRow(Grid layout, (CaptureBerry Berry, string Label, string Image)[] berries, int rowIndex)
    {
        var berryRow = MakeUniformRow(berries.Length, 120);
        Grid.SetRow(berryRow, rowIndex);
        layout.Children.Add(berryRow);
        for (var i = 0; i < berries.Length; i++)
        {
            var item = berries[i];
            var card = CreateItemCard(item.Label, _save.Count(item.Berry), item.Image,
                !_berrySlot.Used && _save.Count(item.Berry) > 0,
                selected: _selectedBerry == item.Berry, berry: true, width: 120);
            Grid.SetColumn(card.Root, i * 2);
            berryRow.Children.Add(card.Root);
            card.Root.MouseLeftButtonDown += (_, e) =>
            {
                if (_busy || _berrySlot.Used || _save.Count(item.Berry) <= 0) { RefreshCards(); return; }
                _selectedBerry = item.Berry;
                _selectedBall = null;
                _dragCandidate = card;
                _dragStart = e.GetPosition(card.Root);
                RefreshCards();
                e.Handled = true;
            };
            card.Root.MouseMove += (_, e) => BeginItemDrag(card, new CaptureItemDrag(null, item.Berry, GetItemOriginScreen(card)), e);
            _berryCards.Add(card);
        }
    }

    private UIElement CreateCaptureTab(string label, int index)
    {
        var selected = _activeTab == index;
        var button = PixelButtonUi.Button(label, selected ? "green" : "blue", 124, 58,
            () => { if (_busy || _activeTab == index) return; _activeTab = index; BuildCaptureView(); },
            enabled: !_busy, selected: selected, fontSize: 14, toolTip: $"Pestaña {label}");
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    private static Grid MakeUniformRow(int columns, double cardWidth = 124)
    {
        var gap = columns == 4 ? 8 : 12;
        var row = new Grid { Width = columns * cardWidth + (columns - 1) * gap, Margin = new Thickness(0),
            ClipToBounds = false, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        for (var i = 0; i < columns * 2 - 1; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i % 2 == 0 ? cardWidth : gap) });
        return row;
    }

    private ItemCard CreateItemCard(string name, int count, string iconPath, bool enabled,
        bool selected, bool berry, double width = 124)
    {
        bool IsCurrentlySelected() => berry
            ? BerryForLabel(name) == _selectedBerry || BerryForLabel(name) == _berrySlot.ActiveBerry
            : BallForLabel(name) == _selectedBall;
        var buttonWidth = width;
        const double buttonHeight = 76;
        var root = new Grid { Width = buttonWidth, Height = buttonHeight, Background = PixelButtonUi.HitTestBackground,
            IsHitTestVisible = true, ClipToBounds = false, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        var normalState = SelectBlueState(enabled, selected, false);
        var background = PixelButtonUi.NineSlice(normalState, buttonWidth, buttonHeight, 16, 16);
        root.Children.Add(background);
        var content = new Grid { Margin = new Thickness(4, 2, 4, 2), IsHitTestVisible = false };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Image
        {
            Width = 48,
            Height = 48,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        SetBitmap(icon, LoadImage(iconPath));
        Grid.SetColumn(icon, 0);
        content.Children.Add(icon);
        var label = new TextBlock
        {
            Text = FormatItemLabel(name, count), Margin = new Thickness(2, 0, 0, 0),
            FontFamily = MinecraftFont, FontSize = berry ? 13 : 12.5,
            Foreground = enabled ? new SolidColorBrush(Color.FromRgb(25, 37, 51)) : new SolidColorBrush(Color.FromRgb(72, 78, 85)),
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        root.Children.Add(content);
        root.MouseEnter += (_, _) => { if (!_busy && root.IsEnabled) PixelButtonUi.SetState(background, SelectBlueState(true, IsCurrentlySelected(), true)); };
        root.MouseLeave += (_, _) => PixelButtonUi.SetState(background, SelectBlueState(root.IsEnabled, IsCurrentlySelected(), false));
        root.MouseLeftButtonDown += (_, _) => { if (!_busy && root.IsEnabled) PixelButtonUi.SetState(background, BluePressed); };
        root.MouseLeftButtonUp += (_, _) => PixelButtonUi.SetState(background, SelectBlueState(root.IsEnabled, IsCurrentlySelected(), true));
        return new ItemCard(root, background, label, name, iconPath, berry);
    }

    private void RefreshCards()
    {
        foreach (var card in _ballCards)
        {
            var ball = BallForLabel(card.Name);
            var enabled = !_busy && _save.Count(ball) > 0;
            var selected = _selectedBall == ball;
            PixelButtonUi.SetState(card.Background, SelectBlueState(enabled, selected, false));
            card.Label.Text = FormatItemLabel(card.Name, _save.Count(ball));
            card.Label.Foreground = enabled ? Brushes.Black : Brushes.DimGray;
            card.Root.IsEnabled = enabled;
        }
        foreach (var card in _berryCards)
        {
            var berry = BerryForLabel(card.Name);
            var enabled = !_busy && !_berrySlot.Used && _save.Count(berry) > 0;
            PixelButtonUi.SetState(card.Background, SelectBlueState(enabled, _selectedBerry == berry || _berrySlot.ActiveBerry == berry, false));
            card.Label.Text = _berrySlot.Used && _berrySlot.ActiveBerry == berry ? $"{FormatItemName(card.Name)}\nActiva" : FormatItemLabel(card.Name, _save.Count(berry));
            card.Label.Foreground = enabled ? Brushes.Black : Brushes.DimGray;
            card.Root.IsEnabled = enabled;
        }
        var canLaunch = !_busy && ((_selectedBall is { } selectedBall && _save.Count(selectedBall) > 0)
            || (_selectedBerry is { } selectedBerry && !_berrySlot.Used && _save.Count(selectedBerry) > 0));
        UpdateActionButton(_launchButton, canLaunch);
        UpdateActionButton(_fleeButton, !_busy);
    }

    private static string FormatItemLabel(string name, int count) => $"{FormatItemName(name)}\n×{count}";

    private static string FormatItemName(string name) => name switch
    {
        "Poké Ball" => "Poké\nBall",
        "Super Ball" => "Super\nBall",
        "Ultra Ball" => "Ultra\nBall",
        "Master Ball" => "Master\nBall",
        "Frambu Plateada" => "Frambu\nPlateada",
        "Frambu Dorada" => "Frambu\nDorada",
        "Latano Plateada" => "Latano\nPlateada",
        "Latano Dorada" => "Latano\nDorada",
        _ => name
    };

    private void BeginItemDrag(ItemCard card, CaptureItemDrag item, System.Windows.Input.MouseEventArgs e)
    {
        var available = item.Ball is { } dragBall ? _save.Count(dragBall) > 0
            : item.Berry is { } dragBerry && !_berrySlot.Used && _save.Count(dragBerry) > 0;
        if (_busy || !available || e.LeftButton != MouseButtonState.Pressed
            || _dragCandidate != card) return;
        var point = e.GetPosition(card.Root);
        if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragCandidate = null;
        var data = new DataObject(ItemDragFormat, item);
        var itemSprite = LoadImage(item.Ball is { } b ? BallIcon(b) : BerryIcon(item.Berry!.Value));
        var preview = new DragPreviewWindow(itemSprite, 48, 48, scaleWithDesktopVisuals: true);
        preview.Show();
        System.Windows.GiveFeedbackEventHandler feedback = (_, args) => { args.UseDefaultCursors = false; Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow; args.Handled = true; };
        card.Root.GiveFeedback += feedback;
        try { _ = DragDrop.DoDragDrop(card.Root, data, DragDropEffects.Copy); }
        finally
        {
            card.Root.GiveFeedback -= feedback;
            Mouse.OverrideCursor = null;
            preview.Close();
        }
    }

    private async Task FleeAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshCards();
        await _garden.FleeEncounterAsync();
        Close();
    }

    private void ShowCaptureResult(CaptureResolution resolution)
    {
        _resultSparkTimer.Stop();
        _resultSparkImage = null;
        _resultSparkFrame = -1;
        RootCanvas.Children.Clear();
        var rewards = resolution.Rewards?.Where(item => item.Quantity > 0).ToArray() ?? [];
        var rewardPanelHeight = rewards.Length == 0 ? 0 : RewardPanelVisual.HeightFor(rewards.Length);
        var rewardTop = ResultPanelHeight + 4;
        var height = rewards.Length == 0 ? ResultPanelHeight : rewardTop + rewardPanelHeight;
        SetPanelDimensions(ResultPanelWidth, height);
        AddNineSliceFrame(_resultFrame, ResultPanelWidth, ResultPanelHeight, 20, 20);

        var details = new Grid { Width = ResultPanelWidth - 52, Height = 108, ClipToBounds = true };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Canvas.SetLeft(details, 26); Canvas.SetTop(details, 40); RootCanvas.Children.Add(details);

        var icon = LoadPokemonIcon(_shiny ? _pokemon.IconShiny : _pokemon.IconNormal);
        if (_shiny && resolution.Success)
        {
            var sparks = new Image
            {
                Source = _shinySparkFrames[0],
                Width = 81,
                Height = 108,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(sparks, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(sparks, EdgeMode.Aliased);
            Grid.SetColumn(sparks, 0);
            details.Children.Add(sparks);
            _resultSparkImage = sparks;
            _resultSparkFrame = 0;
        }
        var pokemonIcon = new Image
        {
            Source = icon,
            Width = 108,
            Height = 108,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(pokemonIcon, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(pokemonIcon, EdgeMode.Aliased);
        details.Children.Add(pokemonIcon);
        if (_resultSparkImage is not null) _resultSparkTimer.Start();

        var info = new Grid { Margin = new Thickness(14, 1, 4, 1) };
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        Grid.SetColumn(info, 1);
        details.Children.Add(info);

        var numberRow = new Grid();
        numberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        numberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        numberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = MakeText($"N.º {_metadata.PokedexNumber}", 18, Colors.Black, TextAlignment.Left, VerticalAlignment.Center);
        numberRow.Children.Add(number);
        if (_shiny)
        {
            var shiny = new Image
            {
                Source = _shinyMark,
                Width = 30,
                Height = 30,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 3, 0)
            };
            RenderOptions.SetBitmapScalingMode(shiny, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(shiny, EdgeMode.Aliased);
            Grid.SetColumn(shiny, 1);
            numberRow.Children.Add(shiny);
        }
        if (_alpha)
        {
            var alpha = new Image
            {
                Source = _alphaMark, Width = 25, Height = 25, Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0),
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(alpha, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(alpha, EdgeMode.Aliased);
            Grid.SetColumn(alpha, 2);
            numberRow.Children.Add(alpha);
        }
        info.Children.Add(numberRow);

        var name = MakeText(PokemonSex.GetDisplayName(_metadata), 22, Colors.Black, TextAlignment.Left, VerticalAlignment.Center);
        name.TextWrapping = TextWrapping.Wrap;
        name.TextTrimming = TextTrimming.WordEllipsis;
        Grid.SetRow(name, 1);
        info.Children.Add(name);

        var typeRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        for (var i = 0; i < _metadata.Types.Count; i++)
        {
            typeRow.Children.Add(MakeTypeItem(_metadata.Types[i]));
        }
        Grid.SetRow(typeRow, 2);
        info.Children.Add(typeRow);

        if (rewards.Length > 0 && _rewardBalance is not null)
        {
            var panelJoin = new Border
            {
                Width = ResultPanelWidth,
                Height = rewardTop - ResultPanelHeight,
                Background = new SolidColorBrush(Color.FromRgb(249, 247, 238)),
                IsHitTestVisible = false
            };
            Canvas.SetTop(panelJoin, ResultPanelHeight);
            RootCanvas.Children.Add(panelJoin);
            var rewardPanel = RewardPanelVisual.Create(rewards, _rewardBalance, "RECOMPENSAS");
            Canvas.SetLeft(rewardPanel, 0); Canvas.SetTop(rewardPanel, rewardTop); RootCanvas.Children.Add(rewardPanel);
        }

        var continueButton = CreateImageButton("Continuar", _greenNormal, _greenHover, _greenPressed, _greenDisabled, ContinueAsync, fontSize: 17);
        continueButton.Root.Width = 180;
        continueButton.Root.Height = 50;
        continueButton.Root.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        continueButton.Root.VerticalAlignment = VerticalAlignment.Center;
        Canvas.SetLeft(continueButton.Root, (ResultPanelWidth - continueButton.Root.Width) / 2);
        Canvas.SetTop(continueButton.Root, ResultPanelHeight - continueButton.Root.Height - 16);
        RootCanvas.Children.Add(continueButton.Root);
    }

    private void OnResultSparkTick(object? sender, EventArgs e)
    {
        if (_resultSparkImage is null || _resultSparkFrame < 0)
        {
            _resultSparkTimer.Stop();
            return;
        }
        if (_resultSparkFrame + 1 >= _shinySparkFrames.Count)
        {
            _resultSparkTimer.Stop();
            _resultSparkImage.Source = null;
            _resultSparkImage = null;
            _resultSparkFrame = -1;
            return;
        }
        _resultSparkFrame++;
        _resultSparkImage.Source = _shinySparkFrames[_resultSparkFrame];
    }

    private async Task ContinueAsync()
    {
        if (_busy) return;
        _busy = true;
        await _garden.CloseEncounterAsync();
        Close();
    }

    private Grid MakeTypeItem(string type)
    {
        var item = new Grid { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Image
        {
            Source = LoadImage($"ui/types_pokemon/{type}.png"),
            Width = 30,
            Height = 30,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);
        item.Children.Add(icon);
        var label = MakeText(TypeNameSpanish(type), 15, Colors.Black, TextAlignment.Left, VerticalAlignment.Center);
        Grid.SetColumn(label, 1);
        item.Children.Add(label);
        return item;
    }

    private static string TypeNameSpanish(string type) => type switch
    {
        "normal" => "Normal", "fighting" => "Lucha", "flying" => "Volador", "poison" => "Veneno",
        "ground" => "Tierra", "rock" => "Roca", "bug" => "Bicho", "ghost" => "Fantasma",
        "steel" => "Acero", "fire" => "Fuego", "water" => "Agua", "grass" => "Planta",
        "electric" => "Eléctrico", "psychic" => "Psíquico", "ice" => "Hielo", "dragon" => "Dragón",
        "dark" => "Siniestro", "fairy" => "Hada",
        _ => throw new InvalidDataException($"Tipo Pokémon desconocido: {type}")
    };

    private TextBlock MakeText(string text, double size, System.Windows.Media.Color color,
        TextAlignment alignment, VerticalAlignment verticalAlignment)
        => new()
        {
            Text = text,
            FontFamily = MinecraftFont,
            FontSize = size,
            Foreground = new SolidColorBrush(color),
            TextAlignment = alignment,
            VerticalAlignment = verticalAlignment,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };

    private void SetPanelDimensions(int width, int height)
    {
        _designPanelWidth = width;
        _designPanelHeight = height;
        RootCanvas.Height = height;
        RootCanvas.Width = width;
        if (IsLoaded) ApplyUiScaleAndPosition();
    }

    private void AddNineSliceFrame(BitmapSource source, int width, int height, int insetX, int insetY)
    {
        var frame = new Grid { Width = width, Height = height, IsHitTestVisible = false };
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(insetX) });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(insetX) });
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(insetY) });
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(insetY) });

        var sourceWidths = new[] { insetX, source.PixelWidth - 2 * insetX, insetX };
        var sourceHeights = new[] { insetY, source.PixelHeight - 2 * insetY, insetY };
        var sourceX = new[] { 0, insetX, source.PixelWidth - insetX };
        var sourceY = new[] { 0, insetY, source.PixelHeight - insetY };
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            var slice = new CroppedBitmap(source,
                new Int32Rect(sourceX[column], sourceY[row], sourceWidths[column], sourceHeights[row]));
            slice.Freeze();
            var image = new Image { Source = slice, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
            Grid.SetRow(image, row);
            Grid.SetColumn(image, column);
            frame.Children.Add(image);
        }
        RootCanvas.Children.Add(frame);
    }

    private static void UpdateActionButton(ActionButton? button, bool enabled)
    {
        if (button is null) return;
        PixelButtonUi.SetState(button.Background, enabled ? button.Normal : button.Disabled);
        button.Label.Foreground = enabled ? Brushes.Black : Brushes.DimGray;
        button.Root.IsEnabled = enabled;
    }

    private ActionButton CreateImageButton(string label, BitmapSource normal,
        BitmapSource hover, BitmapSource pressed, BitmapSource disabled, Func<Task> action, double fontSize)
    {
        var root = new Grid
        {
            Margin = new Thickness(0, 5, 0, 5),
            Background = Brushes.Transparent,
            IsHitTestVisible = true,
            ClipToBounds = false,
            Width = 124,
            Height = 58,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var background = PixelButtonUi.NineSlice(normal, 124, 58, 16, 16);
        root.Children.Add(background);
        var text = new TextBlock
        {
            Text = label, FontFamily = MinecraftFont, FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(25, 37, 51)),
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            IsHitTestVisible = false
        };
        root.Children.Add(text);
        root.MouseEnter += (_, _) => { if (!_busy && root.IsEnabled) PixelButtonUi.SetState(background, hover); };
        root.MouseLeave += (_, _) => { if (!_busy && root.IsEnabled) PixelButtonUi.SetState(background, normal); };
        root.MouseLeftButtonDown += (_, e) => { if (!_busy && root.IsEnabled) PixelButtonUi.SetState(background, pressed); e.Handled = true; };
        root.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            if (_busy || !root.IsEnabled) { PixelButtonUi.SetState(background, disabled); return; }
            PixelButtonUi.SetState(background, hover);
            await action();
        };
        return new ActionButton(root, background, text, normal, hover, pressed, disabled);
    }

    private static string BallIcon(CaptureBall ball) => ball switch { CaptureBall.Poke => "items/balls/poke.png", CaptureBall.Great => "items/balls/great.png", CaptureBall.Ultra => "items/balls/ultra.png", _ => "items/balls/master.png" };
    private static string BerryIcon(CaptureBerry berry) => berry switch { CaptureBerry.Razz => "items/berries/razz-gen7.png", CaptureBerry.SilverRazz => "items/berries/silver-razz.png", CaptureBerry.GoldenRazz => "items/berries/golden-razz.png", CaptureBerry.Nanab => "items/berries/nanab-gen7.png", CaptureBerry.SilverNanab => "items/berries/silver-nanab.png", _ => "items/berries/golden-nanab.png" };

    private Drawing.Point GetItemOriginScreen(ItemCard card)
    {
        var p = card.Root.PointToScreen(new Point(card.Root.ActualWidth / 2, card.Root.ActualHeight / 2));
        return new Drawing.Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    private static System.Windows.Input.Cursor CreateItemCursor(BitmapSource source)
    {
        const int size = 32;
        var scaled = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        var factor = Math.Min((size - 4d) / source.PixelWidth, (size - 4d) / source.PixelHeight);
        var w = Math.Max(1, (int)Math.Round(source.PixelWidth * factor));
        var h = Math.Max(1, (int)Math.Round(source.PixelHeight * factor));
        var x0 = (size - w) / 2; var y0 = (size - h) / 2;
        var src = new byte[source.PixelHeight * source.PixelWidth * 4];
        source.CopyPixels(src, source.PixelWidth * 4, 0);
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var sx = Math.Min(source.PixelWidth - 1, x * source.PixelWidth / w);
            var sy = Math.Min(source.PixelHeight - 1, y * source.PixelHeight / h);
            Buffer.BlockCopy(src, (sy * source.PixelWidth + sx) * 4, pixels, ((y + y0) * size + x + x0) * 4, 4);
        }
        scaled.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0); writer.Write((short)2); writer.Write((short)1);
            writer.Write((byte)size); writer.Write((byte)size); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((short)16); writer.Write((short)16);
            writer.Write(40 + size * size * 4 + ((size + 31) / 32) * 4 * size); writer.Write(22);
            writer.Write(40); writer.Write(size); writer.Write(size * 2); writer.Write((short)1); writer.Write((short)32);
            writer.Write(0); writer.Write(size * size * 4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (var y = size - 1; y >= 0; y--) writer.Write(pixels, y * size * 4, size * 4);
            writer.Write(new byte[((size + 31) / 32) * 4 * size]);
        }
        stream.Position = 0;
        return new System.Windows.Input.Cursor(stream);
    }

    private void PositionBesideGarden() => ApplyUiScaleAndPosition();

    private void ApplyUiScaleAndPosition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var gardenBounds = _garden.GetPhysicalBounds();
        var dpi = _garden.GetDpiScale();
        var screen = Forms.Screen.FromRectangle(gardenBounds);
        var area = screen.WorkingArea;
        var horizontalAvailable = Math.Max(area.Right - gardenBounds.Right, gardenBounds.Left - area.Left);
        var verticalAvailable = Math.Max(area.Bottom - gardenBounds.Bottom, gardenBounds.Top - area.Top);
        // Keep readable window controls at 1× and 2× garden settings; grow them with 4×.
        var requestedScale = _garden.Scale / 3d;
        // El resultado azul conserva la misma escala con o sin recompensas; el panel amarillo
        // añade altura al conjunto, pero no interviene en el ajuste visual del panel azul.
        var scaleReferenceHeight = _designPanelWidth == ResultPanelWidth ? ResultPanelHeight : _designPanelHeight;
        var fullWidth = _designPanelWidth * requestedScale * dpi.X;
        var fullHeight = scaleReferenceHeight * requestedScale * dpi.Y;
        var fitScale = Math.Min(area.Width / (double)_designPanelWidth / dpi.X,
            area.Height / (double)scaleReferenceHeight / dpi.Y);
        var actualScale = Math.Min(requestedScale, Math.Max(0.25d, fitScale));
        var width = (int)Math.Ceiling(_designPanelWidth * actualScale * dpi.X);
        var height = (int)Math.Ceiling(_designPanelHeight * actualScale * dpi.Y);
        if (fullWidth <= horizontalAvailable && fullHeight <= area.Height) actualScale = requestedScale;
        else if (fullWidth > horizontalAvailable && fullHeight <= verticalAvailable && fullWidth > area.Width)
            actualScale = Math.Min(requestedScale, Math.Max(0.25d, Math.Min(area.Width / (double)_designPanelWidth / dpi.X, verticalAvailable / (double)scaleReferenceHeight / dpi.Y)));
        width = (int)Math.Ceiling(_designPanelWidth * actualScale * dpi.X);
        height = (int)Math.Ceiling(_designPanelHeight * actualScale * dpi.Y);
        if (Math.Abs(_actualUiScale - actualScale) > 0.0001d)
        {
            _actualUiScale = actualScale;
            RootCanvas.LayoutTransform = new ScaleTransform(actualScale, actualScale);
        }
        Width = _designPanelWidth * actualScale;
        Height = _designPanelHeight * actualScale;
        var gap = Math.Max(2, (int)Math.Round(8 * actualScale * dpi.X));
        var actionBounds = _actionButtons?.GetPhysicalBounds() ?? Drawing.Rectangle.Empty;
        var beforeActionX = actionBounds.IsEmpty ? gardenBounds.Left - gap - width : actionBounds.Left - gap - width;
        var candidates = new (int X, int Y)[]
        {
            (gardenBounds.Right + gap, gardenBounds.Top),
            (beforeActionX, gardenBounds.Top),
            (gardenBounds.Left, gardenBounds.Bottom + gap),
            (gardenBounds.Left, gardenBounds.Top - gap - height)
        };
        bool OverlapsOccupied((int X, int Y) candidate)
        {
            var rect = new Drawing.Rectangle(candidate.X, candidate.Y, width, height);
            return rect.IntersectsWith(gardenBounds) || (!actionBounds.IsEmpty && rect.IntersectsWith(actionBounds));
        }
        var clampedCandidates = candidates.Select(candidate => (
            X: Math.Clamp(candidate.X, area.Left, Math.Max(area.Left, area.Right - width)),
            Y: Math.Clamp(candidate.Y, area.Top, Math.Max(area.Top, area.Bottom - height)))).ToArray();
        var validChoice = clampedCandidates.Where(candidate => !OverlapsOccupied(candidate))
            .Select(candidate => ((int X, int Y)?)candidate).FirstOrDefault();
        var choice = validChoice ?? clampedCandidates.OrderBy(candidate =>
        {
            var rect = new Drawing.Rectangle(candidate.X, candidate.Y, width, height);
            return (rect.IntersectsWith(gardenBounds) ? 100000L : 0L)
                + (!actionBounds.IsEmpty && rect.IntersectsWith(actionBounds) ? 100000L : 0L)
                + Math.Abs((long)candidate.X - gardenBounds.Left) + Math.Abs((long)candidate.Y - gardenBounds.Top);
        }).First();
        var xFinal = Math.Clamp(choice.X, area.Left, Math.Max(area.Left, area.Right - width));
        var yFinal = Math.Clamp(choice.Y, area.Top, Math.Max(area.Top, area.Bottom - height));
        SetWindowPos(hwnd, IntPtr.Zero, xFinal, yFinal, width, height, SwpNoActivate | SwpNoZOrder);
        _lastPhysicalBounds = new Drawing.Rectangle(xFinal, yFinal, width, height);
        _lastWorkArea = area;
    }

    internal void RepositionBesideGarden() => PositionBesideGarden();

#if DEBUG
    internal void SaveCaptureScreenshot(string path)
    {
        RootCanvas.UpdateLayout();
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(_designPanelWidth * _actualUiScale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(_designPanelHeight * _actualUiScale));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(RootCanvas);
        bitmap.Freeze();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    internal void ShowCaptureResultDebug(params RewardItemGrant[] rewards)
    {
        ShowCaptureResult(new CaptureResolution(true, rewards.Length > 0, false, 1d, rewards));
        UpdateLayout();
    }

    internal void SetResultSparkFrameDebug(int frame)
    {
        _resultSparkTimer.Stop();
        if (_resultSparkImage is null) return;
        if (frame < 0 || frame >= _shinySparkFrames.Count)
        {
            _resultSparkImage.Source = null;
            _resultSparkImage = null;
            _resultSparkFrame = -1;
            return;
        }
        _resultSparkFrame = frame;
        _resultSparkImage.Source = _shinySparkFrames[frame];
    }
#endif

    internal Drawing.Point GetBallOriginScreen(CaptureBall ball)
    {
        var label = ball switch
        {
            CaptureBall.Poke => "Poké Ball",
            CaptureBall.Great => "Super Ball",
            CaptureBall.Ultra => "Ultra Ball",
            CaptureBall.Master => "Master Ball",
            _ => throw new ArgumentOutOfRangeException(nameof(ball))
        };
        var card = _ballCards.FirstOrDefault(item => item.Name == label);
        if (card is null || !card.Root.IsLoaded) return new Drawing.Point((int)Left, (int)Top);
        var point = card.Root.PointToScreen(new Point(card.Root.ActualWidth / 2, card.Root.ActualHeight / 2));
        return new Drawing.Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private BitmapSource LoadImage(string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", relative.Replace('/', Path.DirectorySeparatorChar));
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); converted.Freeze();
        return converted;
    }

    private BitmapSource LoadPokemonIcon(string relative)
    {
        // Catalog icons are two-frame horizontal sheets (128x64 or 160x80).
        // Display the first frame without changing the original asset.
        var sheet = LoadImage(relative);
        var frame = new CroppedBitmap(sheet, new Int32Rect(0, 0, sheet.PixelWidth / 2, sheet.PixelHeight));
        frame.Freeze();
        return frame;
    }

    private static void SetBitmap(Image image, BitmapSource bitmap)
    {
        image.Source = bitmap;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
    }

    private static BitmapSource SelectBlueState(bool enabled, bool selected, bool hover)
        => !enabled ? BlueDisabled : selected ? BluePressed : hover ? BlueHover : BlueNormal;

    private static BitmapSource LoadStaticUiImage(string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "ui", relative);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    private static CaptureBall BallForLabel(string label) => label switch
    {
        "Poké Ball" => CaptureBall.Poke,
        "Super Ball" => CaptureBall.Great,
        "Ultra Ball" => CaptureBall.Ultra,
        "Master Ball" => CaptureBall.Master,
        _ => throw new ArgumentOutOfRangeException(nameof(label))
    };

    private static CaptureBerry BerryForLabel(string label) => label switch
    {
        "Frambu" => CaptureBerry.Razz,
        "Frambu Dorada" => CaptureBerry.GoldenRazz,
        "Frambu Plateada" => CaptureBerry.SilverRazz,
        "Latano" => CaptureBerry.Nanab,
        "Latano Plateada" => CaptureBerry.SilverNanab,
        "Latano Dorada" => CaptureBerry.GoldenNanab,
        _ => throw new ArgumentOutOfRangeException(nameof(label))
    };

    private static System.Windows.Media.FontFamily MinecraftFont
    {
        get
        {
            try { return new FontFamily(new Uri("pack://application:,,,/assets/fonts/"), "#Minecraft"); }
            catch { return new FontFamily("Minecraft"); }
        }
    }

    private static BitmapSource BlueNormal { get; } = LoadStaticUiImage(Path.Combine("buttons", "palette", "blue_normal_028.png"));
    private static BitmapSource BlueHover { get; } = LoadStaticUiImage(Path.Combine("buttons", "palette", "blue_hover_029.png"));
    private static BitmapSource BluePressed { get; } = LoadStaticUiImage(Path.Combine("buttons", "palette", "blue_pressed_030.png"));
    private static BitmapSource BlueDisabled { get; } = LoadStaticUiImage(Path.Combine("buttons", "palette", "blue_disabled_031.png"));

    internal sealed record CaptureItemDrag(CaptureBall? Ball, CaptureBerry? Berry, Drawing.Point OriginScreen);

    private sealed record ItemCard(Grid Root, Grid Background, TextBlock Label, string Name,
        string IconPath, bool Berry);

    private sealed record ActionButton(Grid Root, Grid Background, TextBlock Label, BitmapSource Normal,
        BitmapSource Hover, BitmapSource Pressed, BitmapSource Disabled);


    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
