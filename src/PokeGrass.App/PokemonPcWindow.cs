using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Image = System.Windows.Controls.Image;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Path = System.IO.Path;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Forms = System.Windows.Forms;

namespace PokeGrass;

internal sealed class PokemonPcWindow : Window
{
    private const int SlotCount = 30;
    private const int TeamSlotCount = 6;
    private readonly GameSaveStore _save;
    private readonly PokemonCatalog _catalog;
    private readonly PokemonSpeciesMetadataCatalog _metadata;
    private readonly MegaEvolutionCatalog _megaEvolutions;
    private readonly Func<CapturedPokemon, Point?, bool> _sendToDesktop;
    private readonly Func<string, bool> _locateDesktopCompanion;
    private readonly Func<string, bool> _returnDesktopCompanion;
    private readonly string _assets = Path.Combine(AppContext.BaseDirectory, "assets");
    private readonly string _settingsPath = Environment.GetEnvironmentVariable("POKEGRASS_PC_WINDOW_SETTINGS_PATH") is { Length: > 0 } settingsOverride
        ? Path.GetFullPath(settingsOverride)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "pc-window.json");
    private readonly Canvas _boxScene = new() { Width = 624, Height = 564 };
    private readonly Image _boxBackground = new() { Width = 624, Height = 564, Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas _teamScene = new() { Width = 624, Height = 564, Visibility = Visibility.Collapsed };
    private readonly Grid _sceneHost = new();
    private Viewbox? _boxViewbox;
    private Viewbox? _teamViewbox;
    private readonly Button _boxesTabButton;
    private readonly Button _teamTabButton;
    private readonly Grid _slotGrid = new();
    private readonly TextBlock _boxName = new();
    private readonly TextBlock _nameText = new();
    private readonly TextBlock _numberText = new();
    private readonly TextBlock _dateText = new();
    private readonly TextBlock _collectionText = new();
    private readonly Image _portrait = new();
    private Border? _portraitFrame;
    private readonly Image _shinyMark = new();
    private readonly Image _megaMark = new();
    private readonly Image _alphaMark = new();
    private readonly StackPanel _types = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
    private Border? _typesFrame;
    private readonly Button _leftButton;
    private readonly Button _rightButton;
    private readonly Button _plusButton;
    private readonly Button _specialBoxButton;
    private readonly Button _closeButton;
    private readonly Button _releaseButton;
    private readonly Button _desktopButton;
    private readonly Button _locateButton;
    private readonly Button _returnToPcButton;
    private readonly List<Border> _slotTiles = [];
    private readonly List<Image> _slotIcons = [];
    private readonly List<Grid> _slotSelections = [];
    private readonly List<Image> _slotShinyMarks = [];
    private readonly List<Image> _slotMegaMarks = [];
    private readonly List<Image> _slotAlphaMarks = [];
    private readonly List<Border> _teamTiles = [];
    private readonly List<Image> _teamIcons = [];
    private readonly List<Image> _teamShinyMarks = [];
    private readonly List<Image> _teamMegaMarks = [];
    private readonly List<Image> _teamAlphaMarks = [];
    private readonly BitmapSource[] _selectedCorners = LoadCorners(Load("ui/slots/selected_red_061.png"));
    private readonly BitmapSource[] _focusCorners = LoadCorners(Load("ui/slots/focus_blue_067.png"));
    private string? _selectedRecordId;
    private string? _movingRecordId;
    private string? _dragRecordId;
    private Point _dragStart;
    private bool _dragging;
    private int? _teamDragSourceSlot;
    private DragPreviewWindow? _dragPreviewWindow;
    private int _boxIndex;
    private int _backgroundFrameIndex;
    private bool _teamView;
#if DEBUG
    private bool _debugAutoConfirmRelease;
#endif
    private BoxOptionsWindow? _debugOptions;
    private BoxBackgroundGalleryWindow? _debugGallery;

    internal PokemonPcWindow(GameSaveStore save, PokemonCatalog catalog, PokemonSpeciesMetadataCatalog metadata,
        Func<CapturedPokemon, Point?, bool> sendToDesktop, Func<string, bool> locateDesktopCompanion,
        Func<string, bool> returnDesktopCompanion, MegaEvolutionCatalog megaEvolutions)
    {
        _save = save; _catalog = catalog; _metadata = metadata; _megaEvolutions = megaEvolutions; _sendToDesktop = sendToDesktop;
        _locateDesktopCompanion = locateDesktopCompanion; _returnDesktopCompanion = returnDesktopCompanion;
        Title = "PokéGrass · PC Pokémon"; Width = 1220; Height = 820; MinWidth = 980; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; Background = new SolidColorBrush(Color.FromRgb(246, 240, 216));
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(9), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        ShowInTaskbar = true; Topmost = true; UseLayoutRounding = true; SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        PreviewKeyDown += OnKeyDown;
        MouseRightButtonDown += (_, e) => { if (_movingRecordId is not null) { CancelMove(); e.Handled = true; } };
        Loaded += (_, _) =>
        {
            RestoreBoundsFromDisk();
            var boxes = _save.GetPcSnapshot().Boxes;
            _boxIndex = boxes.Select((box, index) => (box, index)).First(entry => !entry.box.IsSpecial).index;
            Refresh();
        };
        LocationChanged += (_, _) => SaveBounds(); SizeChanged += (_, _) => SaveBounds(); Closed += (_, _) => { CloseDragPreview(); SaveBounds(); };

        _leftButton = CreateSpriteButton("ui/arrows/navigation_1_1_002.png", "Caja anterior", () => Navigate(-1), 38, 46);
        _rightButton = CreateSpriteButton("ui/arrows/navigation_1_2_003.png", "Caja siguiente", () => Navigate(1), 38, 46);
        _plusButton = CreateSpriteButton("ui/controls/utility_r3_c1_024.png", "Crear caja", CreateBox, 36, 36);
        _specialBoxButton = CreateSpecialBoxButton();
        _closeButton = CreateSpriteButton("ui/controls/utility_r5_c4_048.png", "Cerrar PC", Close, 34, 36);
        // Usa el botón pixel-art compartido, que conserva un hit target explícito
        // incluso con la plantilla sin chrome WPF usada por esta ventana.
        _releaseButton = PixelButtonUi.Button("Liberar", "red", 186, 86, ConfirmRelease, fontSize: 16);
        _desktopButton = CreateTextButton("Enviar al escritorio", "blue", SendSelectedToDesktop, 186, 86);
        _locateButton = CreateTextButton("Localizar en el escritorio", "blue", LocateSelectedCompanion, 186, 86);
        _returnToPcButton = CreateTextButton("Devolver a la PC", "red", ReturnSelectedCompanion, 186, 86);
        _locateButton.Visibility = Visibility.Collapsed; _returnToPcButton.Visibility = Visibility.Collapsed;
        _boxesTabButton = CreateTextButton("Cajas", "green", () => SetTeamView(false), 120, 38);
        _teamTabButton = CreateTextButton("Equipo", "blue", () => SetTeamView(true), 120, 38);
        _portrait.Stretch = Stretch.Uniform; _portrait.Width = 190; _portrait.Height = 190; _portrait.HorizontalAlignment = System.Windows.HorizontalAlignment.Center; _portrait.VerticalAlignment = VerticalAlignment.Center;
        Pixel(_portrait);
        _shinyMark.Source = Load("ui/special_attribute/shiny-stars.png"); _shinyMark.Width = 34; _shinyMark.Height = 34; _shinyMark.Stretch = Stretch.Uniform; Pixel(_shinyMark);
        _megaMark.Source = Load("ui/special_attribute/mega-evolution.png"); _megaMark.Width = 23; _megaMark.Height = 23; _megaMark.Stretch = Stretch.Uniform; Pixel(_megaMark);
        _alphaMark.Source = Load("ui/special_attribute/alpha-icon.png"); _alphaMark.Width = 24; _alphaMark.Height = 24; _alphaMark.Stretch = Stretch.Uniform; Pixel(_alphaMark);

        var designedContent = new Grid { Width = 1220, Height = 820 };
        BuildTeamScene();
        designedContent.Children.Add(BuildLayout());
        var responsiveContent = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = designedContent };
        RenderOptions.SetBitmapScalingMode(responsiveContent, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(responsiveContent, EdgeMode.Aliased);
        Content = responsiveContent;
    }

    internal void Refresh()
    {
        if (!IsLoaded) return;
        var snapshot = _save.GetPcSnapshot();
        if (snapshot.Boxes.Count == 0) return;
        _boxIndex = Math.Clamp(_boxIndex, 0, snapshot.Boxes.Count - 1);
        var box = snapshot.Boxes[_boxIndex];
        _boxName.Text = box.Name;
        SetSpriteButtonEnabledVisual(_leftButton, _boxIndex > 0);
        SetSpriteButtonEnabledVisual(_rightButton, _boxIndex < snapshot.Boxes.Count - 1);
        _plusButton.IsEnabled = true;
        SetBackground(box.Background);
        _sceneHost.Children.Clear();
        var activeScene = _teamView ? (UIElement)_teamViewbox! : _boxViewbox!;
        _boxScene.Visibility = _teamView ? Visibility.Collapsed : Visibility.Visible;
        _teamScene.Visibility = _teamView ? Visibility.Visible : Visibility.Collapsed;
        _sceneHost.Children.Add(activeScene);
        activeScene.Visibility = Visibility.Visible;

        for (var index = 0; index < SlotCount; index++)
        {
            var recordId = box.Slots[index];
            var record = recordId is null ? null : snapshot.Captures.FirstOrDefault(item => item.RecordId == recordId);
            var tile = _slotTiles[index];
            tile.Tag = record?.RecordId;
            var recordMetadata = record is null ? null : _metadata.GetForCapture(record.Id);
            tile.ToolTip = record is null ? null : $"{PokemonSex.GetDisplayName(recordMetadata!)}{(record.IsAlpha ? " · Alfa" : string.Empty)}{(record.IsShiny ? " · Shiny" : string.Empty)}{(record.MegaFormId is not null ? " · Mega" : string.Empty)} · {recordMetadata!.PokedexNumber:000} · Clic izquierdo para seleccionar · Clic derecho para mover/intercambiar · Arrastra a otra casilla para intercambiar o fuera del PC para enviarlo al escritorio";
            _slotIcons[index].Source = record is null ? null : LoadPokemonIcon(record, _slotIcons[index]);
            _slotIcons[index].Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
            var isSelected = record is not null && record.RecordId == _selectedRecordId;
            var isMoving = _movingRecordId is not null && record?.RecordId == _movingRecordId;
            var showMarker = isSelected || isMoving;
            _slotSelections[index].Visibility = showMarker ? Visibility.Visible : Visibility.Collapsed;
            var corners = isSelected ? _selectedCorners : _focusCorners;
            for (var corner = 0; corner < 4; corner++) ((Image)_slotSelections[index].Children[corner]).Source = showMarker ? corners[corner] : null;
            _slotShinyMarks[index].Visibility = record?.IsShiny == true ? Visibility.Visible : Visibility.Collapsed;
            _slotMegaMarks[index].Visibility = record?.MegaFormId is not null ? Visibility.Visible : Visibility.Collapsed;
            _slotAlphaMarks[index].Visibility = record?.IsAlpha == true ? Visibility.Visible : Visibility.Collapsed;
        }

        var teamIds = _save.GetDesktopTeamSlots();
        for (var index = 0; index < TeamSlotCount; index++)
        {
            var recordId = teamIds[index];
            var record = recordId is null ? null : snapshot.Captures.FirstOrDefault(item => item.RecordId == recordId);
            var tile = _teamTiles[index];
            tile.Tag = record?.RecordId;
            var recordMetadata = record is null ? null : _metadata.GetForCapture(record.Id);
            tile.ToolTip = record is null ? "Casilla vacía" : $"{PokemonSex.GetDisplayName(recordMetadata!)}{(record.IsAlpha ? " · Alfa" : string.Empty)}{(record.IsShiny ? " · Shiny" : string.Empty)}{(record.MegaFormId is not null ? " · Mega" : string.Empty)} · {recordMetadata!.PokedexNumber:000} · Clic para consultar · Arrastra para reorganizar";
            tile.Opacity = record is null || record.RecordId == _selectedRecordId ? 1 : .78;
            _teamIcons[index].Source = record is null ? null : LoadPokemonIcon(record, _teamIcons[index], 106, 102, 1.8);
            _teamIcons[index].Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
            _teamShinyMarks[index].Visibility = record?.IsShiny == true ? Visibility.Visible : Visibility.Collapsed;
            _teamMegaMarks[index].Visibility = record?.MegaFormId is not null ? Visibility.Visible : Visibility.Collapsed;
            _teamAlphaMarks[index].Visibility = record?.IsAlpha == true ? Visibility.Visible : Visibility.Collapsed;
        }
        DrawPokemonDetails(snapshot);
        _releaseButton.IsEnabled = !_teamView && _selectedRecordId is not null && _movingRecordId is null;
        var selected = _selectedRecordId is null
            ? null : snapshot.Captures.FirstOrDefault(item => item.RecordId == _selectedRecordId);
        var companions = _save.GetDesktopCompanions();
        var alreadyOnDesktop = selected is not null && companions.Any(item => item.RecordId == selected.RecordId);
        var canSend = !_teamView && selected is not null && !alreadyOnDesktop && companions.Count < TeamSlotCount;
        _desktopButton.IsEnabled = canSend;
        _desktopButton.IsHitTestVisible = canSend;
        _desktopButton.Opacity = canSend ? 1 : .58;
        _desktopButton.ToolTip = selected is null ? "Selecciona un Pokémon capturado"
            : alreadyOnDesktop ? "Este Pokémon ya está en el escritorio"
            : companions.Count >= TeamSlotCount ? "El equipo está completo: ya tiene 6 Pokémon"
            : "Enviar este Pokémon al escritorio";
        var canUseTeamActions = _teamView && selected is not null && alreadyOnDesktop;
        _locateButton.IsEnabled = canUseTeamActions;
        _returnToPcButton.IsEnabled = canUseTeamActions;
        _locateButton.Opacity = canUseTeamActions ? 1 : .58;
        _returnToPcButton.Opacity = canUseTeamActions ? 1 : .58;
        _releaseButton.Visibility = _teamView ? Visibility.Collapsed : Visibility.Visible;
        _desktopButton.Visibility = _teamView ? Visibility.Collapsed : Visibility.Visible;
        _locateButton.Visibility = _teamView ? Visibility.Visible : Visibility.Collapsed;
        _returnToPcButton.Visibility = _teamView ? Visibility.Visible : Visibility.Collapsed;
        _boxesTabButton.Opacity = _teamView ? .68 : 1;
        _teamTabButton.Opacity = _teamView ? 1 : .68;
    }

    private void SendSelectedToDesktop()
    {
        if (_selectedRecordId is null) return;
        var selected = _save.GetPcSnapshot().Captures.FirstOrDefault(item => item.RecordId == _selectedRecordId);
        if (selected is not null) _sendToDesktop(selected, null);
        Refresh();
    }

    private void LocateSelectedCompanion()
    {
        if (_selectedRecordId is not null) _locateDesktopCompanion(_selectedRecordId);
        Refresh();
    }

    private void ReturnSelectedCompanion()
    {
        if (_selectedRecordId is not null && _returnDesktopCompanion(_selectedRecordId)) _selectedRecordId = null;
        Refresh();
    }

    private void SetTeamView(bool showTeam)
    {
        if (_teamView == showTeam) return;
        _teamView = showTeam;
        Refresh();
    }

    private UIElement BuildLayout()
    {
        var outer = new Border { Margin = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(246, 240, 216)), BorderBrush = new SolidColorBrush(Color.FromRgb(124, 104, 61)), BorderThickness = new Thickness(5), Padding = new Thickness(9) };
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(324) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildInformationPanel());

        var right = new Grid(); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) }); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var topBar = BuildPcHeader(); Grid.SetRow(topBar, 0); right.Children.Add(topBar);
        BuildBoxScene();
        _boxViewbox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _boxScene, Margin = new Thickness(2, 0, 2, 0) };
        _teamViewbox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _teamScene, Margin = new Thickness(2, 0, 2, 0) };
        RenderOptions.SetBitmapScalingMode(_boxViewbox, BitmapScalingMode.NearestNeighbor); RenderOptions.SetBitmapScalingMode(_teamViewbox, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_boxViewbox, EdgeMode.Aliased); RenderOptions.SetEdgeMode(_teamViewbox, EdgeMode.Aliased);
        _sceneHost.Children.Add(_boxViewbox);
        _sceneHost.Children.Add(_teamViewbox);
        Grid.SetRow(_sceneHost, 1); right.Children.Add(_sceneHost);
        Grid.SetColumn(right, 2); root.Children.Add(right);
        outer.Child = root; return outer;
    }

    private UIElement BuildInformationPanel()
    {
        var panel = new Grid { Background = new SolidColorBrush(Color.FromRgb(222, 219, 235)), Margin = new Thickness(0, 0, 0, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(98) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(222) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition());

        var title = new Grid { Margin = new Thickness(4, 2, 4, 0) };
        var titleFrame = new Image { Source = Load("ui/frames/decorative_1_large_036.png"), Stretch = Stretch.Fill, IsHitTestVisible = false }; Pixel(titleFrame); title.Children.Add(titleFrame);
        title.Children.Add(new TextBlock { Text = "PKMN DATA", FontFamily = PixelFont, FontSize = 20, Foreground = new SolidColorBrush(Color.FromRgb(57, 51, 59)), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center });
        Grid.SetRow(title, 0); panel.Children.Add(title);

        var portraitFrame = _portraitFrame = new Border { Margin = new Thickness(14, 12, 14, 5), Background = new SolidColorBrush(Color.FromRgb(189, 222, 236)), BorderBrush = new SolidColorBrush(Color.FromRgb(100, 112, 137)), BorderThickness = new Thickness(5), ClipToBounds = true };
        portraitFrame.Child = _portrait; Grid.SetRow(portraitFrame, 1); panel.Children.Add(portraitFrame);

        var info = new StackPanel { Margin = new Thickness(14, 2, 14, 5), VerticalAlignment = VerticalAlignment.Top };
        _nameText.FontFamily = PixelFont; _nameText.FontSize = 23; _nameText.MaxWidth = 260; _nameText.Foreground = new SolidColorBrush(Color.FromRgb(42, 37, 48)); _nameText.TextAlignment = TextAlignment.Center; _nameText.TextWrapping = TextWrapping.Wrap; _nameText.VerticalAlignment = VerticalAlignment.Center; _nameText.Margin = new Thickness(0, 3, 0, 3);
        _numberText.FontFamily = PixelFont; _numberText.FontSize = 19; _numberText.Foreground = new SolidColorBrush(Color.FromRgb(52, 49, 64)); _numberText.TextAlignment = TextAlignment.Center; _numberText.Margin = new Thickness(0, 3, 0, 4);
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        nameRow.Children.Add(_nameText); nameRow.Children.Add(_shinyMark); nameRow.Children.Add(_megaMark); nameRow.Children.Add(_alphaMark);
        var typesFrame = _typesFrame = new Border { Background = new SolidColorBrush(Color.FromRgb(246, 240, 216)), BorderBrush = new SolidColorBrush(Color.FromRgb(171, 150, 96)), BorderThickness = new Thickness(2), Padding = new Thickness(5, 3, 5, 3), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4), Child = _types };
        _dateText.FontFamily = PixelFont; _dateText.FontSize = 16; _dateText.Foreground = new SolidColorBrush(Color.FromRgb(43, 39, 48)); _dateText.TextAlignment = TextAlignment.Center; _dateText.Margin = new Thickness(0, 4, 0, 6);
        _collectionText.FontFamily = PixelFont; _collectionText.FontSize = 16; _collectionText.Foreground = new SolidColorBrush(Color.FromRgb(48, 44, 57)); _collectionText.TextAlignment = TextAlignment.Center; _collectionText.TextWrapping = TextWrapping.Wrap; _collectionText.Margin = new Thickness(3, 8, 3, 6);
        info.Children.Add(nameRow); info.Children.Add(_numberText); info.Children.Add(typesFrame); info.Children.Add(_dateText); info.Children.Add(_collectionText);
        Grid.SetRow(info, 2); panel.Children.Add(info);

        var actions = new StackPanel { Margin = new Thickness(12, 8, 12, 10), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        actions.Children.Add(_releaseButton); actions.Children.Add(_desktopButton);
        actions.Children.Add(_locateButton); actions.Children.Add(_returnToPcButton);
        Grid.SetRow(actions, 3); panel.Children.Add(actions);
        return new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(92, 89, 112)), BorderThickness = new Thickness(5), Child = panel };
    }

    private UIElement BuildPcHeader()
    {
        var header = new Grid { Margin = new Thickness(3, 0, 3, 0), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "PC POKÉMON", FontFamily = PixelFont, FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(86, 72, 49)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0)
        };
        header.Children.Add(title);
        AdaptiveWindowSupport.DragFrom(this, header);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _boxesTabButton.Margin = new Thickness(0, 0, 4, 0); _teamTabButton.Margin = new Thickness(0);
        tabs.Children.Add(_boxesTabButton); tabs.Children.Add(_teamTabButton);
        Grid.SetColumn(tabs, 1); header.Children.Add(tabs);
        _closeButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        _closeButton.VerticalAlignment = VerticalAlignment.Center;
        _closeButton.Margin = new Thickness(3, 0, 1, 0);
        Grid.SetColumn(_closeButton, 2); header.Children.Add(_closeButton);
        return header;
    }

    private void BuildBoxScene()
    {
        Pixel(_boxBackground); _boxScene.Children.Add(_boxBackground);
        _boxName.FontFamily = PixelFont; _boxName.FontSize = 23; _boxName.Foreground = new SolidColorBrush(Color.FromRgb(68, 65, 84)); _boxName.TextAlignment = TextAlignment.Center; _boxName.VerticalAlignment = VerticalAlignment.Center; _boxName.Cursor = Cursors.Hand;
        _boxName.ToolTip = "Opciones de caja"; _boxName.MouseLeftButtonUp += (_, e) => { ShowBoxOptions(); e.Handled = true; };
        _boxName.Width = 230; _boxName.Height = 42; Canvas.SetLeft(_boxName, 196); Canvas.SetTop(_boxName, 27); _boxScene.Children.Add(_boxName);
        PlaceHeaderButton(_leftButton, 120, 25); PlaceHeaderButton(_rightButton, 438, 25);
        PlaceHeaderButton(_specialBoxButton, 58, 31); PlaceHeaderButton(_plusButton, 530, 31);

        _slotGrid.Width = 584; _slotGrid.Height = 432; _slotGrid.Background = Brushes.Transparent;
        for (var column = 0; column < 6; column++) _slotGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 5; row++) _slotGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < SlotCount; index++)
        {
            var slot = index;
            var tile = new Border { Margin = new Thickness(0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ClipToBounds = false };
            var cell = new Grid { Background = Brushes.Transparent, ClipToBounds = false };
            var icon = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            var selected = CreateSlotMarker();
            var shiny = new Image { Width = 27, Height = 19, Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 5, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed, Source = Load("ui/special_attribute/shiny-stars.png") };
            var mega = new Image { Width = 19, Height = 19, Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(5, 6, 0, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed, Source = Load("ui/special_attribute/mega-evolution.png") };
            var alpha = new Image { Width = 20, Height = 20, Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed, Source = Load("ui/special_attribute/alpha-icon.png") };
            Pixel(icon); Pixel(shiny); Pixel(mega); Pixel(alpha);
            cell.Children.Add(icon); cell.Children.Add(selected); cell.Children.Add(shiny); cell.Children.Add(mega); cell.Children.Add(alpha); tile.Child = cell;
            tile.PreviewMouseLeftButtonDown += (_, e) => SlotMouseDown(slot, tile, e);
            tile.PreviewMouseMove += (_, e) => SlotMouseMove(e);
            tile.PreviewMouseLeftButtonUp += (_, e) => SlotMouseUp(slot, tile, e);
            tile.LostMouseCapture += (_, _) =>
            {
                if (_dragRecordId is null) return;
                _dragRecordId = null; _dragging = false; Mouse.OverrideCursor = null;
                CloseDragPreview(); Refresh();
            };
            tile.PreviewMouseRightButtonDown += (_, e) => SlotRightMouseDown(tile, e);
            Grid.SetColumn(tile, index % 6); Grid.SetRow(tile, index / 6); _slotGrid.Children.Add(tile);
            _slotTiles.Add(tile); _slotIcons.Add(icon); _slotSelections.Add(selected); _slotShinyMarks.Add(shiny); _slotMegaMarks.Add(mega); _slotAlphaMarks.Add(alpha);
        }
        // These bounds align the invisible 6×5 logical grid to the green field in the 156×141 source image.
        Canvas.SetLeft(_slotGrid, 20); Canvas.SetTop(_slotGrid, 116); _boxScene.Children.Add(_slotGrid);
    }

    private void BuildTeamScene()
    {
        var backgroundPath = Path.Combine(_assets, "pc", "backgrounds", "team.gif");
        var decoder = new GifBitmapDecoder(new Uri(backgroundPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var background = new Image { Source = decoder.Frames[0], Width = 624, Height = 564, Stretch = Stretch.Fill, IsHitTestVisible = false };
        Pixel(background);
        _teamScene.Children.Add(background);

        // team.gif is a single 156×141 frame. Its six printed slots occupy
        // x=12..48, 60..96, 108..144 and y=48..77, 81..110.
        // The scene is the exact 4× pixel-art canvas used by the box view.
        for (var index = 0; index < TeamSlotCount; index++)
        {
            var slot = index;
            var column = index % 3;
            var row = index / 3;
            var tile = new Border
            {
                Width = 144, Height = 116,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ClipToBounds = false,
                Tag = null,
                ToolTip = "Casilla vacía"
            };
            var icon = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Pixel(icon);
            var content = new Grid();
            content.Children.Add(icon);
            var mega = new Image { Source = Load("ui/special_attribute/mega-evolution.png"), Width = 23, Height = 23,
                Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 10, 0, 0),
                IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            var shiny = new Image { Source = Load("ui/special_attribute/shiny-stars.png"), Width = 27, Height = 19,
                Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 12, 0),
                IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            var alpha = new Image { Source = Load("ui/special_attribute/alpha-icon.png"), Width = 21, Height = 21,
                Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0),
                IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            Pixel(mega); Pixel(shiny); Pixel(alpha); content.Children.Add(mega); content.Children.Add(shiny); content.Children.Add(alpha); tile.Child = content;
            Canvas.SetLeft(tile, 48 + column * 192);
            Canvas.SetTop(tile, 192 + row * 132);
            tile.PreviewMouseLeftButtonDown += (_, e) => TeamSlotMouseDown(slot, tile, e);
            tile.PreviewMouseMove += (_, e) => SlotMouseMove(e);
            tile.PreviewMouseLeftButtonUp += (_, e) => TeamSlotMouseUp(slot, tile, e);
            tile.LostMouseCapture += (_, _) =>
            {
                if (_dragRecordId is null) return;
                _dragRecordId = null; _teamDragSourceSlot = null; _dragging = false; Mouse.OverrideCursor = null;
                CloseDragPreview(); Refresh();
            };
            _teamScene.Children.Add(tile);
            _teamTiles.Add(tile); _teamIcons.Add(icon); _teamShinyMarks.Add(shiny); _teamMegaMarks.Add(mega); _teamAlphaMarks.Add(alpha);
        }
    }

    private void TeamSlotMouseDown(int slot, Border tile, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var recordId = tile.Tag as string;
        _selectedRecordId = recordId;
        if (recordId is not null)
        {
            _dragRecordId = recordId;
            _teamDragSourceSlot = slot;
            _dragStart = e.GetPosition(this);
            _dragging = false;
            tile.CaptureMouse();
        }
        Refresh();
        e.Handled = true;
    }

    private void TeamSlotMouseUp(int slot, Border tile, MouseButtonEventArgs e)
    {
        if (_dragRecordId is null || _teamDragSourceSlot is not { } sourceSlot) return;
        var wasDragging = _dragging;
        var id = _dragRecordId;
        _dragRecordId = null;
        _teamDragSourceSlot = null;
        if (wasDragging)
        {
            var targetSlot = TeamSlotAt(e.GetPosition(_teamScene));
            if (targetSlot >= 0 && targetSlot != sourceSlot) _save.SwapDesktopTeamSlots(sourceSlot, targetSlot);
            _selectedRecordId = id;
            Mouse.OverrideCursor = null;
            CloseDragPreview();
        }
        if (tile.IsMouseCaptured) tile.ReleaseMouseCapture();
        Refresh();
        e.Handled = true;
    }

    private int TeamSlotAt(Point point)
    {
        for (var index = 0; index < _teamTiles.Count; index++)
        {
            var tile = _teamTiles[index];
            var left = Canvas.GetLeft(tile); var top = Canvas.GetTop(tile);
            if (point.X >= left && point.X < left + tile.Width && point.Y >= top && point.Y < top + tile.Height) return index;
        }
        return -1;
    }

    private void PlaceHeaderButton(Button button, double x, double y)
    {
        Canvas.SetLeft(button, x); Canvas.SetTop(button, y); _boxScene.Children.Add(button);
    }

    private static void SetSpriteButtonEnabledVisual(Button button, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Opacity = 1;
        if (button.Content is Image image) image.Opacity = enabled ? 1 : .42;
    }

    private Grid CreateSlotMarker()
    {
        var marker = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        foreach (var alignment in new[]
        {
            (System.Windows.HorizontalAlignment.Left, VerticalAlignment.Top),
            (System.Windows.HorizontalAlignment.Right, VerticalAlignment.Top),
            (System.Windows.HorizontalAlignment.Left, VerticalAlignment.Bottom),
            (System.Windows.HorizontalAlignment.Right, VerticalAlignment.Bottom)
        })
        {
            var corner = new Image { Width = 22, Height = 22, Stretch = Stretch.Fill, HorizontalAlignment = alignment.Item1, VerticalAlignment = alignment.Item2, IsHitTestVisible = false };
            Pixel(corner); marker.Children.Add(corner);
        }
        return marker;
    }

    private static BitmapSource[] LoadCorners(BitmapSource source)
    {
        const int size = 24;
        var width = source.PixelWidth; var height = source.PixelHeight;
        var crops = new[]
        {
            new Int32Rect(0, 0, size, size),
            new Int32Rect(width - size, 0, size, size),
            new Int32Rect(0, height - size, size, size),
            new Int32Rect(width - size, height - size, size, size)
        };
        return crops.Select(rect => { var crop = new CroppedBitmap(source, rect); crop.Freeze(); return (BitmapSource)crop; }).ToArray();
    }

    private void DrawPokemonDetails(PcSnapshot snapshot)
    {
        var record = _selectedRecordId is null ? null : snapshot.Captures.FirstOrDefault(item => item.RecordId == _selectedRecordId);
        if (record is null)
        {
            _portrait.Visibility = Visibility.Collapsed; _nameText.Text = "Resumen de colección"; _numberText.Text = string.Empty;
            _shinyMark.Visibility = Visibility.Collapsed; _megaMark.Visibility = Visibility.Collapsed; _alphaMark.Visibility = Visibility.Collapsed; if (_typesFrame is not null) _typesFrame.Visibility = Visibility.Collapsed;
            _dateText.Text = string.Empty; _collectionText.Visibility = Visibility.Visible;
            var teamCount = _save.GetDesktopTeamSlots().Count(id => id is not null);
            _collectionText.Text = _teamView
                ? $"Equipo: {teamCount}/6\nEjemplares: {snapshot.Captures.Count}\nShiny: {snapshot.Captures.Count(item => item.IsShiny)}"
                : $"Ejemplares: {snapshot.Captures.Count}\nNormales: {snapshot.Captures.Count(item => !item.IsShiny)}\nShiny: {snapshot.Captures.Count(item => item.IsShiny)}\nCajas: {snapshot.Boxes.Count}";
            return;
        }
        var metadata = _metadata.GetForCapture(record.Id);
        var entry = _catalog.GetForCapture(record.Id);
        MegaEvolutionDefinition? megaForm = null;
        var hasMega = record.MegaFormId is not null && _megaEvolutions.TryGetForm(record.MegaFormId, out megaForm!);
        var portrait = hasMega
            ? LoadMegaDetailsSprite(megaForm!, record.IsShiny,
                PokemonSex.Get(metadata).Equals("female", StringComparison.OrdinalIgnoreCase))
            : LoadAbsolute(Path.Combine(_assets, (record.IsShiny ? entry.EncounterShiny : entry.EncounterNormal).Replace('/', Path.DirectorySeparatorChar)));
        SetPortrait(portrait); _portrait.Visibility = Visibility.Visible;
        var displayName = hasMega ? megaForm!.DisplayName : metadata.DisplayName;
        if (PokemonSex.Get(metadata).Equals("female", StringComparison.OrdinalIgnoreCase)
            && !displayName.EndsWith(" F", StringComparison.OrdinalIgnoreCase)) displayName += " F";
        _nameText.Text = displayName; _numberText.Text = $"N.º {metadata.PokedexNumber:000}";
        _shinyMark.Visibility = record.IsShiny ? Visibility.Visible : Visibility.Collapsed;
        _megaMark.Visibility = hasMega ? Visibility.Visible : Visibility.Collapsed;
        _alphaMark.Visibility = record.IsAlpha ? Visibility.Visible : Visibility.Collapsed;
        var attributeCount = (record.IsShiny ? 1 : 0) + (hasMega ? 1 : 0) + (record.IsAlpha ? 1 : 0);
        _nameText.MaxWidth = Math.Max(150, 260 - attributeCount * 30);
        if (_typesFrame is not null) _typesFrame.Visibility = Visibility.Visible;
        _types.Children.Clear();
        foreach (var type in metadata.Types)
        {
            var typeIcon = new Image { Source = Load($"ui/types_pokemon/{type}.png"), Width = 78, Height = 34, Margin = new Thickness(3, 0, 3, 0), Stretch = Stretch.Uniform };
            Pixel(typeIcon); _types.Children.Add(typeIcon);
        }
        _dateText.Text = $"Capturado · {record.CapturedAt.ToLocalTime():yyyy-MM-dd}";
        _collectionText.Visibility = Visibility.Collapsed;
    }

    private void SlotMouseDown(int slot, Border tile, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_movingRecordId is not null)
        {
            if (_save.MoveRecord(_movingRecordId, CurrentBoxId(), slot))
            { _selectedRecordId = _movingRecordId; _movingRecordId = null; Mouse.OverrideCursor = null; CloseDragPreview(); Refresh(); }
            e.Handled = true; return;
        }
        var recordId = tile.Tag as string;
        if (recordId is null) { _selectedRecordId = null; Refresh(); e.Handled = true; return; }
        _dragRecordId = recordId;
        _teamDragSourceSlot = null;
        _dragStart = e.GetPosition(this);
        _dragging = false;
        _selectedRecordId = recordId;
        tile.CaptureMouse();
        Refresh();
    }

    private void SlotRightMouseDown(Border tile, MouseButtonEventArgs e)
    {
        if (_movingRecordId is not null)
        {
            CancelMove();
            e.Handled = true;
            return;
        }

        if (tile.Tag is not string recordId) return;
        _dragRecordId = null;
        _teamDragSourceSlot = null;
        _dragging = false;
        _selectedRecordId = recordId;
        _movingRecordId = recordId;
        Mouse.OverrideCursor = Cursors.Arrow;
        ShowDragPreview(recordId);
        Refresh();
        e.Handled = true;
    }

    private void SlotMouseMove(MouseEventArgs e)
    {
        if (_dragRecordId is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        if (!_dragging && (Math.Abs(current.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(current.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance))
        {
            _dragging = true;
            Mouse.OverrideCursor = Cursors.Arrow;
            ShowDragPreview(_dragRecordId);
        }
    }

    private void SlotMouseUp(int slot, Border tile, MouseButtonEventArgs e)
    {
        if (_dragRecordId is null) return;
        var id = _dragRecordId; _dragRecordId = null; _teamDragSourceSlot = null;
        if (_dragging)
        {
            var target = SlotAt(e.GetPosition(_slotGrid));
            var snapshot = _save.GetPcSnapshot();
            var record = snapshot.Captures.FirstOrDefault(item => item.RecordId == id);
            if (record is not null && target < 0 && GetCursorPos(out var cursor)
                && IsDesktopDrop(cursor.X, cursor.Y))
                _sendToDesktop(record, new Point(cursor.X, cursor.Y));
            else if (target >= 0)
                _save.MoveRecord(id, CurrentBoxId(), target);
            Mouse.OverrideCursor = null; Refresh();
            CloseDragPreview();
        }
        else
        {
            // A plain left click only selects; movement starts only after the
            // pointer crosses the system drag threshold.
            Refresh();
        }
        if (tile.IsMouseCaptured) tile.ReleaseMouseCapture();
        e.Handled = true;
    }

    private bool IsDesktopDrop(int screenX, int screenY)
    {
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds)) return false;
        var insidePc = screenX >= bounds.Left && screenX < bounds.Right && screenY >= bounds.Top && screenY < bounds.Bottom;
        var isOnDesktopScreen = Forms.Screen.AllScreens.Any(screen =>
            screen.WorkingArea.Contains(screenX, screenY));
        return !insidePc && isOnDesktopScreen;
    }

    internal bool ContainsScreenPoint(int screenX, int screenY)
    {
        if (!IsVisible || WindowState == WindowState.Minimized
            || !GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds)) return false;
        return screenX >= bounds.Left && screenX < bounds.Right
            && screenY >= bounds.Top && screenY < bounds.Bottom;
    }

    internal IntPtr GetDragPreviewHandle() => _dragPreviewWindow?.GetWindowHandle() ?? IntPtr.Zero;

    private int SlotAt(Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.X >= _slotGrid.ActualWidth || point.Y >= _slotGrid.ActualHeight) return -1;
        var column = Math.Clamp((int)(point.X / (_slotGrid.ActualWidth / 6d)), 0, 5);
        var row = Math.Clamp((int)(point.Y / (_slotGrid.ActualHeight / 5d)), 0, 4);
        return row * 6 + column;
    }

    private void CancelMove()
    {
        _movingRecordId = null; _dragRecordId = null; _teamDragSourceSlot = null; _dragging = false;
        Mouse.OverrideCursor = null;
        CloseDragPreview();
        foreach (var tile in _slotTiles) if (tile.IsMouseCaptured) tile.ReleaseMouseCapture();
        Refresh();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_movingRecordId is not null || _dragRecordId is not null)) { CancelMove(); e.Handled = true; }
    }

    private void ShowDragPreview(string recordId)
    {
        CloseDragPreview();
        var record = _save.GetPcSnapshot().Captures.FirstOrDefault(item => item.RecordId == recordId);
        if (record is null) return;
        var sizingImage = new Image();
        var source = LoadPokemonIcon(record, sizingImage);
        var preview = new DragPreviewWindow(source, Math.Max(24, sizingImage.Width), Math.Max(24, sizingImage.Height));
        _dragPreviewWindow = preview;
        preview.Closed += (_, _) => { if (ReferenceEquals(_dragPreviewWindow, preview)) _dragPreviewWindow = null; };
        preview.Show();
    }

    private void CloseDragPreview()
    {
        var preview = _dragPreviewWindow;
        _dragPreviewWindow = null;
        if (preview?.IsVisible == true) preview.Close();
    }

    private void Navigate(int direction)
    {
        _boxIndex = Math.Clamp(_boxIndex + direction, 0, _save.GetPcSnapshot().Boxes.Count - 1);
        Refresh();
    }

    private void CreateBox()
    {
        _save.AddBox(); _boxIndex = _save.GetPcSnapshot().Boxes.Count - 1; _selectedRecordId = null; Refresh();
    }

    private void CreateSpecialBox()
    {
        _save.AddSpecialBox();
        var boxes = _save.GetPcSnapshot().Boxes;
        _boxIndex = boxes.Select((box, index) => (box, index)).First(entry => entry.box.IsSpecial).index;
        _selectedRecordId = null;
        Refresh();
    }

    private void ShowBoxOptions()
    {
        var boxes = _save.GetPcSnapshot().Boxes;
        var box = boxes[_boxIndex];
        var dialog = new BoxOptionsWindow(this, box, _save, _boxIndex, boxes.Count);
        dialog.Owner = this; dialog.Closed += (_, _) => Refresh(); dialog.Show();
    }

    private void ConfirmRelease()
    {
        if (_selectedRecordId is null) { Refresh(); return; }
        var record = _save.GetPcSnapshot().Captures.FirstOrDefault(item => item.RecordId == _selectedRecordId);
        if (record is null) { _selectedRecordId = null; Refresh(); return; }
        var name = _metadata.GetForCapture(record.Id).DisplayName;
        if (!ConfirmReleasePrompt($"¿Borrar a {name} del PC?", "Esta acción es permanente y no se puede deshacer. No recibirás objetos.")) return;
        if (record.IsShiny && !ConfirmReleasePrompt("Confirmación shiny", $"{name} es shiny. ¿Confirmas su liberación permanente?")) return;
        if (!_save.ReleaseRecord(record.RecordId, out _))
        {
            _selectedRecordId = null;
            Refresh();
            PixelConfirmWindow.Notify(this, "No se pudo liberar", "Vuelve a seleccionar el Pokémon e inténtalo otra vez.");
            return;
        }
        _selectedRecordId = null;
        Refresh();
    }

    private bool ConfirmReleasePrompt(string title, string message)
    {
#if DEBUG
        if (_debugAutoConfirmRelease) return true;
#endif
        return PixelConfirmWindow.Confirm(this, title, message);
    }

    private string CurrentBoxId() => _save.GetPcSnapshot().Boxes[_boxIndex].Id;

    private BitmapSource LoadPokemonIcon(CapturedPokemon record, Image icon)
        => LoadPokemonIcon(record, icon, 90, 76, 1.5);

    private BitmapSource LoadPokemonIcon(CapturedPokemon record, Image icon, double maxWidth, double maxHeight, double requestedScale)
    {
        var entry = _catalog.GetForCapture(record.Id);
        var relative = record.MegaFormId is not null && _megaEvolutions.TryGetForm(record.MegaFormId, out var megaForm)
            ? (record.IsShiny ? megaForm.IconShiny : megaForm.IconNormal)
            : (record.IsShiny ? entry.IconShiny : entry.IconNormal);
        var source = LoadAbsolute(Path.Combine(_assets, relative.Replace('/', Path.DirectorySeparatorChar)));
        // Catalog icon PNGs are 128×64 two-frame sheets; display the first native 64×64 frame.
        var width = source.PixelWidth >= 128 && source.PixelHeight >= 64 ? 64 : source.PixelWidth;
        var height = source.PixelWidth >= 128 && source.PixelHeight >= 64 ? 64 : source.PixelHeight;
        var frame = new CroppedBitmap(source, new Int32Rect(0, 0, width, height)); frame.Freeze();
        var (visibleFrame, bounds) = CropToVisiblePixels(frame);
        // Current cell art is 68 px in a 96.7 × 86.4 logical cell. This scales its
        // visible pixels by 1.5 while capping unusually full icons within each slot.
        var scale = Math.Min(68d / 64d * requestedScale, Math.Min(maxWidth / bounds.Width, maxHeight / bounds.Height));
        icon.Width = bounds.Width * scale; icon.Height = bounds.Height * scale;
        return visibleFrame;
    }

    private BitmapSource LoadMegaDetailsSprite(MegaEvolutionDefinition form, bool isShiny, bool isFemale)
    {
        var relative = MegaEvolutionCatalog.GetDetailsEncounterPath(form, isShiny, isFemale, _assets);
        if (!relative.StartsWith("pokemon/encounter/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"El retrato Mega de {form.DisplayName} debe provenir de la carpeta encounter: {relative}");
        return LoadAbsolute(Path.Combine(_assets, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private void SetPortrait(BitmapSource source)
    {
        var (visibleSprite, bounds) = CropToVisiblePixels(source);
        // Match the old 190/192 canvas scale, enlarged by 50%. Only transparent
        // margins are excluded; the frame and every visible sprite pixel remain intact.
        const double requestedScale = 190d / 192d * 1.5d;
        const double frameInnerWidth = 276d;
        const double frameInnerHeight = 202d;
        var scale = Math.Min(requestedScale, Math.Min(frameInnerWidth / bounds.Width, frameInnerHeight / bounds.Height));
        _portrait.Source = visibleSprite;
        _portrait.Width = Math.Max(1, bounds.Width * scale);
        _portrait.Height = Math.Max(1, bounds.Height * scale);
    }

    private static (BitmapSource Image, Int32Rect Bounds) CropToVisiblePixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var left = converted.PixelWidth; var top = converted.PixelHeight; var right = -1; var bottom = -1;
        for (var y = 0; y < converted.PixelHeight; y++)
        for (var x = 0; x < converted.PixelWidth; x++)
        {
            if (pixels[y * stride + x * 4 + 3] == 0) continue;
            if (x < left) left = x; if (x > right) right = x;
            if (y < top) top = y; if (y > bottom) bottom = y;
        }
        if (right < left || bottom < top) return (source, new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight));
        var bounds = new Int32Rect(left, top, right - left + 1, bottom - top + 1);
        var crop = new CroppedBitmap(source, bounds); crop.Freeze();
        return (crop, bounds);
    }

    private void SetBackground(string background)
    {
        var path = Path.Combine(_assets, "pc", "backgrounds", Path.GetFileName(background));
        if (!File.Exists(path)) path = Path.Combine(_assets, "pc", "backgrounds", "1.gif");
        var decoder = new GifBitmapDecoder(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        _backgroundFrameIndex = 0; _boxBackground.Source = decoder.Frames[_backgroundFrameIndex]; Pixel(_boxBackground);
    }

    internal void SelectDebug(string? pokemonId)
    {
        _selectedRecordId = pokemonId is null ? null : _save.GetPcSnapshot().Captures.FirstOrDefault(record => record.Id.Equals(pokemonId, StringComparison.OrdinalIgnoreCase))?.RecordId;
        Refresh();
    }
#if DEBUG
    internal void SelectTeamViewForDebug(bool showTeam) => SetTeamView(showTeam);
    internal void SelectTeamPokemonForDebug(string pokemonId)
    {
        _teamView = true;
        _selectedRecordId = _save.GetPcSnapshot().Captures.FirstOrDefault(record => record.Id.Equals(pokemonId, StringComparison.OrdinalIgnoreCase))?.RecordId;
        Refresh();
    }
    internal bool CanReleaseDebug => _releaseButton.IsEnabled;
    internal bool ClickReleaseAndConfirmForDebug()
    {
        var before = _save.GetPcSnapshot().Captures.Count;
        if (!CanReleaseDebug) return false;
        _debugAutoConfirmRelease = true;
        try { _releaseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { _debugAutoConfirmRelease = false; }
        return _save.GetPcSnapshot().Captures.Count == before - 1 && _selectedRecordId is null;
    }
#endif
    internal Window ShowBoxOptionsForDebug()
    {
        var boxes = _save.GetPcSnapshot().Boxes;
        _debugOptions = new BoxOptionsWindow(this, boxes[_boxIndex], _save, _boxIndex, boxes.Count) { Owner = this };
        _debugOptions.Show(); return _debugOptions;
    }
    internal Window ShowBackgroundGalleryForDebug()
    {
        var boxes = _save.GetPcSnapshot().Boxes;
        _debugGallery = new BoxBackgroundGalleryWindow(this, boxes[_boxIndex], _save, modal: false) { Owner = this };
        _debugGallery.Show(); return _debugGallery;
    }

    private Button CreateSpriteButton(string relative, string tip, Action action, double width, double height)
    {
        var image = new Image { Source = Load(relative), Width = width, Height = height, Stretch = Stretch.Uniform };
        Pixel(image);
        var button = new Button { Content = image, ToolTip = tip, Width = width + 4, Height = height + 4, Padding = new Thickness(1), Margin = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        button.Template = BareButtonTemplate();
        button.MouseEnter += (_, _) => { if (button.IsEnabled) image.Opacity = .82; };
        button.MouseLeave += (_, _) => image.Opacity = button.IsEnabled ? 1 : .42;
        button.PreviewMouseLeftButtonDown += (_, _) => { if (button.IsEnabled) image.Opacity = .62; };
        button.PreviewMouseLeftButtonUp += (_, _) => { if (button.IsEnabled) image.Opacity = .82; };
        button.Click += (_, _) => { if (button.IsEnabled) action(); };
        return button;
    }

    private Button CreateSpecialBoxButton()
    {
        var greenButton = new Image { Source = Load("ui/controls/utility_r3_c1_024.png"),
            Width = 40, Height = 40, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        Pixel(greenButton);
        var star = new System.Windows.Shapes.Polygon
        {
            Fill = new SolidColorBrush(Color.FromRgb(0, 94, 43)),
            Points = new PointCollection
            {
                new(12, 0), new(15, 9), new(24, 9), new(17, 14), new(20, 23),
                new(12, 18), new(4, 23), new(7, 14), new(0, 9), new(9, 9)
            },
            Width = 24, Height = 24, Stretch = System.Windows.Media.Stretch.Fill,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetEdgeMode(star, EdgeMode.Aliased);
        var content = new Grid { Width = 40, Height = 40, ClipToBounds = false,
            Background = PixelButtonUi.HitTestBackground };
        content.Children.Add(greenButton);
        content.Children.Add(star);
        var button = new Button { Content = content, ToolTip = "Crear caja especial", Width = 44, Height = 44,
            Padding = new Thickness(0), Margin = new Thickness(0), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, Template = BareButtonTemplate() };
        button.MouseEnter += (_, _) => { if (button.IsEnabled) content.Opacity = .82; };
        button.MouseLeave += (_, _) => content.Opacity = button.IsEnabled ? 1 : .42;
        button.PreviewMouseLeftButtonDown += (_, _) => { if (button.IsEnabled) content.Opacity = .62; };
        button.PreviewMouseLeftButtonUp += (_, _) => { if (button.IsEnabled) content.Opacity = .82; };
        button.Click += (_, _) => { if (button.IsEnabled) CreateSpecialBox(); };
        return button;
    }

    private Button TransparentIconButton(string relative, string tip, Action action, double width, double height)
    {
        var image = new Image { Source = Load(relative), Width = width, Height = height, Stretch = Stretch.Uniform };
        Pixel(image);
        var button = new Button { Content = image, Width = width + 2, Height = height + 2, ToolTip = tip, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Cursor = Cursors.Hand };
        button.Template = BareButtonTemplate();
        button.MouseEnter += (_, _) => image.Opacity = .82; button.MouseLeave += (_, _) => image.Opacity = 1;
        button.PreviewMouseLeftButtonDown += (_, _) => image.Opacity = .62; button.PreviewMouseLeftButtonUp += (_, _) => image.Opacity = .82;
        button.Click += (_, _) => action(); return button;
    }

    private Button CreateTextButton(string label, string color, Action action, double width, double height, bool enabled = true)
    {
        var normal = Load($"ui/buttons/palette/{color}_{(enabled ? "normal" : "disabled")}_{(color == "blue" ? (enabled ? "028" : "031") : color == "red" ? (enabled ? "017" : "020") : (enabled ? "009" : "012"))}.png");
        var hover = enabled ? Load($"ui/buttons/palette/{color}_hover_{(color == "blue" ? "029" : color == "red" ? "018" : "010")}.png") : normal;
        var pressed = enabled ? Load($"ui/buttons/palette/{color}_pressed_{(color == "blue" ? "030" : color == "red" ? "019" : "011")}.png") : normal;
        var background = new Grid { Width = width, Height = height, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        SetButtonNineSlice(background, normal);
        var text = new TextBlock { Text = label, FontFamily = PixelFont, FontSize = label == "Enviar al escritorio" ? 14 : 16, MaxWidth = width - 20, TextWrapping = TextWrapping.Wrap, Foreground = enabled ? Brushes.White : new SolidColorBrush(Color.FromRgb(83, 84, 91)), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, IsHitTestVisible = false };
        // BareButtonTemplate no dibuja el Background del Button: dar al contenido
        // una superficie alfa hit-testable hace que el clic alcance también el sprite.
        var content = new Grid { Background = PixelButtonUi.HitTestBackground, IsHitTestVisible = true };
        content.Children.Add(background); content.Children.Add(text);
        var button = new Button { Content = content, Width = width, Height = height, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(0), BorderThickness = new Thickness(0), IsEnabled = enabled, Cursor = enabled ? Cursors.Hand : Cursors.Arrow, ToolTip = label };
        button.Template = BareButtonTemplate(); button.Background = Brushes.Transparent;
        if (enabled)
        {
            button.MouseEnter += (_, _) => SetButtonNineSlice(background, hover);
            button.MouseLeave += (_, _) => SetButtonNineSlice(background, normal);
            button.PreviewMouseLeftButtonDown += (_, _) => SetButtonNineSlice(background, pressed);
            button.PreviewMouseLeftButtonUp += (_, _) => SetButtonNineSlice(background, hover);
            button.Click += (_, _) => action();
        }
        return button;
    }

    private static void SetButtonNineSlice(Grid grid, BitmapSource source)
    {
        const int edgeWidth = 16;
        var leftWidth = Math.Min(edgeWidth, Math.Max(1, source.PixelWidth / 3));
        var rightWidth = leftWidth;
        var middleWidth = Math.Max(1, source.PixelWidth - leftWidth - rightWidth);
        grid.Children.Clear(); grid.ColumnDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        var x = 0;
        foreach (var (width, column) in new[] { (leftWidth, 0), (middleWidth, 1), (rightWidth, 2) })
        {
            var crop = new CroppedBitmap(source, new Int32Rect(x, 0, width, source.PixelHeight)); crop.Freeze();
            var image = new Image { Source = crop, Stretch = Stretch.Fill, IsHitTestVisible = false };
            Pixel(image); Grid.SetColumn(image, column); grid.Children.Add(image); x += width;
        }
    }

    private static ControlTemplate BareButtonTemplate()
    {
        var root = new FrameworkElementFactory(typeof(ContentPresenter));
        root.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        root.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        return new ControlTemplate(typeof(Button)) { VisualTree = root };
    }

    private static string FormatBoxBackground(string name) => Path.GetFileName(name);

    private void RestoreBoundsFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var state = JsonSerializer.Deserialize<PcWindowState>(File.ReadAllText(_settingsPath));
            if (state is null || state.Width < MinWidth || state.Height < MinHeight) return;
            Width = state.Width; Height = state.Height;
            Left = state.Left; Top = state.Top;
            AdaptiveWindowSupport.KeepAccessible(this);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { EncounterDiagnostics.WriteException("pc-window-restore", exception); }
    }

    private void SaveBounds()
    {
        if (!IsLoaded || WindowState != WindowState.Normal) return;
        try
        {
            AdaptiveWindowSupport.KeepAccessible(this);
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new PcWindowState(Left, Top, Width, Height)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { EncounterDiagnostics.WriteException("pc-window-save", exception); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    private static BitmapSource Load(string relative) => LoadAbsolute(Path.Combine(AppContext.BaseDirectory, "assets", relative.Replace('/', Path.DirectorySeparatorChar)));
    private static BitmapSource LoadAbsolute(string path)
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    private static void Pixel(Image image) { RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(image, EdgeMode.Aliased); image.SnapsToDevicePixels = true; }
    private static FontFamily PixelFont => new(new Uri("pack://application:,,,/assets/fonts/"), "#Minecraft");
    private sealed record PcWindowState(double Left, double Top, double Width, double Height);

}

internal sealed class BoxOptionsWindow : Window
{
    private readonly PokemonBox _box;
    private readonly GameSaveStore _save;
    private readonly int _boxIndex;
    private readonly int _boxCount;
    private readonly PokemonPcWindow _pc;
    private readonly Image _backgroundPreview;
    private readonly TextBlock _backgroundName;
    internal BoxOptionsWindow(PokemonPcWindow pc, PokemonBox box, GameSaveStore save, int boxIndex, int boxCount)
    {
        _pc = pc; _box = box; _save = save; _boxIndex = boxIndex; _boxCount = boxCount; Owner = pc;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Width = 540; Height = 356; MinWidth = 460; MinHeight = 320;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; AllowsTransparency = false;
        Title = "PokéGrass · Configurar caja"; Background = new SolidColorBrush(Color.FromRgb(244, 239, 217)); ShowInTaskbar = UiTestMode.Enabled; Topmost = true;
        var layout = new Grid { Margin = new Thickness(34, 20, 34, 18), ClipToBounds = true };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        var title = new TextBlock { Text = "CONFIGURAR CAJA", FontFamily = PixelUi.PixelFont, FontSize = 22,
            Foreground = new SolidColorBrush(Color.FromRgb(72, 62, 44)), TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Background = Brushes.Transparent, Margin = new Thickness(4, 0, 4, 2) };
        layout.Children.Add(title); AdaptiveWindowSupport.DragFrom(this, title);
        var sections = new Grid { Margin = new Thickness(2, 2, 2, 6) };
        sections.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.05, GridUnitType.Star) });
        sections.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.3, GridUnitType.Star) });
        sections.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.95, GridUnitType.Star) });
        sections.Children.Add(Section("Nombre de la caja", box.Name, "Renombrar", Rename));
        var bgSection = new Grid { Margin = new Thickness(0, 4, 0, 4), Background = new SolidColorBrush(Color.FromRgb(250, 247, 226)), MinHeight = 72 };
        bgSection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        bgSection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bgSection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(144) });
        _backgroundPreview = new Image { Width = 92, Height = 58, Stretch = Stretch.Uniform, Margin = new Thickness(6), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        SetPreview(_backgroundPreview, box.Background);
        Grid.SetColumn(_backgroundPreview, 0); bgSection.Children.Add(_backgroundPreview);
        var backgroundText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 4, 0) };
        backgroundText.Children.Add(new TextBlock { Text = "FONDO ACTUAL", FontFamily = PixelUi.PixelFont, FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(72, 62, 44)) });
        _backgroundName = new TextBlock { Text = Path.GetFileNameWithoutExtension(box.Background), FontFamily = PixelUi.PixelFont, FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 46, 38)), Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        backgroundText.Children.Add(_backgroundName); Grid.SetColumn(backgroundText, 1); bgSection.Children.Add(backgroundText);
        var bgButton = PixelUi.TextButton("Cambiar fondo", ChangeBackground, true, 132, 46);
        bgButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Center; bgButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(bgButton, 2); bgSection.Children.Add(bgButton); Grid.SetRow(bgSection, 1); sections.Children.Add(bgSection);
        var order = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = new SolidColorBrush(Color.FromRgb(250, 247, 226)), MinHeight = 48 };
        order.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        order.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) }); order.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        var orderLabel = new TextBlock { Text = "Orden de la caja", FontFamily = PixelUi.PixelFont, FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(72, 62, 44)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        order.Children.Add(orderLabel);
        var boxes = _save.GetPcSnapshot().Boxes;
        var canMoveLeft = boxIndex > 0 && boxes[boxIndex - 1].IsSpecial == box.IsSpecial;
        var canMoveRight = boxIndex < boxCount - 1 && boxes[boxIndex + 1].IsSpecial == box.IsSpecial;
        var left = Arrow("ui/arrows/navigation_1_1_002.png", "Mover caja a la izquierda", () => { _save.ReorderBox(_box.Id, -1); Close(); }, canMoveLeft);
        var right = Arrow("ui/arrows/navigation_1_2_003.png", "Mover caja a la derecha", () => { _save.ReorderBox(_box.Id, 1); Close(); }, canMoveRight);
        Grid.SetColumn(left, 1); Grid.SetColumn(right, 2); order.Children.Add(left); order.Children.Add(right); Grid.SetRow(order, 2); sections.Children.Add(order);
        Grid.SetRow(sections, 1); layout.Children.Add(sections);
        var footer = new Grid { Margin = new Thickness(2, 1, 2, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var canDelete = boxCount > 1 && box.Slots.All(id => id is null);
        var delete = DialogButton("Eliminar caja", DeleteBox, canDelete, 176, 44, "red"); delete.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        var close = DialogButton("Cerrar", Close, true, 136, 44, "blue"); close.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        Grid.SetColumn(delete, 0); Grid.SetColumn(close, 1); footer.Children.Add(delete); footer.Children.Add(close);
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        var frame = PixelButtonUi.NineSlice(PixelUi.Load("ui/frames/decorative_1_large_001.png"), Width, Height, 32, 32);
        var root = new Grid(); root.Children.Add(frame); root.Children.Add(layout);
        Content = root;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(8), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        SizeChanged += (_, _) =>
        {
            var newFrame = PixelButtonUi.NineSlice(PixelUi.Load("ui/frames/decorative_1_large_001.png"), ActualWidth, ActualHeight, 32, 32);
            root.Children.RemoveAt(0); root.Children.Insert(0, newFrame);
        };
        Closed += (_, _) => _pc.Refresh();
    }

    private UIElement Section(string label, string value, string buttonLabel, Action action)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = new SolidColorBrush(Color.FromRgb(250, 247, 226)), MinHeight = 50 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(144) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
        text.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontFamily = PixelUi.PixelFont, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(100, 87, 60)) });
        text.Children.Add(new TextBlock { Text = value, FontFamily = PixelUi.PixelFont, FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(55, 49, 41)), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(text);
        var button = DialogButton(buttonLabel, action, true, 126, 44); button.HorizontalAlignment = System.Windows.HorizontalAlignment.Center; button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 1); row.Children.Add(button); return row;
    }

    private static Button DialogButton(string label, Action action, bool enabled, double width, double height, string color = "green")
        => PixelButtonUi.Button(label, color, width, height, action, enabled, fontSize: 14);

    private Button Arrow(string asset, string tip, Action action, bool enabled)
    {
        var image = new Image { Source = PixelUi.Load(asset), Width = 34, Height = 38, Stretch = Stretch.Uniform, Opacity = enabled ? 1 : .4 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        var button = new Button { Content = image, Width = 56, Height = 44, Margin = new Thickness(4, 1, 4, 1), ToolTip = tip, IsEnabled = enabled,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = enabled ? Cursors.Hand : Cursors.Arrow, Template = PixelUi.BareTemplate() };
        if (enabled) button.Click += (_, _) => action(); return button;
    }

    private void Rename()
    {
        var dialog = new Window { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 510, Height = 230, MinWidth = 480, MinHeight = 210, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResize, Background = new SolidColorBrush(Color.FromRgb(246, 240, 216)), ShowInTaskbar = false };
        var panel = new Grid { Margin = new Thickness(22) }; panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) }); panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new TextBlock { Text = "RENOMBRAR CAJA", FontFamily = PixelUi.PixelFont, FontSize = 19, Foreground = new SolidColorBrush(Color.FromRgb(70, 58, 46)), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(heading); AdaptiveWindowSupport.DragFrom(dialog, heading);
        var input = new TextBox { Text = _box.Name, FontFamily = PixelUi.PixelFont, FontSize = 17, Height = 46, Padding = new Thickness(10), BorderBrush = new SolidColorBrush(Color.FromRgb(118, 101, 69)), Background = new SolidColorBrush(Color.FromRgb(255, 251, 232)), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 4, 6, 8) }; Grid.SetRow(input, 1); panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(PixelUi.TextButton("Cancelar", () => dialog.DialogResult = false, true, 172, 52, "blue"));
        buttons.Children.Add(PixelUi.TextButton("Confirmar", () => { _save.RenameBox(_box.Id, input.Text); dialog.DialogResult = true; }, true, 172, 52));
        Grid.SetRow(buttons, 2); panel.Children.Add(buttons);
        dialog.Content = new Border { BorderThickness = new Thickness(8), BorderBrush = new ImageBrush(PixelUi.Load("ui/frames/decorative_2_large_057.png")) { Stretch = Stretch.Fill }, Child = panel };
        System.Windows.Shell.WindowChrome.SetWindowChrome(dialog, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(8), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        if (dialog.ShowDialog() == true) { _pc.Refresh(); Close(); }
    }

    private void ChangeBackground()
    {
        var gallery = new BoxBackgroundGalleryWindow(_pc, _box, _save) { Owner = this };
        if (gallery.ShowDialog() == true)
        {
            var refreshed = _save.GetPcSnapshot().Boxes.FirstOrDefault(candidate => candidate.Id == _box.Id);
            if (refreshed is not null) _box.Background = refreshed.Background;
            SetPreview(_backgroundPreview, _box.Background);
            _backgroundName.Text = Path.GetFileNameWithoutExtension(_box.Background);
            _pc.Refresh();
        }
    }

    private static void SetPreview(Image image, string background)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "pc", "backgrounds", Path.GetFileName(background));
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "assets", "pc", "backgrounds", "1.gif");
        image.Source = new GifBitmapDecoder(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
    }

    private void DeleteBox()
    {
        if (PixelConfirmWindow.Confirm(this, "Eliminar caja vacía", "Esta acción no se puede deshacer."))
        { _save.DeleteEmptyBox(_box.Id); Close(); }
    }
}

