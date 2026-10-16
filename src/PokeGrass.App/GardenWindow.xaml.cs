using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;

namespace PokeGrass;

public partial class GardenWindow : Window
{
    private const double CaptureBallLogicalSize = 24;
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WmDpiChanged = 0x02E0;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MinimumVisiblePixels = 24;
    internal const int EffectBleedX = 24;
    internal const int EffectBleedY = 24;
    private const int AppearanceDurationFrames = 5;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(100);

    private readonly BitmapSource _gardenBitmap;
    private readonly IReadOnlyList<GrassRow> _grassRows;
    private readonly IReadOnlyList<BitmapSource> _pokemonShadows;
    private readonly PokemonCatalog _catalog;
    private readonly RarityConfiguration _rarity;
    private readonly PokemonSpeciesMetadataCatalog? _speciesMetadata;
    private readonly GameSaveStore? _gameSave;
    private readonly TimedItemEffects? _timedEffects;
    private readonly RewardBalance? _rewardBalance;
    private readonly BitmapSource? _timedEffectIndicatorFrame;
    private readonly Dictionary<string, BitmapSource> _timedEffectIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly EncounterAudio _audio = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _encounterTimer;
    private readonly DispatcherTimer _shinySparkTimer;
    private readonly DispatcherTimer _alphaEffectTimer;
    private readonly List<BitmapSource> _grassFrames;
    private readonly List<BitmapSource> _exclamationFrames;
    private readonly List<BitmapSource> _leafFrames;
    private readonly IReadOnlyList<BitmapSource> _shinySparkFrames;
    private readonly IReadOnlyList<BitmapSource> _alphaEffectFrames;
    private readonly Dictionary<(string Id, bool Shiny), BitmapSource> _pokemonSpriteCache = new();
    private readonly Dictionary<(string Id, bool Shiny), SpriteSupport> _pokemonSupportCache = new();
    private readonly Dictionary<(string Id, bool Shiny), int> _pokemonShadowChoiceCache = new();
    private readonly List<GrassZone> _grassZones = [];

    private int _positionX;
    private int _positionY;
    private HwndSource? _windowSource;
    private NativePoint _dragStartCursor;
    private System.Drawing.Point _dragStartWindow;
    private int _scale;
    private bool _isLocked;
    private bool _isDragging;
    private bool _wasVisible;
    private DateTime _nextEncounterAt;
    private TimeSpan _remainingInterval;
    private int _alertFramesLeft;
    private int _animationFrame;
    private int _burstFramesLeft;
    private EncounterState _state;
    private GrassZone _activeZone;
    private PokemonEntry? _activePokemon;
    private string _activeRarity = "common";
    private bool _activeShiny;
    private bool _activeAlpha;
    private bool _playEncounterAudio;
    private bool _shinySparkPlayed;
    private int _shinySparkFrame = -1;
    private int _alphaEffectFrame = -1;
    private bool _alphaEffectPlayed;
    private BitmapSource? _starsSprite;
    private BitmapSource? _captureBallBitmap;
    private CaptureVisualStage _captureStage;
    private Point _captureFlightStart;
    private Point _captureFlightTarget;
    private Point _captureBallPosition;
    private CaptureBall? _captureBall;
    private int _captureFrame;
    private bool _capturePanelIssued;
    private bool _capturePokemonHidden;
    private string? _diagnosticPokemonId;
    private bool? _diagnosticShiny;

    public int Scale => _scale;
    internal bool HasActiveEncounter => _state != EncounterState.Idle;
    internal event Action<int>? ScaleChanged;
    public bool IsLocked => _isLocked;
    internal event Action<bool>? LockedChanged;
    public event Action<bool>? EncounterStateChanged;
    public event Action<string>? EncounterNotice;
    public event Action<PokemonEntry, bool, bool>? CaptureReady;
    public event Action? CollectionChanged;
    public event Action? EncounterEnded;
    internal event Action<CapturePanelWindow.CaptureItemDrag>? ItemDroppedOnPokemon;

    internal GardenWindow(GardenSettings settings, PokemonCatalog catalog, RarityConfiguration rarity,
        PokemonSpeciesMetadataCatalog? speciesMetadata = null, TimedItemEffects? timedEffects = null,
        RewardBalance? rewardBalance = null, GameSaveStore? gameSave = null)
    {
        InitializeComponent();
        _catalog = catalog;
        _rarity = rarity;
        _speciesMetadata = speciesMetadata;
        _gameSave = gameSave;
        _timedEffects = timedEffects;
        _rewardBalance = rewardBalance;
        _timedEffectIndicatorFrame = TryLoadBitmap("ui/frames/decorative_2_small_058.png");
        if (_timedEffects is not null)
        {
            _timedEffects.RulesChanged += OnTimedEffectsChanged;
            _timedEffects.Changed += OnTimedEffectDisplayChanged;
        }
        _positionX = settings.X;
        _positionY = settings.Y;
        _scale = GardenSettings.NormalizeScale(settings.Scale);
        _isLocked = settings.IsLocked;

        _gardenBitmap = LoadBitmap("Garden/Jardín.png");
        _grassRows = LoadGardenGrassRows();
        _pokemonShadows = LoadPokemonShadows();
        if (_gardenBitmap.PixelWidth != 128 || _gardenBitmap.PixelHeight != 80)
            throw new InvalidDataException("Garden/Jardín.png debe conservar la cuadrícula nativa de 128x80.");
        foreach (var row in _grassRows)
        {
            foreach (var x in new[] { 40, 56, 72, 88 })
                _grassZones.Add(new GrassZone(new Point(x, (row.Top + row.Bottom) / 2d), row.Index, row.Bottom + 1));
        }
        _grassFrames = LoadSheetFrames("ui/effects/grass_rustle.png", 10, 1, 160, trim: false);
        _exclamationFrames = LoadSheetFrames("ui/effects/exclamation.png", 9, 1, 160, trim: false);
        _leafFrames = LoadSheetFrames("ui/effects/leaves.png", 8, 7, 128, trim: true);
        _starsSprite = TryLoadBitmap("ui/effects/stars.png");
        _shinySparkFrames = ShinySparkFrames.Load(LoadBitmap);
        _alphaEffectFrames = AlphaEffectFrames.Load(Path.Combine(AppContext.BaseDirectory,
            AlphaEffectFrames.EncounterAssetPath.Replace('/', Path.DirectorySeparatorChar)), AlphaEffectFrames.EncounterCount,
            findTransparentSeams: true);

        GardenImage.Source = _gardenBitmap;
        RenderOptions.SetBitmapScalingMode(GardenImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(GardenImage, EdgeMode.Aliased);
        SceneCanvas.MouseLeftButtonDown += OnSceneMouseLeftButtonDown;
        SceneCanvas.MouseMove += OnSceneMouseMove;
        SceneCanvas.MouseLeftButtonUp += OnSceneMouseLeftButtonUp;
        SceneCanvas.LostMouseCapture += (_, _) => EndDrag();
        SceneCanvas.AllowDrop = true;
        SceneCanvas.DragOver += OnSceneDragOver;
        SceneCanvas.Drop += OnSceneDrop;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSettings(); };
        _encounterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameInterval };
        _encounterTimer.Tick += OnEncounterTick;
        _shinySparkTimer = new DispatcherTimer(DispatcherPriority.Render)
            { Interval = TimeSpan.FromMilliseconds(ShinySparkFrames.FrameMilliseconds) };
        _shinySparkTimer.Tick += OnShinySparkTick;
        _alphaEffectTimer = new DispatcherTimer(DispatcherPriority.Render)
            { Interval = TimeSpan.FromMilliseconds(AlphaEffectFrames.FrameMilliseconds) };
        _alphaEffectTimer.Tick += OnAlphaEffectTick;

