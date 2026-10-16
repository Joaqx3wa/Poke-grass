using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace PokeGrass;

internal sealed class RewardItemDefinition
{
    public required string Name { get; init; }
    public required string Icon { get; init; }
}

internal sealed class RewardDrop
{
    public required string Id { get; init; }
    public int Weight { get; init; } = 1;
    public double Chance { get; init; } = 1;
    public int Min { get; init; } = 1;
    public int Max { get; init; } = 1;
}

internal sealed class RewardBalance
{
    public int Version { get; init; }
    public int SupplyIntervalSeconds { get; init; } = 2400;
    public Dictionary<string, RewardItemDefinition> Items { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RewardDrop> SupplyBase { get; init; } = [];
    public int SupplyBonusRolls { get; init; } = 2;
    public List<RewardDrop> SupplyPool { get; init; } = [];
    public List<RewardDrop> DailyBase { get; init; } = [];
    public int DailyBonusRolls { get; init; } = 2;
    public List<RewardDrop> DailyPool { get; init; } = [];
    public Dictionary<string, List<RewardDrop>> NormalDuplicateRewards { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int ShinyDuplicateBonusRolls { get; init; } = 2;
    public int ShinyDuplicateMasterBallDenominator { get; init; } = 2048;
    public List<RewardDrop> ShinyDuplicatePool { get; init; } = [];

    public static RewardBalance Load(string path)
    {
        var result = JsonSerializer.Deserialize<RewardBalance>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("El archivo de balance de recompensas está vacío o no es válido.");
        if (result.Version != 1 || result.SupplyIntervalSeconds < 1 || result.ShinyDuplicateMasterBallDenominator < 2)
            throw new InvalidDataException("El balance de recompensas tiene una versión o intervalo no válido.");
        foreach (var (id, item) in result.Items)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Icon))
                throw new InvalidDataException("Cada objeto del balance debe tener ID, nombre e icono.");
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", item.Icon.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidDataException($"Falta el icono de recompensa {item.Icon}.");
        }
        foreach (var drop in result.AllDrops())
            if (!result.Items.ContainsKey(drop.Id) || drop.Min < 1 || drop.Max < drop.Min || drop.Weight < 0 || drop.Chance is < 0 or > 1)
                throw new InvalidDataException($"Recompensa no válida para el objeto '{drop.Id}'.");
        if (result.AllDrops().Any(drop => drop.Id.Equals("master-ball", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("La Master Ball solo puede otorgarse mediante la probabilidad especial de shiny duplicado.");
        foreach (var tier in new[] { "common", "uncommon", "rare", "epic", "legendary" })
            if (!result.NormalDuplicateRewards.TryGetValue(tier, out var drops) || drops.Count == 0)
                throw new InvalidDataException($"Falta el balance de duplicados de rareza {tier}.");
        foreach (var pool in new[] { result.SupplyPool, result.DailyPool, result.ShinyDuplicatePool })
            if (pool.Count == 0 || pool.All(drop => drop.Weight == 0)) throw new InvalidDataException("Una tabla ponderada está vacía.");
        return result;
    }

    internal IReadOnlyList<RewardItemGrant> MakeSupply(Random random) => BuildPackage(SupplyBase, SupplyPool, SupplyBonusRolls, random);
    internal IReadOnlyList<RewardItemGrant> MakeDailyGift(Random random) => BuildPackage(DailyBase, DailyPool, DailyBonusRolls, random);

    internal RewardItemGrant MakeCompanionGift(Random random)
    {
        // Reuse the common supply table: its weights favor everyday items and keep rare items scarce.
        var pool = SupplyPool.Where(drop => !drop.Id.Equals("master-ball", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (pool.Length == 0) throw new InvalidDataException("La tabla de regalos no contiene objetos permitidos.");
        var selected = Choose(pool, random);
        return new RewardItemGrant(selected.Id, random.Next(selected.Min, selected.Max + 1));
    }

    internal RewardItemGrant MakeBerryRequestGift(string requestedBerryId, Random random)
    {
        var golden = requestedBerryId.Equals("golden-razz-berry", StringComparison.OrdinalIgnoreCase)
            || requestedBerryId.Equals("golden-nanab-berry", StringComparison.OrdinalIgnoreCase);
        var silver = requestedBerryId.Equals("silver-razz-berry", StringComparison.OrdinalIgnoreCase)
            || requestedBerryId.Equals("silver-nanab-berry", StringComparison.OrdinalIgnoreCase);

        if (golden && random.Next(1000) == 0)
            return new RewardItemGrant("master-ball", 1);

        var weightedItems = golden
            ? new (string Id, int Weight)[]
            {
                ("ultra-ball", 18), ("golden-razz-berry", 14), ("golden-nanab-berry", 12),
                ("repel", 7), ("super-repel", 8), ("max-repel", 6),
                ("incense-full", 5), ("incense-lax", 5), ("incense-luck", 3), ("incense-odd", 4),
                ("incense-pure", 2), ("incense-rock", 4), ("incense-rose", 4), ("incense-sea", 4),
                ("incense-wave", 4)
            }
            : silver
                ? new (string Id, int Weight)[]
                {
                    ("poke-ball", 10), ("super-ball", 20), ("ultra-ball", 8),
                    ("silver-razz-berry", 14), ("silver-nanab-berry", 14),
                    ("repel", 12), ("super-repel", 7), ("max-repel", 2)
                }
                : new (string Id, int Weight)[]
                {
                    ("poke-ball", 42), ("razz-berry", 30), ("nanab-berry", 28)
                };

        var totalWeight = weightedItems.Sum(item => item.Weight);
        var roll = random.Next(totalWeight);
        foreach (var item in weightedItems)
        {
            roll -= item.Weight;
            if (roll < 0) return new RewardItemGrant(item.Id, 1);
        }
        return new RewardItemGrant(weightedItems[^1].Id, 1);
    }

    internal IReadOnlyList<RewardItemGrant> MakeCaptureRewards(bool duplicate, bool shiny, bool shinyDuplicate, string rarity, Random random)
    {
        if (duplicate && !shiny)
            return RollDrops((IEnumerable<RewardDrop>)(NormalDuplicateRewards.TryGetValue(rarity, out var drops) ? drops : NormalDuplicateRewards["common"]), random);
        if (shinyDuplicate)
        {
            var result = RollDrops(ShinyDuplicatePool, random, ShinyDuplicateBonusRolls).ToList();
            if (random.Next(ShinyDuplicateMasterBallDenominator) == 0) Add(result, "master-ball", 1);
            return result;
        }
        return [];
    }

    private IEnumerable<RewardDrop> AllDrops() => SupplyBase.Concat(SupplyPool).Concat(DailyBase).Concat(DailyPool)
        .Concat(NormalDuplicateRewards.Values.SelectMany(items => items)).Concat(ShinyDuplicatePool);

    private static IReadOnlyList<RewardItemGrant> BuildPackage(IEnumerable<RewardDrop> baseDrops, IReadOnlyList<RewardDrop> pool, int rolls, Random random)
    {
        var result = RollDrops(baseDrops, random).ToList();
        result.AddRange(RollDrops(pool, random, rolls));
        return Coalesce(result);
    }

    private static IReadOnlyList<RewardItemGrant> RollDrops(IEnumerable<RewardDrop> drops, Random random, int rolls = 1)
    {
        var result = new List<RewardItemGrant>();
        for (var i = 0; i < rolls; i++)
            foreach (var drop in drops)
                if (random.NextDouble() < drop.Chance) Add(result, drop.Id, random.Next(drop.Min, drop.Max + 1));
        return Coalesce(result);
    }

    private static RewardDrop Choose(IReadOnlyList<RewardDrop> pool, Random random)
    {
        var total = pool.Sum(item => item.Weight);
        var roll = random.Next(1, total + 1);
        foreach (var item in pool)
        {
            roll -= item.Weight;
            if (roll <= 0) return item;
        }
        return pool[^1];
    }

    private static void Add(List<RewardItemGrant> target, string id, int quantity)
    {
        var existing = target.FindIndex(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing < 0) target.Add(new RewardItemGrant(id, quantity));
        else target[existing] = target[existing] with { Quantity = target[existing].Quantity + quantity };
    }

    private static IReadOnlyList<RewardItemGrant> RollDrops(IReadOnlyList<RewardDrop> pool, Random random, int rolls)
    {
        var result = new List<RewardItemGrant>();
        for (var i = 0; i < rolls; i++)
        {
            var drop = Choose(pool, random);
            if (random.NextDouble() < drop.Chance) Add(result, drop.Id, random.Next(drop.Min, drop.Max + 1));
        }
        return Coalesce(result);
    }

    private static IReadOnlyList<RewardItemGrant> Coalesce(IEnumerable<RewardItemGrant> items) => items
        .Where(item => item.Quantity > 0).GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
        .Select(group => new RewardItemGrant(group.Key, group.Sum(item => item.Quantity))).ToArray();
}

internal sealed record RewardItemGrant(string Id, int Quantity);

internal sealed class RewardProgressSave
{
    public int SupplySecondsRemaining { get; set; } = 2400;
    public string? LastDailyLocalDate { get; set; }
    public List<RewardNotificationSave> PendingNotifications { get; set; } = [];
}

internal sealed class RewardNotificationSave
{
    public string EventId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<RewardItemGrant> Items { get; set; } = [];
}

internal sealed class AppliedRewardEventSave
{
    public string EventId { get; set; } = string.Empty;
    public bool IsDuplicate { get; set; }
    public bool IsShinyDuplicate { get; set; }
    public List<RewardItemGrant> Items { get; set; } = [];
}

internal sealed record CaptureRecordResult(bool Duplicate, bool ShinyDuplicate, IReadOnlyList<RewardItemGrant> Rewards);

internal sealed class RewardService : IDisposable
{
    private readonly GameSaveStore _save;
    private readonly RewardBalance _balance;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly Random _random = new();
    private int _supplySecondsRemaining;
    private int _persistTicks;
    private readonly Func<DateOnly> _today;

    internal event Action? PendingRewardsChanged;
    internal RewardBalance Balance => _balance;

    internal RewardService(GameSaveStore save, RewardBalance balance, bool startTimer = true, Func<DateOnly>? dateProvider = null, Random? random = null)
    {
        _save = save;
        _balance = balance;
        _today = dateProvider ?? (() => DateOnly.FromDateTime(DateTime.Now));
        if (random is not null) _random = random;
        var progress = save.GetRewardProgress();
        _supplySecondsRemaining = Math.Clamp(progress.SupplySecondsRemaining, 1, balance.SupplyIntervalSeconds);
        _timer.Tick += Tick;
        if (startTimer) _timer.Start();
        CheckDailyGift(_today());
    }

    internal void SaveOnShutdown() => _save.SaveRewardProgress(_supplySecondsRemaining);
    internal void StopForDataReload() => _timer.Stop();

    internal bool TryGetPending(out RewardNotificationSave? notification)
    {
        return _save.TryGetRewardNotification(out notification);
    }

    internal void CompletePending(string eventId) => _save.CompleteRewardNotification(eventId);

    internal void TickForTest() => Tick(this, EventArgs.Empty);

    private void Tick(object? sender, EventArgs e)
    {
        CheckDailyGift(_today());
        if (--_supplySecondsRemaining <= 0)
        {
            _supplySecondsRemaining = _balance.SupplyIntervalSeconds;
            var eventId = $"supply:{Guid.NewGuid():N}";
            _save.TryGrantScheduledReward(eventId, "Suministros periódicos", _balance.MakeSupply(_random), _supplySecondsRemaining, null);
            PendingRewardsChanged?.Invoke();
        }
        if (++_persistTicks >= 10) { _persistTicks = 0; _save.SaveRewardProgress(_supplySecondsRemaining); }
    }

    private void CheckDailyGift(DateOnly today)
    {
        var date = today.ToString("yyyy-MM-dd");
        var last = _save.GetRewardProgress().LastDailyLocalDate;
        if (string.Equals(last, date, StringComparison.Ordinal)) return;
        _save.TryGrantScheduledReward($"daily:{date}", "Regalo diario", _balance.MakeDailyGift(_random), _supplySecondsRemaining, date);
        PendingRewardsChanged?.Invoke();
    }

    public void Dispose() { _timer.Stop(); SaveOnShutdown(); }
}

internal static class RewardPanelVisual
{
    internal const int DesignWidth = 480;
    // Mantener el encabezado por debajo del bisel superior del marco amarillo.
    // Las filas tienen altura suficiente para iconos nítidos y nombres legibles.
    private const int RowsTopMargin = 45;
    private const int HeaderHeight = 34;
    private const int RowHeight = 80;
    private const int BottomMargin = 30;

    internal static int HeightFor(int rewardCount, int width = DesignWidth, int headerHeight = HeaderHeight,
        int rowsTopMargin = RowsTopMargin, int rowHeight = RowHeight, int bottomMargin = BottomMargin, int rowGap = 4) =>
        rowsTopMargin + headerHeight + rowGap + Math.Max(1, (int)Math.Ceiling(rewardCount / 2d)) * rowHeight + bottomMargin;

    internal static System.Windows.Controls.Grid Create(IReadOnlyList<RewardItemGrant> rewards, RewardBalance balance, string title,
        int width = DesignWidth, Action? closeAction = null)
    {
        var validRewards = rewards.Where(item => item.Quantity > 0 && balance.Items.ContainsKey(item.Id)).ToArray();
        if (validRewards.Length == 0) throw new ArgumentException("El panel requiere al menos una recompensa válida.", nameof(rewards));
        var isToast = closeAction is not null;
        var rowsTopMargin = isToast ? 24 : RowsTopMargin;
        var rowHeight = isToast ? 70 : RowHeight;
        var bottomMargin = isToast ? 6 : BottomMargin;
        var rowGap = isToast ? 2 : 4;
        var headerHeight = isToast && title.Length > 18 ? 48 : HeaderHeight;
        var height = HeightFor(validRewards.Length, width, headerHeight, rowsTopMargin, rowHeight, bottomMargin, rowGap);
        var root = new System.Windows.Controls.Grid { Width = width, Height = height };
        // Algunos marcos conservan alfa en el centro; cubrirlo evita que el escritorio
        // se transparente a través del panel de recompensas.
        root.Children.Add(new System.Windows.Controls.Border
        {
            Margin = new System.Windows.Thickness(width < 400 ? 16 : 24),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 247, 236)),
            IsHitTestVisible = false
        });
        var frame = PixelButtonUi.NineSlice(PixelButtonUi.Load("ui/frames/decorative_2_small_058.png"), width, height, 18, 18);
        root.Children.Add(frame);

        var sideMargin = width < 400 ? 16 : 28;
        var content = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(sideMargin, rowsTopMargin, sideMargin, bottomMargin) };
        content.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(headerHeight) });
        content.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(rowGap) });
        var itemRows = Math.Max(1, (int)Math.Ceiling(validRewards.Length / 2d));
        for (var row = 0; row < itemRows; row++)
            content.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(rowHeight) });
        if (closeAction is null)
        {
            var heading = MakeText(title.ToUpperInvariant(), 20, bold: true);
            System.Windows.Controls.Grid.SetRow(heading, 0);
            content.Children.Add(heading);
        }
        else
        {
            var header = new System.Windows.Controls.Grid();
            header.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            header.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(38) });
            var headingFontSize = title.Length > 18 ? 14 : 18;
            var heading = MakeText(title.ToUpperInvariant(), headingFontSize, bold: true);
            heading.TextWrapping = System.Windows.TextWrapping.Wrap;
            System.Windows.Controls.Grid.SetColumn(heading, 0);
            header.Children.Add(heading);
            var close = PixelButtonUi.Button("×", "red", 30, 30, closeAction, fontSize: 18, toolTip: "Cerrar notificación");
            System.Windows.Controls.Grid.SetColumn(close, 1);
            header.Children.Add(close);
            System.Windows.Controls.Grid.SetRow(header, 0);
            content.Children.Add(header);
        }

        var cards = new System.Windows.Controls.Grid();
        var columnCount = validRewards.Length == 1 ? 1 : 2;
        for (var column = 0; column < columnCount; column++)
            cards.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        for (var row = 0; row < itemRows; row++)
            cards.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(rowHeight) });
        for (var index = 0; index < validRewards.Length; index++)
        {
            var reward = validRewards[index];
            var definition = balance.Items[reward.Id];
            var card = new System.Windows.Controls.Grid
            {
            Margin = new System.Windows.Thickness(isToast ? 1 : 2, 1, isToast ? 1 : 2, 1),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            if (columnCount == 1)
            {
                card.Width = Math.Min(width - sideMargin * 2, 320);
                card.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            }
            var iconSize = isToast ? 64 : 76;
            var iconColumnWidth = isToast ? 70 : 82;
            card.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(iconColumnWidth) });
            card.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            var icon = new System.Windows.Controls.Image
            {
                Source = PixelButtonUi.Load(definition.Icon), Width = iconSize, Height = iconSize,
                Stretch = System.Windows.Media.Stretch.Uniform, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, SnapsToDevicePixels = true, IsHitTestVisible = false
            };
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(icon, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
            System.Windows.Media.RenderOptions.SetEdgeMode(icon, System.Windows.Media.EdgeMode.Aliased);
            card.Children.Add(icon);
            var longestWord = definition.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).DefaultIfEmpty(definition.Name).Max(word => word.Length);
            var itemFontSize = width < 400 ? longestWord > 8 || definition.Name.Length + reward.Quantity.ToString().Length > 11 ? 14 : 16 : 16;
            var text = MakeText(width < 400 ? $"{definition.Name} ×{reward.Quantity}" : $"{definition.Name}\n×{reward.Quantity}", itemFontSize);
            text.TextWrapping = System.Windows.TextWrapping.Wrap;
            text.TextTrimming = System.Windows.TextTrimming.None;
            text.Margin = new System.Windows.Thickness(0, 0, 1, 0);
            text.TextAlignment = System.Windows.TextAlignment.Left;
            text.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            System.Windows.Controls.Grid.SetColumn(text, 1);
            card.Children.Add(text);
            var cardRow = index / 2;
            var cardColumn = columnCount == 1 ? 0 : index % 2;
            System.Windows.Controls.Grid.SetRow(card, cardRow);
            System.Windows.Controls.Grid.SetColumn(card, cardColumn);
            cards.Children.Add(card);
        }
        System.Windows.Controls.Grid.SetRow(cards, 2);
        System.Windows.Controls.Grid.SetRowSpan(cards, itemRows);
        content.Children.Add(cards);
        root.Children.Add(content);
        return root;
    }

    private static System.Windows.Controls.TextBlock MakeText(string text, double size, bool bold = false)
    {
        var result = new System.Windows.Controls.TextBlock
        {
        Text = text, FontFamily = PixelUi.PixelFont, FontSize = size, FontWeight = bold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 42, 32)),
        TextAlignment = System.Windows.TextAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        SnapsToDevicePixels = true, IsHitTestVisible = false
        };
        System.Windows.Media.TextOptions.SetTextFormattingMode(result, System.Windows.Media.TextFormattingMode.Display);
        System.Windows.Media.TextOptions.SetTextRenderingMode(result, System.Windows.Media.TextRenderingMode.Aliased);
        return result;
    }
}