internal sealed class BoxBackgroundGalleryWindow : Window
{
    private readonly bool _modal;
    private readonly PokemonBox _box;
    private readonly GameSaveStore _save;
    private readonly TextBlock _previewName;
    private readonly TextBlock _currentName;
    private readonly Image _previewImage;
    private readonly List<(string File, Grid Frame, Button Button)> _tiles = [];
    private string _selectedBackground;
    internal BoxBackgroundGalleryWindow(PokemonPcWindow pc, PokemonBox box, GameSaveStore save, bool modal = true)
    {
        _modal = modal; _box = box; _save = save; _selectedBackground = Path.GetFileName(box.Background);
        Owner = pc; WindowStartupLocation = WindowStartupLocation.CenterOwner; Width = 760; Height = 610; MinWidth = 610; MinHeight = 450;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; AllowsTransparency = false;
        Title = "PokéGrass · Fondos de caja"; Background = new SolidColorBrush(Color.FromRgb(246, 240, 216)); ShowInTaskbar = UiTestMode.Enabled; Topmost = true;
        var layout = new Grid { Margin = new Thickness(34, 24, 34, 22) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(98) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        var title = new TextBlock { Text = "FONDOS DE CAJA", FontFamily = PixelUi.PixelFont, FontSize = 21,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 61, 44)), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, TextAlignment = TextAlignment.Center };
        layout.Children.Add(title); AdaptiveWindowSupport.DragFrom(this, title);