        SourceInitialized += OnSourceInitialized;
        LocationChanged += (_, _) => ScheduleSave();
        Loaded += (_, _) => StartRandomInterval();
        IsVisibleChanged += OnVisibilityChanged;
        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            _encounterTimer.Stop();
            _shinySparkTimer.Stop();
            _alphaEffectTimer.Stop();
            if (_timedEffects is not null)
            {
                _timedEffects.RulesChanged -= OnTimedEffectsChanged;
                _timedEffects.Changed -= OnTimedEffectDisplayChanged;
            }
            _audio.Dispose();
            _windowSource?.RemoveHook(WindowHook);
            SaveSettings();
        };
    }

    public void SetScale(int scale)
    {
        var currentPosition = GetCurrentPosition();
        _positionX = currentPosition.X;
        _positionY = currentPosition.Y;
        _scale = GardenSettings.NormalizeScale(scale);
        UpdatePhysicalPixelScale();
        Dispatcher.BeginInvoke(() =>
        {
            ApplyPhysicalWindowSize();
            KeepGardenOnScreen();
            RenderScene();
            ScaleChanged?.Invoke(_scale);
            SaveSettings();
        }, DispatcherPriority.Loaded);
    }

    private void OnTimedEffectsChanged()
    {
        if (_state == EncounterState.Idle) StartRandomInterval();
    }

    private void OnTimedEffectDisplayChanged() => RenderScene();

    public void SetLocked(bool isLocked)
    {
        _isLocked = isLocked;
        SaveSettings();
        LockedChanged?.Invoke(_isLocked);
    }

    public void SaveSettings()
    {
        var position = GetCurrentPosition();
        _positionX = position.X;
        _positionY = position.Y;
        GardenSettings.Save(new GardenSettings(_positionX, _positionY, _scale, _isLocked));
    }

    public void TriggerShake(int? zoneIndex = null)
    {
        if (_state is EncounterState.Appearing or EncounterState.WaitingForInteraction)
            return;
        BeginAlert(zoneIndex);
    }

    internal void SetDiagnosticEncounter(string? pokemonId, bool? shiny)
    {
        if (string.IsNullOrWhiteSpace(pokemonId) && shiny is null) return;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POKEGRASS_SAVE_PATH")))
            throw new InvalidOperationException("El encuentro diagnóstico requiere un archivo de guardado aislado.");
        if (pokemonId is not null && !_catalog.ById.ContainsKey(pokemonId))
            throw new ArgumentException($"La forma '{pokemonId}' no está en el catálogo.", nameof(pokemonId));
        _diagnosticPokemonId = pokemonId;
        _diagnosticShiny = shiny;
        EncounterDiagnostics.Write("diagnostic", $"Forced encounter selection enabled; pokemon={pokemonId ?? "random"}; shiny={shiny?.ToString() ?? "random"}");
    }

    public void ForceEncounter(bool shiny, string? pokemonId = null, bool playAudio = true, int? zoneIndex = null, bool alpha = false)
    {
        if (_state is EncounterState.Appearing or EncounterState.WaitingForInteraction)
            return;
        _activeZone = zoneIndex.HasValue && zoneIndex.Value >= 0 && zoneIndex.Value < _grassZones.Count
            ? _grassZones[zoneIndex.Value]
            : _grassZones[Random.Shared.Next(_grassZones.Count)];
        var pokemon = pokemonId is not null && _catalog.ById.TryGetValue(pokemonId, out var selected)
            ? selected
            : _catalog.Entries[Random.Shared.Next(_catalog.Entries.Count)];
        BeginPokemonEncounter(pokemon, shiny, "development", playAudio, alpha);
    }