internal sealed class RewardToastWindow : System.Windows.Window
{
    // Aproximadamente cuatro módulos de césped a escala 3×.
    private const int DesignWidth = 300;
    private readonly DispatcherTimer _timer;
    private readonly int _screenLeft;
    private readonly int _screenTop;
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    internal RewardToastWindow(RewardNotificationSave notification, RewardBalance balance, double uiScale,
        System.Drawing.Rectangle gardenBounds, System.Drawing.Rectangle actionBounds, System.Drawing.Rectangle workArea,
        double dpiX, double dpiY)
    {
        var panel = RewardPanelVisual.Create(notification.Items, balance, notification.Title, DesignWidth, Close);
        var designHeight = (int)panel.Height;
        var fit = Math.Min(workArea.Width / (DesignWidth * dpiX), workArea.Height / (designHeight * dpiY));
        var scale = Math.Clamp(Math.Min(uiScale, fit), 0.25, uiScale);
        var width = (int)Math.Ceiling(DesignWidth * scale * dpiX);
        var height = (int)Math.Ceiling(designHeight * scale * dpiY);
        var gap = Math.Max(8, (int)Math.Round(12 * dpiX));
        var candidates = new[]
        {
            new System.Drawing.Point(gardenBounds.Right + gap, gardenBounds.Top),
            new System.Drawing.Point(gardenBounds.Left - width - gap, gardenBounds.Top),
            new System.Drawing.Point(gardenBounds.Left, gardenBounds.Bottom + gap),
            new System.Drawing.Point(gardenBounds.Left, gardenBounds.Top - height - gap)
        };
        var placed = candidates.Select(point => new System.Drawing.Point(
                Math.Clamp(point.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - width)),
                Math.Clamp(point.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height))))
            .OrderBy(point =>
            {
                var bounds = new System.Drawing.Rectangle(point.X, point.Y, width, height);
                var gardenOverlap = bounds.IntersectsWith(gardenBounds) ? 100000 : 0;
                var actionOverlap = !actionBounds.IsEmpty && bounds.IntersectsWith(actionBounds) ? 50000 : 0;
                return gardenOverlap + actionOverlap + Math.Abs(point.X - gardenBounds.Left) + Math.Abs(point.Y - gardenBounds.Top);
            }).First();
        _screenLeft = placed.X; _screenTop = placed.Y; _screenWidth = width; _screenHeight = height;

        Width = DesignWidth * scale;
        Height = designHeight * scale;
        WindowStyle = System.Windows.WindowStyle.None;
        ResizeMode = System.Windows.ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        panel.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        Content = panel;
        var positionPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "reward-notice-position.json");
        AdaptiveWindowSupport.ConfigureMovable(this, positionPath);
        AdaptiveWindowSupport.DragFrom(this, panel);
        SourceInitialized += (_, _) => PositionPhysical();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(6 + Math.Max(0, designHeight / 48 - 1) * 2) };
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void PositionPhysical()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, IntPtr.Zero, _screenLeft, _screenTop, _screenWidth, _screenHeight, 0x0010 | 0x0004);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
