using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Panel = System.Windows.Controls.Panel;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace PokeGrass;

internal sealed class BagWindow : Window
{
    internal const string BerryFeedDragFormat = "PokéGrass.BerryFeed";
    private static readonly BagItem[] Items =
    [
        new("poke-ball", "Poké Ball", "items/balls/poke.png", BagTab.Balls, "Poké Ball para usar durante un encuentro."),
        new("super-ball", "Super Ball", "items/balls/great.png", BagTab.Balls, "Aumenta la probabilidad de captura durante un encuentro."),
        new("ultra-ball", "Ultra Ball", "items/balls/ultra.png", BagTab.Balls, "Aumenta mucho la probabilidad de captura durante un encuentro."),
        new("master-ball", "Master Ball", "items/balls/master.png", BagTab.Balls, "Captura garantizada durante un encuentro."),
        new("razz-berry", "Frambu", "items/berries/razz-gen7.png", BagTab.Berries, "Mejora la probabilidad de captura durante un encuentro."),
        new("silver-razz-berry", "Frambu Plateada", "items/berries/silver-razz.png", BagTab.Berries, "Mejora más la probabilidad de captura."),
        new("golden-razz-berry", "Frambu Dorada", "items/berries/golden-razz.png", BagTab.Berries, "Ofrece la mayor mejora de captura sin garantizarla."),
        new("nanab-berry", "Latano", "items/berries/nanab-gen7.png", BagTab.Berries, "Reduce la probabilidad de huida tras un fallo."),
        new("silver-nanab-berry", "Latano Plateada", "items/berries/silver-nanab.png", BagTab.Berries, "Reduce más la probabilidad de huida."),
        new("golden-nanab-berry", "Latano Dorada", "items/berries/golden-nanab.png", BagTab.Berries, "Ofrece la mayor protección contra la huida."),
        new("repel", "Repelente", "items/repels/repel.png", BagTab.Repels, "Evita nuevos encuentros durante 15 minutos."),
        new("super-repel", "Superrepelente", "items/repels/super-repel.png", BagTab.Repels, "Evita nuevos encuentros durante 30 minutos."),
        new("max-repel", "Repelente Máximo", "items/repels/max-repel.png", BagTab.Repels, "Evita nuevos encuentros durante 60 minutos."),
        new("incense-full", "Incienso Lento", "items/incense/full.png", BagTab.Incense, "Encuentros cada 2–4 min durante 45 min."),
        new("incense-lax", "Incienso Suave", "items/incense/lax.png", BagTab.Incense, "Encuentros cada 2–4 min; aviso de 30 s durante 30 min."),
        new("incense-luck", "Incienso Duplo", "items/incense/luck.png", BagTab.Incense, "Encuentros cada 1–2 min durante 10 min."),
        new("incense-odd", "Incienso Raro", "items/incense/odd.png", BagTab.Incense, "Favorece tipo Psíquico durante 25 min."),
        new("incense-pure", "Incienso Puro", "items/incense/pure.png", BagTab.Incense, "Shiny 1/256 durante 10 min."),
        new("incense-rock", "Incienso Roca", "items/incense/rock.png", BagTab.Incense, "Favorece tipo Roca durante 25 min."),
        new("incense-rose", "Incienso Floral", "items/incense/rose.png", BagTab.Incense, "Favorece tipo Planta durante 25 min."),
        new("incense-sea", "Incienso Marino", "items/incense/sea.png", BagTab.Incense, "Favorece tipo Agua durante 30 min."),
        new("incense-wave", "Incienso Aqua", "items/incense/wave.png", BagTab.Incense, "Favorece tipo Agua; encuentros cada 1–2 min durante 10 min.")
    ];