#if DEBUG
    private string? _debugCaptureSequenceDirectory;

    internal void SetDebugCaptureSequenceDirectory(string path) => _debugCaptureSequenceDirectory = path;
    internal int ShinySparkFrameDebug => _shinySparkFrame;
    internal int AlphaEffectFrameDebug => _alphaEffectFrame;

    internal void SetDebugShinySparkFrame(int frame)
    {
        if (_activePokemon is null || !_activeShiny)
            throw new InvalidOperationException("Solo puede forzarse una partícula shiny durante un encuentro shiny.");
        _shinySparkTimer.Stop();
        _shinySparkPlayed = true;
        _shinySparkFrame = frame < 0 || frame >= _shinySparkFrames.Count ? -1 : frame;
        RenderScene();
    }

    internal Drawing.Rectangle GetRenderBounds()
    {
        if (_windowSource is not null && GetWindowRect(_windowSource.Handle, out var bounds))
            return new Drawing.Rectangle(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        return Drawing.Rectangle.Empty;
    }

    internal void SetDebugPosition(int x, int y)
    {
        _positionX = x;
        _positionY = y;
        if (_windowSource is not null)
            SetWindowPos(_windowSource.Handle, IntPtr.Zero, x - EffectBleedX * _scale, y - EffectBleedY * _scale,
                0, 0, SwpNoActivate | SwpNoSize | SwpNoZOrder);
        RenderScene();
    }

    internal void SaveSceneCapture(string path)
    {
        SceneCanvas.UpdateLayout();
        RenderScene();
        SceneCanvas.UpdateLayout();
        var (dpiX, dpiY) = GetWindowDpiScale();
        var width = Math.Max(1, (int)Math.Ceiling(SceneCanvas.ActualWidth * dpiX));
        var height = Math.Max(1, (int)Math.Ceiling(SceneCanvas.ActualHeight * dpiY));
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        bitmap.Render(SceneCanvas);
        bitmap.Freeze();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var pokemonDetail = _activePokemon is null ? "none" : DescribeActivePokemon();
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), FormattableString.Invariant($"state={_state}; captureStage={_captureStage}; ball={_captureBall}; ballPosition={_captureBallPosition.X:F1},{_captureBallPosition.Y:F1}; pokemonHidden={_capturePokemonHidden}; {pokemonDetail}; children={SceneCanvas.Children.Count}; canvas={width}x{height}"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private string DescribeActivePokemon()
    {
        var sprite = LoadPokemonBitmap(_activePokemon!, _activeShiny);
        var layout = GetPokemonLayout(sprite);
        var support = GetPokemonSupport(_activePokemon!, _activeShiny, sprite);
        var activeScale = PokemonPlacement.Scale * GetActivePokemonSizeMultiplier();
        var shadowIndex = GetPokemonShadowIndex(_activePokemon!, _activeShiny, support);
        var shadow = _pokemonShadows[shadowIndex];
        var shadowCenterX = layout.Left + support.VisibleCenterX * activeScale;
        var shadowFootY = layout.Top + support.Y * activeScale;
        var shadowLayout = PokemonPlacement.CalculateShadow(shadow.PixelWidth, shadow.PixelHeight,
            shadowCenterX, shadowFootY, GetActivePokemonSizeMultiplier());
        return FormattableString.Invariant($"pokemon={_activePokemon!.Id}; source={sprite.PixelWidth}x{sprite.PixelHeight}; layout={layout.Left:F2},{layout.Top:F2},{layout.Width:F2},{layout.Height:F2}; anchor={_activeZone.Center.X:F1},{_activeZone.FootY:F1}; opacity={GetPokemonOpacity():F2}; shadow={shadowIndex + 1}:{shadowLayout.Left:F2},{shadowLayout.Top:F2},{shadowLayout.Width:F2},{shadowLayout.Height:F2}; shadowCenter={shadowCenterX:F2}; shadowFoot={shadowFootY:F2}; lowerWidth={support.LowerBodyWidth}");
    }
#endif

    public void CloseEncounter()
    {
        if (_state == EncounterState.Idle)
            return;
        _activePokemon = null;
        _activeAlpha = false;
        _captureStage = CaptureVisualStage.None;
        _captureBallBitmap = null;
        _capturePokemonHidden = false;
        _captureBall = null;
        _capturePanelIssued = false;
        _state = EncounterState.Idle;
        _burstFramesLeft = 0;
        StopShinySparks();
        StopAlphaEffect();
        RenderScene();
        EncounterStateChanged?.Invoke(false);
        EncounterEnded?.Invoke();
        StartRandomInterval();
    }

    public Task CloseEncounterAsync()
    {
        CloseEncounter();
        return Task.CompletedTask;
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var visible = IsVisible;
        if (visible == _wasVisible) return;
        _wasVisible = visible;
        if (visible)
        {
            if (_state == EncounterState.Idle)
                _nextEncounterAt = DateTime.UtcNow + _remainingInterval;
            _encounterTimer.Start();
            if (_shinySparkFrame >= 0) _shinySparkTimer.Start();
            if (_alphaEffectFrame >= 0) _alphaEffectTimer.Start();
            RenderScene();
        }
        else
        {
            if (_state == EncounterState.Idle)
                _remainingInterval = MaxZero(_nextEncounterAt - DateTime.UtcNow);
            _encounterTimer.Stop();
            _shinySparkTimer.Stop();
            _alphaEffectTimer.Stop();
        }
    }

    private void StartRandomInterval()
    {
        if (_state is EncounterState.Appearing or EncounterState.WaitingForInteraction)
            return;
        _state = EncounterState.Idle;
        _activePokemon = null;
        _activeAlpha = false;
        StopAlphaEffect();
        _remainingInterval = _timedEffects?.HasRepel == true ? TimeSpan.Zero
            : TimeSpan.FromSeconds(_timedEffects?.EncounterIntervalSeconds(Random.Shared) ?? EncounterRules.NextIntervalSeconds(Random.Shared));
        _nextEncounterAt = DateTime.UtcNow + _remainingInterval;
        EncounterStateChanged?.Invoke(false);
        if (IsVisible) _encounterTimer.Start();
        RenderScene();
    }

    private void OnEncounterTick(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        try
        {
            switch (_state)
            {
                case EncounterState.Idle when _timedEffects?.HasRepel != true && DateTime.UtcNow >= _nextEncounterAt:
                    BeginAlert();
                    break;
                case EncounterState.Alert:
                    _alertFramesLeft--;
                    _animationFrame++;
                    if (_alertFramesLeft <= 0)
                    {
                        EncounterDiagnostics.Write("alert-timeout", $"row={_activeZone.RowIndex}; center={_activeZone.Center.X:F2},{_activeZone.Center.Y:F2}");
                        StartRandomInterval();
                        return;
                    }
                    RenderScene();
                    break;
                case EncounterState.Appearing:
                    _animationFrame++;
                    _burstFramesLeft--;
                    if (_burstFramesLeft <= 0)
                    {
                        _state = EncounterState.WaitingForInteraction;
                        if (_activeShiny) StartShinySparks();
                        if (_activeAlpha) StartAlphaEffect();
                    }
                    RenderScene();
                    if (_state == EncounterState.WaitingForInteraction)
                    {
                        EncounterStateChanged?.Invoke(true);
                        if (!_capturePanelIssued && _activePokemon is not null)
                        {
                            _capturePanelIssued = true;
                            CaptureReady?.Invoke(_activePokemon, _activeShiny, _activeAlpha);
                        }
                    }
                    break;
                case EncounterState.WaitingForInteraction:
                    if ((_activeShiny && _shinySparkFrame >= 0) || (_activeAlpha && _alphaEffectFrame >= 0)) RenderScene();
                    break;
            }
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"timer state={_state}; pokemon={_activePokemon?.Id ?? "none"}", exception);
            RecoverFromEncounterError();
        }
    }

    private void BeginAlert(int? zoneIndex = null)
    {
        if (_state is EncounterState.Alert or EncounterState.Appearing or EncounterState.WaitingForInteraction)
            return;
        _activeZone = zoneIndex.HasValue && zoneIndex.Value >= 0 && zoneIndex.Value < _grassZones.Count
            ? _grassZones[zoneIndex.Value]
            : _grassZones[Random.Shared.Next(_grassZones.Count)];
        _state = EncounterState.Alert;
        _animationFrame = 0;
        _alertFramesLeft = (_timedEffects?.AlertSeconds ?? 10) * 10;
        EncounterDiagnostics.Write("alert-start", $"row={_activeZone.RowIndex}; center={_activeZone.Center.X:F2},{_activeZone.Center.Y:F2}; footY={_activeZone.FootY:F2}; durationSeconds={_alertFramesLeft / 10d:F1}");
        _audio.PlayEffect("sounds/effects/grass_rustle.mp3");
        EncounterStateChanged?.Invoke(true);
        RenderScene();
        if (IsVisible) _encounterTimer.Start();
    }

    private void OnSceneMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var sourcePoint = ToSourcePoint(e.GetPosition(SceneCanvas));
        if (_state == EncounterState.Alert && IsInsideActiveZone(sourcePoint, _activeZone))
        {
            e.Handled = true;
            EncounterDiagnostics.Write("alert-click", $"Accepted click at logical={sourcePoint.X:F2},{sourcePoint.Y:F2}; row={_activeZone.RowIndex}; center={_activeZone.Center.X:F2},{_activeZone.Center.Y:F2}");
            TrySpawnRandomPokemon();
            return;
        }
        if (_isLocked || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            return;
        if (_windowSource is null
            || !GetCursorPos(out _dragStartCursor)
            || !GetWindowRect(_windowSource.Handle, out var rect)
            || !SceneCanvas.CaptureMouse())
            return;
        _dragStartWindow = GetCurrentPosition();
        _isDragging = true;
        e.Handled = true;
    }

    private void OnSceneMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed
            || !GetCursorPos(out var cursor)
            || _windowSource is null)
        {
            EndDrag();
            return;
        }
        var requestedX = _dragStartWindow.X + cursor.X - _dragStartCursor.X;
        var requestedY = _dragStartWindow.Y + cursor.Y - _dragStartCursor.Y;
        var position = KeepGardenAccessible(requestedX, requestedY);
        SetWindowPos(_windowSource.Handle, IntPtr.Zero, position.X - EffectBleedX * _scale, position.Y - EffectBleedY * _scale, 0, 0,
            SwpNoActivate | SwpNoSize | SwpNoZOrder);
        ScheduleSave();
        e.Handled = true;
    }

    private void OnSceneMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        if (SceneCanvas.IsMouseCaptured) SceneCanvas.ReleaseMouseCapture();
        SaveSettings();
    }

    private void TrySpawnRandomPokemon()
    {
        try
        {
            EncounterDiagnostics.Write("encounter-select", $"Selecting encounter; catalog={_catalog.Entries.Count}; rarityComplete={_rarity.IsCompleteFor(_catalog.Entries)}");
            if (!_rarity.IsCompleteFor(_catalog.Entries))
            {
                var error = "Falta completar assets/data/encounter-rarity.json: asigna cada ID del catálogo a una sola rareza. No se cambia el peso 55/28/12/4/1.";
                EncounterNotice?.Invoke(error);
                StartRandomInterval();
                return;
            }
            var (rarity, pokemon) = _diagnosticPokemonId is { } diagnosticId
                ? (_rarity.TierOrder.First(tier => _rarity.Species[tier].Contains(diagnosticId, StringComparer.OrdinalIgnoreCase)), _catalog.ById[diagnosticId])
                : ChooseEncounterPokemon(Random.Shared);
            var shiny = _diagnosticShiny ?? (Random.Shared.Next(_timedEffects?.ShinyDenominator ?? 512) == 0);
            var alpha = EncounterRules.RollAlpha(Random.Shared);
            EncounterDiagnostics.Write("encounter-selected", $"pokemon={pokemon.Id}; rarity={rarity}; shiny={shiny}; alpha={alpha}");
            BeginPokemonEncounter(pokemon, shiny, rarity, alpha: alpha);
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException("alert-click-to-spawn", exception);
            RecoverFromEncounterError();
        }
    }

    private void RecoverFromEncounterError()
    {
        _audio.Stop();
        StopShinySparks();
        StopAlphaEffect();
        _capturePanelIssued = false;
        _activePokemon = null;
        _activeShiny = false;
        _captureStage = CaptureVisualStage.None;
        _captureBallBitmap = null;
        _captureBall = null;
        _capturePokemonHidden = false;
        _burstFramesLeft = 0;
        _state = EncounterState.Idle;
        EncounterStateChanged?.Invoke(false);
        EncounterEnded?.Invoke();
        StartRandomInterval();
        try { RenderScene(); }
        catch (Exception renderException) { EncounterDiagnostics.WriteException("encounter-recovery-render", renderException); }
        EncounterNotice?.Invoke("Hubo un problema al preparar el encuentro. PokéGrass sigue funcionando; revisa el registro si vuelve a ocurrir.");
    }

    private (string Rarity, PokemonEntry Entry) ChooseEncounterPokemon(Random random)
    {
        var (tier, normal) = _catalog.Choose(_rarity, random);
        var favoredType = _timedEffects?.FavoredType;
        if (favoredType is not null && _speciesMetadata is not null)
        {
            var favored = _catalog.GetTier(tier, _rarity)
                .Where(entry => _speciesMetadata.Get(entry.Id).Types.Contains(favoredType, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (favored.Length > 0) normal = favored[random.Next(favored.Length)];
        }

        return (tier, SelectEncounterSex(normal, random));
    }

    private PokemonEntry SelectEncounterSex(PokemonEntry selected, Random random)
    {
        var captures = _gameSave?.GetPcSnapshot().Captures ?? [];
        return _speciesMetadata is null
            ? selected
            : EncounterGenderSelector.Select(selected, _catalog, _speciesMetadata, captures, random);
    }

    private void BeginPokemonEncounter(PokemonEntry pokemon, bool shiny, string rarity, bool playAudio = true, bool alpha = false)
    {
        EncounterDiagnostics.Write("encounter-prepare", $"pokemon={pokemon.Id}; shiny={shiny}; alpha={alpha}; rarity={rarity}; audio={playAudio}");
        _activeZone = PlacePokemonInValidRow(pokemon, shiny, alpha, _activeZone);
        EncounterDiagnostics.Write("encounter-position", $"pokemon={pokemon.Id}; row={_activeZone.RowIndex}; center={_activeZone.Center.X:F2},{_activeZone.Center.Y:F2}; footY={_activeZone.FootY:F2}");
        _activePokemon = pokemon;
        _activeShiny = shiny;
        _activeAlpha = alpha;
        _activeRarity = rarity;
        _state = EncounterState.Appearing;
        _captureStage = CaptureVisualStage.None;
        _capturePanelIssued = false;
        _capturePokemonHidden = false;
        _playEncounterAudio = playAudio;
        _shinySparkPlayed = false;
        StopShinySparks();
        _alphaEffectPlayed = false;
        StopAlphaEffect();
        _animationFrame = 0;
        _burstFramesLeft = AppearanceDurationFrames;
        if (playAudio)
            _audio.PlayCry(pokemon.Id);
        RenderScene();
        EncounterDiagnostics.Write("encounter-rendered", $"pokemon={pokemon.Id}; shiny={shiny}; alpha={alpha}; canvasChildren={SceneCanvas.Children.Count}");
        if (IsVisible) _encounterTimer.Start();
        EncounterStateChanged?.Invoke(true);
    }

    internal async Task<CaptureAttempt> ThrowCaptureBallAsync(CaptureBall ball, Drawing.Point originScreen,
        CaptureBerry? berry, GameSaveStore save, PokemonSpeciesMetadata metadata, bool shiny, Random? randomOverride = null)
    {
        if (_state != EncounterState.WaitingForInteraction || _activePokemon is null
            || _activePokemon.Id != metadata.Id || _captureStage != CaptureVisualStage.None)
            return new CaptureAttempt(null);
        if (!save.TryConsume(ball))
            return new CaptureAttempt(null);

        _captureBall = ball;
        _captureBallBitmap = LoadBitmap(ball switch
        {
            CaptureBall.Poke => "items/balls/poke.png",
            CaptureBall.Great => "items/balls/great.png",
            CaptureBall.Ultra => "items/balls/ultra.png",
            CaptureBall.Master => "items/balls/master.png",
            _ => throw new ArgumentOutOfRangeException(nameof(ball))
        });
        var sprite = LoadPokemonBitmap(_activePokemon, _activeShiny);
        var support = GetPokemonSupport(_activePokemon, _activeShiny, sprite);
        var pokemonLayout = GetPokemonLayout(sprite);
        var gardenBounds = GetPhysicalBounds();
        _captureFlightStart = new Point(
            (originScreen.X - gardenBounds.Left) / (double)_scale,
            (originScreen.Y - gardenBounds.Top) / (double)_scale);
        var pokemonScale = PokemonPlacement.Scale * GetActivePokemonSizeMultiplier();
        var targetX = pokemonLayout.Left + support.VisibleCenterX * pokemonScale;
        var targetY = pokemonLayout.Top + support.AlphaBottom * pokemonScale;
        var ballRadius = CaptureBallLogicalSize / 2;
        _captureFlightTarget = new Point(
            Math.Clamp(targetX, PokemonPlacement.GreenLeft + ballRadius, PokemonPlacement.GreenRight - ballRadius),
            Math.Clamp(targetY, PokemonPlacement.GreenTop + ballRadius, PokemonPlacement.GreenBottom - ballRadius));
        _capturePokemonHidden = false;
        _captureFrame = 0;
        _captureStage = CaptureVisualStage.Flight;
        var captureRandom = randomOverride ?? Random.Shared;
        var outcome = CaptureMechanics.Roll(metadata, ball, berry, shiny, captureRandom);
        await AnimateCaptureFramesAsync(6, frame =>
        {
            _captureFrame = frame;
            var t = frame / 5d;
            // The small upward arc is a screen-space trajectory, not a change to the Pokémon's position or scale.
            var x = _captureFlightStart.X + (_captureFlightTarget.X - _captureFlightStart.X) * t;
            var y = _captureFlightStart.Y + (_captureFlightTarget.Y - _captureFlightStart.Y) * t - Math.Sin(Math.PI * t) * 6;
            _captureBallPosition = new Point(Math.Round(x), Math.Round(y));
        });

        _captureStage = CaptureVisualStage.Falling;
        _capturePokemonHidden = true;
        await AnimateCaptureFramesAsync(4, frame =>
        {
            _captureFrame = frame;
            var t = frame / 3d;
            _captureBallPosition = new Point(_captureFlightTarget.X,
                Math.Round(_captureFlightTarget.Y - 7 + t * 7));
        });

        _captureStage = CaptureVisualStage.Shaking;
        for (var shake = 0; shake < 3; shake++)
        {
            _audio.PlayEffect("sounds/effects/ball_shake.mp3");
            var shakeIndex = shake;
            await AnimateCaptureFramesAsync(4, frame =>
            {
                _captureFrame = shakeIndex * 4 + frame;
                _captureBallPosition = _captureFlightTarget;
            });
        }

        if (outcome.Success)
        {
            CaptureRecordResult recorded;
            if (_rewardBalance is not null)
                recorded = save.TryRecordCaptureWithRewards(metadata, shiny, _activeRarity, _rewardBalance,
                    Guid.NewGuid().ToString("N"), captureRandom, _activeAlpha);
            else
            {
                save.TryRecordCapture(metadata, shiny, out var legacyDuplicate, _activeAlpha);
                recorded = new CaptureRecordResult(legacyDuplicate, false, []);
            }
            CollectionChanged?.Invoke();
            _captureStage = CaptureVisualStage.Success;
            _captureBallPosition = _captureFlightTarget;
            _audio.PlayEffect("sounds/effects/capture_success.mp3");
            RenderScene();
            await Task.Delay(350);
            return new CaptureAttempt(new CaptureResolution(true, recorded.Duplicate, false, outcome.Chance, recorded.Rewards));
        }

        _captureStage = CaptureVisualStage.OpeningFailure;
        _audio.PlayEffect("sounds/effects/ball_opens.mp3");
        await AnimateCaptureFramesAsync(3, frame => { _captureFrame = frame; });
        _captureStage = CaptureVisualStage.None;
        _captureBall = null;
        _captureBallBitmap = null;
        _capturePokemonHidden = false;
        RenderScene();
        var fled = CaptureMechanics.RollFlee(metadata, _activeRarity, berry, shiny, captureRandom);
        if (fled)
            await FleeEncounterAsync();
        return new CaptureAttempt(new CaptureResolution(false, false, fled, outcome.Chance));
    }

    public async Task FleeEncounterAsync()
    {
        if (_state != EncounterState.WaitingForInteraction || _activePokemon is null) return;
        _captureStage = CaptureVisualStage.Fleeing;
        _captureFrame = 0;
        _audio.PlayEffect("sounds/effects/pokemon_runs.mp3");
        await AnimateCaptureFramesAsync(8, frame => _captureFrame = frame);
        CloseEncounter();
    }

    private async Task AnimateCaptureFramesAsync(int count, Action<int> update)
    {
        for (var frame = 0; frame < count; frame++)
        {
            update(frame);
            RenderScene();
#if DEBUG
            if (_debugCaptureSequenceDirectory is not null)
            {
                Directory.CreateDirectory(_debugCaptureSequenceDirectory);
                SaveSceneCapture(Path.Combine(_debugCaptureSequenceDirectory, $"{_captureStage}-{_captureFrame:D2}.png"));
            }
#endif
            await Task.Delay(75);
        }
        update(count - 1);
        RenderScene();
    }

    public Drawing.Rectangle GetPhysicalBounds()
    {
        if (_windowSource is not null && GetWindowRect(_windowSource.Handle, out var bounds))
            return new Drawing.Rectangle(bounds.Left + EffectBleedX * _scale, bounds.Top + EffectBleedY * _scale,
                _gardenBitmap.PixelWidth * _scale, _gardenBitmap.PixelHeight * _scale);
        return new Drawing.Rectangle(_positionX, _positionY, _gardenBitmap.PixelWidth * _scale, _gardenBitmap.PixelHeight * _scale);
    }

    public (double X, double Y) GetDpiScale() => GetWindowDpiScale();

    private void OnSceneDragOver(object sender, System.Windows.DragEventArgs e)
    {
        var hit = e.Data.GetDataPresent(CapturePanelWindow.ItemDragFormat)
            && e.Data.GetData(CapturePanelWindow.ItemDragFormat) is CapturePanelWindow.CaptureItemDrag
            && _activePokemon is not null && _state == EncounterState.WaitingForInteraction
            && _captureStage == CaptureVisualStage.None
            && IsInsideVisiblePokemon(ToSourcePoint(e.GetPosition(SceneCanvas)), _activePokemon, _activeShiny);
        e.Effects = hit ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSceneDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(CapturePanelWindow.ItemDragFormat) is CapturePanelWindow.CaptureItemDrag drag
            && _activePokemon is not null && _state == EncounterState.WaitingForInteraction
            && _captureStage == CaptureVisualStage.None
            && IsInsideVisiblePokemon(ToSourcePoint(e.GetPosition(SceneCanvas)), _activePokemon, _activeShiny))
        {
            ItemDroppedOnPokemon?.Invoke(drag);
            e.Handled = true;
            return;
        }
        e.Handled = true;
    }

    private bool IsInsideVisiblePokemon(Point sourcePoint, PokemonEntry pokemon, bool shiny)
    {
        var sprite = LoadPokemonBitmap(pokemon, shiny);
        var support = GetPokemonSupport(pokemon, shiny, sprite);
        var sizeMultiplier = pokemon == _activePokemon ? GetActivePokemonSizeMultiplier() : 1d;
        var layout = PokemonPlacement.Calculate(sprite.PixelWidth, sprite.PixelHeight, support,
            _activeZone.Center.X, _activeZone.FootY, sizeMultiplier);
        var scale = PokemonPlacement.Scale * sizeMultiplier;
        return sourcePoint.X >= layout.Left + support.AlphaLeft * scale
            && sourcePoint.X <= layout.Left + (support.AlphaRight + 1) * scale
            && sourcePoint.Y >= layout.Top + support.AlphaTop * scale
            && sourcePoint.Y <= layout.Top + (support.AlphaBottom + 1) * scale;
    }

    private static bool IsInsideActiveZone(Point point, GrassZone zone) =>
        Math.Abs(point.X - zone.Center.X) <= 18
        && point.Y >= zone.Center.Y - 27
        && point.Y <= zone.Center.Y + 13;

    private Point ToSourcePoint(Point canvasPoint)
    {
        var (dpiX, dpiY) = GetWindowDpiScale();
        return new Point(canvasPoint.X * dpiX / _scale - EffectBleedX,
            canvasPoint.Y * dpiY / _scale - EffectBleedY);
    }

    private void RenderScene()
    {
        if (!IsInitialized) return;
        SceneCanvas.Children.Clear();
        ConfigureImage(GardenImage, _gardenBitmap);
        Put(GardenImage, 0, 0);

        DrawAllGrassRows();
        if (_shinySparkFrame >= 0 && _activePokemon is not null && _activeShiny && !_capturePokemonHidden
            && _state is EncounterState.Appearing or EncounterState.WaitingForInteraction)
            DrawShinySparks(_activePokemon);
        if (_activePokemon is not null && !_capturePokemonHidden
            && _state is EncounterState.Appearing or EncounterState.WaitingForInteraction)
        {
            DrawPokemonShadow(_activePokemon, _activeShiny);
            DrawPokemon(_activePokemon, _activeShiny);
        }
        if (_alphaEffectFrame >= 0 && _activePokemon is not null && _activeAlpha && !_capturePokemonHidden
            && _state is EncounterState.Appearing or EncounterState.WaitingForInteraction)
            DrawAlphaEffect(_activePokemon, _activeShiny, _alphaEffectFrame);

        if (_state == EncounterState.Alert)
        {
            DrawAlertLeaves();
            var marker = _exclamationFrames[(_animationFrame / 2) % _exclamationFrames.Count];
            var markerCenter = ClampEffectCenter(_activeZone.Center.X, _activeZone.Center.Y - 15, 20, 20);
            DrawCentered(marker, markerCenter.X, markerCenter.Y, 20, 20);
        }
        else if (_burstFramesLeft > 0)
        {
            DrawLeaves(5, floating: false);
        }

        DrawCaptureAnimation();
        DrawTimedEffectIndicator();
    }

    private void DrawTimedEffectIndicator()
    {
        if (_timedEffects is null || _timedEffectIndicatorFrame is null
            || (!_timedEffects.HasRepel && !_timedEffects.HasIncense)) return;

        var iconPath = _timedEffects.HasRepel
            ? "items/repels/repel.png"
            : IncenseIconPath(_timedEffects.IncenseId);
        if (iconPath is null) return;
        if (!_timedEffectIconCache.TryGetValue(iconPath, out var iconBitmap))
        {
            iconBitmap = TryLoadBitmap(iconPath)!;
            if (iconBitmap is null) return;
            _timedEffectIconCache.Add(iconPath, iconBitmap);
        }

        var remaining = _timedEffects.HasRepel ? _timedEffects.RepelSeconds : _timedEffects.IncenseSeconds;
        var timeText = FormatTimedEffectTime(remaining);
        var (dpiX, dpiY) = GetWindowDpiScale();
        var scaleX = _scale / dpiX;
        var scaleY = _scale / dpiY;

        // The status frame keeps its native ~2.1:1 ratio. Its 32-source-pixel
        // width equals two 16px grass cells, or 96 physical pixels at garden 3×.
        const double designWidth = 32;
        var width = designWidth * scaleX;
        var height = designWidth * _timedEffectIndicatorFrame.PixelHeight / _timedEffectIndicatorFrame.PixelWidth * scaleY;
        var left = ((_gardenBitmap.PixelWidth - designWidth) / 2 + EffectBleedX) * scaleX;
        var top = (EffectBleedY - 20) * scaleY;

        var indicator = new Grid { Width = width, Height = height, IsHitTestVisible = false };
        var frame = new Image
        {
            Source = _timedEffectIndicatorFrame, Width = width, Height = height,
            Stretch = Stretch.Fill, IsHitTestVisible = false, SnapsToDevicePixels = true
        };
        ConfigureImage(frame, _timedEffectIndicatorFrame);
        indicator.Children.Add(frame);

        var icon = new Image
        {
            Source = iconBitmap, Width = 8 * scaleX, Height = 8 * scaleY,
            Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2.5 * scaleX, 0, 0, 0),
            IsHitTestVisible = false, SnapsToDevicePixels = true
        };
        ConfigureImage(icon, iconBitmap);
        indicator.Children.Add(icon);

        var time = new TextBlock
        {
            Text = timeText, FontFamily = PixelUi.PixelFont, FontSize = 4.5 * scaleX,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 45, 37)),
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10 * scaleX, 0, 2.5 * scaleX, 0),
            IsHitTestVisible = false, SnapsToDevicePixels = true
        };
        TextOptions.SetTextRenderingMode(time, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(time, TextFormattingMode.Display);
        indicator.Children.Add(time);
        Canvas.SetLeft(indicator, left);
        Canvas.SetTop(indicator, top);
        SceneCanvas.Children.Add(indicator);
    }

    private static string? IncenseIconPath(string? id) => id switch
    {
        "incense-full" => "items/incense/full.png",
        "incense-lax" => "items/incense/lax.png",
        "incense-luck" => "items/incense/luck.png",
        "incense-odd" => "items/incense/odd.png",
        "incense-pure" => "items/incense/pure.png",
        "incense-rock" => "items/incense/rock.png",
        "incense-rose" => "items/incense/rose.png",
        "incense-sea" => "items/incense/sea.png",
        "incense-wave" => "items/incense/wave.png",
        _ => null
    };

    private static string FormatTimedEffectTime(int seconds)
    {
        seconds = Math.Max(0, seconds);
        if (seconds >= 3600) return $"{seconds / 3600}h{seconds / 60 % 60:D2}";
        return $"{seconds / 60:D2}:{seconds % 60:D2}";
    }

    private void DrawCaptureAnimation()
    {
        if (_activePokemon is null) return;
        if (_captureStage is CaptureVisualStage.Flight or CaptureVisualStage.Falling
            or CaptureVisualStage.Shaking or CaptureVisualStage.Success or CaptureVisualStage.OpeningFailure)
        {
            if (_captureBallBitmap is not null)
            {
                var size = CaptureBallLogicalSize;
                var position = _captureBallPosition;
                if (_captureStage == CaptureVisualStage.Flight)
                    position = new Point(Math.Round(position.X), Math.Round(position.Y));
                if (_captureStage == CaptureVisualStage.Shaking)
                {
                    var offset = (_captureFrame % 4) switch { 0 => -1, 1 => 1, 2 => 1, _ => 0 };
                    position = new Point(position.X + offset, position.Y);
                }
                var ball = MakeImage(_captureBallBitmap);
                if (_captureStage == CaptureVisualStage.OpeningFailure)
                    ball.Opacity = Math.Clamp(1d - _captureFrame / 3d, 0, 1);
                PutSized(ball, position.X - size / 2, position.Y - size / 2, size, size);
            }
            if (_captureStage == CaptureVisualStage.Success && _starsSprite is not null)
            {
                var stars = MakeImage(_starsSprite);
                var sparkWidth = 14d;
                var sparkHeight = sparkWidth * _starsSprite.PixelHeight / _starsSprite.PixelWidth;
                stars.Opacity = _captureFrame % 4 < 2 ? 1 : 0.7;
                PutSized(stars, _captureFlightTarget.X - sparkWidth / 2,
                    _captureFlightTarget.Y - sparkHeight / 2, sparkWidth, sparkHeight);
            }
        }
        else if (_captureStage == CaptureVisualStage.Fleeing)
        {
            var rustle = _grassFrames[_captureFrame % _grassFrames.Count];
            DrawCentered(rustle, _activeZone.Center.X, _activeZone.Center.Y + 4, 17, 17);
            DrawLeaves(3, floating: false);
        }
    }

    private void StartShinySparks()
    {
        if (_shinySparkPlayed || !_activeShiny) return;
        _shinySparkPlayed = true;
        _shinySparkFrame = 0;
        if (_playEncounterAudio) _audio.PlayEffect("sounds/effects/shiny_sparkle.mp3");
        if (IsVisible) _shinySparkTimer.Start();
    }

    private void StopShinySparks()
    {
        _shinySparkTimer?.Stop();
        _shinySparkFrame = -1;
    }

    private void OnShinySparkTick(object? sender, EventArgs e)
    {
        if (!IsVisible || _shinySparkFrame < 0)
        {
            _shinySparkTimer.Stop();
            return;
        }
        if (_shinySparkFrame + 1 >= _shinySparkFrames.Count)
        {
            StopShinySparks();
            RenderScene();
            return;
        }
        _shinySparkFrame++;
        RenderScene();
    }

    private void StartAlphaEffect()
    {
        if (_alphaEffectPlayed || !_activeAlpha || _alphaEffectFrames.Count == 0) return;
        _alphaEffectPlayed = true;
        _alphaEffectFrame = 0;
        if (IsVisible) _alphaEffectTimer.Start();
    }

    private void StopAlphaEffect()
    {
        _alphaEffectTimer?.Stop();
        _alphaEffectFrame = -1;
    }

    private void OnAlphaEffectTick(object? sender, EventArgs e)
    {
        if (!IsVisible || !_activeAlpha || _alphaEffectFrame < 0)
        {
            _alphaEffectTimer.Stop();
            return;
        }
        if (_alphaEffectFrame + 1 >= _alphaEffectFrames.Count)
        {
            StopAlphaEffect();
            RenderScene();
            return;
        }
        _alphaEffectFrame++;
        RenderScene();
    }

    private void DrawAlphaEffect(PokemonEntry pokemon, bool shiny, int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _alphaEffectFrames.Count) return;
        var sprite = LoadPokemonBitmap(pokemon, shiny);
        var layout = GetPokemonLayout(sprite);
        var support = GetPokemonSupport(pokemon, shiny, sprite);
        var scale = PokemonPlacement.Scale * GetActivePokemonSizeMultiplier();
        var left = layout.Left + support.AlphaLeft * scale;
        var right = layout.Left + (support.AlphaRight + 1) * scale;
        var top = layout.Top + support.AlphaTop * scale;
        var bottom = layout.Top + (support.AlphaBottom + 1) * scale;
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        var frame = _alphaEffectFrames[frameIndex];
        var aspect = frame.PixelWidth / (double)frame.PixelHeight;
        var width = Math.Clamp(Math.Max((right - left) * 1.2, (bottom - top) * .72), 24d, 72d);
        var height = width / aspect;
        var maxHeight = Math.Min(centerY + EffectBleedY, _gardenBitmap.PixelHeight + EffectBleedY - centerY) * 2d;
        if (height > maxHeight && maxHeight > 0) { height = maxHeight; width = height * aspect; }
        var image = MakeImage(frame);
        image.Opacity = .92;
        PutSized(image, centerX - width / 2, centerY - height / 2, width, height);
    }

    private void DrawShinySparks(PokemonEntry pokemon)
    {
        if (_shinySparkFrame < 0 || _shinySparkFrame >= _shinySparkFrames.Count) return;
        var sprite = LoadPokemonBitmap(pokemon, shiny: true);
        var layout = GetPokemonLayout(sprite);
        var support = GetPokemonSupport(pokemon, shiny: true, sprite);
        var scale = PokemonPlacement.Scale * GetActivePokemonSizeMultiplier();
        var visibleLeft = layout.Left + support.AlphaLeft * scale;
        var visibleRight = layout.Left + (support.AlphaRight + 1) * scale;
        var visibleTop = layout.Top + support.AlphaTop * scale;
        var visibleBottom = layout.Top + (support.AlphaBottom + 1) * scale;
        var visibleWidth = visibleRight - visibleLeft;
        var visibleHeight = visibleBottom - visibleTop;
        // Keep the effect proportional for small and large species while leaving
        // enough transparent space around the body. The sprite is drawn afterward.
        var size = Math.Clamp(Math.Max(visibleWidth, visibleHeight) * 1.75, 28d, 92d);
        var centerX = (visibleLeft + visibleRight) / 2;
        var centerY = (visibleTop + visibleBottom) / 2;
        var frame = MakeImage(_shinySparkFrames[_shinySparkFrame]);
        frame.Opacity = 0.94;
        var frameWidth = size * _shinySparkFrames[_shinySparkFrame].PixelWidth
            / _shinySparkFrames[_shinySparkFrame].PixelHeight;
        PutSized(frame, centerX - frameWidth / 2, centerY - size / 2, frameWidth, size);
    }

    private void DrawPokemon(PokemonEntry entry, bool shiny)
    {
        var sprite = LoadPokemonBitmap(entry, shiny);
        var layout = GetPokemonLayout(sprite);
        var image = MakeImage(sprite);
        image.Opacity = GetPokemonOpacity();
        PutSized(image, layout.Left, layout.Top, layout.Width, layout.Height);
    }

    private void DrawPokemonShadow(PokemonEntry entry, bool shiny)
    {
        var sprite = LoadPokemonBitmap(entry, shiny);
        var support = GetPokemonSupport(entry, shiny, sprite);
        var shadow = _pokemonShadows[GetPokemonShadowIndex(entry, shiny, support)];
        var pokemonLayout = GetPokemonLayout(sprite);
        var pokemonScale = PokemonPlacement.Scale * GetActivePokemonSizeMultiplier();
        var shadowCenterX = pokemonLayout.Left + support.VisibleCenterX * pokemonScale;
        var shadowFootY = pokemonLayout.Top + support.Y * pokemonScale;
        var layout = PokemonPlacement.CalculateShadow(shadow.PixelWidth, shadow.PixelHeight,
            shadowCenterX, shadowFootY, GetActivePokemonSizeMultiplier());
        PutSized(MakeImage(shadow), layout.Left, layout.Top, layout.Width, layout.Height);
    }

    private int GetPokemonShadowIndex(PokemonEntry entry, bool shiny, SpriteSupport support)
    {
        var key = (entry.Id, shiny);
        if (_pokemonShadowChoiceCache.TryGetValue(key, out var selected)) return selected;
        selected = PokemonPlacement.SelectShadowVariant(support.LowerBodyWidth,
            _pokemonShadows.Select(shadow => shadow.PixelWidth).ToArray());
        _pokemonShadowChoiceCache[key] = selected;
        return selected;
    }

    private double GetPokemonOpacity() => _captureStage == CaptureVisualStage.Fleeing
        ? Math.Clamp(1d - _captureFrame / 8d, 0, 1)
        : _state == EncounterState.Appearing
            ? Math.Clamp(1d - _burstFramesLeft / (double)AppearanceDurationFrames, 0, 1)
            : 1d;

    private double GetActivePokemonSizeMultiplier() => _activeAlpha ? 1.25d : 1d;

    private PokemonLayout GetPokemonLayout(BitmapSource sprite)
    {
        var support = GetPokemonSupport(_activePokemon!, _activeShiny, sprite);
        return PokemonPlacement.Calculate(sprite.PixelWidth, sprite.PixelHeight, support,
            _activeZone.Center.X, _activeZone.FootY, GetActivePokemonSizeMultiplier());
    }

    private GrassZone PlacePokemonInValidRow(PokemonEntry entry, bool shiny, bool alpha, GrassZone preferredZone)
    {
        var sprite = LoadPokemonBitmap(entry, shiny);
        var support = GetPokemonSupport(entry, shiny, sprite);
        var validFootY = PokemonPlacement.ChooseFootY(support,
            _grassRows.Select(row => row.Bottom + 1d), alpha ? 1.25d : 1d);
        if (!double.IsFinite(validFootY))
            throw new InvalidDataException($"{entry.Id} no tiene una fila de apoyo compatible con su escala fija.");
        var row = _grassRows.OrderBy(candidate => Math.Abs(candidate.Bottom + 1 - validFootY)).First();
        return new GrassZone(new Point(preferredZone.Center.X, (row.Top + row.Bottom) / 2d), row.Index, validFootY);
    }

    private SpriteSupport GetPokemonSupport(PokemonEntry entry, bool shiny, BitmapSource sprite)
    {
        var key = (entry.Id, shiny);
        if (_pokemonSupportCache.TryGetValue(key, out var cached)) return cached;
        var converted = new FormatConvertedBitmap(sprite, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var left = converted.PixelWidth;
        var top = converted.PixelHeight;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < converted.PixelHeight; y++)
        for (var x = 0; x < converted.PixelWidth; x++)
        {
            if (pixels[y * stride + x * 4 + 3] == 0) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top)
            throw new InvalidDataException($"El sprite de {entry.Id} no contiene píxeles visibles.");

        var supportLeft = converted.PixelWidth;
        var supportRight = -1;
        for (var y = Math.Max(top, bottom - 2); y <= bottom; y++)
        for (var x = left; x <= right; x++)
            if (pixels[y * stride + x * 4 + 3] >= 128)
            {
                supportLeft = Math.Min(supportLeft, x);
                supportRight = Math.Max(supportRight, x);
            }
        var footX = supportRight < supportLeft ? (left + right + 1) / 2d : (supportLeft + supportRight + 1) / 2d;
        var lowerBodyLeft = converted.PixelWidth;
        var lowerBodyRight = -1;
        for (var y = Math.Max(top, bottom - 24); y <= bottom; y++)
        for (var x = left; x <= right; x++)
            if (pixels[y * stride + x * 4 + 3] >= 128)
            {
                lowerBodyLeft = Math.Min(lowerBodyLeft, x);
                lowerBodyRight = Math.Max(lowerBodyRight, x);
            }
        if (lowerBodyRight < lowerBodyLeft)
        {
            lowerBodyLeft = left;
            lowerBodyRight = right;
        }
        var result = new SpriteSupport(footX, bottom + 1, left, top, right, bottom, lowerBodyLeft, lowerBodyRight);
        _pokemonSupportCache[key] = result;
        return result;
    }

    private BitmapSource LoadPokemonBitmap(PokemonEntry entry, bool shiny)
    {
        var key = (entry.Id, shiny);
        if (_pokemonSpriteCache.TryGetValue(key, out var cached)) return cached;
        var relative = shiny ? entry.EncounterShiny : entry.EncounterNormal;
        var bitmap = LoadBitmap(relative);
        if (bitmap.PixelWidth != PokemonPlacement.EncounterCanvasSize || bitmap.PixelHeight != PokemonPlacement.EncounterCanvasSize)
            throw new InvalidDataException($"El lienzo de encuentro de {entry.Id} debe ser 192×192; se encontró {bitmap.PixelWidth}×{bitmap.PixelHeight}.");
        _pokemonSpriteCache.Add(key, bitmap);
        return bitmap;
    }

    private void DrawLeaves(int count, bool floating)
    {
        if (_leafFrames.Count == 0) return;
        for (var i = 0; i < count; i++)
        {
            var sprite = _leafFrames[(_animationFrame * 3 + i * 11) % _leafFrames.Count];
            var fit = Math.Min(5d / sprite.PixelWidth, 5d / sprite.PixelHeight);
            var width = Math.Max(1, sprite.PixelWidth * fit);
            var height = Math.Max(1, sprite.PixelHeight * fit);
            var angle = (i * 137 + _animationFrame * (floating ? 17 : 29)) * Math.PI / 180d;
            var radius = floating ? 11 + (_animationFrame + i * 3) % 6 : 7 + (_animationFrame + i) % 12;
            var x = Math.Round(_activeZone.Center.X + Math.Cos(angle) * radius, MidpointRounding.AwayFromZero);
            var y = Math.Round(_activeZone.Center.Y + Math.Sin(angle) * radius - (floating ? (_animationFrame % 6) : 0), MidpointRounding.AwayFromZero);
            var visibleCenter = ClampEffectCenter(x, y, width, height);
            var image = MakeImage(sprite);
            PutSized(image, visibleCenter.X - width / 2, visibleCenter.Y - height / 2, width, height);
        }
    }

    private void DrawAlertLeaves()
    {
        if (_leafFrames.Count == 0) return;

        // Four small leaves orbit just outside the exclamation mark and drift up/out.
        // The marker is rendered afterward, so foliage can never obscure its shape.
        ReadOnlySpan<double> baseOffsetsX = [-9, 9, -15, 15];
        ReadOnlySpan<double> baseOffsetsY = [-10, -10, 0, 0];
        for (var i = 0; i < baseOffsetsX.Length; i++)
        {
            var cycle = ((_animationFrame + i * 6) % 24) / 24d;
            var sprite = _leafFrames[(_animationFrame + i * 9) % _leafFrames.Count];
            var fit = Math.Min(4d / sprite.PixelWidth, 4d / sprite.PixelHeight);
            var width = Math.Max(1, sprite.PixelWidth * fit);
            var height = Math.Max(1, sprite.PixelHeight * fit);
            var x = _activeZone.Center.X + baseOffsetsX[i] + Math.Sin(cycle * Math.PI * 2d) * 2d;
            var y = _activeZone.Center.Y + baseOffsetsY[i] - cycle * 7d;
            var visibleCenter = ClampEffectCenter(x, y, width, height);
            PutSized(MakeImage(sprite), visibleCenter.X - width / 2, visibleCenter.Y - height / 2, width, height);
        }
    }

    private Point ClampEffectCenter(double x, double y, double logicalWidth, double logicalHeight)
    {
        var origin = GetCurrentPosition();
        var gardenWidth = _gardenBitmap.PixelWidth * _scale;
        var gardenHeight = _gardenBitmap.PixelHeight * _scale;
        var screen = Forms.Screen.FromPoint(new Drawing.Point(origin.X + gardenWidth / 2, origin.Y + gardenHeight / 2)).Bounds;
        var halfWidth = logicalWidth / 2d;
        var halfHeight = logicalHeight / 2d;
        const double screenSafetyMargin = 2d;
        var minX = (screen.Left - origin.X) / (double)_scale + halfWidth + screenSafetyMargin;
        var maxX = (screen.Right - origin.X) / (double)_scale - halfWidth - screenSafetyMargin;
        var minY = (screen.Top - origin.Y) / (double)_scale + halfHeight + screenSafetyMargin;
        var maxY = (screen.Bottom - origin.Y) / (double)_scale - halfHeight - screenSafetyMargin;
        return new Point(maxX >= minX ? Math.Clamp(x, minX, maxX) : x,
            maxY >= minY ? Math.Clamp(y, minY, maxY) : y);
    }

    private void DrawCentered(BitmapSource sprite, double x, double y, double logicalWidth, double logicalHeight)
    {
        var fit = Math.Min(logicalWidth / sprite.PixelWidth, logicalHeight / sprite.PixelHeight);
        var width = sprite.PixelWidth * fit;
        var height = sprite.PixelHeight * fit;
        var image = MakeImage(sprite);
        PutSized(image, x - width / 2, y - height / 2, width, height);
    }

    private Image MakeImage(BitmapSource bitmap)
    {
        var image = new Image { Source = bitmap, Stretch = Stretch.Fill, IsHitTestVisible = false,
            SnapsToDevicePixels = true, UseLayoutRounding = true };
        ConfigureImage(image, bitmap);
        return image;
    }

    private static void ConfigureImage(Image image, BitmapSource bitmap)
    {
        image.Source = bitmap;
        image.Stretch = Stretch.Fill;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
    }

    private void Put(Image image, double x, double y)
    {
        PutSized(image, x, y, _gardenBitmap.PixelWidth, _gardenBitmap.PixelHeight);
    }

    private void PutSized(Image image, double x, double y, double width, double height)
    {
        var (dpiX, dpiY) = GetWindowDpiScale();
        image.Width = width * _scale / dpiX;
        image.Height = height * _scale / dpiY;
        Canvas.SetLeft(image, (x + EffectBleedX) * _scale / dpiX);
        Canvas.SetTop(image, (y + EffectBleedY) * _scale / dpiY);
        SceneCanvas.Children.Add(image);
    }

    private BitmapSource LoadBitmap(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var normalized = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);
        normalized.Freeze();
        return normalized;
    }

    private IReadOnlyList<GrassRow> LoadGardenGrassRows()
    {
        // Keep each supplied transparent row independent so it can be depth-sorted
        // around the Pokémon. All source PNGs remain untouched.
        var layers = new[]
        {
            LoadGrassRow("Garden/OverlayCesped 3.png"),
            LoadGrassRow("Garden/OverlayCesped2.png"),
            LoadGrassRow("Garden/OverlayCesped1.png")
        };
        if (layers.Any(row => row.Bitmap.PixelWidth != _gardenBitmap.PixelWidth
            || row.Bitmap.PixelHeight != _gardenBitmap.PixelHeight))
            throw new InvalidDataException("Las capas OverlayCesped deben tener las mismas dimensiones que el jardín.");
        if (layers.Any(row => row.Bottom < row.Top))
            throw new InvalidDataException("Cada overlay debe incluir píxeles visibles con alfa.");
        return layers.OrderBy(row => row.Top)
            .Select((row, index) => row with { Index = index })
            .ToArray();
    }

    private IReadOnlyList<BitmapSource> LoadPokemonShadows()
    {
        var expectedDimensions = new[] { (Width: 72, Height: 16), (Width: 100, Height: 20), (Width: 136, Height: 24) };
        var shadows = new List<BitmapSource>(expectedDimensions.Length);
        for (var index = 0; index < expectedDimensions.Length; index++)
        {
            var shadow = LoadBitmap($"pokemon/pokemons_shadow/{index + 1}.png");
            var expected = expectedDimensions[index];
            if (shadow.PixelWidth != expected.Width || shadow.PixelHeight != expected.Height)
                throw new InvalidDataException($"La sombra {index + 1} debe medir {expected.Width}×{expected.Height}; se encontró {shadow.PixelWidth}×{shadow.PixelHeight}.");
            var pixels = new byte[shadow.PixelWidth * shadow.PixelHeight * 4];
            shadow.CopyPixels(pixels, shadow.PixelWidth * 4, 0);
            var hasTransparent = false;
            var hasVisible = false;
            for (var offset = 3; offset < pixels.Length; offset += 4)
            {
                hasTransparent |= pixels[offset] == 0;
                hasVisible |= pixels[offset] > 0;
            }
            if (!hasTransparent || !hasVisible)
                throw new InvalidDataException($"La sombra {index + 1} debe conservar transparencia alfa real y píxeles visibles.");
            shadows.Add(shadow);
        }
        return shadows;
    }

    private GrassRow LoadGrassRow(string path)
    {
        var bitmap = LoadBitmap(path);
        var (top, bottom) = GetAlphaVerticalBounds(bitmap);
        return new GrassRow(bitmap, top, bottom, -1);
    }

    private static (int Top, int Bottom) GetAlphaVerticalBounds(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var top = bitmap.PixelHeight;
        var bottom = -1;
        for (var y = 0; y < bitmap.PixelHeight; y++)
        for (var x = 0; x < bitmap.PixelWidth; x++)
            if (pixels[(y * bitmap.PixelWidth + x) * 4 + 3] > 0)
            {
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        return (top, bottom);
    }

    private void DrawAllGrassRows()
    {
        foreach (var row in _grassRows)
            Put(MakeImage(row.Bitmap), 0, 0);
    }

    private BitmapSource? TryLoadBitmap(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? LoadBitmap(relativePath) : null;
    }

    private List<BitmapSource> LoadSheetFrames(string relativePath, int columns, int rows, int cellSize, bool trim)
    {
        var sheet = LoadBitmap(relativePath);
        var frames = new List<BitmapSource>();
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            BitmapSource frame = new CroppedBitmap(sheet, new Int32Rect(column * cellSize, row * cellSize, cellSize, cellSize));
            frame.Freeze();
            if (trim)
            {
                var trimmed = TrimAlpha(frame);
                if (trimmed is not null) frames.Add(trimmed);
            }
            else if (frame.PixelWidth > 0 && frame.PixelHeight > 0) frames.Add(frame);
        }
        return frames;
    }

    private static BitmapSource? TrimAlpha(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var left = converted.PixelWidth;
        var top = converted.PixelHeight;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < converted.PixelHeight; y++)
        for (var x = 0; x < converted.PixelWidth; x++)
        {
            if (pixels[y * stride + x * 4 + 3] < 51) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top) return null;
        var crop = new CroppedBitmap(converted, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        crop.Freeze();
        return crop;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowHook);
        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(extendedStyle | WsExNoActivate));
        UpdatePhysicalPixelScale();
        var position = GetRestoredPosition();
        _positionX = position.X;
        _positionY = position.Y;
        SetWindowPos(handle, IntPtr.Zero, position.X - EffectBleedX * _scale, position.Y - EffectBleedY * _scale,
            0, 0, SwpNoActivate | SwpNoSize | SwpNoZOrder);
        Dispatcher.BeginInvoke(() =>
        {
            ApplyPhysicalWindowSize();
            KeepGardenOnScreen();
            RenderScene();
            SaveSettings();
        }, DispatcherPriority.Loaded);
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            var info = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
            info.MaxTrackSize = new NativePoint(32767, 32767);
            Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
            handled = true;
            return IntPtr.Zero;
        }
        if (message == WmDpiChanged)
            Dispatcher.BeginInvoke(() =>
            {
                var currentGardenPosition = GetCurrentPosition();
                _positionX = currentGardenPosition.X;
                _positionY = currentGardenPosition.Y;
                UpdatePhysicalPixelScale();
                ApplyPhysicalWindowSize();
                KeepGardenOnScreen();
                RenderScene();
                SaveSettings();
            }, DispatcherPriority.Loaded);
        return IntPtr.Zero;
    }

    private void UpdatePhysicalPixelScale()
    {
        var (dpiScaleX, dpiScaleY) = GetWindowDpiScale();
        var width = _gardenBitmap.PixelWidth * _scale / dpiScaleX;
        var height = _gardenBitmap.PixelHeight * _scale / dpiScaleY;
        SceneCanvas.Width = Width = (_gardenBitmap.PixelWidth + EffectBleedX * 2) * _scale / dpiScaleX;
        SceneCanvas.Height = Height = (_gardenBitmap.PixelHeight + EffectBleedY * 2) * _scale / dpiScaleY;
        GardenImage.Width = width;
        GardenImage.Height = height;
    }

    private (double X, double Y) GetWindowDpiScale()
    {
        if (_windowSource is not null)
        {
            var dpi = GetDpiForWindow(_windowSource.Handle);
            if (dpi != 0) return (dpi / 96d, dpi / 96d);
        }
        var visualDpi = VisualTreeHelper.GetDpi(this);
        return (visualDpi.DpiScaleX, visualDpi.DpiScaleY);
    }

    private void ApplyPhysicalWindowSize()
    {
        if (_windowSource is null || !GetWindowRect(_windowSource.Handle, out var rect)) return;
        var x = _positionX - EffectBleedX * _scale;
        var y = _positionY - EffectBleedY * _scale;
        var width = (_gardenBitmap.PixelWidth + EffectBleedX * 2) * _scale;
        var height = (_gardenBitmap.PixelHeight + EffectBleedY * 2) * _scale;
        if (rect.Left == x && rect.Top == y && rect.Right - rect.Left == width && rect.Bottom - rect.Top == height) return;
        SetWindowPos(_windowSource.Handle, IntPtr.Zero, x, y, width, height, SwpNoActivate | SwpNoZOrder);
    }

    private void ScheduleSave()
    {
        if (!IsVisible || _windowSource is null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private System.Drawing.Point GetCurrentPosition()
    {
        if (_windowSource is not null && GetWindowRect(_windowSource.Handle, out var rect))
            return new System.Drawing.Point(rect.Left + EffectBleedX * _scale, rect.Top + EffectBleedY * _scale);
        return new System.Drawing.Point(_positionX, _positionY);
    }

    private System.Drawing.Point GetRestoredPosition()
    {
        var bounds = new Drawing.Rectangle(_positionX, _positionY, _gardenBitmap.PixelWidth * _scale, _gardenBitmap.PixelHeight * _scale);
        if (Forms.Screen.AllScreens.Any(screen =>
        {
            var intersection = Drawing.Rectangle.Intersect(screen.Bounds, bounds);
            return intersection.Width >= MinimumVisiblePixels && intersection.Height >= MinimumVisiblePixels;
        })) return new System.Drawing.Point(_positionX, _positionY);
        var primary = Forms.Screen.PrimaryScreen?.Bounds ?? new Drawing.Rectangle(0, 0, 1024, 768);
        return new System.Drawing.Point(primary.Left + 24, primary.Top + 24);
    }

    private void KeepGardenOnScreen()
    {
        if (_windowSource is null || !GetWindowRect(_windowSource.Handle, out var rect)) return;
        var gardenX = rect.Left + EffectBleedX * _scale;
        var gardenY = rect.Top + EffectBleedY * _scale;
        var position = KeepGardenAccessible(gardenX, gardenY);
        if (position.X != gardenX || position.Y != gardenY)
            SetWindowPos(_windowSource.Handle, IntPtr.Zero, position.X - EffectBleedX * _scale, position.Y - EffectBleedY * _scale, 0, 0, SwpNoActivate | SwpNoSize | SwpNoZOrder);
    }

    private System.Drawing.Point KeepGardenAccessible(int requestedX, int requestedY)
    {
        var width = _gardenBitmap.PixelWidth * _scale;
        var height = _gardenBitmap.PixelHeight * _scale;
        return Forms.Screen.AllScreens.Select(screen =>
        {
            var area = screen.WorkingArea;
            var visibleWidth = Math.Min(MinimumVisiblePixels, width);
            var visibleHeight = Math.Min(MinimumVisiblePixels, height);
            var minX = area.Left + visibleWidth - width;
            var maxX = area.Right - visibleWidth;
            var minY = area.Top + visibleHeight - height;
            var maxY = area.Bottom - visibleHeight;
            var x = Math.Clamp(requestedX, minX, maxX);
            var y = Math.Clamp(requestedY, minY, maxY);
            var dx = (long)x - requestedX;
            var dy = (long)y - requestedY;
            return (Position: new System.Drawing.Point(x, y), Distance: dx * dx + dy * dy);
        }).MinBy(candidate => candidate.Distance).Position;
    }

    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static long GetDpiForWindow(IntPtr hwnd) => GetDpiForWindowNative(hwnd);
    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));
    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")] private static extern uint GetDpiForWindowNative(IntPtr hwnd);

    private sealed record GrassRow(BitmapSource Bitmap, int Top, int Bottom, int Index);
    private readonly record struct GrassZone(Point Center, int RowIndex, double FootY);
    private enum EncounterState { Idle, Alert, Appearing, WaitingForInteraction }
    private enum CaptureVisualStage { None, Flight, Falling, Shaking, OpeningFailure, Success, Fleeing }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint
    {
        public int X;
        public int Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }
}