        var previewRow = new Grid { Margin = new Thickness(4, 2, 4, 6), Background = new SolidColorBrush(Color.FromRgb(250, 247, 226)) };
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _previewImage = new Image { Width = 144, Height = 82, Stretch = Stretch.Uniform, Margin = new Thickness(6), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        previewRow.Children.Add(_previewImage);
        var previewInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        _currentName = new TextBlock { Text = $"Fondo actual: {Path.GetFileNameWithoutExtension(_selectedBackground)}", FontFamily = PixelUi.PixelFont, FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 70, 54)), Margin = new Thickness(0, 0, 0, 5) };
        _previewName = new TextBlock { Text = $"Vista previa: {Path.GetFileNameWithoutExtension(_selectedBackground)}", FontFamily = PixelUi.PixelFont, FontSize = 17,
            Foreground = new SolidColorBrush(Color.FromRgb(49, 43, 35)) };
        previewInfo.Children.Add(_currentName); previewInfo.Children.Add(_previewName); Grid.SetColumn(previewInfo, 1); previewRow.Children.Add(previewInfo);
        Grid.SetRow(previewRow, 1); layout.Children.Add(previewRow);
        SetGalleryPreview(_selectedBackground);

        var wrap = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(4) };
        var directory = Path.Combine(AppContext.BaseDirectory, "assets", "pc", "backgrounds");
        foreach (var file in Directory.GetFiles(directory).Where(path => Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase)
                     || Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(path => Path.GetFileNameWithoutExtension(path) == "team" ? int.MaxValue : int.TryParse(Path.GetFileNameWithoutExtension(path), out var number) ? number : int.MaxValue))
        {
            var fileName = Path.GetFileName(file);
            var decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var image = new Image { Source = decoder.Frames[0], Width = 100, Height = 64, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(image);
            stack.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(fileName), FontFamily = PixelUi.PixelFont, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 46, 38)), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
            var card = new Grid { Width = 112, Height = 91, Background = Brushes.Transparent, IsHitTestVisible = true, Focusable = true, Cursor = Cursors.Hand };
            var frame = PixelButtonUi.NineSlice(PixelUi.Load(string.Equals(fileName, _selectedBackground, StringComparison.OrdinalIgnoreCase)
                ? "ui/buttons/palette/yellow_normal_041.png" : "ui/buttons/palette/green_normal_009.png"), 112, 91, 12, 12);
            card.Children.Add(frame); card.Children.Add(stack);
            var button = new Button { Content = card, Width = 112, Height = 91, Margin = new Thickness(3), Padding = new Thickness(0), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, ToolTip = $"Vista previa del fondo {Path.GetFileNameWithoutExtension(fileName)}", Template = GalleryTileTemplate() };
            // The nested pixel-art visuals can consume normal Button input on some WPF/WindowChrome
            // combinations. Listen to the routed mouse-up across the full tile as well as Click so
            // an actual pointer release always updates the preview, even when it lands on an Image.
            button.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                SelectBackground(fileName);
            }), handledEventsToo: true);
            button.Click += (_, _) => SelectBackground(fileName);
            wrap.Children.Add(button); _tiles.Add((fileName, frame, button));
        }
        var scroll = new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(231, 226, 205)), Margin = new Thickness(4, 0, 4, 4), CanContentScroll = true };
        Grid.SetRow(scroll, 2); layout.Children.Add(scroll);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(PixelButtonUi.Button("Cancelar", "blue", 142, 44, Cancel, fontSize: 14));
        footer.Children.Add(PixelButtonUi.Button("Aplicar", "green", 142, 44, Apply, fontSize: 14));
        Grid.SetRow(footer, 3); layout.Children.Add(footer);
        var root = new Grid(); root.Children.Add(PixelButtonUi.NineSlice(PixelUi.Load("ui/frames/decorative_1_large_001.png"), Width, Height, 32, 32)); root.Children.Add(layout);
        Content = root;
        SizeChanged += (_, _) =>
        {
            var newFrame = PixelButtonUi.NineSlice(PixelUi.Load("ui/frames/decorative_1_large_001.png"), ActualWidth, ActualHeight, 32, 32);
            root.Children.RemoveAt(0); root.Children.Insert(0, newFrame);
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(8), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
    }

    private static ControlTemplate GalleryTileTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void SelectBackground(string fileName)
    {
        _selectedBackground = fileName;
        SetGalleryPreview(fileName);
        foreach (var tile in _tiles)
        {
            var selected = tile.File.Equals(fileName, StringComparison.OrdinalIgnoreCase);
            PixelButtonUi.SetState(tile.Frame, PixelUi.Load(selected ? "ui/buttons/palette/yellow_normal_041.png" : "ui/buttons/palette/green_normal_009.png"));
            tile.Button.ToolTip = $"Vista previa del fondo {Path.GetFileNameWithoutExtension(tile.File)}" + (selected ? " (seleccionado)" : string.Empty);
        }
    }

    private void SetGalleryPreview(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "pc", "backgrounds", Path.GetFileName(fileName));
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "assets", "pc", "backgrounds", "1.gif");
        _previewImage.Source = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        _previewName.Text = $"Vista previa: {Path.GetFileNameWithoutExtension(fileName)}";
        RenderOptions.SetBitmapScalingMode(_previewImage, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(_previewImage, EdgeMode.Aliased);
    }

    private void Apply()
    {
        if (!_tiles.Any(tile => tile.File.Equals(_selectedBackground, StringComparison.OrdinalIgnoreCase))) return;
        _save.SetBoxBackground(_box.Id, _selectedBackground);
        if (_modal) DialogResult = true; else Close();
    }

    private void Cancel()
    {
        if (_modal) DialogResult = false; else Close();
    }
}