    private const double InitialWidth = 820;
    private const double InitialHeight = 540;
    private const double BaseCardWidth = 150;
    private const double BaseCardHeight = 90;
    private readonly GameSaveStore _save;
    private readonly TimedItemEffects _effects;
    private readonly Grid _itemsGrid = new();
    private ScrollViewer? _itemScroll;
    private readonly TextBlock _description = new();
    private readonly TextBlock _activeStatus = new();
    private readonly TextBlock _scrollHint = new();
    private readonly Image _detailIcon = new();
    private readonly TextBlock _detailName = new();
    private readonly TextBlock _detailCount = new();
    private readonly Button _useButton;
    private readonly Button[] _tabButtons = new Button[4];
    private Grid? _frameGrid;
    private Grid? _outerGrid;
    private BagTab _tab;
    private string? _selectedItem;

    internal BagWindow(GameSaveStore save, TimedItemEffects effects)
    {
        _save = save;
        _effects = effects;
        Title = "PokéGrass · Bolsa";
        Width = InitialWidth; Height = InitialHeight; MinWidth = 760; MinHeight = 500;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; AllowsTransparency = false;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = UiTestMode.Enabled;
        _useButton = PixelButtonUi.Button("Usar", "green", 124, 58, UseSelected, fontSize: 16);
        Content = Build();
        var boundsPath = Environment.GetEnvironmentVariable("POKEGRASS_BAG_WINDOW_SETTINGS_PATH") is { Length: > 0 } bagPath
            ? System.IO.Path.GetFullPath(bagPath)
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "bag-window.json");
        AdaptiveWindowSupport.Configure(this, boundsPath, 760, 500);
        SizeChanged += (_, _) =>
        {
            if (_outerGrid is null || _frameGrid is null) return;
            _outerGrid.Children.Remove(_frameGrid);
            _frameGrid = PixelButtonUi.NineSlice(PixelButtonUi.Load("ui/frames/decorative_1_large_001.png"), ActualWidth - 8, ActualHeight - 8, 20, 20);
            _outerGrid.Children.Insert(0, _frameGrid);
        };
        Loaded += (_, _) => Refresh();
        _effects.Changed += UpdateActiveStatus;
        Closed += (_, _) => _effects.Changed -= UpdateActiveStatus;
    }

    private UIElement Build()
    {
        var outer = _outerGrid = new Grid { Margin = new Thickness(4) };
        _frameGrid = PixelButtonUi.NineSlice(PixelButtonUi.Load("ui/frames/decorative_1_large_001.png"), InitialWidth - 8, InitialHeight - 8, 20, 20);
        outer.Children.Add(_frameGrid);
        var layout = new Grid { Margin = new Thickness(38, 38, 38, 30) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Background = PixelButtonUi.HitTestBackground };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        var title = new TextBlock
        {
            Text = "BOLSA", FontFamily = PixelUi.PixelFont, FontSize = 26,
            Foreground = Brush(Color.FromRgb(36, 51, 39)), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Grid.SetColumnSpan(title, 2);
        header.Children.Add(title);
        var close = PixelButtonUi.Button("×", "red", 44, 44, Close, fontSize: 20, toolTip: "Cerrar Bolsa");
        close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Center;
        close.Margin = new Thickness(0, 0, 4, 0); Grid.SetColumn(close, 1); header.Children.Add(close);
        Grid.SetRow(header, 0); layout.Children.Add(header);
        AdaptiveWindowSupport.DragFrom(this, header);

        const double tabWidth = 124;
        const double tabGap = 12;
        var tabs = new Grid { Width = tabWidth * 4 + tabGap * 3, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 4, 4) };
        for (var i = 0; i < 7; i++) tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i % 2 == 0 ? tabWidth : tabGap) });
        var names = new[] { "Poké Balls", "Bayas", "Repelentes", "Inciensos" };
        for (var i = 0; i < names.Length; i++)
        {
            var tab = (BagTab)i;
            var button = PixelButtonUi.Button(names[i], "blue", 124, 58, () => SelectTab(tab), fontSize: 13, toolTip: names[i]);
            // Las columnas ya reservan el ancho exacto del sprite; el margen horizontal
            // anterior sumaba 4 px y recortaba o superponía los bordes del botón.
            button.Margin = new Thickness(0, 2, 0, 2);
            _tabButtons[i] = button; Grid.SetColumn(button, i * 2); tabs.Children.Add(button);
        }
        Grid.SetRow(tabs, 1); layout.Children.Add(tabs);

        var body = new Grid { Margin = new Thickness(4, 5, 4, 2) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var inventoryPanel = new Border { Background = Brush(Color.FromRgb(237, 241, 211)),
            BorderBrush = Brush(Color.FromRgb(126, 139, 83)), BorderThickness = new Thickness(3),
            Padding = new Thickness(8), Margin = new Thickness(2, 2, 10, 2) };
        var scroll = new ScrollViewer
        {
            VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(2),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        _itemScroll = scroll;
        _itemsGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        _itemsGrid.VerticalAlignment = VerticalAlignment.Top;
        scroll.Content = _itemsGrid;
        inventoryPanel.Child = scroll;
        Grid.SetColumn(inventoryPanel, 0); body.Children.Add(inventoryPanel);
        _scrollHint.Text = "Rueda para ver más";
        _scrollHint.FontFamily = PixelUi.PixelFont; _scrollHint.FontSize = 12;
        _scrollHint.Foreground = Brush(Color.FromRgb(64, 76, 53));
        _scrollHint.HorizontalAlignment = HorizontalAlignment.Right; _scrollHint.VerticalAlignment = VerticalAlignment.Bottom;
        _scrollHint.Margin = new Thickness(0, 0, 18, 4); _scrollHint.Visibility = Visibility.Collapsed;
        body.Children.Add(_scrollHint);

        var details = new Grid { Margin = new Thickness(0, 2, 2, 2) };
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
        var side = new Border { Background = Brush(Color.FromRgb(250, 247, 226)),
            BorderBrush = Brush(Color.FromRgb(128, 113, 65)), BorderThickness = new Thickness(3),
            Padding = new Thickness(14, 8, 14, 8), Child = details };
        var sideTitle = new TextBlock { Text = "INFO. DEL OBJETO", FontFamily = PixelUi.PixelFont,
            FontSize = 16, Foreground = Brush(Color.FromRgb(49, 57, 37)), TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(sideTitle, 0); details.Children.Add(sideTitle);
        _detailIcon.Width = 66; _detailIcon.Height = 66; _detailIcon.Stretch = System.Windows.Media.Stretch.Uniform;
        _detailIcon.HorizontalAlignment = HorizontalAlignment.Center; _detailIcon.VerticalAlignment = VerticalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(_detailIcon, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(_detailIcon, EdgeMode.Aliased);
        Grid.SetRow(_detailIcon, 1); details.Children.Add(_detailIcon);
        var titleRow = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _detailName.FontFamily = PixelUi.PixelFont; _detailName.FontSize = 19; _detailName.Foreground = Brush(Color.FromRgb(34, 42, 31));
        _detailName.TextAlignment = TextAlignment.Center; _detailName.TextWrapping = TextWrapping.Wrap;
        _detailCount.FontFamily = PixelUi.PixelFont; _detailCount.FontSize = 15; _detailCount.Foreground = Brush(Color.FromRgb(57, 67, 47));
        _detailCount.TextAlignment = TextAlignment.Center;
        titleRow.Children.Add(_detailName); titleRow.Children.Add(_detailCount);
        Grid.SetRow(titleRow, 2); details.Children.Add(titleRow);
        var description = new StackPanel { Margin = new Thickness(3, 4, 3, 5) };
        _description.FontFamily = PixelUi.PixelFont; _description.FontSize = 15; _description.Foreground = Brush(Color.FromRgb(37, 43, 32));
        _description.TextWrapping = TextWrapping.Wrap; _description.VerticalAlignment = VerticalAlignment.Top;
        description.Children.Add(_description);
        _activeStatus.FontFamily = PixelUi.PixelFont; _activeStatus.FontSize = 14; _activeStatus.Foreground = Brush(Color.FromRgb(45, 57, 38));
        _activeStatus.TextWrapping = TextWrapping.Wrap; _activeStatus.Margin = new Thickness(0, 8, 0, 0);
        description.Children.Add(_activeStatus);
        Grid.SetRow(description, 3); details.Children.Add(description);
        _useButton.HorizontalAlignment = HorizontalAlignment.Center; _useButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_useButton, 4); details.Children.Add(_useButton);
        Grid.SetColumn(side, 1); body.Children.Add(side);
        Grid.SetRow(body, 2); layout.Children.Add(body);
        var designLayer = new Grid { Width = InitialWidth, Height = InitialHeight };
        designLayer.Children.Add(layout);
        var contentView = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = designLayer };
        RenderOptions.SetBitmapScalingMode(contentView, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(contentView, EdgeMode.Aliased);
        TextOptions.SetTextRenderingMode(contentView, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(contentView, TextFormattingMode.Display);
        outer.Children.Add(contentView);
        return outer;
    }

    private static SolidColorBrush Brush(Color color) => new(color);

    private void SelectTab(BagTab tab)
    {
        _tab = tab;
        _selectedItem = null;
        _itemScroll?.ScrollToTop();
        Refresh();
    }

    internal void Refresh()
    {
        if (!IsLoaded && Content is null) return;
        var names = new[] { "Poké Balls", "Bayas", "Repelentes", "Inciensos" };
        for (var i = 0; i < _tabButtons.Length; i++)
        {
            var old = _tabButtons[i]; var tabIndex = i;
            var replacement = PixelButtonUi.Button(names[i], i == (int)_tab ? "green" : "blue", 124, 58,
                () => SelectTab((BagTab)tabIndex), selected: i == (int)_tab, fontSize: 14, toolTip: names[i]);
            replacement.Margin = old.Margin; Grid.SetColumn(replacement, i * 2);
            if (old.Parent is Panel panel) { var at = panel.Children.IndexOf(old); panel.Children.Remove(old); panel.Children.Insert(at, replacement); }
            _tabButtons[i] = replacement;
        }

        _itemsGrid.Children.Clear(); _itemsGrid.RowDefinitions.Clear(); _itemsGrid.ColumnDefinitions.Clear();
        var available = Items.Where(item => item.Tab == _tab).ToArray();
        _scrollHint.Visibility = available.Length > 9 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < 3; i++) _itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BaseCardWidth + 8) });
        var rows = Math.Max(1, (int)Math.Ceiling(available.Length / 3d));
        for (var i = 0; i < rows; i++) _itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BaseCardHeight + 8) });
        if (available.Length == 0)
            _itemsGrid.Children.Add(new TextBlock { Text = "No hay objetos en esta categoría.", FontFamily = PixelUi.PixelFont, FontSize = 18,
                Foreground = Brush(Color.FromRgb(42, 51, 36)), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        for (var index = 0; index < available.Length; index++)
        {
            var item = available[index];
            var card = MakeItemCard(item, _save.Count(item.Id), _selectedItem == item.Id);
            Grid.SetRow(card, index / 3); Grid.SetColumn(card, index % 3); _itemsGrid.Children.Add(card);
        }
        _itemsGrid.Width = 3 * (BaseCardWidth + 8); _itemsGrid.Height = rows * (BaseCardHeight + 8);

        var selectedItem = Items.FirstOrDefault(item => item.Id == _selectedItem && item.Tab == _tab);
        _detailName.Text = selectedItem?.Name ?? "Selecciona un objeto";
        _detailCount.Text = selectedItem is null ? string.Empty : $"Cantidad disponible: ×{_save.Count(selectedItem.Id)}";
        _detailIcon.Source = selectedItem is null ? null : PixelButtonUi.Load(selectedItem.Icon);
        _detailIcon.Opacity = selectedItem is null ? 0 : _save.Count(selectedItem.Id) > 0 ? 1 : .42;
        _description.Text = selectedItem?.Description ?? "Elige un objeto de la lista para ver su descripción y efecto.";
        var canUse = selectedItem is not null && _save.Count(selectedItem.Id) > 0 && selectedItem.Tab is BagTab.Repels or BagTab.Incense;
        _useButton.IsEnabled = canUse; _useButton.IsHitTestVisible = canUse;
        _useButton.Cursor = canUse ? Cursors.Hand : Cursors.Arrow;
        if (_useButton.Content is Grid content && content.Children.Count > 0 && content.Children[0] is Grid bg)
            PixelButtonUi.SetState(bg, PixelButtonUi.Load(canUse ? "ui/buttons/palette/green_normal_009.png" : "ui/buttons/palette/green_disabled_012.png"));
        _useButton.ToolTip = canUse ? "Usar objeto seleccionado" : "Las Balls y bayas solo se usan durante encuentros.";
        UpdateActiveStatus();
    }

    private void UpdateActiveStatus()
    {
        if (_effects.HasRepel)
            _activeStatus.Text = $"Activo: Repelente\n{_effects.RepelSeconds / 60:D2}:{_effects.RepelSeconds % 60:D2} restantes";
        else if (_effects.HasIncense)
            _activeStatus.Text = $"Activo: {Items.FirstOrDefault(item => item.Id == _effects.IncenseId)?.Name ?? "Incienso"}\n{_effects.IncenseSeconds / 60:D2}:{_effects.IncenseSeconds % 60:D2} restantes";
        else _activeStatus.Text = "Efecto activo: ninguno";
    }

    private Button MakeItemCard(BagItem item, int count, bool selected)
    {
        var enabled = count > 0;
        var root = new Button { Width = BaseCardWidth, Height = BaseCardHeight, Margin = new Thickness(4), IsEnabled = true,
            Background = PixelButtonUi.HitTestBackground, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow, Focusable = false, Template = PixelButtonUi.BareButtonTemplate(), ToolTip = item.Description };
        ToolTipService.SetShowOnDisabled(root, true);
        var normal = PixelButtonUi.Load("ui/buttons/palette/blue_normal_028.png");
        var hover = PixelButtonUi.Load("ui/buttons/palette/blue_hover_029.png");
        var pressed = PixelButtonUi.Load("ui/buttons/palette/blue_pressed_030.png");
        var background = PixelButtonUi.NineSlice(!enabled ? PixelButtonUi.Load("ui/buttons/palette/blue_disabled_031.png") : selected ? pressed : normal, BaseCardWidth, BaseCardHeight, 16, 16);
        var content = new Grid { Margin = new Thickness(16, 10, 16, 10), IsHitTestVisible = false };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Image { Source = PixelButtonUi.Load(item.Icon), Width = 48, Height = 48, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased); icon.Opacity = enabled ? 1 : .42;
        Grid.SetColumn(icon, 0); content.Children.Add(icon);
        var label = new TextBlock { Text = $"{item.Name}\n×{count}", FontFamily = PixelUi.PixelFont, FontSize = 14,
            Foreground = Brush(enabled ? Color.FromRgb(24, 35, 55) : Color.FromRgb(103, 106, 111)), TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, LineHeight = 19 };
        Grid.SetColumn(label, 1); content.Children.Add(label);
        var stack = new Grid { Width = BaseCardWidth, Height = BaseCardHeight, Background = PixelButtonUi.HitTestBackground, IsHitTestVisible = true };
        stack.Children.Add(background); stack.Children.Add(content); root.Content = stack;
        root.MouseEnter += (_, _) => { if (enabled && !selected) PixelButtonUi.SetState(background, hover); };
        root.MouseLeave += (_, _) => PixelButtonUi.SetState(background, !enabled ? PixelButtonUi.Load("ui/buttons/palette/blue_disabled_031.png") : selected ? pressed : normal);
        root.PreviewMouseLeftButtonDown += (_, _) => { if (enabled) PixelButtonUi.SetState(background, pressed); };
        var dragStartX = 0d;
        var dragStartY = 0d;
        var dragCandidate = false;
        root.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragCandidate = item.Tab == BagTab.Berries && count > 0;
            if (dragCandidate)
            {
                var start = e.GetPosition(root);
                dragStartX = start.X;
                dragStartY = start.Y;
            }
        };
        root.PreviewMouseMove += (_, e) =>
        {
            if (!dragCandidate || item.Tab != BagTab.Berries || count <= 0 || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(root);
            if (Math.Abs(point.X - dragStartX) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(point.Y - dragStartY) < SystemParameters.MinimumVerticalDragDistance) return;
            dragCandidate = false;
            BeginBerryDrag(root, item);
        };
        root.PreviewMouseLeftButtonUp += (_, _) => dragCandidate = false;
        root.Click += (_, _) => { _selectedItem = item.Id; Refresh(); };
        return root;
    }

    private static void BeginBerryDrag(Button source, BagItem item)
    {
        var image = PixelButtonUi.Load(item.Icon);
        var preview = new DragPreviewWindow(image, 48, 48, scaleWithDesktopVisuals: true);
        preview.Show();
        System.Windows.GiveFeedbackEventHandler feedback = (_, args) =>
        {
            args.UseDefaultCursors = false;
            Mouse.OverrideCursor = Cursors.Arrow;
            args.Handled = true;
        };
        source.GiveFeedback += feedback;
        try
        {
            var data = new System.Windows.DataObject(BerryFeedDragFormat, item.Id);
            _ = DragDrop.DoDragDrop(source, data, System.Windows.DragDropEffects.Copy);
        }
        finally
        {
            source.GiveFeedback -= feedback;
            Mouse.OverrideCursor = null;
            preview.Close();
        }
    }

    private static bool HasTimedUseRules(BagItem item) => TimedItemEffects.IsRepel(item.Id) || TimedItemEffects.IsIncense(item.Id);
    internal void SelectTabDebug(int index) => SelectTab((BagTab)Math.Clamp(index, 0, 3));
    internal void SelectFirstAvailableDebug() { _selectedItem = Items.FirstOrDefault(item => item.Tab == _tab && _save.Count(item.Id) > 0)?.Id; Refresh(); }
    internal void SelectItemDebug(string id) { _selectedItem = Items.FirstOrDefault(item => item.Id == id)?.Id; Refresh(); }
    internal bool CanUseSelectedDebug => _useButton.IsEnabled;
    internal string SelectedDescriptionDebug => _description.Text;

    private void UseSelected()
    {
        if (_selectedItem is null || _save.Count(_selectedItem) <= 0) return;
        var selected = Items.FirstOrDefault(item => item.Id == _selectedItem);
        if (selected is null || !HasTimedUseRules(selected)) return;
        if (TimedItemEffects.IsRepel(selected.Id) && _effects.HasIncense
            && !PixelConfirmWindow.Confirm(this, "Cambiar efecto activo", "Activar este repelente cancelará el incienso activo. ¿Continuar?")) return;
        if (TimedItemEffects.IsIncense(selected.Id) && _effects.HasRepel
            && !PixelConfirmWindow.Confirm(this, "Cambiar efecto activo", "Activar este incienso cancelará el repelente activo. ¿Continuar?")) return;
        if (TimedItemEffects.IsIncense(selected.Id) && _effects.HasIncense
            && !string.Equals(selected.Id, _effects.IncenseId, StringComparison.OrdinalIgnoreCase)
            && !PixelConfirmWindow.Confirm(this, "Reemplazar incienso", "Activar este incienso reemplazará el actual. ¿Continuar?")) return;
        var activated = TimedItemEffects.IsRepel(selected.Id) ? _effects.ActivateRepel(selected.Id) : _effects.ActivateIncense(selected.Id);
        if (activated) { _selectedItem = null; Refresh(); }
    }

    private enum BagTab { Balls, Berries, Repels, Incense }
    private sealed record BagItem(string Id, string Name, string Icon, BagTab Tab, string Description);
}