internal sealed class PixelConfirmWindow : Window
{
    private PixelConfirmWindow(Window owner, string title, string message, bool informational = false)
    {
        Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; Width = 500; Height = 244; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; UseLayoutRounding = true; SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        var root = new Grid();
        root.Children.Add(PixelButtonUi.NineSlice(PixelUi.Load("ui/frames/decorative_1_large_001.png"), Width, Height, 32, 32));
        var layout = new Grid { Margin = new Thickness(42, 28, 42, 26) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        layout.Children.Add(new TextBlock
        {
            Text = title, FontFamily = PixelUi.PixelFont, FontSize = 21,
            Foreground = new SolidColorBrush(Color.FromRgb(47, 44, 31)),
            TextAlignment = TextAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        });
        var body = new TextBlock
        {
            Text = message, FontFamily = PixelUi.PixelFont, FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(47, 44, 31)),
            TextAlignment = TextAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 3, 8, 3)
        };
        Grid.SetRow(body, 1); layout.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        if (informational) buttons.Children.Add(PixelButtonUi.Button("Cerrar", "blue", 150, 54, () => DialogResult = false, fontSize: 15));
        else
        {
            buttons.Children.Add(PixelButtonUi.Button("Cancelar", "blue", 156, 54, Close, fontSize: 15));
            buttons.Children.Add(new Border { Width = 14, IsHitTestVisible = false });
            buttons.Children.Add(PixelButtonUi.Button("Confirmar", "red", 156, 54, () => DialogResult = true, fontSize: 15));
        }
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons);
        root.Children.Add(layout); Content = root;
        RenderOptions.SetBitmapScalingMode(root, BitmapScalingMode.NearestNeighbor);
    }
    internal static bool Confirm(Window owner, string title, string message) => new PixelConfirmWindow(owner, title, message).ShowDialog() == true;
    internal static void Notify(Window owner, string title, string message) => new PixelConfirmWindow(owner, title, message, informational: true).ShowDialog();
#if DEBUG
    internal static Window CreatePreview(Window owner) => new PixelConfirmWindow(owner, "¿Borrar a Hooper del PC?", "Esta acción es permanente y no se puede deshacer. No recibirás objetos.");
#endif
}

internal static class PixelUi
{
    internal static FontFamily PixelFont => new(new Uri("pack://application:,,,/assets/fonts/"), "#Minecraft");
    internal static BitmapSource Load(string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", relative.Replace('/', Path.DirectorySeparatorChar));
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    internal static Button TextButton(string label, Action action, bool enabled, double width, double height, string color = "green")
        => PixelButtonUi.Button(label, color, width, height, action, enabled, fontSize: 11);
    internal static ControlTemplate BareTemplate()
    {
        var root = new FrameworkElementFactory(typeof(ContentPresenter)); root.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch); root.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        return new ControlTemplate(typeof(Button)) { VisualTree = root };
    }
}
