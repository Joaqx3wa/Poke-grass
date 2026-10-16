using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Brushes = System.Windows.Media.Brushes;

namespace PokeGrass;

/// <summary>A single transparent, click-through-unless-on-pixel desktop companion.</summary>
internal sealed class DesktopCompanionWindow : Window
{
    private static readonly IReadOnlyDictionary<CompanionEmotion, BitmapSource> ReactionSprites =
        Enum.GetValues<CompanionEmotion>().ToDictionary(emotion => emotion, LoadReactionSpriteCore);
    private static readonly IReadOnlyList<BitmapSource> ShinySparkleFrames =
        ShinySparkFrames.Load(relative => LoadBitmap(Path.Combine(AppContext.BaseDirectory, "assets", relative.Replace('/', Path.DirectorySeparatorChar))));
    private static readonly IReadOnlyList<BitmapSource> MegaAuraAnimationFrames =
        MegaAuraFrames.Load(Path.Combine(AppContext.BaseDirectory, MegaAuraFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
    private static readonly IReadOnlyList<BitmapSource> MegaBubbleAnimationFrames =
        MegaBubbleFrames.Load(Path.Combine(AppContext.BaseDirectory, MegaBubbleFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
    private static readonly IReadOnlyList<BitmapSource> DittoTransformAnimationFrames =
        DittoTransformFrames.Load(Path.Combine(AppContext.BaseDirectory, DittoTransformFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
    private static readonly IReadOnlyList<BitmapSource> AlphaEffectAnimationFrames =
        AlphaEffectFrames.Load(Path.Combine(AppContext.BaseDirectory, AlphaEffectFrames.CompanionAssetPath.Replace('/', Path.DirectorySeparatorChar)), AlphaEffectFrames.CompanionCount);
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;
    private const int GwlExStyle = -20;
    private const int WhMouseLowLevel = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;
    private const uint GwOwner = 4;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static IntPtr _outsideClickHook;
    private static IntPtr _openContextMenuHandle;
    private static DesktopCompanionWindow? _openContextMenuOwner;
    private static readonly LowLevelMouseProc OutsideClickHookProc = OnOutsideContextMenuClick;
    private const int ReactionCanvasTop = 50;
    internal const int DittoTransformCanvasTopPadding = 24;
    private const double DittoTransformRaisePixels = 10d;
    private const double DittoTransformLeftOffsetPixels = 8d;
    private const int ReactionSpriteSize = 48;
    private const double EvolutionEffectHeight = 120d;
    private const int ShinySparkleCanvasWidth = 192;
    private const int ShinySparkleBottomPadding = 96;
    private const int SleepChanceOneIn = 180;
    private const int MinSleepSeconds = 50;
    private const int MaxSleepSecondsExclusive = 71;
    private const int SocialCheckMinSeconds = 8;
    private const int SocialCheckMaxSecondsExclusive = 16;
    private const int SocialRangePixels = 120;
    private const int SocialCooldownMinSeconds = 18;
    private const int SocialCooldownMaxSecondsExclusive = 29;
    private const int PositivePairNegativeImmunitySeconds = 60;
    private const int NegativePairPositiveCooldownSeconds = 20;
    private const int CollisionFleeChancePercent = 25;
    private const int MinCollisionFleeMilliseconds = 3000;
    private const int MaxCollisionFleeMillisecondsExclusive = 4501;
    private const int CollisionReactionChancePercent = 35;
    private const int CollisionReactionCooldownSeconds = 2;
    private const double CollisionFleeSpeedMultiplier = 5.0;
    private const double WanderEdgeAvoidancePixels = 150d;
    private const int EdgeDizzyMilliseconds = 1500;
    private const int EdgeRunMilliseconds = 1900;
    private const double RallyRunSpeedMultiplier = 3.6d;
    private const double SoloToyRunSpeedMultiplier = CollisionFleeSpeedMultiplier * .85d;
    private const int SoloToyRunFrameMilliseconds = 85;
    private const double ToyChaseNoProgressLimitSeconds = 5d;
    private const double CardinalStairSegmentPixels = 128d;

    private readonly GameSaveStore _save;
    private readonly CapturedPokemon _capture;
    private PokemonEntry _entry;
    private readonly Action<DesktopCompanionWindow> _returned;
    private readonly Func<DesktopCompanionWindow, Point, bool> _tryReturnToPcOnDrop;
    private readonly Action<DesktopCompanionWindow> _restoreOverlayZOrder;
    private readonly Func<string, string?, bool> _feedBerry;
    private readonly Func<IReadOnlyList<DesktopCompanionWindow>> _getCompanions;
    private readonly Action<DesktopCompanionWindow> _reunirEquipo;
    private readonly Action<DesktopCompanionWindow> _toggleToy;
    private readonly Func<bool> _isToyVisible;
    private readonly Action<DesktopCompanionWindow> _cancelRally;
    private readonly Action<DesktopCompanionWindow> _cancelToyChase;
    private readonly Action<DesktopCompanionWindow, Point> _toyPushed;
    private readonly Func<Rect> _getToyBounds;
    private readonly Func<double> _getGroupSpacing;
    private readonly PokemonSpeciesMetadataCatalog? _speciesMetadata;
    private readonly PokemonCatalog? _pokemonCatalog;
    private readonly Action<DesktopCompanionWindow, DesktopCompanionWindow> _reportBattleChance;
    private readonly Action<DesktopCompanionWindow> _cancelBattle;
    private readonly Func<string, bool> _applyPermanentEvolution;
    private readonly Func<DesktopCompanionWindow, DesktopCompanionWindow, bool> _tryCouplesEncounter;
    private readonly MegaEvolutionCatalog _megaEvolutions;
    private readonly Func<string?, bool> _changeMegaForm;
    private readonly Image _sprite = new() { Stretch = Stretch.None, IsHitTestVisible = true };
    private readonly TranslateTransform _rallySpriteTransform = new();
    private readonly Image _shadow = new() { Stretch = Stretch.None, IsHitTestVisible = false };
    private readonly Image _megaAuraImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Opacity = .64 };
    private readonly Image _megaBubbleImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Image _dittoTransformImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Image _evolutionImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Image _shinySparkleImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Image _alphaEffectImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Image _reactionImage = new() { Width = ReactionSpriteSize, Height = ReactionSpriteSize, Stretch = Stretch.None, IsHitTestVisible = false };
    private readonly Border _berryRequestBubble = new()
    {
        Width = 54, Height = 50, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
        BorderBrush = Brushes.Black, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 244)),
        Visibility = Visibility.Collapsed, IsHitTestVisible = false, Child = new Image
        {
            Width = 36, Height = 36, Stretch = Stretch.None, IsHitTestVisible = false
        }
    };
    private readonly Canvas _canvas = new();
    private readonly BitmapSource[,] _frames = new BitmapSource[4, 4];
    private BitmapSource[,]? _dittoOriginalFrames;
    private BitmapSource[,]? _dittoTargetFrames;
    private BitmapSource[,]? _evolutionOriginalFrames;
    private BitmapSource[,]? _evolutionTargetFrames;
    private IReadOnlyList<BitmapSource>? _evolutionAnimationFrames;
    private PokemonSpeciesMetadata? _evolutionTargetMetadata;
    private IReadOnlyList<string> _dittoCopiedTypes = Array.Empty<string>();
    private string? _dittoCopiedRecordId;
    private string? _dittoReturnReason;
    private DateTime _nextDittoAmbientCheck;
    private DateTime _dittoReturnAt;
    private bool _dittoTransformActive;
    private bool _dittoTransformReversing;
    private bool _dittoTransformed;
    private int _dittoTransformFrame = -1;
    private int _evolutionFrame = -1;
    private bool _evolutionPromptPending;
    private bool _evolutionActive;
    private readonly bool[][] _alphaMasks = new bool[16][];
    private readonly Int32Rect[,] _visibleFrameBounds = new Int32Rect[4, 4];
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _megaAuraTimer;
    private readonly DispatcherTimer _megaBubbleTimer;
    private readonly DispatcherTimer _dittoTransformTimer;
    private readonly DispatcherTimer _evolutionTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new();
    private readonly Dictionary<string, DateTime> _positiveInteractionUntil = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _negativeInteractionUntil = new(StringComparer.Ordinal);
    private readonly EncounterAudio _audio = new();
    private readonly Forms.Screen _primaryScreen;
    private HwndSource? _source;
    private int _row;
    private int _column;
    private bool _pinned;
    private bool _persistPositionOnClose = true;
    private bool _walking;
    private bool _avoidanceDirectionPending;
    private bool _fleeingAfterCollision;
    private bool _edgeRunPending;
    private bool _dragPending;
    private bool _dragging;
    private bool _wasSleepingOnPress;
    private Point _dragStartScreen;
    private Point _windowStartScreen;
    private Point _direction;
    private Point _edgeRunDirection;
    private double _speed;
    private double _positionX;
    private double _positionY;
    private DateTime _nextDecision;
    private DateTime _faceUserUntil;
    private DateTime _lastPersistence;
    private DateTime _lastTick;
    private int _lastFrame = -1;
    private readonly int _frameWidth;
    private readonly int _frameHeight;
    private readonly int _canvasWidth;
    private readonly double _baseCanvasHeight;
    private double _visualScale = 1d;
    private readonly int _spriteOffsetX;
    private readonly int _shadowHeight;
    private readonly ContextMenu _contextMenu;
    private readonly MenuItem _pinMenuItem;
    private readonly MenuItem _megaMenuItem;
    private readonly MenuItem _rallyMenuItem;
    private readonly MenuItem _toyMenuItem;
    private readonly Point? _dropCenter;
    private Forms.Screen[] _screens = Forms.Screen.AllScreens;
    private DateTime _nextScreenRefresh;
    private DateTime _nextSocialDecision;
    private DateTime _nextCollisionReactionCheck;
    private DateTime _reactionUntil;
    private DateTime _edgeStunnedUntil;
    private DateTime _feedingReactionUntil;
    private DateTime _sleepUntil;
    private DateTime _interactionUntil;
    private DateTime _nextShinySparkle;
    private DateTime _nextShinySparkleFrame;
    private DateTime _nextMegaAura;
    private DateTime _megaWalkResumeAt;
    private DateTime _nextAlphaEffect;
    private DateTime _nextAlphaEffectFrame;
    private string? _interactionPartnerId;
    private string? _requestedBerryId;
    private Point? _rallyTarget;
    private Point? _rallyDetourTarget;
    private DateTime _rallyLastProgressAt;
    private int _rallyBlockedAttempts;
    private bool _rallyArrived;
    private Point? _toyTarget;
    private bool? _rallyHorizontalFirst;
    private bool? _toyChaseHorizontalFirst;
    private double _rallyAxisRemaining;
    private double _toyChaseAxisRemaining;
    private bool _toyPlayParticipant;
    private bool _toyWaitingForPass;
    private bool _toyChaseIsRunning;
    private bool _toyAwaitingBallSeparation;
    private Point _toySeparationOrigin;
    private DateTime _rallyUntil;
    private DateTime _nextRallyJumpAt;
    private DateTime _rallyJumpStartedAt;
    private DateTime _couplesJumpStartedAt;
    private double _couplesJumpHeight;
    private DateTime _berryRequestUntil;
    private DateTime _battleCelebrationStartedAt;
    private DateTime _battleCelebrationUntil;
    private DateTime _battleEvacuationUntil;
    private DateTime _battleEvacuationLastProgressAt;
    private Point _battleEvacuationCenter;
    private Point _battleEvacuationLastCenter;
    private double _battleEvacuationSafeRadius;
    private DateTime _toyRetryAfter;
    private DateTime _toyLastProgressAt;
    private double _toyBestDistance = double.PositiveInfinity;
    private bool _isRallyParticipant;
    private bool _rallyApproachFailed;
    private bool _rallyEventActive;
    private bool _rallyEventSuppressReactions;
    private bool _rallyCelebrating;
    private bool _battleParticipant;
    private bool _couplesParticipant;
    private string? _couplesPartnerId;
    private bool _battlePreparing;
    private string? _battlePartnerId;
    private double _battleTargetCenterX;
    private double _battleTargetCenterY;
    private CompanionEmotion? _activeEmotion;
    private int _shinySparkleFrame = -1;
    private int _megaAuraFrame = -1;
    private int _megaBubbleFrame = -1;
    private int _megaBubbleProgress;
    private bool _megaTransformationActive;
    private bool _megaTransformationReversing;
    private bool _megaAuraReversing;
    private BitmapSource[,]? _megaTransitionFrames;
    private PokemonEntry? _megaTransitionEntry;
    private int _alphaEffectFrame = -1;

    internal string RecordId => _capture.RecordId;
    private string BattleMetadataId => _capture.MegaFormId is { } megaFormId
        && _megaEvolutions.TryGetForm(megaFormId, out _)
            ? _megaEvolutions.GetBasePokemonIdForForm(megaFormId) : _entry.Id;
    internal string BattleSpeciesId => _speciesMetadata?.Get(BattleMetadataId).SpeciesId ?? BattleMetadataId.ToLowerInvariant();
    internal string BattleFormId => _capture.MegaFormId ?? _entry.Id;
    internal bool IsMegaEvolved => _capture.MegaFormId is { } megaFormId
        && _megaEvolutions.TryGetForm(megaFormId, out _);
    internal IReadOnlyList<string> BattleTypes => _dittoTransformed
        ? _dittoCopiedTypes
        : _speciesMetadata is null ? Array.Empty<string>()
        : BattleEffectCatalog.GetTypesForForm(BattleFormId, _speciesMetadata.Get(BattleMetadataId).Types);
    internal bool IsDitto => _capture.Id.Equals("DITTO", StringComparison.OrdinalIgnoreCase);
    private int SpriteCanvasTop => ReactionCanvasTop + (IsDitto ? DittoTransformCanvasTopPadding : 0);
    internal bool IsDittoTransforming => _dittoTransformActive;
    internal bool IsDittoTransformed => _dittoTransformed;
    internal bool IsEvolutionPendingOrActive => _evolutionPromptPending || _evolutionActive;
    internal bool IsPinned => _pinned;
    internal bool IsBattleParticipant => _battleParticipant;
    internal bool IsBattleCelebrating => _battleCelebrationStartedAt != DateTime.MinValue;
    internal string CouplesSpeciesId => _speciesMetadata?.Get(BattleMetadataId).SpeciesId ?? BattleMetadataId.ToLowerInvariant();
    internal string CouplesSex => _speciesMetadata is null ? string.Empty : PokemonSex.Get(_speciesMetadata.GetForCapture(_capture.Id));
    internal bool CanBeginCouples => IsVisible && !_dragPending && !_dragging
        && !_battleParticipant && !_couplesParticipant && !_megaTransformationActive
        && !_dittoTransformActive && (!IsDitto || !_dittoTransformed)
        && !_isRallyParticipant && !_rallyEventActive && !_toyPlayParticipant && !_toyTarget.HasValue
        && !_toyWaitingForPass && _interactionPartnerId is null && _requestedBerryId is null
        && (_activeEmotion is null || _pinned && _activeEmotion == CompanionEmotion.Sleep)
        && (_pinned || DateTime.UtcNow >= _sleepUntil) && DateTime.UtcNow >= _feedingReactionUntil;
    internal bool IsCouplesParticipant => _couplesParticipant;
    internal bool CanContinueCouples => _couplesParticipant && IsVisible && !_dragPending
        && !_dragging && !_megaTransformationActive && !_battleParticipant && !_rallyEventActive
        && !_toyTarget.HasValue && !_toyPlayParticipant && _requestedBerryId is null
        && DateTime.UtcNow >= _feedingReactionUntil;
    internal bool CanBeginFriendlyBattle => IsVisible && !_pinned && !_dragPending && !_dragging
        && !_battleParticipant && !_megaTransformationActive && !_dittoTransformActive && !_evolutionPromptPending
        && !_evolutionActive && _battleCelebrationStartedAt == DateTime.MinValue && !_isRallyParticipant && !_rallyEventActive
        && !_toyPlayParticipant && !_toyTarget.HasValue && !_toyWaitingForPass && _interactionPartnerId is null
        && _requestedBerryId is null
        && (_activeEmotion is null || _activeEmotion == CompanionEmotion.Angry && DateTime.UtcNow < _reactionUntil)
        && !_fleeingAfterCollision
        && DateTime.UtcNow >= _sleepUntil && DateTime.UtcNow >= _feedingReactionUntil;
    internal bool IsRallyParticipant => _isRallyParticipant;
    internal bool IsRallyArrived => _isRallyParticipant && _rallyArrived;
    internal bool IsNearRallyTarget
    {
        get
        {
            if (_rallyTarget is not { } target) return _rallyArrived;
            var body = VisibleBodyScreenBounds;
            if (body.IsEmpty) return false;
            var center = new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
            return (center - target).Length <= Math.Max(28d * _visualScale, Math.Max(body.Width, body.Height) * .35d);
        }
    }
    internal bool CanJoinTeamRally => IsVisible && !_dragPending && !_dragging && !_battleParticipant && _interactionPartnerId is null;

    internal void SetRallyEventActive(bool active)
    {
        _rallyEventActive = active;
        _rallyEventSuppressReactions = active;
    }
    internal bool CanPlayWithToy => IsVisible && !_pinned && !_dragPending && !_battleParticipant && !_toyPlayParticipant
        && _interactionPartnerId is null && _toyTarget is null && DateTime.UtcNow >= _toyRetryAfter
        && DateTime.UtcNow >= _sleepUntil && DateTime.UtcNow >= _faceUserUntil && DateTime.UtcNow >= _reactionUntil;
#if DEBUG
    internal void ShowSparklePreviewForDebug()
    {
        ActivateReaction(CompanionEmotion.Heart, TimeSpan.FromSeconds(2.5));
        StartShinySparkles(restart: true);
    }
    internal void ShowMegaAuraPreviewForDebug()
    {
        ActivateReaction(CompanionEmotion.Heart, TimeSpan.FromSeconds(2.5));
        StartMegaAura(restart: true);
    }
    internal void SaveMegaAuraPreviewForDebug(string path) => SaveEffectPreviewForDebug(path);
    internal void PrepareSizeComparisonForDebug()
    {
        SetPinned(true);
        _row = 0;
        _column = 0;
        _alphaEffectFrame = -1;
        _alphaEffectImage.Source = null;
        _alphaEffectImage.Visibility = Visibility.Collapsed;
        _megaAuraFrame = -1;
        _megaAuraImage.Source = null;
        _megaAuraImage.Visibility = Visibility.Collapsed;
        _shinySparkleFrame = -1;
        _shinySparkleImage.Source = null;
        _shinySparkleImage.Visibility = Visibility.Collapsed;
        _reactionImage.Source = null;
        _reactionImage.Visibility = Visibility.Collapsed;
        _megaAuraTimer.Stop();
        RenderFrame();
    }
    internal void ShowAlphaPreviewForDebug(bool includeOtherEffects = true)
    {
        ActivateReaction(CompanionEmotion.Heart, TimeSpan.FromSeconds(2.5));
        StartAlphaEffect(restart: true);
        if (includeOtherEffects)
        {
            StartShinySparkles(restart: true);
            StartMegaAura(restart: true);
        }
        else
        {
            _shinySparkleFrame = -1;
            _shinySparkleImage.Source = null;
            _shinySparkleImage.Visibility = Visibility.Collapsed;
            _megaAuraFrame = -1;
            _megaAuraImage.Source = null;
            _megaAuraImage.Visibility = Visibility.Collapsed;
            _megaAuraTimer.Stop();
        }
    }
    internal void SaveAlphaPreviewForDebug(string path) => SaveEffectPreviewForDebug(path);
    private void SaveEffectPreviewForDebug(string path)
    {
        var previous = _canvas.Background;
        _canvas.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 32, 39));
        _canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(_canvas.Width)),
            Math.Max(1, (int)Math.Ceiling(_canvas.Height)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_canvas);
        bitmap.Freeze();
        _canvas.Background = previous;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
#endif
    internal void SuppressPersistenceOnClose() => _persistPositionOnClose = false;
    internal bool IsBeingDragged => _dragPending;
    internal Rect VisibleBodyScreenBounds
    {
        get
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var windowBounds)) return Rect.Empty;
            var bounds = _visibleFrameBounds[_row, _column];
            var dpi = GetDpiScale();
            return CompanionCollisionGeometry.ToScreenRect(windowBounds.Left, windowBounds.Top, _spriteOffsetX,
                SpriteCanvasTop, bounds, dpi, _visualScale);
        }
    }

    // Keep toy contact on the visible body/legs rather than the full sprite
    // silhouette. Reactions are rendered separately above the sprite and must
    // never participate in the toy's physical collision.
    internal Rect ToyContactScreenBounds
    {
        get
        {
            var body = VisibleBodyScreenBounds;
            if (body.IsEmpty) return Rect.Empty;
            var insetX = body.Width * .12d;
            var insetTop = body.Height * .28d;
            var insetBottom = body.Height * .08d;
            return new Rect(body.X + insetX, body.Y + insetTop,
                Math.Max(1d, body.Width - insetX * 2d),
                Math.Max(1d, body.Height - insetTop - insetBottom));
        }
    }

    internal DesktopCompanionWindow(GameSaveStore save, CapturedPokemon capture, PokemonEntry entry,
        double x, double y, bool isPinned, Action<DesktopCompanionWindow> returned, Point? dropCenter = null,
        Func<IReadOnlyList<DesktopCompanionWindow>>? getCompanions = null, Func<string, string?, bool>? feedBerry = null,
        MegaEvolutionCatalog? megaEvolutions = null, Func<string?, bool>? changeMegaForm = null,
        Action<DesktopCompanionWindow>? reunirEquipo = null, Action<DesktopCompanionWindow>? toggleToy = null,
        Func<bool>? isToyVisible = null, Action<DesktopCompanionWindow>? cancelRally = null,
        Action<DesktopCompanionWindow>? cancelToyChase = null, Action<DesktopCompanionWindow, Point>? toyPushed = null,
        Func<Rect>? getToyBounds = null, Func<double>? getGroupSpacing = null,
        PokemonSpeciesMetadataCatalog? speciesMetadata = null,
        Action<DesktopCompanionWindow, DesktopCompanionWindow>? reportBattleChance = null,
        Action<DesktopCompanionWindow>? cancelBattle = null,
        Func<DesktopCompanionWindow, DesktopCompanionWindow, bool>? tryCouplesEncounter = null,
        Func<DesktopCompanionWindow, Point, bool>? tryReturnToPcOnDrop = null,
        Action<DesktopCompanionWindow>? restoreOverlayZOrder = null,
        PokemonCatalog? pokemonCatalog = null,
        Func<string, bool>? applyPermanentEvolution = null)
    {
        _save = save;
        _capture = capture;
        _entry = entry;
        _returned = returned;
        _tryReturnToPcOnDrop = tryReturnToPcOnDrop ?? ((_, _) => false);
        _restoreOverlayZOrder = restoreOverlayZOrder ?? (_ => { });
        _feedBerry = feedBerry ?? ((_, _) => false);
        _getCompanions = getCompanions ?? (() => [this]);
        _reunirEquipo = reunirEquipo ?? (_ => { });
        _toggleToy = toggleToy ?? (_ => { });
        _isToyVisible = isToyVisible ?? (() => false);
        _cancelRally = cancelRally ?? (_ => { });
        _cancelToyChase = cancelToyChase ?? (_ => { });
        _toyPushed = toyPushed ?? ((_, _) => { });
        _getToyBounds = getToyBounds ?? (() => Rect.Empty);
        _getGroupSpacing = getGroupSpacing ?? (() => 192d);
        _speciesMetadata = speciesMetadata;
        _pokemonCatalog = pokemonCatalog;
        _reportBattleChance = reportBattleChance ?? ((_, _) => { });
        _cancelBattle = cancelBattle ?? (_ => { });
        _applyPermanentEvolution = applyPermanentEvolution ?? (_ => false);
        _tryCouplesEncounter = tryCouplesEncounter ?? ((_, _) => false);
        _megaEvolutions = megaEvolutions ?? MegaEvolutionCatalog.Empty;
        _changeMegaForm = changeMegaForm ?? (_ => false);
        _positionX = x;
        _positionY = y;
        _pinned = isPinned;
        _dropCenter = dropCenter;

        var relativePath = capture.IsShiny ? entry.WalkingShiny : entry.WalkingNormal;
        var sheetPath = Path.Combine(AppContext.BaseDirectory, "assets", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var sheet = LoadBitmap(sheetPath);
        if (sheet.PixelHeight % 4 != 0)
            throw new InvalidDataException($"La cuadrícula de caminata de {entry.Id} no se puede dividir en 4×4: {sheet.PixelWidth}×{sheet.PixelHeight}.");

        var xEdges = Enumerable.Range(0, 5).Select(index => index * sheet.PixelWidth / 4).ToArray();
        var yEdges = Enumerable.Range(0, 5).Select(index => index * sheet.PixelHeight / 4).ToArray();
        var baseFrameWidth = Enumerable.Range(0, 4).Max(index => xEdges[index + 1] - xEdges[index]);
        var baseFrameHeight = sheet.PixelHeight / 4;
        var sizeMultiplier = 1.5 * (capture.IsAlpha ? 1.25 : 1d);
        _frameWidth = (int)Math.Round(baseFrameWidth * sizeMultiplier, MidpointRounding.AwayFromZero);
        _frameHeight = (int)Math.Round(baseFrameHeight * sizeMultiplier, MidpointRounding.AwayFromZero);
        var shadowVariant = baseFrameWidth < 80 ? 1 : baseFrameWidth < 112 ? 2 : 3;
        var shadowSource = LoadBitmap(Path.Combine(AppContext.BaseDirectory, "assets", "pokemon", "pokemons_shadow", $"{shadowVariant}.png"));
        var shadowScale = 0.75 * (capture.IsAlpha ? 1.25 : 1d);
        _shadow.Width = (int)Math.Round(shadowSource.PixelWidth * shadowScale, MidpointRounding.AwayFromZero);
        _shadow.Height = _shadowHeight = (int)Math.Round(shadowSource.PixelHeight * shadowScale, MidpointRounding.AwayFromZero);
        _shadow.Source = ScaleNearestNeighbor(shadowSource, (int)_shadow.Width, (int)_shadow.Height);
        var needsWideEffectCanvas = capture.IsShiny || capture.MegaFormId is not null || capture.IsAlpha || IsDitto;
        var effectCanvasWidth = IsDitto ? 240 : ShinySparkleCanvasWidth;
        _canvasWidth = Math.Max(Math.Max(_frameWidth, (int)_shadow.Width), needsWideEffectCanvas ? effectCanvasWidth : 0);
        _spriteOffsetX = (_canvasWidth - _frameWidth) / 2;
        _canvas.Width = _canvasWidth;
        _canvas.Height = _frameHeight + SpriteCanvasTop + (needsWideEffectCanvas ? ShinySparkleBottomPadding : 0);
        _baseCanvasHeight = _canvas.Height;
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
        {
            var rect = new Int32Rect(xEdges[column], yEdges[row], xEdges[column + 1] - xEdges[column], yEdges[row + 1] - yEdges[row]);
            var originalFrame = PadFrame(new CroppedBitmap(sheet, rect), baseFrameWidth, baseFrameHeight);
            _frames[row, column] = ScaleNearestNeighbor(originalFrame, _frameWidth, _frameHeight);
        }
        if (IsDitto) _dittoOriginalFrames = CloneFrames(_frames);

        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
        {
            _alphaMasks[row * 4 + column] = BuildAlphaMask(_frames[row, column]);
            _visibleFrameBounds[row, column] = GetAlphaBounds(_alphaMasks[row * 4 + column], _frameWidth, _frameHeight);
        }
        Width = _canvasWidth;
        Height = _canvas.Height;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
        // Keep the Ditto sprite anchored where it was before widening its canvas for the transformation.
        if (IsDitto) _positionX -= _spriteOffsetX;
        Left = IsDitto ? x - _spriteOffsetX : x;
        Top = y;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Canvas.SetLeft(_shadow, (_canvasWidth - _shadow.Width) / 2);
        Canvas.SetTop(_shadow, SpriteCanvasTop);
        _canvas.Children.Add(_shadow);
        _sprite.RenderTransform = _rallySpriteTransform;
        Canvas.SetLeft(_sprite, _spriteOffsetX);
        Canvas.SetTop(_sprite, SpriteCanvasTop);
        _canvas.Children.Add(_sprite);
        _canvas.Children.Add(_alphaEffectImage);
        _canvas.Children.Add(_megaAuraImage);
        _canvas.Children.Add(_shinySparkleImage);
        _canvas.Children.Add(_megaBubbleImage);
        _canvas.Children.Add(_dittoTransformImage);
        _canvas.Children.Add(_evolutionImage);
        _canvas.Children.Add(_reactionImage);
        _canvas.Children.Add(_berryRequestBubble);
        Content = _canvas;
        UpdateVisualScale(DesktopVisualScaleService.Current, preserveAnchor: false);
        DesktopVisualScaleService.Changed += OnDesktopVisualScaleChanged;
        Closed += (_, _) => DesktopVisualScaleService.Changed -= OnDesktopVisualScaleChanged;
        RenderOptions.SetBitmapScalingMode(_sprite, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_sprite, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(_megaAuraImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_megaAuraImage, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(_megaBubbleImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_megaBubbleImage, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(_dittoTransformImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_dittoTransformImage, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(_evolutionImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_evolutionImage, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(_shinySparkleImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_shinySparkleImage, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(_alphaEffectImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_alphaEffectImage, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(_reactionImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_reactionImage, EdgeMode.Aliased);
        _sprite.Source = _frames[_row, _column];

        _contextMenu = new ContextMenu
        {
            Width = 196,
            MinWidth = 196,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HasDropShadow = true,
            StaysOpen = false
        };
        var listSprite = PixelButtonUi.Load("ui/panels/list_2_063.png");
        // The unused left gutter in the normal row makes the menu labels look off-center.
        // Crop it away while keeping the highlighted blue row intact.
        var normalRow = new CroppedBitmap(listSprite, new Int32Rect(40, 43, 170, 26));
        var selectedRow = new CroppedBitmap(listSprite, new Int32Rect(8, 70, 202, 36));
        normalRow.Freeze(); selectedRow.Freeze();
        _contextMenu.Resources[typeof(MenuItem)] = CreateCompanionMenuItemStyle(normalRow, selectedRow);
        _megaMenuItem = new MenuItem();
        _megaMenuItem.Click += (_, _) =>
        {
            if (_isRallyParticipant) _cancelRally(this);
            if (_capture.MegaFormId is not null) BeginMegaDevolution();
            else
            {
                var forms = _megaEvolutions.GetFormsFor(_capture);
                if (forms.Count == 1) BeginMegaEvolution(forms[0].FormId);
            }
        };
        _contextMenu.Items.Add(_megaMenuItem);
        _pinMenuItem = new MenuItem();
        _pinMenuItem.Click += (_, _) => SetPinned(!_pinned);
        _contextMenu.Items.Add(_pinMenuItem);
        _rallyMenuItem = new MenuItem { Header = "Reunir equipo" };
        _rallyMenuItem.Click += (_, _) => _reunirEquipo(this);
        _contextMenu.Items.Add(_rallyMenuItem);
        _toyMenuItem = new MenuItem();
        _toyMenuItem.Click += (_, _) => _toggleToy(this);
        _contextMenu.Items.Add(_toyMenuItem);
        var returnMenuItem = new MenuItem { Header = "Devolver a la PC" };
        returnMenuItem.Click += (_, _) => ReturnToPc();
        _contextMenu.Items.Add(returnMenuItem);
        _contextMenu.Opened += (_, _) =>
        {
            _pinMenuItem.Header = _pinned ? "Desfijar" : "Fijar";
            _toyMenuItem.Header = _isToyVisible() ? "Guardar juguete" : "Sacar juguete";
            _pinMenuItem.IsEnabled = !_megaTransformationActive;
            _toyMenuItem.IsEnabled = !_megaTransformationActive;
            _rallyMenuItem.IsEnabled = !_megaTransformationActive;
            RefreshMegaMenu();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                RaiseContextMenuAboveOverlays();
                StartOutsideContextMenuClickMonitor();
            }));
        };
        _contextMenu.Closed += (_, _) => StopOutsideContextMenuClickMonitor();
        _sprite.ContextMenu = _contextMenu;

        _sprite.PreviewMouseLeftButtonDown += OnLeftButtonDown;
        _sprite.PreviewMouseMove += OnMouseMove;
        _sprite.PreviewMouseLeftButtonUp += OnLeftButtonUp;
        _sprite.PreviewMouseRightButtonUp += (_, e) => { _contextMenu.IsOpen = true; e.Handled = true; };
        DragOver += OnBerryDragOver;
        Drop += OnBerryDrop;

        _primaryScreen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.First();
        _speed = 42d * Math.Clamp(64d / _frameWidth, .45d, 1.2d);
        _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(700, 1800));
        _nextSocialDecision = DateTime.UtcNow.AddSeconds(_random.Next(4, 9));
        _nextShinySparkle = DateTime.UtcNow.AddSeconds(_random.Next(28, 51));
        _nextMegaAura = DateTime.UtcNow.AddSeconds(_random.Next(24, 43));
        _lastTick = DateTime.UtcNow;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTimerTick;
        _megaAuraTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(MegaAuraFrames.FrameMilliseconds) };
        _megaAuraTimer.Tick += OnMegaAuraTick;
        _megaBubbleTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(MegaBubbleFrames.FrameMilliseconds) };
        _megaBubbleTimer.Tick += OnMegaBubbleTick;
        _dittoTransformTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(DittoTransformFrames.FrameMilliseconds) };
        _dittoTransformTimer.Tick += OnDittoTransformTick;
        _evolutionTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(EvolutionAnimationFrames.FrameMilliseconds) };
        _evolutionTimer.Tick += OnEvolutionTick;
        _nextDittoAmbientCheck = DateTime.UtcNow.AddSeconds(_random.Next(60, 121));

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ClampToAvailableScreen();
            if (_pinned) ShowPinnedSleepReaction();
            _timer.Start();
            SavePosition();
            StartShinySparkles(restart: true);
            StartMegaAura(restart: true);
            StartAlphaEffect(restart: true);
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _lastTick = DateTime.UtcNow;
                if (_pinned) ShowPinnedSleepReaction();
                _timer.Start();
                if (_megaAuraFrame >= 0) _megaAuraTimer.Start();
                if (_megaTransformationActive) _megaBubbleTimer.Start();
                if (_dittoTransformActive) _dittoTransformTimer.Start();
                if (_evolutionActive) _evolutionTimer.Start();
            }
            else { _timer.Stop(); _megaAuraTimer.Stop(); _megaBubbleTimer.Stop(); _dittoTransformTimer.Stop(); _evolutionTimer.Stop(); CancelInteraction(); SetEmotion(null); _sleepUntil = DateTime.MinValue; }
        };
        Closed += (_, _) =>
        {
            CancelInteraction();
            _timer.Stop();
            _megaAuraTimer.Stop();
            _megaBubbleTimer.Stop();
            _dittoTransformTimer.Stop();
            _evolutionTimer.Stop();
            CancelDittoTransformation();
            CancelPermanentEvolution();
            _megaBubbleImage.Source = null;
            _megaBubbleImage.Visibility = Visibility.Collapsed;
            _dittoTransformImage.Source = null;
            _dittoTransformImage.Visibility = Visibility.Collapsed;
            _evolutionImage.Source = null;
            _evolutionImage.Visibility = Visibility.Collapsed;
            _audio.Dispose();
            if (_persistPositionOnClose) SavePosition();
            _source?.RemoveHook(WindowHook);
        };
    }

    private void RefreshMegaMenu()
    {
        _megaMenuItem.Items.Clear();
        _megaMenuItem.IsEnabled = !_megaTransformationActive;
        if (_capture.MegaFormId is not null)
        {
            _megaMenuItem.Header = "Desmegaevolucionar";
            _megaMenuItem.Visibility = Visibility.Visible;
            return;
        }

        var forms = _megaEvolutions.GetFormsFor(_capture);
        _megaMenuItem.Visibility = forms.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _megaMenuItem.Header = "Megaevolucionar";
        if (forms.Count <= 1) return;
        foreach (var form in forms)
        {
            var option = new MenuItem { Header = form.DisplayName, Tag = form.FormId };
            option.Click += (_, _) => BeginMegaEvolution(form.FormId);
            _megaMenuItem.Items.Add(option);
        }
    }

    private void BeginMegaEvolution(string formId)
    {
        _cancelBattle(this);
        if (_megaTransformationActive || _capture.MegaFormId is not null
            || !_megaEvolutions.TryGetForm(formId, out var form)
            || !form.BasePokemonId.Equals(_capture.Id, StringComparison.OrdinalIgnoreCase)) return;

        BitmapSource[,] transitionFrames;
        PokemonEntry megaEntry;
        try
        {
            megaEntry = _megaEvolutions.GetEntry(formId);
            transitionFrames = LoadMegaTransitionFrames(megaEntry);
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"mega-transition-sprites:{formId}", exception);
            return;
        }

        // Persist before playing so an interrupted animation still restores as
        // Mega after closing the window or restarting the application.
        if (!_changeMegaForm(formId)) return;
        _capture.MegaFormId = formId;
        _megaTransitionEntry = megaEntry;
        _megaTransitionFrames = transitionFrames;
        _cancelRally(this);
        _cancelToyChase(this);
        _toyTarget = null;
        _toyPlayParticipant = false;
        _toyWaitingForPass = false;
        CancelInteraction();
        _sleepUntil = DateTime.MinValue;
        SetEmotion(null);
        _walking = false;
        _direction = new Point(0, 1);
        _row = 0;
        _column = 0;
        _sprite.Opacity = 1;
        _lastFrame = -1;
        _nextDecision = DateTime.MaxValue;
        _faceUserUntil = DateTime.MinValue;
        _megaWalkResumeAt = DateTime.MaxValue;
        RenderFrame();
        _megaAuraTimer.Stop();
        _megaAuraFrame = -1;
        _megaAuraReversing = false;
        _megaAuraImage.Source = null;
        _megaAuraImage.Visibility = Visibility.Collapsed;
        _megaTransformationActive = true;
        _megaTransformationReversing = false;
        _megaBubbleProgress = 0;
        _megaBubbleFrame = 0;
        _megaBubbleImage.Source = MegaBubbleAnimationFrames[0];
        _megaBubbleImage.Opacity = 1;
        _megaBubbleImage.Visibility = Visibility.Visible;
        PositionMegaBubble();
        _megaMenuItem.IsEnabled = false;
        _pinMenuItem.IsEnabled = false;
        _toyMenuItem.IsEnabled = false;
        _rallyMenuItem.IsEnabled = false;
        PlayMegaEvolutionSounds();
        if (IsVisible) _megaBubbleTimer.Start();
    }

    private void BeginMegaDevolution()
    {
        _cancelBattle(this);
        if (_megaTransformationActive || _capture.MegaFormId is not { } formId) return;

        PokemonEntry baseEntry;
        BitmapSource[,] transitionFrames;
        try
        {
            baseEntry = _megaEvolutions.GetBaseEntryForForm(formId);
            transitionFrames = LoadMegaTransitionFrames(baseEntry);
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"mega-detransition-sprites:{formId}", exception);
            return;
        }

        _cancelRally(this);
        _cancelToyChase(this);
        _toyTarget = null;
        _toyPlayParticipant = false;
        _toyWaitingForPass = false;
        CancelInteraction();
        _sleepUntil = DateTime.MinValue;
        SetEmotion(null);
        _walking = false;
        _direction = new Point(0, 1);
        _row = 0;
        _column = 0;
        _sprite.Opacity = 1;
        _lastFrame = -1;
        _nextDecision = DateTime.MaxValue;
        _faceUserUntil = DateTime.MinValue;
        _megaWalkResumeAt = DateTime.MaxValue;
        _megaTransitionEntry = baseEntry;
        _megaTransitionFrames = transitionFrames;
        RenderFrame();

        _megaTransformationActive = true;
        _megaTransformationReversing = true;
        _megaBubbleProgress = 0;
        _megaBubbleFrame = -1;
        _megaMenuItem.IsEnabled = false;
        _pinMenuItem.IsEnabled = false;
        _toyMenuItem.IsEnabled = false;
        _rallyMenuItem.IsEnabled = false;
        _megaAuraTimer.Stop();
        _megaAuraReversing = true;
        _megaAuraFrame = MegaAuraAnimationFrames.Count - 1;
        _megaAuraImage.Source = MegaAuraAnimationFrames[_megaAuraFrame];
        _megaAuraImage.Visibility = Visibility.Visible;
        PositionMegaAura();
        PlayMegaEvolutionSounds();
        if (IsVisible) _megaAuraTimer.Start();
    }

    private BitmapSource[,] LoadMegaTransitionFrames(PokemonEntry entry)
    {
        var relativePath = _capture.IsShiny ? entry.WalkingShiny : entry.WalkingNormal;
        var sheet = LoadBitmap(Path.Combine(AppContext.BaseDirectory, "assets", relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (sheet.PixelHeight % 4 != 0)
            throw new InvalidDataException($"La cuadrícula Mega de {entry.Id} no se puede dividir en 4×4: {sheet.PixelWidth}×{sheet.PixelHeight}.");

        var xEdges = Enumerable.Range(0, 5).Select(index => index * sheet.PixelWidth / 4).ToArray();
        var yEdges = Enumerable.Range(0, 5).Select(index => index * sheet.PixelHeight / 4).ToArray();
        var baseWidth = Enumerable.Range(0, 4).Max(index => xEdges[index + 1] - xEdges[index]);
        var baseHeight = sheet.PixelHeight / 4;
        var fitScale = Math.Min(_frameWidth / (double)baseWidth, _frameHeight / (double)baseHeight);
        var fitWidth = Math.Max(1, (int)Math.Round(baseWidth * fitScale, MidpointRounding.AwayFromZero));
        var fitHeight = Math.Max(1, (int)Math.Round(baseHeight * fitScale, MidpointRounding.AwayFromZero));
        var result = new BitmapSource[4, 4];
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
        {
            var rect = new Int32Rect(xEdges[column], yEdges[row], xEdges[column + 1] - xEdges[column], yEdges[row + 1] - yEdges[row]);
            var cell = PadFrame(new CroppedBitmap(sheet, rect), baseWidth, baseHeight);
            var scaled = ScaleNearestNeighbor(cell, fitWidth, fitHeight);
            result[row, column] = PadFrame(scaled, _frameWidth, _frameHeight);
        }
        return result;
    }

    private void OnMegaBubbleTick(object? sender, EventArgs e)
    {
        if (!IsVisible || !_megaTransformationActive || _megaTransitionFrames is null)
        {
            _megaBubbleTimer.Stop();
            return;
        }

        if (_megaTransformationReversing)
        {
            if (_megaBubbleProgress >= MegaBubbleAnimationFrames.Count - 1)
            {
                CompleteMegaDevolutionAnimation();
                return;
            }

            _megaBubbleProgress++;
            _megaBubbleFrame = MegaBubbleAnimationFrames.Count - 1 - _megaBubbleProgress;
            _megaBubbleImage.Source = MegaBubbleAnimationFrames[_megaBubbleFrame];
            PositionMegaBubble();
            if (_megaBubbleProgress == 9) _sprite.Source = _megaTransitionFrames[0, 0];
            if (_megaBubbleProgress >= 9)
                _sprite.Opacity = .1d + (_megaBubbleProgress - 9) / 9d * .9d;
            return;
        }

        if (_megaBubbleFrame + 1 >= MegaBubbleAnimationFrames.Count)
        {
            CompleteMegaEvolutionAnimation();
            return;
        }

        _megaBubbleFrame++;
        _megaBubbleImage.Source = MegaBubbleAnimationFrames[_megaBubbleFrame];
        PositionMegaBubble();
        if (_megaBubbleFrame == 8) _sprite.Source = _megaTransitionFrames[0, 0];
        if (MegaBubbleFrames.MegaIsVisibleAt(_megaBubbleFrame))
            _sprite.Opacity = MegaBubbleFrames.MegaOpacityAt(_megaBubbleFrame);
    }

    private void CompleteMegaEvolutionAnimation()
    {
        _megaBubbleTimer.Stop();
        if (_megaTransitionFrames is not null)
        {
            _frames.CopyFrom(_megaTransitionFrames);
            for (var row = 0; row < 4; row++)
            for (var column = 0; column < 4; column++)
            {
                _alphaMasks[row * 4 + column] = BuildAlphaMask(_frames[row, column]);
                _visibleFrameBounds[row, column] = GetAlphaBounds(_alphaMasks[row * 4 + column], _frameWidth, _frameHeight);
            }
        }
        if (_megaTransitionEntry is not null) _entry = _megaTransitionEntry;
        _megaTransitionFrames = null;
        _megaTransitionEntry = null;
        _megaTransformationActive = false;
        _megaTransformationReversing = false;
        _megaBubbleFrame = -1;
        _megaBubbleImage.Source = null;
        _megaBubbleImage.Visibility = Visibility.Collapsed;
        _sprite.Opacity = 1;
        _row = 0;
        _column = 0;
        _lastFrame = -1;
        _walking = false;
        if (_pinned) ShowPinnedSleepReaction();
        RenderFrame();

        // The existing mega2 aura is the reveal effect. It is visual only;
        // the mega-evolution sound was already played once at bubble start.
        StartMegaAura(restart: true);
        _megaWalkResumeAt = DateTime.UtcNow.AddMilliseconds(
            MegaAuraFrames.Count * MegaAuraFrames.FrameMilliseconds);
    }

    private void CompleteMegaDevolutionAnimation()
    {
        _megaBubbleTimer.Stop();
        if (!_changeMegaForm(null))
        {
            EncounterDiagnostics.Write("mega-detransition", $"Could not persist normal form for {_capture.RecordId}; kept Mega form.");
            _megaBubbleFrame = -1;
            _megaBubbleImage.Source = null;
            _megaBubbleImage.Visibility = Visibility.Collapsed;
            _sprite.Opacity = 1;
            _megaTransformationActive = false;
            _megaTransformationReversing = false;
            _megaTransitionFrames = null;
            _megaTransitionEntry = null;
            _megaAuraReversing = false;
            _megaAuraFrame = -1;
            _megaAuraImage.Source = null;
            _megaAuraImage.Visibility = Visibility.Collapsed;
            _walking = false;
            _lastFrame = -1;
            _nextDecision = DateTime.UtcNow;
            StartMegaAura(restart: true);
            RefreshMegaMenu();
            RenderFrame();
            return;
        }

        _capture.MegaFormId = null;
        if (_megaTransitionFrames is not null)
        {
            _frames.CopyFrom(_megaTransitionFrames);
            for (var row = 0; row < 4; row++)
            for (var column = 0; column < 4; column++)
            {
                _alphaMasks[row * 4 + column] = BuildAlphaMask(_frames[row, column]);
                _visibleFrameBounds[row, column] = GetAlphaBounds(_alphaMasks[row * 4 + column], _frameWidth, _frameHeight);
            }
        }
        if (_megaTransitionEntry is not null) _entry = _megaTransitionEntry;
        _megaTransitionFrames = null;
        _megaTransitionEntry = null;
        _megaTransformationActive = false;
        _megaTransformationReversing = false;
        _megaAuraReversing = false;
        _megaBubbleFrame = -1;
        _megaBubbleImage.Source = null;
        _megaBubbleImage.Visibility = Visibility.Collapsed;
        _megaAuraFrame = -1;
        _megaAuraImage.Source = null;
        _megaAuraImage.Visibility = Visibility.Collapsed;
        _sprite.Opacity = 1;
        _row = 0;
        _column = 0;
        _lastFrame = -1;
        _walking = false;
        _nextDecision = DateTime.UtcNow;
        if (_pinned) ShowPinnedSleepReaction();
        RefreshMegaMenu();
        RenderFrame();
    }

    private void PositionMegaBubble()
    {
        if (_megaBubbleFrame < 0 || _megaBubbleImage.Source is not BitmapSource frame) return;
        var bounds = _visibleFrameBounds[_row, _column];
        if (bounds.IsEmpty) return;
        var centerX = _spriteOffsetX + bounds.X + bounds.Width / 2d;
        var centerY = SpriteCanvasTop + bounds.Y + bounds.Height / 2d;
        var aspectRatio = frame.PixelWidth / (double)frame.PixelHeight;
        var bubbleHeight = Math.Clamp(Math.Max(bounds.Width, bounds.Height) * 1.45d * 1.25d * 1.25d, 80d, 312.5d);
        var maxHeightByWidth = Math.Min(centerX, _canvasWidth - centerX) * 2d / aspectRatio;
        var maxHeightByHeight = Math.Min(centerY, _canvas.Height - centerY) * 2d;
        bubbleHeight = Math.Min(bubbleHeight, Math.Min(maxHeightByWidth, maxHeightByHeight));
        var bubbleWidth = bubbleHeight * aspectRatio;
        _megaBubbleImage.Width = bubbleWidth;
        _megaBubbleImage.Height = bubbleHeight;
        Canvas.SetLeft(_megaBubbleImage, centerX - bubbleWidth / 2d);
        Canvas.SetTop(_megaBubbleImage, centerY - bubbleHeight / 2d);
    }

    private void PositionDittoTransform()
    {
        if (_dittoTransformImage.Source is not BitmapSource frame) return;
        var bounds = _visibleFrameBounds[_row, _column];
        if (bounds.IsEmpty) return;
        // Keep the effect anchored and sized to Ditto's original sprite. The
        // copied Pokémon may have a much larger or smaller visible footprint.
        var dittoBounds = _dittoOriginalFrames is { } originalFrames
            ? GetAlphaBounds(BuildAlphaMask(originalFrames[0, 0]), _frameWidth, _frameHeight)
            : bounds;
        var centerX = _spriteOffsetX + dittoBounds.X + dittoBounds.Width / 2d;
        var centerY = SpriteCanvasTop + dittoBounds.Y + dittoBounds.Height / 2d;
        var aspectRatio = frame.PixelWidth / (double)frame.PixelHeight;
        var effectHeight = Math.Clamp(Math.Max(dittoBounds.Width, dittoBounds.Height) * 1.45d * 1.5d, 120d, 300d);
        effectHeight = Math.Min(effectHeight, Math.Min(
            Math.Min(centerX, _canvasWidth - centerX) * 2d / aspectRatio,
            Math.Min(centerY, _canvas.Height - centerY) * 2d));
        var effectWidth = effectHeight * aspectRatio;
        _dittoTransformImage.Width = effectWidth;
        _dittoTransformImage.Height = effectHeight;
        Canvas.SetLeft(_dittoTransformImage, centerX - effectWidth / 2d - DittoTransformLeftOffsetPixels);
        Canvas.SetTop(_dittoTransformImage, centerY - effectHeight / 2d - DittoTransformRaisePixels);
    }

    internal bool TryBeginEvolutionPrompt()
    {
        if (!IsVisible || IsDitto || _battleParticipant || _megaTransformationActive
            || _dittoTransformActive || _evolutionPromptPending || _evolutionActive)
            return false;
        _evolutionPromptPending = true;
        _walking = false;
        _nextDecision = DateTime.MaxValue;
        SetEmotion(null);
        RenderFrame();
        return true;
    }

    internal void CancelEvolutionPrompt()
    {
        if (!_evolutionPromptPending || _evolutionActive) return;
        _evolutionPromptPending = false;
        _nextDecision = DateTime.UtcNow.AddMilliseconds(500);
    }

    internal bool BeginPermanentEvolution(string targetId)
    {
        if (!_evolutionPromptPending || _evolutionActive || _pokemonCatalog is null || _speciesMetadata is null
            || !_pokemonCatalog.ById.TryGetValue(targetId, out var targetEntry)) return false;
        try
        {
            _evolutionTargetMetadata = _speciesMetadata.Get(targetId);
            var walkingPath = Path.Combine(AppContext.BaseDirectory, "assets",
                (_capture.IsShiny ? targetEntry.WalkingShiny : targetEntry.WalkingNormal).Replace('/', Path.DirectorySeparatorChar));
            _evolutionTargetFrames = LoadWalkingFrames(walkingPath);
            _evolutionAnimationFrames = EvolutionAnimationFrames.Load(Path.Combine(AppContext.BaseDirectory,
                "assets", EvolutionAnimationFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"pokemon-evolution-assets:{targetId}", exception);
            CancelPermanentEvolution();
            return false;
        }

        var soundPath = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "effects", "evolve.mp3");
        if (File.Exists(soundPath)) EncounterAudio.PlayEffectToCompletion("sounds/effects/evolve.mp3");
        else EncounterDiagnostics.Write("pokemon-evolution-sound", $"Missing evolve effect: {soundPath}");

        _evolutionOriginalFrames = CloneFrames(_frames);
        _evolutionFrame = 0;
        _evolutionActive = true;
        _evolutionPromptPending = false;
        _walking = false;
        _row = 0;
        _column = 0;
        _direction = new Point(0, 1);
        _nextDecision = DateTime.MaxValue;
        _lastFrame = -1;
        _sprite.Opacity = 1d;
        _evolutionImage.Source = _evolutionAnimationFrames[0];
        _evolutionImage.Visibility = Visibility.Visible;
        _evolutionImage.Opacity = 1d;
        SetEvolutionCanvasExpanded(true);
        PositionEvolutionEffect();
        RenderFrame();
        if (IsVisible) _evolutionTimer.Start();
        return true;
    }

    private void OnEvolutionTick(object? sender, EventArgs e)
    {
        if (!IsVisible || !_evolutionActive || _evolutionAnimationFrames is null)
        {
            _evolutionTimer.Stop();
            return;
        }
        if (_evolutionFrame >= EvolutionAnimationFrames.Count - 1)
        {
            CompletePermanentEvolution();
            return;
        }

        _evolutionFrame++;
        _evolutionImage.Source = _evolutionAnimationFrames[_evolutionFrame];
        PositionEvolutionEffect();

        if (_evolutionFrame == 17 && _evolutionTargetFrames is not null)
        {
            _frames.CopyFrom(_evolutionTargetFrames);
            UpdateFrameMasks();
            _row = 0;
            _column = 0;
            _lastFrame = -1;
            RenderFrame();
        }

        if (_evolutionFrame <= 9)
            _sprite.Opacity = Math.Max(0d, 1d - _evolutionFrame / 9d);
        else if (_evolutionFrame < 17)
            _sprite.Opacity = 0d;
        else
            _sprite.Opacity = Math.Clamp(.1d + (_evolutionFrame - 17) / 9d * .9d, .1d, 1d);
    }

    private void CompletePermanentEvolution()
    {
        _evolutionTimer.Stop();
        var applied = false;
        try
        {
            applied = _evolutionTargetMetadata is not null
                && _applyPermanentEvolution(_evolutionTargetMetadata.Id);
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"pokemon-evolution-commit:{_capture.RecordId}", exception);
        }
        if (!applied)
        {
            EncounterDiagnostics.Write("pokemon-evolution", $"Could not commit evolution for {_capture.RecordId}; restored the original species.");
            CancelPermanentEvolution();
            return;
        }

        _evolutionActive = false;
        _evolutionPromptPending = false;
        _evolutionFrame = -1;
        _evolutionImage.Source = null;
        _evolutionImage.Visibility = Visibility.Collapsed;
        SetEvolutionCanvasExpanded(false);
        _sprite.Opacity = 1d;
        _evolutionOriginalFrames = null;
        _evolutionTargetFrames = null;
        _evolutionAnimationFrames = null;
        _evolutionTargetMetadata = null;
    }

    private void CancelPermanentEvolution()
    {
        _evolutionTimer.Stop();
        if (_evolutionOriginalFrames is not null)
        {
            _frames.CopyFrom(_evolutionOriginalFrames);
            UpdateFrameMasks();
        }
        _evolutionPromptPending = false;
        _evolutionActive = false;
        _evolutionFrame = -1;
        _evolutionOriginalFrames = null;
        _evolutionTargetFrames = null;
        _evolutionAnimationFrames = null;
        _evolutionTargetMetadata = null;
        _evolutionImage.Source = null;
        _evolutionImage.Visibility = Visibility.Collapsed;
        SetEvolutionCanvasExpanded(false);
        _sprite.Opacity = 1d;
        _row = 0;
        _column = 0;
        _lastFrame = -1;
        _nextDecision = DateTime.UtcNow.AddMilliseconds(500);
        RenderFrame();
    }

    private void PositionEvolutionEffect()
    {
        if (_evolutionImage.Source is not BitmapSource frame) return;
        var bounds = _evolutionOriginalFrames is { } originalFrames
            ? GetAlphaBounds(BuildAlphaMask(originalFrames[0, 0]), _frameWidth, _frameHeight)
            : _visibleFrameBounds[_row, _column];
        if (bounds.IsEmpty) return;
        var centerX = _spriteOffsetX + bounds.X + bounds.Width / 2d;
        var centerY = SpriteCanvasTop + bounds.Y + bounds.Height / 2d;
        var aspectRatio = frame.PixelWidth / (double)frame.PixelHeight;
        var height = EvolutionEffectHeight;
        var width = height * aspectRatio;
        _evolutionImage.Width = width;
        _evolutionImage.Height = height;
        Canvas.SetLeft(_evolutionImage, centerX - width / 2d - 2d);
        Canvas.SetTop(_evolutionImage, centerY - height / 2d);
    }

    private void SetEvolutionCanvasExpanded(bool expanded)
    {
        var height = expanded ? Math.Max(_baseCanvasHeight, _baseCanvasHeight + EvolutionEffectHeight) : _baseCanvasHeight;
        if (Math.Abs(_canvas.Height - height) < .1d) return;
        _canvas.Height = height;
        MinHeight = 0;
        MaxHeight = double.PositiveInfinity;
        Height = height * _visualScale;
        MinHeight = MaxHeight = Height;
    }

    internal bool BeginDittoBattleTransform(DesktopCompanionWindow opponent)
    {
        if (!IsDitto || ReferenceEquals(this, opponent) || opponent.IsDitto) return false;
        return BeginDittoTransform(opponent, "battle", oppositeSexVisual: false);
    }

#if DEBUG
    internal bool BeginDittoTransformPreviewForDebug(DesktopCompanionWindow target) =>
        BeginDittoTransform(target, "debug", oppositeSexVisual: false);

    internal bool BeginMegaEvolutionPreviewForDebug(string formId)
    {
        BeginMegaEvolution(formId);
        return _megaTransformationActive;
    }

    internal void SetMegaEvolutionPreviewFrameForDebug(int frameIndex)
    {
        if (!_megaTransformationActive || _megaTransitionFrames is null
            || frameIndex is < 0 or >= MegaBubbleFrames.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        _megaBubbleTimer.Stop();
        _megaBubbleFrame = frameIndex;
        _megaBubbleProgress = frameIndex;
        _megaBubbleImage.Source = MegaBubbleAnimationFrames[frameIndex];
        _megaBubbleImage.Opacity = 1d;
        _megaBubbleImage.Visibility = Visibility.Visible;
        PositionMegaBubble();
        if (MegaBubbleFrames.MegaIsVisibleAt(frameIndex))
        {
            _sprite.Source = _megaTransitionFrames[0, 0];
            _sprite.Opacity = MegaBubbleFrames.MegaOpacityAt(frameIndex);
        }
        else
        {
            RenderFrame();
            _sprite.Opacity = 1d;
        }
    }

    internal void SaveMegaEvolutionPreviewForDebug(string path) => SaveEffectPreviewForDebug(path);
    internal void SaveEvolutionPreviewForDebug(string path) => SaveEffectPreviewForDebug(path);

    internal void SetEvolutionPreviewFrameForDebug(int frameIndex)
    {
        if (!_evolutionActive || _evolutionAnimationFrames is null
            || frameIndex is < 0 or >= EvolutionAnimationFrames.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        _evolutionTimer.Stop();
        _evolutionFrame = frameIndex;
        _evolutionImage.Source = _evolutionAnimationFrames[frameIndex];
        _evolutionImage.Visibility = Visibility.Visible;
        PositionEvolutionEffect();
        if (frameIndex >= 17 && _evolutionTargetFrames is not null)
        {
            _frames.CopyFrom(_evolutionTargetFrames);
            UpdateFrameMasks();
            _row = 0;
            _column = 0;
            _lastFrame = -1;
            RenderFrame();
        }
        _sprite.Opacity = frameIndex <= 9
            ? Math.Max(0d, 1d - frameIndex / 9d)
            : frameIndex < 17 ? 0d : Math.Clamp(.1d + (frameIndex - 17) / 9d * .9d, .1d, 1d);
    }

    internal void CompleteEvolutionPreviewForDebug() => CompletePermanentEvolution();

    internal void SetDittoTransformPreviewFrameForDebug(int frameIndex)
    {
        if (!_dittoTransformActive || frameIndex is < 0 or >= DittoTransformFrames.Columns * DittoTransformFrames.Rows)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        _dittoTransformTimer.Stop();
        _dittoTransformFrame = frameIndex;
        _dittoTransformImage.Source = DittoTransformAnimationFrames[frameIndex];
        _dittoTransformImage.Opacity = 1d;
        if (_dittoTargetFrames is not null && frameIndex >= 13)
            _frames.CopyFrom(_dittoTargetFrames);
        else if (_dittoOriginalFrames is not null)
            _frames.CopyFrom(_dittoOriginalFrames);
        UpdateFrameMasks();
        _row = 0;
        _column = 0;
        _lastFrame = -1;
        RenderFrame();
        _sprite.Opacity = frameIndex < 13
            ? DittoTransformFrames.SourceOpacityAt(frameIndex)
            : DittoTransformFrames.TargetOpacityAt(frameIndex);
        if (frameIndex >= 9 && frameIndex < 13) _sprite.Opacity = 0d;
        PositionDittoTransform();
    }
#endif

    internal bool BeginDittoCouplesTransform(DesktopCompanionWindow partner)
    {
        if (!IsDitto || _dittoTransformed || _dittoTransformActive || partner.IsDitto) return false;
        return BeginDittoTransform(partner, "couples", oppositeSexVisual: true);
    }

    internal void ScheduleDittoReturn(string reason, TimeSpan delay)
    {
        if (!IsDitto || !_dittoTransformed || _dittoReturnReason != reason) return;
        _dittoReturnAt = DateTime.UtcNow + delay;
    }

    private bool BeginDittoTransform(DesktopCompanionWindow target, string reason, bool oppositeSexVisual)
    {
        if (!IsDitto || _dittoTransformActive || _megaTransformationActive || target.IsDitto) return false;
        var visualFrames = oppositeSexVisual ? LoadOppositeSexFrames(target) : null;
        _dittoTargetFrames = visualFrames ?? ResizeFrames(target._frames);
        _dittoCopiedTypes = target.BattleTypes.ToArray();
        _dittoCopiedRecordId = target.RecordId;
        _dittoReturnReason = reason;
        _dittoTransformReversing = false;
        BeginDittoTransition();
        return true;
    }

    private BitmapSource[,]? LoadOppositeSexFrames(DesktopCompanionWindow target)
    {
        if (_pokemonCatalog is null || _speciesMetadata is null) return null;
        var targetMetadata = _speciesMetadata.GetForCapture(target._capture.Id);
        var targetSex = PokemonSex.Get(targetMetadata);
        if (targetSex is not ("male" or "female")) return null;
        var desiredSex = targetSex == "male" ? "female" : "male";
        var candidate = _pokemonCatalog.Entries
            .Where(entry => !entry.Id.Equals("DITTO", StringComparison.OrdinalIgnoreCase))
            .Select(entry => (Entry: entry, Metadata: _speciesMetadata.Get(entry.Id)))
            .Where(item => item.Metadata.SpeciesId.Equals(targetMetadata.SpeciesId, StringComparison.OrdinalIgnoreCase)
                && PokemonSex.Get(item.Metadata).Equals(desiredSex, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Metadata.Form.Equals(targetMetadata.Form, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
        if (candidate.Entry is null) return null;
        var path = Path.Combine(AppContext.BaseDirectory, "assets",
            (target._capture.IsShiny ? candidate.Entry.WalkingShiny : candidate.Entry.WalkingNormal).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        try { return LoadWalkingFrames(path); }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"ditto-couples-visual:{candidate.Entry.Id}", exception);
            return null;
        }
    }

    private BitmapSource[,] LoadWalkingFrames(string path)
    {
        var sheet = LoadBitmap(path);
        if (sheet.PixelWidth % 4 != 0 || sheet.PixelHeight % 4 != 0)
            throw new InvalidDataException($"La hoja de caminata de pareja no se divide en 4×4: {sheet.PixelWidth}×{sheet.PixelHeight}.");
        var cellWidth = sheet.PixelWidth / 4;
        var cellHeight = sheet.PixelHeight / 4;
        var result = new BitmapSource[4, 4];
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
        {
            var cell = new CroppedBitmap(sheet, new Int32Rect(column * cellWidth, row * cellHeight, cellWidth, cellHeight));
            result[row, column] = FitDittoFrame(cell);
        }
        return result;
    }

    private BitmapSource[,] ResizeFrames(BitmapSource[,] source)
    {
        var result = new BitmapSource[4, 4];
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++) result[row, column] = FitDittoFrame(source[row, column]);
        return result;
    }

    private BitmapSource FitDittoFrame(BitmapSource source)
    {
        var scale = Math.Min(_frameWidth / (double)source.PixelWidth, _frameHeight / (double)source.PixelHeight);
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        return PadFrame(ScaleNearestNeighbor(source, width, height), _frameWidth, _frameHeight);
    }

    private static BitmapSource[,] CloneFrames(BitmapSource[,] source)
    {
        var copy = new BitmapSource[4, 4];
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++) copy[row, column] = source[row, column];
        return copy;
    }

    private void BeginDittoTransition()
    {
        if (!IsDitto || _dittoTransformActive) return;
        _cancelRally(this);
        _cancelToyChase(this);
        CancelInteraction();
        _sleepUntil = DateTime.MinValue;
        _walking = false;
        _column = 0;
        _row = 0;
        _nextDecision = DateTime.MaxValue;
        _dittoTransformActive = true;
        _dittoTransformFrame = 0;
        _dittoTransformImage.Source = DittoTransformAnimationFrames[0];
        _dittoTransformImage.Visibility = Visibility.Visible;
        _sprite.Opacity = 1d;
        _audio.PlayEffect("sounds/effects/mega-evolution.mp3");
        PositionDittoTransform();
        if (IsVisible) _dittoTransformTimer.Start();
        RenderFrame();
    }

    private void BeginDittoReverse()
    {
        if (!IsDitto || !_dittoTransformed || _dittoTransformActive || _dittoOriginalFrames is null) return;
        _dittoTransformReversing = true;
        _dittoTargetFrames = CloneFrames(_dittoOriginalFrames);
        BeginDittoTransition();
    }

    private void OnDittoTransformTick(object? sender, EventArgs e)
    {
        if (!IsVisible || !_dittoTransformActive || _dittoTargetFrames is null)
        {
            _dittoTransformTimer.Stop();
            return;
        }
        if (_dittoTransformFrame >= 26)
        {
            CompleteDittoTransition();
            return;
        }

        _dittoTransformFrame++;
        _dittoTransformImage.Source = DittoTransformAnimationFrames[_dittoTransformFrame];
        PositionDittoTransform();
        if (_dittoTransformFrame == 13)
        {
            _frames.CopyFrom(_dittoTargetFrames);
            UpdateFrameMasks();
            _row = 0;
            _column = 0;
            _lastFrame = -1;
            RenderFrame();
        }
        _sprite.Opacity = _dittoTransformFrame < 13
            ? DittoTransformFrames.SourceOpacityAt(_dittoTransformFrame)
            : DittoTransformFrames.TargetOpacityAt(_dittoTransformFrame);
        if (_dittoTransformFrame >= 9 && _dittoTransformFrame < 13) _sprite.Opacity = 0d;
    }

    private void CompleteDittoTransition()
    {
        _dittoTransformTimer.Stop();
        if (_dittoTargetFrames is not null) _frames.CopyFrom(_dittoTargetFrames);
        UpdateFrameMasks();
        _dittoTransformed = !_dittoTransformReversing;
        if (!_dittoTransformed)
        {
            _dittoCopiedTypes = Array.Empty<string>();
            _dittoCopiedRecordId = null;
            _dittoReturnAt = DateTime.MinValue;
            _dittoReturnReason = null;
        }
        else if (_dittoReturnReason == "ambient")
        {
            _dittoReturnAt = DateTime.UtcNow.AddSeconds(_random.Next(45, 91));
        }
        _dittoTransformActive = false;
        _dittoTransformFrame = -1;
        _dittoTransformReversing = false;
        _dittoTargetFrames = null;
        _dittoTransformImage.Source = null;
        _dittoTransformImage.Visibility = Visibility.Collapsed;
        _sprite.Opacity = 1d;
        _row = 0;
        _column = 0;
        _lastFrame = -1;
        _walking = false;
        _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(500, 1000));
        RenderFrame();
    }

    private void CancelDittoTransformation()
    {
        _dittoTransformTimer.Stop();
        if (IsDitto && _dittoOriginalFrames is not null)
        {
            _frames.CopyFrom(_dittoOriginalFrames);
            UpdateFrameMasks();
        }
        _dittoTransformActive = false;
        _dittoTransformReversing = false;
        _dittoTransformed = false;
        _dittoTransformFrame = -1;
        _dittoTargetFrames = null;
        _dittoCopiedTypes = Array.Empty<string>();
        _dittoCopiedRecordId = null;
        _dittoReturnReason = null;
        _dittoReturnAt = DateTime.MinValue;
        _dittoTransformImage.Source = null;
        _dittoTransformImage.Visibility = Visibility.Collapsed;
        _sprite.Opacity = 1d;
    }

    internal void CancelDittoTransitionAndRestore()
    {
        if (IsDitto) CancelDittoTransformation();
    }

    private void UpdateFrameMasks()
    {
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
        {
            _alphaMasks[row * 4 + column] = BuildAlphaMask(_frames[row, column]);
            _visibleFrameBounds[row, column] = GetAlphaBounds(_alphaMasks[row * 4 + column], _frameWidth, _frameHeight);
        }
    }

    private void TickAmbientDitto(DateTime now)
    {
        if (!IsDitto || _pinned || _dragPending || _dragging || _battleParticipant || _couplesParticipant
            || _isRallyParticipant || _rallyEventActive || _toyPlayParticipant || _toyTarget.HasValue
            || _toyWaitingForPass || _interactionPartnerId is not null || _requestedBerryId is not null
            || _activeEmotion is not null || _fleeingAfterCollision || _battleEvacuationUntil > now
            || _megaTransformationActive || _dittoTransformActive)
            return;
        if (_dittoTransformed)
        {
            if (_dittoReturnAt != DateTime.MinValue && now >= _dittoReturnAt)
                BeginDittoReverse();
            return;
        }
        if (now < _nextDittoAmbientCheck) return;
        _nextDittoAmbientCheck = now.AddSeconds(_random.Next(60, 121));
        if (_random.Next(100) >= 8) return;
        var candidates = _getCompanions().Where(item => !ReferenceEquals(item, this) && item.IsVisible
            && !item._battleParticipant && !item._couplesParticipant && !item._megaTransformationActive
            && !item._dittoTransformActive && !item._isRallyParticipant && !item._rallyEventActive
            && !item._toyPlayParticipant && !item._toyTarget.HasValue && !item._toyWaitingForPass
            && item._interactionPartnerId is null && item._requestedBerryId is null).ToArray();
        if (candidates.Length == 0) return;
        BeginDittoTransform(candidates[_random.Next(candidates.Length)], "ambient", oppositeSexVisual: false);
    }

    private static Style CreateCompanionMenuItemStyle(BitmapSource normalRow, BitmapSource selectedRow)
    {
        var template = new ControlTemplate(typeof(MenuItem));
        var root = new FrameworkElementFactory(typeof(Grid));
        var stateImage = new FrameworkElementFactory(typeof(Image), "StateImage");
        stateImage.SetValue(Image.SourceProperty, normalRow);
        stateImage.SetValue(Image.StretchProperty, Stretch.Fill);
        stateImage.SetValue(UIElement.IsHitTestVisibleProperty, false);
        stateImage.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.NearestNeighbor);
        stateImage.SetValue(RenderOptions.EdgeModeProperty, EdgeMode.Aliased);
        root.AppendChild(stateImage);

        var label = new FrameworkElementFactory(typeof(ContentPresenter));
        label.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        label.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        label.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        label.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        root.AppendChild(label);
        template.VisualTree = root;
        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Image.SourceProperty, selectedRow, "StateImage"));
        highlighted.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, Brushes.White));
        template.Triggers.Add(highlighted);

        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 30d));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 1, 4, 1)));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 38, 38))));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.FontFamilyProperty, PixelButtonUi.Font));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.FontSizeProperty, 13d));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.VerticalContentAlignmentProperty, System.Windows.VerticalAlignment.Stretch));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, template));
        return style;
    }

    private static BitmapSource LoadBitmap(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Falta el spritesheet de caminata del acompañante.", path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource PadFrame(BitmapSource frame, int width, int height)
    {
        if (frame.PixelWidth == width && frame.PixelHeight == height) return frame;
        var padded = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var x = Math.Max(0, (width - frame.PixelWidth) / 2);
            var y = Math.Max(0, (height - frame.PixelHeight) / 2);
            context.DrawImage(frame, new Rect(x, y, frame.PixelWidth, frame.PixelHeight));
        }
        padded.Render(visual); padded.Freeze();
        return padded;
    }

    private static BitmapSource ScaleNearestNeighbor(BitmapSource source, int width, int height)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var sourceStride = converted.PixelWidth * 4;
        var sourcePixels = new byte[sourceStride * converted.PixelHeight];
        converted.CopyPixels(sourcePixels, sourceStride, 0);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(converted.PixelHeight - 1, y * converted.PixelHeight / height);
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(converted.PixelWidth - 1, x * converted.PixelWidth / width);
                Buffer.BlockCopy(sourcePixels, sourceY * sourceStride + sourceX * 4, pixels, y * stride + x * 4, 4);
            }
        }
        var scaled = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        scaled.Freeze();
        return scaled;
    }

    private static bool[] BuildAlphaMask(BitmapSource frame)
    {
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var mask = new bool[converted.PixelHeight * converted.PixelWidth];
        for (var y = 0; y < converted.PixelHeight; y++)
        for (var x = 0; x < converted.PixelWidth; x++)
            mask[y * converted.PixelWidth + x] = pixels[y * stride + x * 4 + 3] > 8;
        return mask;
    }

    private static Int32Rect GetAlphaBounds(bool[] mask, int width, int height)
    {
        var left = width; var top = height; var right = -1; var bottom = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (!mask[y * width + x]) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        return right < 0 ? new Int32Rect(0, 0, 1, 1) : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowHook);
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
        if (_dropCenter is { } center)
        {
            var dpi = GetDpiScale();
            _positionX = center.X - Width * dpi / 2d;
            _positionY = center.Y - _frameHeight * _visualScale * dpi / 2d;
        }
        ClampToAvailableScreen();
        SavePosition();
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }
        if (message == WmNcHitTest && GetWindowRect(hwnd, out var bounds))
        {
            var packed = lParam.ToInt64();
            var screenX = unchecked((short)(packed & 0xffff));
            var screenY = unchecked((short)((packed >> 16) & 0xffff));
            var localX = screenX - bounds.Left;
            var localY = screenY - bounds.Top;
            var bitmapX = (int)(localX * _canvasWidth / (double)Math.Max(1, bounds.Right - bounds.Left)) - _spriteOffsetX;
            var bitmapY = (int)(localY * _canvas.Height / (double)Math.Max(1, bounds.Bottom - bounds.Top)) - SpriteCanvasTop;
            if (bitmapX < 0 || bitmapY < 0 || bitmapX >= _frameWidth || bitmapY >= _frameHeight
                || !_alphaMasks[_row * 4 + _column][bitmapY * _frameWidth + bitmapX])
            {
                handled = true;
                return new IntPtr(HtTransparent);
            }
        }
        return IntPtr.Zero;
    }

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsDitto && _dittoTransformed && !_dittoTransformActive && !_battleParticipant && !_couplesParticipant)
        {
            BeginDittoReverse();
            e.Handled = true;
            return;
        }
        if (_dittoTransformActive)
        {
            e.Handled = true;
            return;
        }
        _cancelBattle(this);
        if (_megaTransformationActive)
        {
            e.Handled = true;
            return;
        }
        _cancelRally(this);
        _cancelToyChase(this);
        _toyTarget = null;
        _toyChaseIsRunning = false;
        _edgeStunnedUntil = DateTime.MinValue;
        _edgeRunPending = false;
        _wasSleepingOnPress = _pinned || DateTime.UtcNow < _sleepUntil;
        _fleeingAfterCollision = false;
        CancelInteraction();
        SetEmotion(null);
        _sleepUntil = DateTime.MinValue;
        if (!GetCursorPos(out var cursor) || !GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds))
        {
            _wasSleepingOnPress = false;
            return;
        }
        _dragPending = true;
        _dragging = false;
        _dragStartScreen = new Point(cursor.X, cursor.Y);
        _windowStartScreen = new Point(bounds.Left, bounds.Top);
        _sprite.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending || e.LeftButton != MouseButtonState.Pressed || !GetCursorPos(out var cursor)) return;
        var dx = cursor.X - _dragStartScreen.X;
        var dy = cursor.Y - _dragStartScreen.Y;
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) >= 5)
        {
            _dragging = true;
            _cancelRally(this);
            _cancelToyChase(this);
            _toyTarget = null;
            _toyChaseIsRunning = false;
        }
        if (!_dragging) return;
        PlaceAt((int)Math.Round(_windowStartScreen.X + dx), (int)Math.Round(_windowStartScreen.Y + dy));
        e.Handled = true;
    }

    private void OnLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragPending) return;
        _dragPending = false;
        if (_sprite.IsMouseCaptured) _sprite.ReleaseMouseCapture();
        if (_dragging)
        {
            _dragging = false;
            _wasSleepingOnPress = false;
            if (GetCursorPos(out var cursor)
                && _tryReturnToPcOnDrop(this, new Point(cursor.X, cursor.Y)))
            {
                e.Handled = true;
                return;
            }
            ClampToAvailableScreen();
            SavePosition();
            if (_random.Next(2) == 0) ActivateReaction(CompanionEmotion.Dizzy, TimeSpan.FromSeconds(2));
        }
        else
        {
            _row = 0; // Row 0 faces forward in the supplied four-direction sheets.
            _column = 0;
            _faceUserUntil = DateTime.UtcNow.AddMilliseconds(1200);
            _walking = false;
            _nextDecision = _faceUserUntil;
            var reaction = _wasSleepingOnPress ? CompanionEmotion.Angry : CompanionEmotion.Heart;
            _wasSleepingOnPress = false;
            ActivateReaction(reaction, TimeSpan.FromMilliseconds(2300));
            StartShinySparkles(restart: true);
            StartMegaAura(restart: true);
            StartAlphaEffect(restart: true);
            RenderFrame();
            _audio.PlayCry(_capture.Id);
        }
        e.Handled = true;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        var now = DateTime.UtcNow;
        if (_evolutionPromptPending || _evolutionActive)
        {
            _walking = false;
            return;
        }
        var elapsed = Math.Clamp((now - _lastTick).TotalSeconds, 0, .12);
        _lastTick = now;
        if (_megaTransformationActive)
        {
            _walking = false;
            return;
        }
        if (_dittoTransformActive)
        {
            _walking = false;
            return;
        }
        if (now < _megaWalkResumeAt)
        {
            _walking = false;
            _row = 0;
            _column = 0;
            return;
        }
        if (_megaWalkResumeAt != DateTime.MinValue)
        {
            _megaWalkResumeAt = DateTime.MinValue;
            _nextDecision = now;
        }
        UpdateShinySparkles(now);
        UpdateMegaAuraSchedule(now);
        UpdateAlphaEffect(now);
        UpdateRallyJumpVisual(now);
        UpdateBattleCelebration(now);
        if (now >= _nextScreenRefresh)
        {
            _screens = Forms.Screen.AllScreens;
            _nextScreenRefresh = now.AddSeconds(5);
        }
        if (_requestedBerryId is not null && now >= _berryRequestUntil) ClearBerryRequest();
        // Expire timed reactions before early returns for rallies, toy chases,
        // and Pokémon waiting for a pass. Otherwise an angry/sad bubble could
        // remain visible indefinitely while the Pokémon was waiting.
        if (_reactionUntil > DateTime.MinValue && now >= _reactionUntil)
        {
            if (_pinned && _activeEmotion != CompanionEmotion.Sleep)
                ShowPinnedSleepReaction();
            else
                SetEmotion(null);
            if (_sleepUntil != DateTime.MinValue && now >= _sleepUntil) _sleepUntil = DateTime.MinValue;
        }
        if (_battleParticipant)
        {
            TickFriendlyBattlePreparation(elapsed);
            return;
        }
        if (_battleEvacuationUntil > now)
        {
            TickBattleEvacuation(now, elapsed);
            return;
        }
        _battleEvacuationUntil = DateTime.MinValue;
        if (_couplesParticipant)
        {
            _walking = false;
            _column = 0;
            RenderFrame();
            return;
        }
        if (_isRallyParticipant && !_rallyApproachFailed)
        {
            TickTeamRally(now, elapsed);
            return;
        }
        if (_toyTarget is { } toyTarget)
        {
            TickToyChase(toyTarget, now, elapsed);
            return;
        }
        if (_toyPlayParticipant && _toyWaitingForPass)
        {
            _walking = false;
            _column = 0;
            RenderFrame();
            return;
        }
        TickAmbientDitto(now);
        if (_interactionPartnerId is not null)
        {
            if (now >= _interactionUntil) FinishInteraction();
            else { _walking = false; _column = 0; RenderFrame(); return; }
        }
        if (_edgeStunnedUntil != DateTime.MinValue)
        {
            if (_pinned || _dragPending)
            {
                _edgeStunnedUntil = DateTime.MinValue;
                _edgeRunPending = false;
            }
            else
            {
                if (now < _edgeStunnedUntil)
                {
                    _walking = false;
                    _column = 0;
                    RenderFrame();
                    return;
                }

                _edgeStunnedUntil = DateTime.MinValue;
                if (_edgeRunPending)
                {
                    _edgeRunPending = false;
                    _direction = _edgeRunDirection;
                    _row = DirectionRow(_direction);
                    _walking = true;
                    _fleeingAfterCollision = true;
                    _nextDecision = now.AddMilliseconds(EdgeRunMilliseconds);
                    SetEmotion(null);
                    RenderFrame();
                    return;
                }
            }
        }
        if (_pinned || _dragPending || now < _faceUserUntil || now < _sleepUntil)
        {
            if (now < _faceUserUntil) _row = 0;
            _walking = false;
            _column = 0;
            RenderFrame();
            return;
        }

        if (now >= _nextDecision)
        {
            if (_walking)
            {
                _walking = false;
                _fleeingAfterCollision = false;
                _nextDecision = now.AddMilliseconds(_random.Next(700, 2300));
                _column = 0;
            }
            else
            {
                if (_avoidanceDirectionPending)
                {
                    _avoidanceDirectionPending = false;
                    _row = DirectionRow(_direction);
                    _walking = true;
                    _nextDecision = now.AddMilliseconds(_random.Next(1300, 2400));
                }
                else if (_random.Next(SleepChanceOneIn) == 0)
                {
                    var sleep = TimeSpan.FromSeconds(_random.Next(MinSleepSeconds, MaxSleepSecondsExclusive));
                    _sleepUntil = now + sleep;
                    _nextDecision = _sleepUntil;
                    SetEmotion(CompanionEmotion.Sleep, sleep);
                }
                else
                {
                    ChooseDirection();
                    _walking = true;
                    _nextDecision = now.AddMilliseconds(_random.Next(2200, 5200));
                }
            }
        }

        if (now >= _nextSocialDecision && TryStartNearbyInteraction(now))
        {
            RenderFrame();
            return;
        }

        if (TrySeparateOverlap(now))
        {
            _column = 0;
            RenderFrame();
            return;
        }

        if (_walking)
        {
            if (GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds))
            {
                var speed = _speed * (_fleeingAfterCollision ? CollisionFleeSpeedMultiplier : 1d);
                var x = bounds.Left + _direction.X * speed * elapsed;
                var y = bounds.Top + _direction.Y * speed * elapsed;
                if (TryHandleWanderEdgeImpact(x, y, now)) return;
                if (!TryMoveWithoutCollision(x, y, now))
                {
                    _column = 0;
                    if (_activeEmotion != CompanionEmotion.Sad || now >= _reactionUntil)
                    {
                        _walking = false;
                        _nextDecision = now.AddMilliseconds(_random.Next(450, 900));
                    }
                }
                else _column = (int)(_clock.ElapsedMilliseconds / 140 % 4);
            }
        }
        else _column = 0;

        RenderFrame();
        if (now - _lastPersistence >= TimeSpan.FromSeconds(2)) SavePosition();
    }

    private void ChooseDirection()
    {
        // Prefer open space near an edge, while preserving occasional paths beside it.
        Point[] directions = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];
        var body = VisibleBodyScreenBounds;
        var centerX = (int)Math.Round(body.X + body.Width / 2d);
        var centerY = (int)Math.Round(body.Y + body.Height / 2d);
        var screen = _screens.FirstOrDefault(item => item.WorkingArea.Contains(centerX, centerY));
        var weights = directions.Select(direction => DirectionWeightNearEdges(direction, body, screen, _screens)).ToArray();
        var roll = _random.NextDouble() * weights.Sum();
        var selected = 0;
        while (selected < weights.Length - 1 && roll >= weights[selected])
            roll -= weights[selected++];
        _direction = directions[selected];
        _row = DirectionRow(_direction);
    }

    private static double DirectionWeightNearEdges(Point direction, Rect body, Forms.Screen? screen,
        IReadOnlyList<Forms.Screen> screens)
    {
        if (screen is null || body.IsEmpty) return 1d;
        var area = screen.WorkingArea;
        var centerX = (int)Math.Round(body.X + body.Width / 2d);
        var centerY = (int)Math.Round(body.Y + body.Height / 2d);
        var weight = 1d;
        if (!HasAdjacentScreenOnEdge(area, screens, Edge.Left, centerX, centerY)
            && body.Left - area.Left < WanderEdgeAvoidancePixels)
            weight *= direction.X < 0 ? .12d : direction.X > 0 ? 2.3d : .72d;
        if (!HasAdjacentScreenOnEdge(area, screens, Edge.Right, centerX, centerY)
            && area.Right - body.Right < WanderEdgeAvoidancePixels)
            weight *= direction.X > 0 ? .12d : direction.X < 0 ? 2.3d : .72d;
        if (!HasAdjacentScreenOnEdge(area, screens, Edge.Top, centerX, centerY)
            && body.Top - area.Top < WanderEdgeAvoidancePixels)
            weight *= direction.Y < 0 ? .12d : direction.Y > 0 ? 2.3d : .72d;
        if (!HasAdjacentScreenOnEdge(area, screens, Edge.Bottom, centerX, centerY)
            && area.Bottom - body.Bottom < WanderEdgeAvoidancePixels)
            weight *= direction.Y > 0 ? .12d : direction.Y < 0 ? 2.3d : .72d;
        return weight;
    }

    private enum Edge { Left, Right, Top, Bottom }

    private static bool HasAdjacentScreenOnEdge(System.Drawing.Rectangle area,
        IReadOnlyList<Forms.Screen> screens, Edge edge, int centerX, int centerY)
    {
        foreach (var other in screens)
        {
            var adjacent = other.WorkingArea;
            if (adjacent == area) continue;
            var sharesEdge = edge switch
            {
                Edge.Left => Math.Abs(adjacent.Right - area.Left) <= 1
                    && centerY >= adjacent.Top && centerY < adjacent.Bottom,
                Edge.Right => Math.Abs(adjacent.Left - area.Right) <= 1
                    && centerY >= adjacent.Top && centerY < adjacent.Bottom,
                Edge.Top => Math.Abs(adjacent.Bottom - area.Top) <= 1
                    && centerX >= adjacent.Left && centerX < adjacent.Right,
                Edge.Bottom => Math.Abs(adjacent.Top - area.Bottom) <= 1
                    && centerX >= adjacent.Left && centerX < adjacent.Right,
                _ => false
            };
            if (sharesEdge) return true;
        }
        return false;
    }

    internal void ShowBerryRequest(string berryId, string iconPath, TimeSpan duration)
    {
        _requestedBerryId = berryId;
        _berryRequestUntil = DateTime.UtcNow + duration;
        var icon = (Image)_berryRequestBubble.Child;
        icon.Source = LoadBitmap(Path.Combine(AppContext.BaseDirectory, "assets", iconPath.Replace('/', Path.DirectorySeparatorChar)));
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);
        _berryRequestBubble.Visibility = Visibility.Visible;
        RaiseAboveCompanionSprites();
        PositionBerryRequest();
        _audio.PlayCry(_capture.Id);
    }

    internal void RaiseContextMenuAboveOverlays()
    {
        if (!_contextMenu.IsOpen || PresentationSource.FromVisual(_contextMenu) is not HwndSource source
            || source.Handle == IntPtr.Zero) return;
        _openContextMenuHandle = source.Handle;
        _ = SetWindowPos(source.Handle, App.OverlayInsertAfter, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    internal IntPtr GetOpenContextMenuHandle()
    {
        if (!_contextMenu.IsOpen || PresentationSource.FromVisual(_contextMenu) is not HwndSource source)
            return IntPtr.Zero;
        _openContextMenuHandle = source.Handle;
        return source.Handle;
    }

    private void StartOutsideContextMenuClickMonitor()
    {
        if (!_contextMenu.IsOpen || _openContextMenuHandle == IntPtr.Zero) return;
        _openContextMenuOwner = this;
        if (_outsideClickHook != IntPtr.Zero) return;
        _outsideClickHook = SetWindowsHookEx(WhMouseLowLevel, OutsideClickHookProc, GetModuleHandle(null), 0);
    }

    private void StopOutsideContextMenuClickMonitor()
    {
        if (!ReferenceEquals(_openContextMenuOwner, this)) return;
        _openContextMenuOwner = null;
        _openContextMenuHandle = IntPtr.Zero;
        if (_outsideClickHook == IntPtr.Zero) return;
        _ = UnhookWindowsHookEx(_outsideClickHook);
        _outsideClickHook = IntPtr.Zero;
    }

    private static IntPtr OnOutsideContextMenuClick(int code, IntPtr message, IntPtr dataPointer)
    {
        if (code >= 0 && _openContextMenuOwner is { } owner && owner._contextMenu.IsOpen
            && message.ToInt32() is WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown or WmXButtonDown)
        {
            var data = Marshal.PtrToStructure<LowLevelMouseData>(dataPointer);
            var clickedWindow = WindowFromPoint(data.Point);
            var current = clickedWindow;
            var insideMenu = false;
            for (var depth = 0; current != IntPtr.Zero && depth < 16; depth++)
            {
                if (current == _openContextMenuHandle || IsChild(_openContextMenuHandle, current))
                {
                    insideMenu = true;
                    break;
                }
                current = GetWindow(current, GwOwner);
            }
            if (!insideMenu)
                owner.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                    new Action(() => { if (owner._contextMenu.IsOpen) owner._contextMenu.IsOpen = false; }));
        }
        return CallNextHookEx(_outsideClickHook, code, message, dataPointer);
    }

    internal void ClearBerryRequest()
    {
        _requestedBerryId = null;
        _berryRequestUntil = DateTime.MinValue;
        _berryRequestBubble.Visibility = Visibility.Collapsed;
        ((Image)_berryRequestBubble.Child).Source = null;
    }

    internal void ShowPlayRequest(TimeSpan duration) => ActivateReaction(CompanionEmotion.Play, duration);

    internal void ClearPlayRequest()
    {
        if (_activeEmotion != CompanionEmotion.Play) return;
        if (_pinned) ShowPinnedSleepReaction();
        else SetEmotion(null);
    }

    internal bool IsChasingToy => _toyTarget.HasValue;
    internal bool IsToyPlayParticipant => _toyPlayParticipant;

    internal void BeginTeamRallyLeader(DateTime until)
    {
        CancelInteraction();
        _isRallyParticipant = true;
        _rallyApproachFailed = false;
        _fleeingAfterCollision = false;
        _rallyCelebrating = false;
        _rallyTarget = null;
        _rallyDetourTarget = null;
        _rallyArrived = true;
        _rallyBlockedAttempts = 0;
        _rallyUntil = until;
        _walking = false;
        _column = 0;
        SetEmotion(CompanionEmotion.Music, TimeSpan.FromSeconds(2.2));
        _audio.PlayCry(_capture.Id);
        RenderFrame();
    }

    internal void JoinTeamRally(Point target, Point leaderCenter, DateTime until)
    {
        CancelInteraction();
        _sleepUntil = DateTime.MinValue;
        _fleeingAfterCollision = false;
        _rallyCelebrating = false;
        _rallyApproachFailed = false;
        _rallyUntil = until;
        if (_pinned)
        {
            var body = VisibleBodyScreenBounds;
            var ownCenter = new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
            var rallyRange = Math.Max(150d, body.Width * 1.8) + _getGroupSpacing();
            _isRallyParticipant = (ownCenter - leaderCenter).Length <= rallyRange;
            _rallyArrived = _isRallyParticipant;
            if (_isRallyParticipant) SetEmotion(CompanionEmotion.Happy, TimeSpan.FromSeconds(1.8));
            return;
        }

        _isRallyParticipant = true;
        _rallyArrived = false;
        _rallyTarget = target;
        _rallyDetourTarget = null;
        _rallyLastProgressAt = DateTime.UtcNow;
        _rallyBlockedAttempts = 0;
        var rallyBody = VisibleBodyScreenBounds;
        var rallyCenter = rallyBody.IsEmpty ? target : new Point(
            rallyBody.X + rallyBody.Width / 2d, rallyBody.Y + rallyBody.Height / 2d);
        _rallyHorizontalFirst = Math.Abs(target.X - rallyCenter.X) >= Math.Abs(target.Y - rallyCenter.Y);
        _rallyAxisRemaining = Math.Min(CardinalStairSegmentPixels,
            Math.Max(24d, (_rallyHorizontalFirst.Value ? Math.Abs(target.X - rallyCenter.X)
                : Math.Abs(target.Y - rallyCenter.Y)) * .5d));
        _walking = true;
        _nextDecision = until;
        SetEmotion(CompanionEmotion.Happy, TimeSpan.FromSeconds(1.8));
    }

    internal void BeginTeamRallyCelebration(DateTime until, Point groupCenter)
    {
        if (!_isRallyParticipant) return;
        CancelInteraction();
        _rallyUntil = until;
        _rallyTarget = null;
        _rallyDetourTarget = null;
        _rallyArrived = true;
        _rallyApproachFailed = false;
        _rallyCelebrating = true;
        _walking = false;
        _column = 0;
        var body = VisibleBodyScreenBounds;
        if (!body.IsEmpty)
        {
            var center = new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
            var towardCenter = groupCenter - center;
            if (towardCenter.Length > 1d)
            {
                _direction = Math.Abs(towardCenter.X) >= Math.Abs(towardCenter.Y)
                    ? new Point(Math.Sign(towardCenter.X), 0)
                    : new Point(0, Math.Sign(towardCenter.Y));
                _row = DirectionRow(_direction);
            }
        }
        _nextRallyJumpAt = DateTime.UtcNow.AddMilliseconds(_random.Next(900, 2200));
        _rallyJumpStartedAt = DateTime.MinValue;
        _rallySpriteTransform.Y = 0;
        RenderFrame();
    }

    internal void SetTeamRallyEnd(DateTime until)
    {
        if (_isRallyParticipant) _rallyUntil = until;
    }

    internal void CompleteTeamRallyApproach()
    {
        if (!_isRallyParticipant || _rallyCelebrating) return;
        _rallyTarget = null;
        _rallyDetourTarget = null;
        _rallyArrived = true;
        _rallyApproachFailed = false;
        _rallyHorizontalFirst = null;
        _rallyAxisRemaining = 0d;
        _walking = false;
        _column = 0;
        RenderFrame();
    }

    internal void SetTeamRallyReaction(CompanionEmotion emotion, TimeSpan duration)
    {
        if (_isRallyParticipant) SetEmotion(emotion, duration);
    }

    internal void PlayTeamRallyCry()
    {
        if (_isRallyParticipant) _audio.PlayCry(_capture.Id);
    }

    internal void PlayAttentionCry() => _audio.PlayCry(_capture.Id);

    internal void BeginCouples(string partnerId, CompanionEmotion greeting)
    {
        CancelInteraction();
        _couplesParticipant = true;
        _couplesPartnerId = partnerId;
        _interactionPartnerId = partnerId;
        _sleepUntil = DateTime.MinValue;
        _fleeingAfterCollision = false;
        _avoidanceDirectionPending = false;
        _walking = false;
        _column = 0;
        _nextDecision = DateTime.MaxValue;
        SetEmotion(greeting, TimeSpan.FromSeconds(1.6));
        _audio.PlayCry(_capture.Id);
        RenderFrame();
    }

    internal bool MoveForCouples(Point target, double elapsed)
    {
        if (!_couplesParticipant || _pinned || _dragPending || _dragging) return false;
        var body = VisibleBodyScreenBounds;
        if (body.IsEmpty) return false;
        var center = new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
        var delta = target - center;
        var distance = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (distance <= Math.Max(7d, Math.Max(body.Width, body.Height) * .12d))
        {
            _walking = false;
            _column = 0;
            RenderFrame();
            return true;
        }

        var step = Math.Min(distance, Math.Max(0d, _speed * 1.05d * elapsed));
        var direction = new Point(delta.X / distance, delta.Y / distance);
        _direction = Math.Abs(direction.X) >= Math.Abs(direction.Y)
            ? new Point(Math.Sign(direction.X), 0) : new Point(0, Math.Sign(direction.Y));
        _row = DirectionRow(_direction);
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds)) return false;
        TryMoveWithoutCollision(bounds.Left + direction.X * step,
            bounds.Top + direction.Y * step, DateTime.UtcNow);
        var moved = GetWindowRect(new WindowInteropHelper(this).Handle, out var afterMove)
            && Math.Abs(afterMove.Left - bounds.Left) + Math.Abs(afterMove.Top - bounds.Top) >= .5d;
        _walking = moved;
        _column = moved ? (int)(_clock.ElapsedMilliseconds / 120 % 4) : 0;
        RenderFrame();
        return moved;
    }

    internal double CouplesWalkSpeed => _speed * .78d;

    internal bool IsNearCouplesEdge(Point direction)
    {
        var body = VisibleBodyScreenBounds;
        if (body.IsEmpty) return true;
        var center = new System.Drawing.Point((int)Math.Round(body.X + body.Width / 2d),
            (int)Math.Round(body.Y + body.Height / 2d));
        var area = Forms.Screen.FromPoint(center).WorkingArea;
        return direction.X > 0 && body.Right >= area.Right - 4d
            || direction.X < 0 && body.Left <= area.Left + 4d
            || direction.Y > 0 && body.Bottom >= area.Bottom - 4d
            || direction.Y < 0 && body.Top <= area.Top + 4d;
    }

    internal void WalkForCouples(Point direction, double elapsed, double speed)
    {
        if (!_couplesParticipant || _pinned || _dragPending || _dragging) return;
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds)) return;
        var magnitude = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (magnitude <= .01d) return;
        direction = new Point(direction.X / magnitude, direction.Y / magnitude);
        _direction = Math.Abs(direction.X) >= Math.Abs(direction.Y)
            ? new Point(Math.Sign(direction.X), 0) : new Point(0, Math.Sign(direction.Y));
        _row = DirectionRow(_direction);
        var step = speed * elapsed;
        MoveAcrossAvailableScreens(bounds.Left + direction.X * step, bounds.Top + direction.Y * step, bounceAtEdge: true);
        _walking = true;
        _column = (int)(_clock.ElapsedMilliseconds / 120 % 4);
        RenderFrame();
    }

    internal void FaceCouplesPartner(Point? center) { if (center is { } point) FaceToward(point); }
    internal void SetCouplesReaction(CompanionEmotion emotion, TimeSpan duration)
    {
        if (!_couplesParticipant) return;
        SetEmotion(emotion, duration);
        if (emotion == CompanionEmotion.Heart) _audio.PlayCry(_capture.Id);
    }
    internal void PlayCouplesCry() { if (_couplesParticipant) _audio.PlayCry(_capture.Id); }
    internal void SetCouplesJump(double ratio)
    {
        if (!_couplesParticipant) return;
        _couplesJumpHeight = Math.Clamp(ratio, 0d, .2d);
        _couplesJumpStartedAt = DateTime.UtcNow;
    }

    internal void EndCouples(bool cancelled)
    {
        if (!_couplesParticipant) return;
        _couplesParticipant = false;
        _couplesPartnerId = null;
        if (_interactionPartnerId is not null)
        {
            var partner = _getCompanions().FirstOrDefault(item => item.RecordId == _interactionPartnerId);
            if (partner?._interactionPartnerId == RecordId) partner._interactionPartnerId = null;
        }
        _interactionPartnerId = null;
        _rallySpriteTransform.Y = 0;
        _couplesJumpStartedAt = DateTime.MinValue;
        _couplesJumpHeight = 0d;
        _walking = false;
        _column = 0;
        if (cancelled && (_activeEmotion is CompanionEmotion.Happy or CompanionEmotion.Heart or CompanionEmotion.Music))
            SetEmotion(null);
        _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(500, 1200));
        RenderFrame();
    }

    internal Point? GetBattleBodyCenter()
    {
        var body = VisibleBodyScreenBounds;
        return body.IsEmpty ? null : new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
    }

    internal bool IsAtFriendlyBattlePosition
    {
        get
        {
            var body = VisibleBodyScreenBounds;
            if (body.IsEmpty) return false;
            var centerX = body.X + body.Width / 2d;
            var centerY = body.Y + body.Height / 2d;
            return Math.Abs(_battleTargetCenterX - centerX) <= Math.Max(5d, body.Width * .08d)
                && Math.Abs(_battleTargetCenterY - centerY) <= Math.Max(5d, body.Height * .08d);
        }
    }

    internal void BeginFriendlyBattle(string partnerId, double targetCenterX, double targetCenterY, bool faceRight)
    {
        CancelInteraction();
        _sleepUntil = DateTime.MinValue;
        _fleeingAfterCollision = false;
        _battleParticipant = true;
        _battlePreparing = true;
        _battlePartnerId = partnerId;
        _battleTargetCenterX = targetCenterX;
        _battleTargetCenterY = targetCenterY;
        _direction = new Point(faceRight ? 1 : -1, 0);
        _row = DirectionRow(_direction);
        _walking = false;
        _column = 0;
        _nextDecision = DateTime.MaxValue;
        _battleCelebrationUntil = DateTime.MinValue;
        _rallySpriteTransform.Y = 0;
        SetEmotion(CompanionEmotion.Angry, TimeSpan.FromSeconds(1.7));
        RenderFrame();
    }

    internal void StopFriendlyBattlePreparation()
    {
        _battlePreparing = false;
        _walking = false;
        _column = 0;
        RenderFrame();
    }

    internal void KeepAwayFromBattle(Point center, double safeRadius, DateTime now)
    {
        if (!IsVisible || _pinned || _dragPending || _dragging || _battleParticipant || _couplesParticipant
            || _isRallyParticipant || _rallyEventActive || _toyPlayParticipant || _toyTarget.HasValue
            || _toyWaitingForPass || _requestedBerryId is not null || _megaTransformationActive
            || _battleEvacuationUntil > now)
            return;

        var body = VisibleBodyScreenBounds;
        if (body.IsEmpty) return;
        var bodyCenter = new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
        var distance = (bodyCenter - center).Length;
        if (distance > safeRadius + Math.Max(body.Width, body.Height) * .35d) return;

        _battleEvacuationCenter = center;
        _battleEvacuationSafeRadius = safeRadius + Math.Max(body.Width, body.Height) * .35d;
        _battleEvacuationLastCenter = bodyCenter;
        _battleEvacuationLastProgressAt = now;
        _direction = ChooseBattleEvacuationDirection(body, bodyCenter.X - center.X, bodyCenter.Y - center.Y,
            avoidCurrentDirection: false);
        CancelInteraction();
        _sleepUntil = DateTime.MinValue;
        _row = DirectionRow(_direction);
        _walking = true;
        _fleeingAfterCollision = true;
        _battleEvacuationUntil = now.AddSeconds(8);
        _nextDecision = _battleEvacuationUntil;
        SetEmotion(null);
        RenderFrame();
    }

    internal void EndBattleEvacuation()
    {
        if (_battleEvacuationUntil == DateTime.MinValue) return;
        _battleEvacuationUntil = DateTime.MinValue;
        _battleEvacuationLastProgressAt = DateTime.MinValue;
        _battleEvacuationSafeRadius = 0d;
        _fleeingAfterCollision = false;
        _walking = false;
        _column = 0;
        if (!_pinned && !_dragPending)
            _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(500, 1000));
        RenderFrame();
    }

    private void TickBattleEvacuation(DateTime now, double elapsed)
    {
        if (_pinned || _dragPending || _dragging)
        {
            EndBattleEvacuation();
            return;
        }
        var currentBody = VisibleBodyScreenBounds;
        if (currentBody.IsEmpty) return;
        var currentCenter = new Point(currentBody.X + currentBody.Width / 2d, currentBody.Y + currentBody.Height / 2d);
        if ((currentCenter - _battleEvacuationCenter).Length > _battleEvacuationSafeRadius)
        {
            EndBattleEvacuation();
            return;
        }
        if (now - _battleEvacuationLastProgressAt >= TimeSpan.FromMilliseconds(500))
        {
            if ((currentCenter - _battleEvacuationLastCenter).Length < 8d)
            {
                _direction = ChooseBattleEvacuationDirection(currentBody,
                    currentCenter.X - _battleEvacuationCenter.X, currentCenter.Y - _battleEvacuationCenter.Y,
                    avoidCurrentDirection: true);
                _row = DirectionRow(_direction);
            }
            _battleEvacuationLastCenter = currentCenter;
            _battleEvacuationLastProgressAt = now;
        }
        if (_edgeStunnedUntil != DateTime.MinValue)
        {
            if (now < _edgeStunnedUntil)
            {
                _walking = false;
                _column = 0;
                RenderFrame();
                return;
            }
            _edgeStunnedUntil = DateTime.MinValue;
            if (_edgeRunPending)
            {
                _edgeRunPending = false;
                _direction = _edgeRunDirection;
                _row = DirectionRow(_direction);
            }
        }
        if (GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds))
        {
            var speed = _speed * CollisionFleeSpeedMultiplier * elapsed;
            var x = bounds.Left + _direction.X * speed;
            var y = bounds.Top + _direction.Y * speed;
            if (TryMoveBattleEvacuation(x, y))
            {
                _walking = true;
                _column = (int)(_clock.ElapsedMilliseconds / 100 % 4);
            }
            else
            {
                _walking = false;
                _column = 0;
            }
        }
        RenderFrame();
    }

    private Point ChooseBattleEvacuationDirection(Rect body, double awayX, double awayY, bool avoidCurrentDirection)
    {
        var directions = new[] { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) };
        var awayLength = Math.Max(1d, Math.Sqrt(awayX * awayX + awayY * awayY));
        var bestDirection = _direction;
        var bestScore = double.NegativeInfinity;
        foreach (var direction in directions)
        {
            var clearDistance = 0d;
            for (var distance = 24d; distance <= 240d; distance += 24d)
            {
                var candidate = new Rect(body.X + direction.X * distance, body.Y + direction.Y * distance,
                    body.Width, body.Height);
                if (!IsVisibleBodyAcrossScreens(candidate) || IsBattleEvacuationPathBlocked(body, candidate)) break;
                clearDistance = distance;
            }

            var outwardProgress = (direction.X * awayX + direction.Y * awayY) / awayLength;
            var score = clearDistance * 10d + outwardProgress * 50d + _random.NextDouble() * 4d;
            if (avoidCurrentDirection && direction == _direction) score -= 20d;
            if (score <= bestScore) continue;
            bestScore = score;
            bestDirection = direction;
        }
        return bestDirection;
    }

    private bool IsBattleEvacuationPathBlocked(Rect currentBody, Rect candidate)
    {
        foreach (var other in _getCompanions())
        {
            if (other.RecordId == RecordId || !other.IsVisible || other.IsBattleParticipant) continue;
            var otherBody = other.VisibleBodyScreenBounds;
            if (!CompanionCollisionGeometry.Overlaps(candidate, otherBody)) continue;
            var currentOverlap = GetOverlapArea(currentBody, otherBody);
            var candidateOverlap = GetOverlapArea(candidate, otherBody);
            if (candidateOverlap >= currentOverlap - .5d) return true;
        }
        return false;
    }

    private static double GetOverlapArea(Rect first, Rect second)
    {
        var width = Math.Max(0d, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var height = Math.Max(0d, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        return width * height;
    }

    private bool TryMoveBattleEvacuation(double x, double y)
    {
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds)) return false;
        var body = VisibleBodyScreenBounds;
        var candidate = new Rect(body.X + x - bounds.Left, body.Y + y - bounds.Top, body.Width, body.Height);
        if (!IsVisibleBodyAcrossScreens(candidate) || IsBattleEvacuationPathBlocked(body, candidate)) return false;
        MoveAcrossAvailableScreens(x, y, bounceAtEdge: true);
        return true;
    }

    internal void FaceBattleOpponent(Point? opponentCenter)
    {
        if (opponentCenter is { } center) FaceToward(center);
    }

    internal void SetBattleJump(double ratio)
    {
        _rallySpriteTransform.Y = -Math.Clamp(ratio, 0d, .2d) * _frameHeight;
    }

    internal void SetBattleHitReaction(TimeSpan duration)
    {
        ActivateReaction(CompanionEmotion.Dizzy, duration);
    }

    internal void PlayBattleStartCry() => _audio.PlayCry(_capture.Id);

    internal void FinishFriendlyBattle(bool won, Point? opponentCenter, bool cancelled)
    {
        if (!_battleParticipant) return;
        _battleParticipant = false;
        _battlePreparing = false;
        _battlePartnerId = null;
        _rallySpriteTransform.Y = 0;
        _battleCelebrationStartedAt = DateTime.MinValue;
        _battleCelebrationUntil = DateTime.MinValue;
        _fleeingAfterCollision = false;
        _walking = false;
        _column = 0;

        if (cancelled)
        {
            if (IsDitto && _dittoTransformed)
            {
                _dittoReturnReason = "battle";
                _dittoReturnAt = DateTime.UtcNow;
            }
            SetEmotion(null);
            _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(450, 1000));
            RenderFrame();
            return;
        }

        var now = DateTime.UtcNow;
        if (won)
        {
            var duration = TimeSpan.FromMilliseconds(2800);
            _battleCelebrationStartedAt = now;
            _battleCelebrationUntil = now + duration;
            SetEmotion(CompanionEmotion.Win, duration);
            _audio.PlayCry(_capture.Id);
            SetBattleJump(.14d);
            _nextDecision = _battleCelebrationUntil;
            if (IsDitto && _dittoTransformed)
            {
                _dittoReturnReason = "battle";
                _dittoReturnAt = _battleCelebrationUntil;
            }
            RenderFrame();
            return;
        }

        var ownCenter = GetBattleBodyCenter();
        var direction = ownCenter.HasValue && opponentCenter.HasValue
            ? ownCenter.Value.X < opponentCenter.Value.X ? -1 : 1
            : _random.Next(2) == 0 ? -1 : 1;
        _direction = new Point(direction, 0);
        _row = DirectionRow(_direction);
        _walking = true;
        _fleeingAfterCollision = true;
        var fleeDuration = TimeSpan.FromMilliseconds(_random.Next(1400, 2201));
        _nextDecision = now + fleeDuration;
        if (IsDitto && _dittoTransformed)
        {
            _dittoReturnReason = "battle";
            _dittoReturnAt = _nextDecision;
        }
        SetEmotion(CompanionEmotion.Sad, fleeDuration);
        RenderFrame();
    }

    internal void PlayMegaEvolutionSounds()
    {
        if (_capture.MegaFormId is null) return;
        _audio.PlayEffect("sounds/effects/mega-evolution.mp3");
        _audio.PlayCry(_capture.Id);
    }

    internal void ProtectFromNegativeInteractionWith(DesktopCompanionWindow other, DateTime until)
    {
        _positiveInteractionUntil[other.RecordId] = until;
        other._positiveInteractionUntil[RecordId] = until;
    }

    private void UpdateRallyJumpVisual(DateTime now)
    {
        if (_couplesParticipant && _couplesJumpStartedAt != DateTime.MinValue)
        {
            var couplesProgress = (now - _couplesJumpStartedAt).TotalSeconds / .52d;
            if (couplesProgress >= 1d)
            {
                _rallySpriteTransform.Y = 0;
                _couplesJumpStartedAt = DateTime.MinValue;
                _couplesJumpHeight = 0d;
            }
            else
            {
                _rallySpriteTransform.Y = -Math.Sin(couplesProgress * Math.PI)
                    * Math.Clamp(_frameHeight * _couplesJumpHeight, 4d, 12d);
            }
            return;
        }
        if (!_rallyCelebrating)
        {
            if (_rallySpriteTransform.Y != 0) _rallySpriteTransform.Y = 0;
            return;
        }
        if (_rallyJumpStartedAt == DateTime.MinValue && now >= _nextRallyJumpAt)
            _rallyJumpStartedAt = now;
        if (_rallyJumpStartedAt == DateTime.MinValue) return;
        var progress = (now - _rallyJumpStartedAt).TotalSeconds / .52d;
        if (progress >= 1d)
        {
            _rallySpriteTransform.Y = 0;
            _rallyJumpStartedAt = DateTime.MinValue;
            _nextRallyJumpAt = now.AddMilliseconds(_random.Next(1800, 3900));
            return;
        }
        // Visual only: actual window position and collision bounds do not change.
        _rallySpriteTransform.Y = -Math.Sin(progress * Math.PI) * Math.Clamp(_frameHeight * .12d, 4d, 12d);
    }

    internal void CancelTeamRally()
    {
        if (!_isRallyParticipant && _rallyTarget is null) return;
        _isRallyParticipant = false;
        _rallyApproachFailed = false;
        _rallyTarget = null;
        _rallyDetourTarget = null;
        _rallyArrived = false;
        _rallyBlockedAttempts = 0;
        _rallyLastProgressAt = DateTime.MinValue;
        _rallyCelebrating = false;
        _rallyHorizontalFirst = null;
        _rallyAxisRemaining = 0d;
        _rallyUntil = DateTime.MinValue;
        _rallyJumpStartedAt = DateTime.MinValue;
        _rallySpriteTransform.Y = 0;
        _walking = false;
        if (_activeEmotion is CompanionEmotion.Music or CompanionEmotion.Happy or CompanionEmotion.Heart) SetEmotion(null);
        if (_pinned) ShowPinnedSleepReaction();
        if (!_pinned) _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(300, 800));
        RenderFrame();
    }

    private void TickTeamRally(DateTime now, double elapsed)
    {
        if (now >= _rallyUntil)
        {
            if (!_rallyCelebrating) EndTeamRallyApproach();
            else { _walking = false; _column = 0; }
            RenderFrame();
            return;
        }
        if (_pinned || _dragPending || _rallyArrived || _rallyTarget is not { } target)
        {
            _walking = false;
            _column = 0;
            RenderFrame();
            return;
        }

        var body = VisibleBodyScreenBounds;
        var center = new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
        var movementTarget = _rallyDetourTarget ?? target;
        var dx = movementTarget.X - center.X;
        var dy = movementTarget.Y - center.Y;
        if (Math.Sqrt(dx * dx + dy * dy) <= Math.Max(12, Math.Max(body.Width, body.Height) * .22))
        {
            if (_rallyDetourTarget.HasValue)
            {
                _rallyDetourTarget = null;
                _rallyHorizontalFirst = null;
                _rallyAxisRemaining = 0d;
            }
            else
            {
                _rallyTarget = null;
                _rallyArrived = true;
                _walking = false;
                _column = 0;
                SetEmotion(CompanionEmotion.Happy, TimeSpan.FromSeconds(1.8));
            }
            RenderFrame();
            return;
        }

        const double axisAlignmentTolerance = 10d;
        _rallyHorizontalFirst ??= Math.Abs(dx) >= Math.Abs(dy);
        var rallyAxisDelta = _rallyHorizontalFirst.Value ? dx : dy;
        var rallyOtherDelta = _rallyHorizontalFirst.Value ? dy : dx;
        if ((Math.Abs(rallyAxisDelta) <= axisAlignmentTolerance
                && Math.Abs(rallyOtherDelta) > axisAlignmentTolerance)
            || (_rallyAxisRemaining <= axisAlignmentTolerance
                && Math.Abs(rallyOtherDelta) > axisAlignmentTolerance))
        {
            _rallyHorizontalFirst = !_rallyHorizontalFirst.Value;
            rallyOtherDelta = _rallyHorizontalFirst.Value ? dy : dx;
            _rallyAxisRemaining = Math.Min(CardinalStairSegmentPixels, Math.Abs(rallyOtherDelta));
        }
        _direction = _rallyHorizontalFirst.Value
            ? new Point(Math.Sign(dx), 0)
            : new Point(0, Math.Sign(dy));
        _row = DirectionRow(_direction);
        _walking = false;
        _column = 0;
        if (GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds))
        {
            var speed = _speed * RallyRunSpeedMultiplier * elapsed;
            var nextX = bounds.Left + _direction.X * speed;
            var nextY = bounds.Top + _direction.Y * speed;
            var moved = TryMoveRallyWithoutCollision(nextX, nextY, out var blocker)
                && GetWindowRect(new WindowInteropHelper(this).Handle, out var afterMove)
                && Math.Abs(afterMove.Left - bounds.Left) + Math.Abs(afterMove.Top - bounds.Top) >= .5d;
            if (moved)
            {
                _rallyLastProgressAt = now;
                _rallyBlockedAttempts = 0;
                _rallyAxisRemaining = Math.Max(0d, _rallyAxisRemaining - speed);
                _walking = true;
            }
            else
            {
                _walking = false;
                _column = 0;
                if (now - _rallyLastProgressAt >= TimeSpan.FromSeconds(1))
                {
                    // Give a pair a stable right-of-way. Only the Pokémon with
                    // the later ID detours; the other waits briefly instead of
                    // both choosing matching escape directions.
                    var yieldToBlocker = blocker?.IsRallyParticipant == true
                        && string.CompareOrdinal(RecordId, blocker.RecordId) < 0;
                    if (yieldToBlocker)
                    {
                        _rallyLastProgressAt = now;
                        _rallyBlockedAttempts = 0;
                    }
                    else
                    {
                        _rallyBlockedAttempts++;
                        var foundRoute = blocker is not null && TrySetRallyDetour(blocker, target);
                        if (!foundRoute) foundRoute = TrySetNearbyRallyWaypoint(target);
                        if (foundRoute)
                        {
                            _rallyLastProgressAt = now;
                        }
                        else if (_rallyBlockedAttempts >= 3)
                        {
                            EndTeamRallyApproach();
                        }
                        else
                        {
                            _rallyLastProgressAt = now;
                            // Try the other cardinal axis once, then hold it
                            // until movement resumes instead of oscillating.
                            _rallyHorizontalFirst = !_rallyHorizontalFirst.Value;
                            _rallyAxisRemaining = CardinalStairSegmentPixels;
                        }
                    }
                }
            }
            if (_walking) _column = (int)(_clock.ElapsedMilliseconds / 90 % 4);
        }
        RenderFrame();
    }

    internal void EndTeamRallyApproach()
    {
        if (!_isRallyParticipant || _rallyCelebrating) return;
        _rallyApproachFailed = true;
        _isRallyParticipant = false;
        _rallyArrived = false;
        _rallyDetourTarget = null;
        _rallyHorizontalFirst = null;
        _rallyAxisRemaining = 0d;
        _rallyBlockedAttempts = 0;
        _rallyUntil = DateTime.MinValue;
        _walking = false;
        _column = 0;
        if (_activeEmotion is CompanionEmotion.Music or CompanionEmotion.Happy or CompanionEmotion.Heart)
            SetEmotion(null);
        if (!_pinned) _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(300, 800));
    }

    private bool TryMoveRallyWithoutCollision(double x, double y, out DesktopCompanionWindow? blocker)
    {
        blocker = null;
        MoveAcrossAvailableScreens(x, y, bounceAtEdge: true);
        return true;
    }

    private bool TrySetRallyDetour(DesktopCompanionWindow blocker, Point rallyTarget)
    {
        var ownBody = VisibleBodyScreenBounds;
        var obstacle = blocker.VisibleBodyScreenBounds;
        if (ownBody.IsEmpty || obstacle.IsEmpty) return false;
        var ownCenter = new Point(ownBody.X + ownBody.Width / 2d, ownBody.Y + ownBody.Height / 2d);
        var obstacleCenter = new Point(obstacle.X + obstacle.Width / 2d, obstacle.Y + obstacle.Height / 2d);
        var towardObstacle = obstacleCenter - ownCenter;
        var horizontalPass = Math.Abs(towardObstacle.X) >= Math.Abs(towardObstacle.Y);
        var clearance = (horizontalPass ? ownBody.Height + obstacle.Height : ownBody.Width + obstacle.Width) / 2d
            + Math.Clamp(Math.Max(ownBody.Width, ownBody.Height) * .18d, 12d, 30d);
        var candidates = new List<Point>();
        if (horizontalPass)
        {
            candidates.Add(new Point(obstacleCenter.X, obstacleCenter.Y - clearance));
            candidates.Add(new Point(obstacleCenter.X, obstacleCenter.Y + clearance));
            candidates.Add(new Point(obstacleCenter.X - clearance, obstacleCenter.Y));
            candidates.Add(new Point(obstacleCenter.X + clearance, obstacleCenter.Y));
        }
        else
        {
            candidates.Add(new Point(obstacleCenter.X - clearance, obstacleCenter.Y));
            candidates.Add(new Point(obstacleCenter.X + clearance, obstacleCenter.Y));
            candidates.Add(new Point(obstacleCenter.X, obstacleCenter.Y - clearance));
            candidates.Add(new Point(obstacleCenter.X, obstacleCenter.Y + clearance));
        }

        var validCandidates = candidates
            .Where(point => IsRallyWaypointClear(point, ownBody.Width, ownBody.Height))
            .Where(point => !_rallyDetourTarget.HasValue
                || (point - _rallyDetourTarget.Value).Length > Math.Max(12d, clearance * .4d))
            .OrderBy(point => Math.Abs(point.X - rallyTarget.X) + Math.Abs(point.Y - rallyTarget.Y))
            .ToArray();
        if (validCandidates.Length == 0) return false;
        var selected = validCandidates[0];
        if ((selected - ownCenter).Length < Math.Max(12d, clearance * .45d)) return false;
        _rallyDetourTarget = selected;
        _rallyHorizontalFirst = Math.Abs(selected.X - ownCenter.X) >= Math.Abs(selected.Y - ownCenter.Y);
        _rallyAxisRemaining = CardinalStairSegmentPixels;
        return true;
    }

    private bool IsRallyWaypointClear(Point center, double width, double height)
    {
        var candidate = new Rect(center.X - width / 2d, center.Y - height / 2d, width, height);
        var clearance = Math.Clamp(Math.Max(width, height) * .08d, 6d, 12d);
        candidate.Inflate(clearance, clearance);
        if (_getCompanions().Where(item => item.RecordId != RecordId && item.IsVisible)
            .Any(item => CompanionCollisionGeometry.Overlaps(candidate, item.VisibleBodyScreenBounds))) return false;
        return _screens.Any(screen => screen.WorkingArea.Contains((int)candidate.Left, (int)candidate.Top)
            && screen.WorkingArea.Contains((int)candidate.Right, (int)candidate.Bottom));
    }

    private bool TrySetNearbyRallyWaypoint(Point rallyTarget)
    {
        var body = VisibleBodyScreenBounds;
        if (body.IsEmpty) return false;
        var center = new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
        var stride = Math.Max(48d, Math.Max(body.Width, body.Height) * 1.25d);
        var candidates = new List<Point>();
        foreach (var distance in new[] { stride, stride * 1.75d })
        {
            candidates.Add(new Point(center.X - distance, center.Y));
            candidates.Add(new Point(center.X + distance, center.Y));
            candidates.Add(new Point(center.X, center.Y - distance));
            candidates.Add(new Point(center.X, center.Y + distance));
        }

        var valid = candidates
            .Where(point => IsRallyWaypointClear(point, body.Width, body.Height))
            .Where(point => (point - center).Length >= stride * .8d)
            .Where(point => !_rallyDetourTarget.HasValue
                || (point - _rallyDetourTarget.Value).Length > stride * .6d)
            .OrderBy(point => Math.Abs(point.X - rallyTarget.X) + Math.Abs(point.Y - rallyTarget.Y))
            .ToArray();
        if (valid.Length == 0) return false;
        var selected = valid[0];
        _rallyDetourTarget = selected;
        _rallyHorizontalFirst = Math.Abs(selected.X - center.X) >= Math.Abs(selected.Y - center.Y);
        _rallyAxisRemaining = CardinalStairSegmentPixels;
        return true;
    }

    internal void StartToyChase(Point target)
    {
        if (!CanPlayWithToy) return;
        StartToyPursuit(target);
    }

    internal void StartToyPursuit(Point target)
        => StartToyPursuit(target, run: false);

    internal void StartToyPursuit(Point target, bool run, bool waitForBallSeparation = false)
    {
        _toyPlayParticipant = true;
        _toyWaitingForPass = false;
        _toyChaseIsRunning = run;
        _toyAwaitingBallSeparation = waitForBallSeparation;
        _toySeparationOrigin = target;
        _toyTarget = target;
        var body = VisibleBodyScreenBounds;
        var center = body.IsEmpty ? target : new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
        _toyChaseHorizontalFirst = Math.Abs(target.X - center.X) >= Math.Abs(target.Y - center.Y);
        _toyChaseAxisRemaining = Math.Min(CardinalStairSegmentPixels,
            Math.Max(24d, (_toyChaseHorizontalFirst.Value ? Math.Abs(target.X - center.X)
                : Math.Abs(target.Y - center.Y)) * .5d));
        _toyBestDistance = double.PositiveInfinity;
        _toyLastProgressAt = DateTime.UtcNow;
        _walking = true;
    }

    internal void FaceToward(Point target)
    {
        var body = VisibleBodyScreenBounds;
        if (body.IsEmpty) return;
        var center = new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
        var dx = target.X - center.X;
        var dy = target.Y - center.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < .001d) return;
        _direction = Math.Abs(dx) > Math.Abs(dy)
            ? new Point(Math.Sign(dx), 0)
            : new Point(0, Math.Sign(dy));
        _row = DirectionRow(_direction);
        _walking = false;
        _column = 0;
        RenderFrame();
    }

    internal void JoinToyGame()
    {
        _toyPlayParticipant = true;
        _toyWaitingForPass = true;
        _toyChaseIsRunning = false;
        _toyAwaitingBallSeparation = false;
        _toyTarget = null;
        _walking = false;
        _column = 0;
        RenderFrame();
    }

    internal void WaitForToyPass() => JoinToyGame();

    internal void ResumeToyPlayMovement() => _toyWaitingForPass = false;

    internal void UpdateToyChaseTarget(Point target)
    {
        if (_toyTarget is not { } previous) return;
        var targetMovement = (target - previous).Length;
        if (targetMovement >= 6d)
        {
            // The ball is still rolling away; do not mistake that for a blocked
            // Pokémon before it has a chance to catch up after the roll ends.
            _toyBestDistance = double.PositiveInfinity;
            _toyLastProgressAt = DateTime.UtcNow;
        }
        _toyTarget = target;
    }

    internal void StopToyChase()
    {
        _toyTarget = null;
        _toyPlayParticipant = false;
        _toyWaitingForPass = false;
        _toyChaseIsRunning = false;
        _toyChaseHorizontalFirst = null;
        _toyChaseAxisRemaining = 0d;
        _toyAwaitingBallSeparation = false;
        _toyBestDistance = double.PositiveInfinity;
        if (!_pinned && !_dragPending) _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(300, 800));
    }

    private void AbandonToyChase(DateTime now)
    {
        _toyTarget = null;
        _toyPlayParticipant = false;
        _toyWaitingForPass = false;
        _toyChaseIsRunning = false;
        _toyChaseHorizontalFirst = null;
        _toyChaseAxisRemaining = 0d;
        _toyAwaitingBallSeparation = false;
        _toyBestDistance = double.PositiveInfinity;
        _walking = false;
        _nextDecision = now.AddMilliseconds(_random.Next(180, 450));
        // Let this Pokémon resume ordinary wandering instead of immediately
        // repeating an approach it could not complete.
        _toyRetryAfter = now.AddSeconds(_random.Next(14, 24));
        _cancelToyChase(this);
        RenderFrame();
    }

    private void TickToyChase(Point target, DateTime now, double elapsed)
    {
        if (_pinned || _dragPending || _interactionPartnerId is not null)
        {
            StopToyChase();
            return;
        }
        var body = VisibleBodyScreenBounds;
        if (body.IsEmpty) return;
        var center = new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d);
        var dx = target.X - center.X;
        var dy = target.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (_toyBestDistance == double.PositiveInfinity || distance < _toyBestDistance - 3d)
        {
            _toyBestDistance = distance;
            _toyLastProgressAt = now;
        }
        else if (now - _toyLastProgressAt >= TimeSpan.FromSeconds(ToyChaseNoProgressLimitSeconds))
        {
            AbandonToyChase(now);
            return;
        }
        var toyBounds = _getToyBounds();
        var toyRadius = toyBounds.IsEmpty ? 9d : Math.Min(toyBounds.Width, toyBounds.Height) / 2d;
        var contactBody = ToyContactScreenBounds;
        if (_toyAwaitingBallSeparation)
        {
            var ballTravel = (target - _toySeparationOrigin).Length;
            var separationDistance = Math.Max(24d * _visualScale, toyRadius + Math.Min(body.Width, body.Height) * .45d);
            if (ballTravel >= separationDistance && !CircleOverlapsRect(target, toyRadius, body))
                _toyAwaitingBallSeparation = false;
            else
            {
                _walking = false;
                _column = 0;
                RenderFrame();
                return;
            }
        }
        var step = _speed * (_toyChaseIsRunning ? SoloToyRunSpeedMultiplier : .72d) * elapsed;
        // Hold one cardinal axis until aligned, then switch. Re-selecting the
        // dominant axis every frame makes the Pokémon zig-zag diagonally.
        const double axisAlignmentTolerance = 10d;
        _toyChaseHorizontalFirst ??= Math.Abs(dx) >= Math.Abs(dy);
        var chaseAxisDelta = _toyChaseHorizontalFirst.Value ? dx : dy;
        var chaseOtherDelta = _toyChaseHorizontalFirst.Value ? dy : dx;
        if ((Math.Abs(chaseAxisDelta) <= axisAlignmentTolerance
                && Math.Abs(chaseOtherDelta) > axisAlignmentTolerance)
            || (_toyChaseAxisRemaining <= axisAlignmentTolerance
                && Math.Abs(chaseOtherDelta) > axisAlignmentTolerance))
        {
            _toyChaseHorizontalFirst = !_toyChaseHorizontalFirst.Value;
            chaseOtherDelta = _toyChaseHorizontalFirst.Value ? dy : dx;
            _toyChaseAxisRemaining = Math.Min(CardinalStairSegmentPixels, Math.Abs(chaseOtherDelta));
        }
        var horizontalFirst = _toyChaseHorizontalFirst.Value;
        var firstDirection = horizontalFirst
            ? new Point(Math.Sign(dx), 0)
            : new Point(0, Math.Sign(dy));
        var secondDirection = horizontalFirst
            ? new Point(0, Math.Sign(dy))
            : new Point(Math.Sign(dx), 0);
        Func<Point, bool> touchesToy = direction => CircleOverlapsRect(target, toyRadius,
            new Rect(contactBody.X + direction.X * step, contactBody.Y + direction.Y * step,
                contactBody.Width, contactBody.Height));
        if (CircleOverlapsRect(target, toyRadius, contactBody) || touchesToy(firstDirection))
        {
            _toyTarget = null;
            _walking = false;
            _column = 0;
            SetEmotion(CompanionEmotion.Happy, TimeSpan.FromSeconds(1.8));
            if (distance > 0)
            {
                _direction = Math.Abs(dx) > Math.Abs(dy)
                    ? new Point(Math.Sign(dx), 0)
                    : new Point(0, Math.Sign(dy));
                _row = DirectionRow(_direction);
                _toyPushed(this, new Point(dx / distance, dy / distance));
            }
            RenderFrame();
            return;
        }
        _walking = false;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            foreach (var direction in new[] { firstDirection, secondDirection })
            {
                if (direction.X == 0 && direction.Y == 0) continue;
                if (!GetWindowRect(handle, out var beforeMove)) break;
                if (!TryMoveWithoutCollision(beforeMove.Left + direction.X * step,
                    beforeMove.Top + direction.Y * step, now)) continue;
                // Screen-edge clamping can report a successful move even when
                // the window stayed in place. Treat that axis as blocked and
                // try the other one so a target above/below remains reachable.
                if (!GetWindowRect(handle, out var afterMove)
                    || Math.Abs(afterMove.Left - beforeMove.Left)
                        + Math.Abs(afterMove.Top - beforeMove.Top) < .5d)
                    continue;
                var movedX = Math.Abs(afterMove.Left - beforeMove.Left);
                var movedY = Math.Abs(afterMove.Top - beforeMove.Top);
                _toyChaseAxisRemaining = Math.Max(0d,
                    _toyChaseAxisRemaining - (direction.X != 0 ? movedX : movedY));
                _toyChaseHorizontalFirst = direction.X != 0;
                _direction = direction;
                _row = DirectionRow(_direction);
                _walking = true;
                break;
            }
        }
        var frameDuration = _toyChaseIsRunning ? SoloToyRunFrameMilliseconds : 140;
        _column = _walking ? (int)(_clock.ElapsedMilliseconds / frameDuration % 4) : 0;
        if (!_walking && now - _toyLastProgressAt >= TimeSpan.FromSeconds(ToyChaseNoProgressLimitSeconds))
        {
            AbandonToyChase(now);
            return;
        }
        RenderFrame();
    }

    private static bool CircleOverlapsRect(Point center, double radius, Rect rect)
    {
        if (rect.IsEmpty) return false;
        var closestX = Math.Clamp(center.X, rect.Left, rect.Right);
        var closestY = Math.Clamp(center.Y, rect.Top, rect.Bottom);
        var dx = center.X - closestX;
        var dy = center.Y - closestY;
        return dx * dx + dy * dy <= radius * radius;
    }

    private void PositionBerryRequest()
    {
        var bounds = _visibleFrameBounds[_row, _column];
        var centerX = _spriteOffsetX + bounds.X + bounds.Width / 2d;
        Canvas.SetLeft(_berryRequestBubble, Math.Clamp(centerX - _berryRequestBubble.Width / 2d, 0, Math.Max(0, _canvasWidth - _berryRequestBubble.Width)));
        Canvas.SetTop(_berryRequestBubble, Math.Max(0, SpriteCanvasTop + bounds.Y - _berryRequestBubble.Height - 3));
    }

    // The walking sheets are ordered down/front, left, right, up/back.
    private static int DirectionRow(Point direction) => Math.Abs(direction.X) > Math.Abs(direction.Y)
        ? direction.X < 0 ? 1 : 2
        : direction.Y < 0 ? 3 : 0;

    internal void ActivateReaction(CompanionEmotion emotion, TimeSpan? duration = null)
    {
        if (DateTime.UtcNow < _feedingReactionUntil && emotion != CompanionEmotion.Heart) return;
        CancelInteraction();
        _sleepUntil = DateTime.MinValue;
        _fleeingAfterCollision = false;
        var reactionDuration = duration ?? TimeSpan.FromSeconds(2.5);
        _walking = false;
        _column = 0;
        _nextDecision = DateTime.UtcNow + reactionDuration;
        SetEmotion(emotion, reactionDuration);
        RenderFrame();
    }

    private void ActivateFeedingReaction()
    {
        _feedingReactionUntil = DateTime.UtcNow.AddMilliseconds(2500);
        ActivateReaction(CompanionEmotion.Heart, TimeSpan.FromMilliseconds(2500));
        StartShinySparkles(restart: true);
        StartMegaAura(restart: true);
        StartAlphaEffect(restart: true);
    }

    private void OnBerryDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!_megaTransformationActive && e.Data.GetData(BagWindow.BerryFeedDragFormat) is string berryId && _save.Count(berryId) > 0)
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void OnBerryDrop(object sender, System.Windows.DragEventArgs e)
    {
        _cancelBattle(this);
        if (_megaTransformationActive || e.Data.GetData(BagWindow.BerryFeedDragFormat) is not string berryId
            || !_feedBerry(berryId, _requestedBerryId))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (_isRallyParticipant) _cancelRally(this);
        ActivateFeedingReaction();
        if (_requestedBerryId is not null && berryId.Equals(_requestedBerryId, StringComparison.OrdinalIgnoreCase))
            ClearBerryRequest();
        _audio.PlayCry(_capture.Id);
        e.Effects = System.Windows.DragDropEffects.Copy;
        e.Handled = true;
    }

    private bool TryStartNearbyInteraction(DateTime now)
    {
        if (_battleParticipant) return false;
        _nextSocialDecision = now.AddSeconds(_random.Next(SocialCheckMinSeconds, SocialCheckMaxSecondsExclusive));
        if (_pinned || _dragPending || _interactionPartnerId is not null || now < _reactionUntil || now < _faceUserUntil)
            return false;

        var body = VisibleBodyScreenBounds;
        if (body.IsEmpty) return false;
        foreach (var other in _getCompanions().Where(item => item.RecordId != RecordId).OrderBy(item => item.RecordId, StringComparer.Ordinal))
        {
            if (string.CompareOrdinal(RecordId, other.RecordId) >= 0
                || !other.IsVisible || other._pinned || other._dragPending || other._interactionPartnerId is not null
                || other._battleParticipant
                || now < other._reactionUntil || now < other._faceUserUntil)
                continue;
            var otherBody = other.VisibleBodyScreenBounds;
            if (otherBody.IsEmpty) continue;
            if (CompanionCollisionGeometry.Overlaps(body, otherBody)) continue;
            var range = Math.Max(SocialRangePixels * GetDpiScale() * _visualScale, (body.Width + otherBody.Width) * .65);
            if (!CompanionCollisionGeometry.IsNear(body, otherBody, range)) continue;

            var choice = _random.Next(100);
            if (choice >= 84 && HasRecentPositiveInteraction(other, now))
                choice = _random.Next(84); // Keep their interaction positive during the pair cooldown.
            if (choice < 84 && HasRecentNegativeInteraction(other, now)) continue;
            var duration = TimeSpan.FromMilliseconds(_random.Next(1900, 2900));
            var (first, second) = choice < 56
                ? (_random.Next(3) == 0 ? CompanionEmotion.Heart : CompanionEmotion.Happy, CompanionEmotion.Happy)
                : choice < 84 ? (CompanionEmotion.Music, CompanionEmotion.Music)
                : (CompanionEmotion.Angry, CompanionEmotion.Sad);
            BeginInteraction(other, first, second, duration, now);
            return true;
        }
        return false;
    }

    private void BeginInteraction(DesktopCompanionWindow other, CompanionEmotion first, CompanionEmotion second,
        TimeSpan duration, DateTime now)
    {
        if (IsNegativeEmotion(first) || IsNegativeEmotion(second))
            RegisterRecentNegativeInteraction(other, now);
        if (IsPositiveEmotion(first) && IsPositiveEmotion(second))
        {
            var protectedUntil = now.AddSeconds(PositivePairNegativeImmunitySeconds);
            _positiveInteractionUntil[other.RecordId] = protectedUntil;
            other._positiveInteractionUntil[RecordId] = protectedUntil;
        }
        _interactionPartnerId = other.RecordId;
        other._interactionPartnerId = RecordId;
        _interactionUntil = other._interactionUntil = now + duration;
        _walking = other._walking = false;
        _fleeingAfterCollision = other._fleeingAfterCollision = false;
        _column = other._column = 0;
        FaceEachOther(other);
        SetEmotion(first, duration);
        other.SetEmotion(second, duration);
        if (first == CompanionEmotion.Music) _audio.PlayCry(_capture.Id);
        if (second == CompanionEmotion.Music) other._audio.PlayCry(other._capture.Id);
        _nextSocialDecision = other._nextSocialDecision = now.AddSeconds(
            _random.Next(SocialCooldownMinSeconds, SocialCooldownMaxSecondsExclusive));
    }

    private void FaceEachOther(DesktopCompanionWindow other)
    {
        var first = VisibleBodyScreenBounds;
        var second = other.VisibleBodyScreenBounds;
        var dx = second.X + second.Width / 2 - (first.X + first.Width / 2);
        var dy = second.Y + second.Height / 2 - (first.Y + first.Height / 2);
        var firstDirection = Math.Abs(dx) >= Math.Abs(dy)
            ? new Point(dx >= 0 ? 1 : -1, 0)
            : new Point(0, dy >= 0 ? 1 : -1);
        _row = DirectionRow(firstDirection);
        other._row = DirectionRow(new Point(-firstDirection.X, -firstDirection.Y));
    }

    private void FinishInteraction()
    {
        var partnerId = _interactionPartnerId;
        _interactionPartnerId = null;
        _interactionUntil = DateTime.MinValue;
        SetEmotion(null);
        _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(500, 1200));
        if (partnerId is null) return;
        var partner = _getCompanions().FirstOrDefault(item => item.RecordId == partnerId);
        if (partner is null || partner._interactionPartnerId != RecordId) return;
        partner._interactionPartnerId = null;
        partner._interactionUntil = DateTime.MinValue;
        partner.SetEmotion(null);
        partner._nextDecision = DateTime.UtcNow.AddMilliseconds(partner._random.Next(500, 1200));
    }

    private bool HasRecentPositiveInteraction(DesktopCompanionWindow other, DateTime now)
    {
        if (_positiveInteractionUntil.TryGetValue(other.RecordId, out var ownUntil))
        {
            if (now < ownUntil) return true;
            _positiveInteractionUntil.Remove(other.RecordId);
        }
        if (other._positiveInteractionUntil.TryGetValue(RecordId, out var otherUntil))
        {
            if (now < otherUntil) return true;
            other._positiveInteractionUntil.Remove(RecordId);
        }
        return false;
    }

    private static bool IsPositiveEmotion(CompanionEmotion emotion)
        => emotion is CompanionEmotion.Happy or CompanionEmotion.Heart or CompanionEmotion.Music;

    private static bool IsNegativeEmotion(CompanionEmotion emotion)
        => emotion is CompanionEmotion.Angry or CompanionEmotion.Sad;

    private void RegisterRecentNegativeInteraction(DesktopCompanionWindow other, DateTime now)
    {
        var blockedUntil = now.AddSeconds(NegativePairPositiveCooldownSeconds);
        _negativeInteractionUntil[other.RecordId] = blockedUntil;
        other._negativeInteractionUntil[RecordId] = blockedUntil;
    }

    private bool HasRecentNegativeInteraction(DesktopCompanionWindow other, DateTime now)
    {
        if (_negativeInteractionUntil.TryGetValue(other.RecordId, out var ownUntil))
        {
            if (now < ownUntil) return true;
            _negativeInteractionUntil.Remove(other.RecordId);
        }
        if (other._negativeInteractionUntil.TryGetValue(RecordId, out var otherUntil))
        {
            if (now < otherUntil) return true;
            other._negativeInteractionUntil.Remove(RecordId);
        }
        return false;
    }

    private void CancelInteraction()
    {
        var partnerId = _interactionPartnerId;
        _interactionPartnerId = null;
        _interactionUntil = DateTime.MinValue;
        SetEmotion(null);
        if (partnerId is null) return;
        var partner = _getCompanions().FirstOrDefault(item => item.RecordId == partnerId);
        if (partner is null || partner._interactionPartnerId != RecordId) return;
        partner._interactionPartnerId = null;
        partner._interactionUntil = DateTime.MinValue;
        partner.SetEmotion(null);
        partner._nextDecision = DateTime.UtcNow.AddMilliseconds(partner._random.Next(400, 900));
    }

    private void SetEmotion(CompanionEmotion? emotion, TimeSpan? duration = null)
    {
        if (emotion is { } next && next != CompanionEmotion.Heart && DateTime.UtcNow < _feedingReactionUntil) return;
        _activeEmotion = emotion;
        _reactionImage.Source = emotion is { } value ? LoadReactionSprite(value) : null;
        _reactionUntil = emotion.HasValue ? DateTime.UtcNow + (duration ?? TimeSpan.FromSeconds(2.5)) : DateTime.MinValue;
        _reactionImage.Visibility = emotion.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (emotion.HasValue) RaiseAboveCompanionSprites();
        PositionReaction();
    }

    private void RaiseAboveCompanionSprites()
    {
        // Keep the sprite inside the desktop-companion band. Raising this HWND
        // directly can put it above the PC and makes it flash until the next
        // application-wide Z-order refresh.
        _restoreOverlayZOrder(this);
    }

    internal void RaiseAboveToy() => RaiseAboveCompanionSprites();

    private static BitmapSource LoadReactionSprite(CompanionEmotion emotion)
        => ReactionSprites[emotion];

    private static BitmapSource LoadReactionSpriteCore(CompanionEmotion emotion)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "ui", "pokemon_reactions", $"{emotion.ToString().ToLowerInvariant()}.png");
        var source = LoadBitmap(path);
        return ScaleNearestNeighbor(source, ReactionSpriteSize, ReactionSpriteSize);
    }

    private void PositionReaction()
    {
        if (_activeEmotion is null) return;
        var bounds = _visibleFrameBounds[_row, _column];
        var centerX = _spriteOffsetX + bounds.X + bounds.Width / 2d;
        Canvas.SetLeft(_reactionImage, Math.Clamp(centerX - ReactionSpriteSize / 2d, 0, Math.Max(0, _canvasWidth - ReactionSpriteSize)));
        Canvas.SetTop(_reactionImage, Math.Max(0, SpriteCanvasTop + bounds.Y - ReactionSpriteSize - 2));
    }

    private void TickFriendlyBattlePreparation(double elapsed)
    {
        if (!_battlePreparing)
        {
            _walking = false;
            _column = 0;
            RenderFrame();
            return;
        }
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds)) return;
        var body = VisibleBodyScreenBounds;
        if (body.IsEmpty) return;
        var centerX = body.X + body.Width / 2d;
        var centerY = body.Y + body.Height / 2d;
        var dx = _battleTargetCenterX - centerX;
        var dy = _battleTargetCenterY - centerY;
        if (IsAtFriendlyBattlePosition)
        {
            _walking = false;
            _column = 0;
            RenderFrame();
            return;
        }

        var horizontalFirst = Math.Abs(dx) >= Math.Abs(dy);
        var direction = horizontalFirst ? new Point(Math.Sign(dx), 0) : new Point(0, Math.Sign(dy));
        var step = Math.Max(1d, _speed * 1.5d * elapsed);
        var moveX = horizontalFirst ? Math.Sign(dx) * Math.Min(Math.Abs(dx), step) : 0d;
        var moveY = horizontalFirst ? 0d : Math.Sign(dy) * Math.Min(Math.Abs(dy), step);
        // Battle participants have no hitbox for the duration of the event;
        // bystanders are evacuated separately by DesktopPokemonBattle.
        _direction = direction;
        _row = DirectionRow(_direction);
        _walking = true;
        _column = (int)(_clock.ElapsedMilliseconds / 140 % 4);
        MoveAcrossAvailableScreens(bounds.Left + moveX, bounds.Top + moveY, bounceAtEdge: true);
        RenderFrame();
    }

    private void UpdateBattleCelebration(DateTime now)
    {
        if (_battleCelebrationStartedAt == DateTime.MinValue) return;
        if (now >= _battleCelebrationUntil)
        {
            _rallySpriteTransform.Y = 0;
            _battleCelebrationStartedAt = DateTime.MinValue;
            _battleCelebrationUntil = DateTime.MinValue;
            return;
        }
        var duration = (_battleCelebrationUntil - _battleCelebrationStartedAt).TotalSeconds;
        var progress = Math.Clamp((now - _battleCelebrationStartedAt).TotalSeconds / Math.Max(.01d, duration), 0d, 1d);
        _rallySpriteTransform.Y = -Math.Abs(Math.Sin(progress * Math.PI * 4d)) * Math.Clamp(_frameHeight * .14d, 5d, 18d);
    }

    private bool TrySeparateOverlap(DateTime now)
    {
        if (_rallyEventActive || _battleParticipant || _pinned || _dragPending) return false;
        var body = VisibleBodyScreenBounds;
        foreach (var other in _getCompanions().Where(item => item.RecordId != RecordId && item.IsVisible))
        {
            if (other._battleParticipant) continue;
            var otherBody = other.VisibleBodyScreenBounds;
            if (!CompanionCollisionGeometry.Overlaps(body, otherBody)) continue;
            if (!other._pinned && !other._dragPending && string.CompareOrdinal(RecordId, other.RecordId) > 0) continue;
            TryStartCollisionReaction(other, now, out var fleeing);
            var overlapX = Math.Min(body.Right, otherBody.Right) - Math.Max(body.Left, otherBody.Left);
            var overlapY = Math.Min(body.Bottom, otherBody.Bottom) - Math.Max(body.Top, otherBody.Top);
            var horizontal = overlapX <= overlapY;
            var ownCenter = horizontal ? body.X + body.Width / 2 : body.Y + body.Height / 2;
            var otherCenter = horizontal ? otherBody.X + otherBody.Width / 2 : otherBody.Y + otherBody.Height / 2;
            var sign = ownCenter == otherCenter
                ? (string.CompareOrdinal(RecordId, other.RecordId) < 0 ? -1d : 1d)
                : ownCenter < otherCenter ? -1d : 1d;
            var separation = (horizontal ? overlapX : overlapY) + Math.Max(4d, 6d * GetDpiScale()) * _visualScale;
            var direction = horizontal ? new Point(sign, 0) : new Point(0, sign);
            _direction = direction;
            _row = DirectionRow(direction);
            if (GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds))
            {
                MoveAcrossAvailableScreens(bounds.Left + direction.X * separation,
                    bounds.Top + direction.Y * separation, bounceAtEdge: false);
                if (CompanionCollisionGeometry.Overlaps(VisibleBodyScreenBounds, otherBody))
                {
                    var alternateHorizontal = !horizontal;
                    var alternateOwnCenter = alternateHorizontal
                        ? body.X + body.Width / 2 : body.Y + body.Height / 2;
                    var alternateOtherCenter = alternateHorizontal
                        ? otherBody.X + otherBody.Width / 2 : otherBody.Y + otherBody.Height / 2;
                    var alternateSign = alternateOwnCenter == alternateOtherCenter
                        ? (string.CompareOrdinal(RecordId, other.RecordId) < 0 ? -1d : 1d)
                        : alternateOwnCenter < alternateOtherCenter ? -1d : 1d;
                    var alternate = alternateHorizontal
                        ? new Point(alternateSign, 0) : new Point(0, alternateSign);
                    var alternateSeparation = (alternateHorizontal ? overlapX : overlapY)
                        + Math.Max(4d, 6d * GetDpiScale()) * _visualScale;
                    _direction = alternate;
                    _row = DirectionRow(alternate);
                    MoveAcrossAvailableScreens(bounds.Left + alternate.X * alternateSeparation,
                        bounds.Top + alternate.Y * alternateSeparation, bounceAtEdge: false);
                }
                if (!fleeing && !(_activeEmotion == CompanionEmotion.Sad && now < _reactionUntil))
                {
                    _walking = false;
                    _avoidanceDirectionPending = true;
                    _nextDecision = now.AddMilliseconds(_random.Next(400, 701));
                }
                return true;
            }
        }
        return false;
    }

    private bool TryMoveWithoutCollision(double x, double y, DateTime now)
    {
        if (_rallyEventActive)
        {
            MoveAcrossAvailableScreens(x, y, bounceAtEdge: true);
            return true;
        }
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds)) return false;
        var body = VisibleBodyScreenBounds;
        var candidate = new Rect(body.X + x - bounds.Left, body.Y + y - bounds.Top, body.Width, body.Height);
        foreach (var other in _getCompanions().Where(item => item.RecordId != RecordId && item.IsVisible))
        {
            if (other._battleParticipant) continue;
            var otherBody = other.VisibleBodyScreenBounds;
            var overlaps = CompanionCollisionGeometry.Overlaps(candidate, otherBody);
            var spacing = GetSocialSpacing(other);
            var keepDistance = spacing > 0 && IsWithinSocialSpacing(candidate, otherBody, spacing);
            if (!overlaps && !keepDistance) continue;
            var fleeingAfterCollision = false;
            if (overlaps)
            {
                TryStartCollisionReaction(other, now, out fleeingAfterCollision);
            }
            if (!fleeingAfterCollision && !_fleeingAfterCollision)
            {
                var awayX = candidate.X + candidate.Width / 2d - (otherBody.X + otherBody.Width / 2d);
                var awayY = candidate.Y + candidate.Height / 2d - (otherBody.Y + otherBody.Height / 2d);
                var overlapX = Math.Min(candidate.Right, otherBody.Right) - Math.Max(candidate.Left, otherBody.Left);
                var overlapY = Math.Min(candidate.Bottom, otherBody.Bottom) - Math.Max(candidate.Top, otherBody.Top);
                if (overlapX <= overlapY)
                    _direction = new Point(awayX == 0 ? (RecordId.CompareTo(other.RecordId) < 0 ? -1 : 1) : Math.Sign(awayX), 0);
                else
                    _direction = new Point(0, awayY == 0 ? (RecordId.CompareTo(other.RecordId) < 0 ? -1 : 1) : Math.Sign(awayY));
                _row = DirectionRow(_direction);
                _avoidanceDirectionPending = true;
            }
            if (keepDistance && !overlaps && TryMoveAroundGroupedCompanion(x, y, bounds, candidate, otherBody, spacing))
                return true;
            return false;
        }
        MoveAcrossAvailableScreens(x, y, bounceAtEdge: true);
        return true;
    }

    private double GetSocialSpacing(DesktopCompanionWindow other)
    {
        // Rally destinations are spaced by the rally planner. Applying the
        // toy/social group radius while walking to them traps companions at
        // their starting positions; ordinary body collision still applies.
        if (_isRallyParticipant && other._isRallyParticipant) return 0d;
        if (_toyPlayParticipant && other._toyPlayParticipant)
            return Math.Max(12d * _visualScale, _getGroupSpacing());
        return 0d;
    }

    private static bool IsWithinSocialSpacing(Rect first, Rect second, double spacing)
    {
        var expanded = first;
        expanded.Inflate(spacing, spacing);
        return CompanionCollisionGeometry.Overlaps(expanded, second);
    }

    private bool TryMoveAroundGroupedCompanion(double x, double y, NativeRect bounds, Rect candidate,
        Rect otherBody, double spacing)
    {
        var dx = candidate.X + candidate.Width / 2d - (otherBody.X + otherBody.Width / 2d);
        var dy = candidate.Y + candidate.Height / 2d - (otherBody.Y + otherBody.Height / 2d);
        var side = (Math.Abs(dx) > Math.Abs(dy) ? new Point(0, 1) : new Point(1, 0));
        var step = Math.Max(2d, Math.Sqrt(Math.Pow(x - bounds.Left, 2) + Math.Pow(y - bounds.Top, 2)));
        foreach (var sign in new[] { 1d, -1d })
        {
            var offsetX = side.X * step * sign;
            var offsetY = side.Y * step * sign;
            var detour = new Rect(candidate.X + offsetX, candidate.Y + offsetY, candidate.Width, candidate.Height);
            var blocked = _getCompanions().Where(item => item.RecordId != RecordId && item.IsVisible).Any(item =>
            {
                var other = item.VisibleBodyScreenBounds;
                return CompanionCollisionGeometry.Overlaps(detour, other)
                    || (GetSocialSpacing(item) is var otherSpacing && otherSpacing > 0
                        && IsWithinSocialSpacing(detour, other, otherSpacing));
            });
            if (blocked) continue;
            _direction = new Point(side.X * sign, side.Y * sign);
            _row = DirectionRow(_direction);
            MoveAcrossAvailableScreens(bounds.Left + (x - bounds.Left) + offsetX,
                bounds.Top + (y - bounds.Top) + offsetY, bounceAtEdge: true);
            return true;
        }
        return false;
    }

    private bool TryStartCollisionReaction(DesktopCompanionWindow other, DateTime now, out bool fleeing)
    {
        fleeing = false;
        if (_megaTransformationActive || other._megaTransformationActive
            || _battleParticipant || other._battleParticipant
            || _rallyEventSuppressReactions || other._rallyEventSuppressReactions
            || _isRallyParticipant || other._isRallyParticipant) return false;
        if (_tryCouplesEncounter(this, other)) return true;
        if (_couplesParticipant || other._couplesParticipant) return false;
        if (now < _feedingReactionUntil || now < other._feedingReactionUntil) return false;
        if (now < _nextCollisionReactionCheck) return false;
        _nextCollisionReactionCheck = now.AddSeconds(CollisionReactionCooldownSeconds);
        if (HasRecentPositiveInteraction(other, now)) return false;
        var playingWithToy = _toyTarget.HasValue || other._toyTarget.HasValue;
        var reactionChance = playingWithToy ? Math.Max(1, CollisionReactionChancePercent / 4) : CollisionReactionChancePercent;
        if (_random.Next(100) >= reactionChance) return false;

        RegisterRecentNegativeInteraction(other, now);
        other.ReactAngrilyAfterCollision(this, now);
        if (_battleParticipant || other._battleParticipant)
        {
            fleeing = false;
            return true;
        }
        fleeing = ReactSadlyAfterCollision(other, now);
        return true;
    }

    private void ReactAngrilyAfterCollision(DesktopCompanionWindow other, DateTime now)
    {
        if (_couplesParticipant || other._couplesParticipant) return;
        if (_activeEmotion == CompanionEmotion.Angry && now < _reactionUntil) return;
        ActivateReaction(CompanionEmotion.Angry, TimeSpan.FromMilliseconds(2400));
        _reportBattleChance(this, other);
    }

    private bool ReactSadlyAfterCollision(DesktopCompanionWindow other, DateTime now)
    {
        if (_activeEmotion == CompanionEmotion.Sad && now < _reactionUntil) return _fleeingAfterCollision;
        if (_dragPending) return false;
        if (_pinned || _random.Next(100) >= CollisionFleeChancePercent)
        {
            ActivateReaction(CompanionEmotion.Sad, TimeSpan.FromMilliseconds(2400));
            return false;
        }

        CancelInteraction();
        _sleepUntil = DateTime.MinValue;
        var ownBody = VisibleBodyScreenBounds;
        var otherBody = other.VisibleBodyScreenBounds;
        var dx = ownBody.X + ownBody.Width / 2 - (otherBody.X + otherBody.Width / 2);
        var dy = ownBody.Y + ownBody.Height / 2 - (otherBody.Y + otherBody.Height / 2);
        _direction = Math.Abs(dx) >= Math.Abs(dy)
            ? new Point(dx == 0 ? (_random.Next(2) == 0 ? -1 : 1) : dx > 0 ? 1 : -1, 0)
            : new Point(0, dy == 0 ? (_random.Next(2) == 0 ? -1 : 1) : dy > 0 ? 1 : -1);
        _row = DirectionRow(_direction);
        _walking = true;
        _fleeingAfterCollision = true;
        _avoidanceDirectionPending = false;
        _column = 0;
        var fleeDuration = TimeSpan.FromMilliseconds(_random.Next(
            MinCollisionFleeMilliseconds, MaxCollisionFleeMillisecondsExclusive));
        _nextDecision = now + fleeDuration;
        SetEmotion(CompanionEmotion.Sad, fleeDuration);
        RenderFrame();
        return true;
    }

    private void RenderFrame()
    {
        var frameIndex = _row * 4 + _column;
        if (frameIndex == _lastFrame) return;
        if (_megaTransformationActive)
        {
            PositionMegaBubble();
            PositionReaction();
            return;
        }
        var frame = _frames[_row, _column];
        var mask = _alphaMasks[frameIndex];
        var lastVisibleY = -1;
        for (var y = _frameHeight - 1; y >= 0 && lastVisibleY < 0; y--)
            for (var x = 0; x < _frameWidth; x++)
                if (mask[y * _frameWidth + x]) { lastVisibleY = y; break; }
        Canvas.SetTop(_shadow, SpriteCanvasTop + Math.Clamp(lastVisibleY + 1, 0, Math.Max(0, _frameHeight - _shadowHeight)));
        _sprite.Source = frame;
        _lastFrame = frameIndex;
        PositionMegaAura();
        PositionReaction();
        PositionBerryRequest();
        PositionShinySparkles();
        PositionAlphaEffect();
    }

    private void StartAlphaEffect(bool restart = false)
    {
        if (!_capture.IsAlpha || AlphaEffectAnimationFrames.Count == 0 || (!restart && _alphaEffectFrame >= 0)) return;
        _alphaEffectFrame = 0;
        _alphaEffectImage.Source = AlphaEffectAnimationFrames[0];
        _alphaEffectImage.Visibility = Visibility.Visible;
        _nextAlphaEffectFrame = DateTime.UtcNow.AddMilliseconds(AlphaEffectFrames.FrameMilliseconds);
        _nextAlphaEffect = DateTime.UtcNow.AddSeconds(_random.Next(24, 44));
        PositionAlphaEffect();
    }

    private void UpdateAlphaEffect(DateTime now)
    {
        if (!_capture.IsAlpha) return;
        if (_alphaEffectFrame >= 0 && now >= _nextAlphaEffectFrame)
        {
            if (_alphaEffectFrame + 1 >= AlphaEffectAnimationFrames.Count)
            {
                _alphaEffectFrame = -1;
                _alphaEffectImage.Source = null;
                _alphaEffectImage.Visibility = Visibility.Collapsed;
            }
            else
            {
                _alphaEffectFrame++;
                _alphaEffectImage.Source = AlphaEffectAnimationFrames[_alphaEffectFrame];
                _nextAlphaEffectFrame = now.AddMilliseconds(AlphaEffectFrames.FrameMilliseconds);
                PositionAlphaEffect();
            }
        }
        if (_alphaEffectFrame < 0 && now >= _nextAlphaEffect && _walking && !_pinned && !_dragPending
            && _interactionPartnerId is null && now >= _sleepUntil && now >= _faceUserUntil && now >= _reactionUntil)
            StartAlphaEffect();
    }

    private void PositionAlphaEffect()
    {
        if (_alphaEffectFrame < 0 || _alphaEffectImage.Source is not BitmapSource frame) return;
        var bounds = _visibleFrameBounds[_row, _column];
        if (bounds.IsEmpty) return;
        var centerX = _spriteOffsetX + bounds.X + bounds.Width / 2d;
        // The effect's loop is drawn in the lower half of its sheet, so lift its
        // canvas slightly to center the visible ring around the Pokémon's body.
        var centerY = SpriteCanvasTop + bounds.Y + bounds.Height / 2d - bounds.Height * .1;
        var aspectRatio = frame.PixelWidth / (double)frame.PixelHeight;
        // Match the Mega aura's footprint so the Alfa loop reads at the same scale.
        var effectHeight = Math.Clamp(Math.Max(bounds.Width, bounds.Height) * 1.3, 56d, 150d);
        var maxHeightByWidth = Math.Min(centerX, _canvasWidth - centerX) * 2d / aspectRatio;
        var maxHeightByHeight = Math.Min(centerY, _canvas.Height - centerY) * 2d;
        effectHeight = Math.Min(effectHeight, Math.Min(maxHeightByWidth, maxHeightByHeight));
        var effectWidth = effectHeight * aspectRatio;
        _alphaEffectImage.Width = effectWidth;
        _alphaEffectImage.Height = effectHeight;
        Canvas.SetLeft(_alphaEffectImage, centerX - effectWidth / 2d);
        Canvas.SetTop(_alphaEffectImage, centerY - effectHeight / 2d);
    }

    private void StartMegaAura(bool restart = false)
    {
        if (_capture.MegaFormId is null || (!restart && _megaAuraFrame >= 0) || MegaAuraAnimationFrames.Count == 0) return;
        _megaAuraReversing = false;
        _megaAuraFrame = 0;
        _megaAuraImage.Source = MegaAuraAnimationFrames[0];
        _megaAuraImage.Visibility = Visibility.Visible;
        _nextMegaAura = DateTime.UtcNow.AddSeconds(_random.Next(24, 43));
        PositionMegaAura();
        if (IsVisible) _megaAuraTimer.Start();
    }

    private void UpdateMegaAuraSchedule(DateTime now)
    {
        if (_capture.MegaFormId is null || _megaTransformationActive || now < _megaWalkResumeAt) return;
        if (_megaAuraFrame < 0 && now >= _nextMegaAura && _walking && !_pinned && !_dragPending
            && _interactionPartnerId is null && now >= _sleepUntil && now >= _faceUserUntil && now >= _reactionUntil)
            StartMegaAura();
    }

    private void OnMegaAuraTick(object? sender, EventArgs e)
    {
        if (!IsVisible || _megaAuraFrame < 0 || _capture.MegaFormId is null)
        {
            _megaAuraTimer.Stop();
            return;
        }
        if (_megaAuraReversing)
        {
            if (_megaAuraFrame == 0)
            {
                _megaAuraFrame = -1;
                _megaAuraImage.Source = null;
                _megaAuraImage.Visibility = Visibility.Collapsed;
                _megaAuraTimer.Stop();
                BeginReverseMegaBubble();
                return;
            }
            _megaAuraFrame--;
            _megaAuraImage.Source = MegaAuraAnimationFrames[_megaAuraFrame];
            PositionMegaAura();
            return;
        }
        if (_megaAuraFrame + 1 >= MegaAuraAnimationFrames.Count)
        {
            _megaAuraFrame = -1;
            _megaAuraImage.Source = null;
            _megaAuraImage.Visibility = Visibility.Collapsed;
            _megaAuraTimer.Stop();
            return;
        }
        _megaAuraFrame++;
        _megaAuraImage.Source = MegaAuraAnimationFrames[_megaAuraFrame];
        PositionMegaAura();
    }

    private void BeginReverseMegaBubble()
    {
        if (!_megaTransformationActive || !_megaTransformationReversing || _megaTransitionFrames is null) return;
        _megaBubbleProgress = 0;
        _megaBubbleFrame = MegaBubbleAnimationFrames.Count - 1;
        _megaBubbleImage.Source = MegaBubbleAnimationFrames[_megaBubbleFrame];
        _megaBubbleImage.Opacity = 1;
        _megaBubbleImage.Visibility = Visibility.Visible;
        PositionMegaBubble();
        if (IsVisible) _megaBubbleTimer.Start();
    }

    private void PositionMegaAura()
    {
        if (_megaAuraFrame < 0 || _megaAuraImage.Source is not BitmapSource frame) return;
        var bounds = _visibleFrameBounds[_row, _column];
        if (bounds.IsEmpty) return;
        var centerX = _spriteOffsetX + bounds.X + bounds.Width / 2d;
        var centerY = SpriteCanvasTop + bounds.Y + bounds.Height / 2d;
        var aspectRatio = frame.PixelWidth / (double)frame.PixelHeight;
        var auraHeight = Math.Clamp(Math.Max(bounds.Width, bounds.Height) * 1.3, 56d, 150d);
        var maxHeightByWidth = Math.Min(centerX, _canvasWidth - centerX) * 2d / aspectRatio;
        var maxHeightByHeight = Math.Min(centerY, _canvas.Height - centerY) * 2d;
        auraHeight = Math.Min(auraHeight, Math.Min(maxHeightByWidth, maxHeightByHeight));
        var auraWidth = auraHeight * aspectRatio;
        _megaAuraImage.Width = auraWidth;
        _megaAuraImage.Height = auraHeight;
        Canvas.SetLeft(_megaAuraImage, centerX - auraWidth / 2d);
        Canvas.SetTop(_megaAuraImage, centerY - auraHeight / 2d);
    }

    private void StartShinySparkles(bool restart = false)
    {
        if (!_capture.IsShiny || (!restart && _shinySparkleFrame >= 0) || ShinySparkleFrames.Count == 0) return;
        _shinySparkleFrame = 0;
        _shinySparkleImage.Source = ShinySparkleFrames[0];
        _shinySparkleImage.Visibility = Visibility.Visible;
        _nextShinySparkleFrame = DateTime.UtcNow.AddMilliseconds(ShinySparkFrames.FrameMilliseconds);
        _nextShinySparkle = DateTime.UtcNow.AddSeconds(_random.Next(28, 51));
        PositionShinySparkles();
    }

    private void UpdateShinySparkles(DateTime now)
    {
        if (!_capture.IsShiny) return;
        if (_shinySparkleFrame >= 0 && now >= _nextShinySparkleFrame)
        {
            if (_shinySparkleFrame + 1 >= ShinySparkleFrames.Count)
            {
                _shinySparkleFrame = -1;
                _shinySparkleImage.Source = null;
                _shinySparkleImage.Visibility = Visibility.Collapsed;
            }
            else
            {
                _shinySparkleFrame++;
                _shinySparkleImage.Source = ShinySparkleFrames[_shinySparkleFrame];
                _nextShinySparkleFrame = now.AddMilliseconds(ShinySparkFrames.FrameMilliseconds);
            }
        }

        if (_shinySparkleFrame < 0 && now >= _nextShinySparkle && _walking && !_pinned && !_dragPending
            && _interactionPartnerId is null && now >= _sleepUntil && now >= _faceUserUntil && now >= _reactionUntil)
            StartShinySparkles();
    }

    private void PositionShinySparkles()
    {
        if (_shinySparkleFrame < 0 || _shinySparkleImage.Source is not BitmapSource frame) return;
        var bounds = _visibleFrameBounds[_row, _column];
        if (bounds.IsEmpty) return;
        var bodyWidth = bounds.Width;
        var bodyHeight = bounds.Height;
        var centerX = _spriteOffsetX + bounds.X + bodyWidth / 2d;
        var centerY = SpriteCanvasTop + bounds.Y + bodyHeight / 2d;
        var aspectRatio = frame.PixelWidth / (double)frame.PixelHeight;
        var sparkleHeight = Math.Clamp(Math.Max(bodyWidth, bodyHeight) * 2.45, 90d, 220d);
        var maxHeightByWidth = Math.Min(centerX, _canvasWidth - centerX) * 2d / aspectRatio;
        var maxHeightByHeight = Math.Min(centerY, _canvas.Height - centerY) * 2d;
        sparkleHeight = Math.Min(sparkleHeight, Math.Min(maxHeightByWidth, maxHeightByHeight));
        var sparkleWidth = sparkleHeight * aspectRatio;
        _shinySparkleImage.Width = sparkleWidth;
        _shinySparkleImage.Height = sparkleHeight;
        Canvas.SetLeft(_shinySparkleImage, centerX - sparkleWidth / 2d);
        Canvas.SetTop(_shinySparkleImage, centerY - sparkleHeight / 2d);
    }

    private void SetPinned(bool pinned)
    {
        _cancelBattle(this);
        _cancelRally(this);
        _cancelToyChase(this);
        _toyTarget = null;
        _toyChaseIsRunning = false;
        if (pinned)
        {
            CancelInteraction();
            _walking = false;
            _fleeingAfterCollision = false;
            _sleepUntil = DateTime.MinValue;
        }
        _pinned = pinned;
        if (pinned)
            ShowPinnedSleepReaction();
        else if (_activeEmotion == CompanionEmotion.Sleep)
            SetEmotion(null);
        _save.UpdateDesktopCompanion(RecordId, isPinned: pinned);
        if (!pinned) _nextDecision = DateTime.UtcNow.AddMilliseconds(_random.Next(400, 1100));
        RenderFrame();
    }

    private void ShowPinnedSleepReaction()
    {
        _activeEmotion = CompanionEmotion.Sleep;
        _reactionImage.Source = LoadReactionSprite(CompanionEmotion.Sleep);
        _reactionUntil = DateTime.MaxValue;
        _reactionImage.Visibility = Visibility.Visible;
        RaiseAboveCompanionSprites();
        PositionReaction();
    }

    private void ReturnToPc()
    {
        _cancelBattle(this);
        CancelDittoTransformation();
        CancelInteraction();
        _save.RemoveDesktopCompanion(RecordId);
        _returned(this);
        Close();
    }

    private void MoveAcrossAvailableScreens(double x, double y, bool bounceAtEdge)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (_screens.Length == 0) _screens = Forms.Screen.AllScreens;
        var candidateBody = GetVisibleBodyAtWindowPosition(x, y);
        if (candidateBody.IsEmpty) return;
        var centerX = (int)Math.Round(candidateBody.X + candidateBody.Width / 2d);
        var centerY = (int)Math.Round(candidateBody.Y + candidateBody.Height / 2d);
        var target = _screens.FirstOrDefault(screen => screen.WorkingArea.Contains(centerX, centerY));
        if (target is not null && IsVisibleBodyAcrossScreens(candidateBody))
        {
            PlaceAt((int)Math.Round(x), (int)Math.Round(y));
            return;
        }

        GetWindowRect(handle, out var current);
        var currentBody = GetVisibleBodyAtWindowPosition(current.Left, current.Top);
        var currentCenterX = (int)Math.Round(currentBody.X + currentBody.Width / 2d);
        var currentCenterY = (int)Math.Round(currentBody.Y + currentBody.Height / 2d);
        var screen = _screens.FirstOrDefault(item => item.WorkingArea.Contains(currentCenterX, currentCenterY)) ?? _primaryScreen;
        var area = screen.WorkingArea;
        var leftInset = candidateBody.Left - x;
        var topInset = candidateBody.Top - y;
        var minX = area.Left - leftInset;
        var maxX = area.Right - candidateBody.Width - leftInset;
        var minY = area.Top - topInset;
        var maxY = area.Bottom - candidateBody.Height - topInset;
        maxX = Math.Max(minX, maxX);
        maxY = Math.Max(minY, maxY);
        maxX = Math.Max(minX, maxX);
        maxY = Math.Max(minY, maxY);
        if (bounceAtEdge)
        {
            var clampedX = Math.Clamp(x, minX, maxX);
            var clampedY = Math.Clamp(y, minY, maxY);
            if (Math.Abs(clampedX - x) > .5d) _direction = new Point(-_direction.X, _direction.Y);
            if (Math.Abs(clampedY - y) > .5d) _direction = new Point(_direction.X, -_direction.Y);
            (x, y) = (clampedX, clampedY);
            _row = DirectionRow(_direction);
        }
        else
        {
            x = Math.Clamp(x, minX, maxX);
            y = Math.Clamp(y, minY, maxY);
        }
        PlaceAt((int)Math.Round(x), (int)Math.Round(y));
    }

    private bool TryHandleWanderEdgeImpact(double x, double y, DateTime now)
    {
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out _)) return false;
        if (_screens.Length == 0) _screens = Forms.Screen.AllScreens;
        var candidateBody = GetVisibleBodyAtWindowPosition(x, y);
        if (candidateBody.IsEmpty) return false;
        var candidateCenterX = (int)Math.Round(candidateBody.X + candidateBody.Width / 2d);
        var candidateCenterY = (int)Math.Round(candidateBody.Y + candidateBody.Height / 2d);
        var candidateScreen = _screens.FirstOrDefault(screen => screen.WorkingArea.Contains(candidateCenterX, candidateCenterY));
        if (candidateScreen is not null && IsVisibleBodyAcrossScreens(candidateBody)) return false;

        var currentBody = VisibleBodyScreenBounds;
        var currentCenterX = (int)Math.Round(currentBody.X + currentBody.Width / 2d);
        var currentCenterY = (int)Math.Round(currentBody.Y + currentBody.Height / 2d);
        var screen = _screens.FirstOrDefault(item => item.WorkingArea.Contains(currentCenterX, currentCenterY)) ?? _primaryScreen;
        var area = screen.WorkingArea;
        var leftInset = candidateBody.Left - x;
        var topInset = candidateBody.Top - y;
        var minX = area.Left - leftInset;
        var maxX = area.Right - candidateBody.Width - leftInset;
        var minY = area.Top - topInset;
        var maxY = area.Bottom - candidateBody.Height - topInset;
        var hitLeft = x < minX - .5d;
        var hitRight = x > maxX + .5d;
        var hitTop = y < minY - .5d;
        var hitBottom = y > maxY + .5d;
        if (!hitLeft && !hitRight && !hitTop && !hitBottom) return false;

        // A cardinal walk can hit a corner only if it was already outside on
        // the other axis. Choose the boundary with the larger overshoot.
        var horizontalOvershoot = hitLeft ? minX - x : hitRight ? x - maxX : 0d;
        var verticalOvershoot = hitTop ? minY - y : hitBottom ? y - maxY : 0d;
        if (_direction.X != 0 && (horizontalOvershoot >= verticalOvershoot || verticalOvershoot == 0d))
            _edgeRunDirection = new Point(hitLeft ? 1 : -1, 0);
        else
            _edgeRunDirection = new Point(0, hitTop ? 1 : -1);

        PlaceAt((int)Math.Round(Math.Clamp(x, minX, maxX)), (int)Math.Round(Math.Clamp(y, minY, maxY)));
        _walking = false;
        _fleeingAfterCollision = false;
        _avoidanceDirectionPending = false;
        _direction = new Point(-_edgeRunDirection.X, -_edgeRunDirection.Y);
        _row = DirectionRow(_direction);
        ActivateReaction(CompanionEmotion.Dizzy, TimeSpan.FromMilliseconds(EdgeDizzyMilliseconds));
        _edgeStunnedUntil = now.AddMilliseconds(EdgeDizzyMilliseconds);
        _edgeRunPending = true;
        SavePosition();
        return true;
    }

    private Rect GetVisibleBodyAtWindowPosition(double x, double y)
    {
        var bounds = _visibleFrameBounds[_row, _column];
        if (bounds.IsEmpty) return Rect.Empty;
        var dpi = GetDpiScale();
        return new Rect(x + (_spriteOffsetX + bounds.X) * _visualScale * dpi,
            y + (SpriteCanvasTop + bounds.Y) * _visualScale * dpi,
            bounds.Width * _visualScale * dpi, bounds.Height * _visualScale * dpi);
    }

    private void OnDesktopVisualScaleChanged(double scale) => UpdateVisualScale(scale, preserveAnchor: true);

    private void UpdateVisualScale(double scale, bool preserveAnchor)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = handle == IntPtr.Zero ? VisualTreeHelper.GetDpi(this).DpiScaleX : GetDpiScale();
        var currentBounds = _visibleFrameBounds[_row, _column];
        var previousRect = default(NativeRect);
        var anchorX = 0d;
        var anchorY = 0d;
        var hasAnchor = preserveAnchor && handle != IntPtr.Zero
            && GetWindowRect(handle, out previousRect) && !currentBounds.IsEmpty;
        if (hasAnchor)
        {
            anchorX = previousRect.Left + (_spriteOffsetX + currentBounds.X + currentBounds.Width / 2d) * _visualScale * dpi;
            anchorY = previousRect.Top + (SpriteCanvasTop + currentBounds.Y + currentBounds.Height) * _visualScale * dpi;
        }

        _visualScale = scale;
        var visualCanvasHeight = _evolutionActive ? _baseCanvasHeight + EvolutionEffectHeight : _baseCanvasHeight;
        _canvas.Height = visualCanvasHeight;
        _canvas.LayoutTransform = Math.Abs(scale - 1d) < .001d ? null : new ScaleTransform(scale, scale);
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_canvas, EdgeMode.Aliased);
        MinWidth = MinHeight = 0;
        MaxWidth = MaxHeight = double.PositiveInfinity;
        Width = Math.Max(1d, _canvasWidth * scale);
        Height = Math.Max(1d, visualCanvasHeight * scale);
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
        UpdateLayout();

        if (hasAnchor)
        {
            var left = anchorX - (_spriteOffsetX + currentBounds.X + currentBounds.Width / 2d) * scale * dpi;
            var top = anchorY - (SpriteCanvasTop + currentBounds.Y + currentBounds.Height) * scale * dpi;
            PlaceAt((int)Math.Round(left), (int)Math.Round(top));
        }
        else if (handle != IntPtr.Zero)
        {
            ClampToAvailableScreen();
        }
#if DEBUG
        var actualBounds = default(NativeRect);
        if (handle != IntPtr.Zero && GetWindowRect(handle, out actualBounds))
            EncounterDiagnostics.Write("desktop-visual-scale", $"{RecordId}: scale={scale}; canvas={_canvas.Width}x{_canvas.Height}; window={Width}x{Height}; physical={actualBounds.Right - actualBounds.Left}x{actualBounds.Bottom - actualBounds.Top}; dpi={dpi}");
#endif
    }

    private void ClampToAvailableScreen()
    {
        _screens = Forms.Screen.AllScreens;
        var dpi = GetDpiScale();
        var width = (int)Math.Ceiling(_canvasWidth * _visualScale * dpi);
        var height = (int)Math.Ceiling(_canvas.Height * _visualScale * dpi);
        var x = _positionX;
        var y = _positionY - SpriteCanvasTop * dpi;
        var bodyCenterX = (int)Math.Round(_positionX + _frameWidth * _visualScale * dpi / 2d);
        var bodyCenterY = (int)Math.Round(_positionY + _frameHeight * _visualScale * dpi / 2d);
        var screen = _screens.FirstOrDefault(item => item.WorkingArea.Contains(bodyCenterX, bodyCenterY));
        if (screen is null)
        {
            var area = _primaryScreen.WorkingArea;
            x = area.Left + Math.Max(0, (area.Width - width) / 2d);
            y = area.Top + Math.Max(0, (area.Height - height) / 2d);
        }
        else
        {
            var body = GetVisibleBodyAtWindowPosition(x, y);
            if (!IsVisibleBodyAcrossScreens(body))
                MoveAcrossAvailableScreens(x, y, bounceAtEdge: false);
            else
                PlaceAt((int)Math.Round(x), (int)Math.Round(y));
            return;
        }
        PlaceAt((int)Math.Round(x), (int)Math.Round(y));
    }

    private bool IsVisibleBodyAcrossScreens(Rect body)
    {
        if (_screens.Length == 0 || body.IsEmpty) return false;
        var points = new[]
        {
            new Point(body.Left + 1, body.Top + 1), new Point(body.Right - 1, body.Top + 1),
            new Point(body.Left + 1, body.Bottom - 1), new Point(body.Right - 1, body.Bottom - 1),
            new Point(body.X + body.Width / 2d, body.Y + body.Height / 2d)
        };
        return points.All(point => _screens.Any(screen => screen.WorkingArea.Contains((int)point.X, (int)point.Y)));
    }

    private void PlaceAt(int x, int y)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var dpi = GetDpiScale();
        var width = (int)Math.Ceiling(_canvasWidth * _visualScale * dpi);
        var height = (int)Math.Ceiling(_canvas.Height * _visualScale * dpi);
        _positionX = x; _positionY = y + SpriteCanvasTop * dpi;
        SetWindowPos(handle, IntPtr.Zero, x, y, width, height, SwpNoActivate | SwpNoZOrder | SwpNoOwnerZOrder);
    }

    private void SavePosition()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var bounds))
        {
            _positionX = bounds.Left;
            _positionY = bounds.Top + SpriteCanvasTop * GetDpiScale();
            _save.UpdateDesktopCompanion(RecordId, _positionX, _positionY, _pinned);
        }
        _lastPersistence = DateTime.UtcNow;
    }

    private double GetDpiScale()
    {
        var dpi = GetDpiForWindow(new WindowInteropHelper(this).Handle);
        return dpi == 0 ? VisualTreeHelper.GetDpi(this).DpiScaleX : dpi / 96d;
    }

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct LowLevelMouseData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChild(IntPtr parent, IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
}

internal static class ArrayExtensions
{
    internal static void CopyFrom<T>(this T[,] target, T[,] source)
    {
        if (target.GetLength(0) != source.GetLength(0) || target.GetLength(1) != source.GetLength(1))
            throw new ArgumentException("The source and target arrays must have the same dimensions.");
        Array.Copy(source, target, source.Length);
    }
}

internal enum CompanionEmotion { Happy, Heart, Music, Sleep, Sad, Angry, Dizzy, Play, Win }

internal static class CompanionCollisionGeometry
{
    internal static (double X, double Y) ClampAndReflect(double x, double y, int width, int height,
        System.Drawing.Rectangle area, ref Point direction)
    {
        var clampedX = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
        var clampedY = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));

        // Reflect when the window itself reaches an edge. Checking its center alone lets
        // the transparent reaction margin hit the work-area boundary while the center is
        // still inside, causing the companion to keep walking into the clamp every tick.
        if ((direction.X < 0 && x < clampedX) || (direction.X > 0 && x > clampedX))
            direction = new Point(-direction.X, direction.Y);
        if ((direction.Y < 0 && y < clampedY) || (direction.Y > 0 && y > clampedY))
            direction = new Point(direction.X, -direction.Y);

        return (clampedX, clampedY);
    }

    internal static Rect ToScreenRect(double windowX, double windowY, int spriteOffsetX, int spriteOffsetY,
        Int32Rect visiblePixels, double dpiScale, double visualScale = 1d) => new(
        windowX + (spriteOffsetX + visiblePixels.X) * dpiScale * visualScale,
        windowY + (spriteOffsetY + visiblePixels.Y) * dpiScale * visualScale,
        Math.Max(1, visiblePixels.Width * dpiScale * visualScale), Math.Max(1, visiblePixels.Height * dpiScale * visualScale));

    internal static bool Overlaps(Rect firstVisibleBody, Rect secondVisibleBody) =>
        !firstVisibleBody.IsEmpty && !secondVisibleBody.IsEmpty && firstVisibleBody.IntersectsWith(secondVisibleBody);

    internal static bool IsNear(Rect firstVisibleBody, Rect secondVisibleBody, double range)
    {
        if (firstVisibleBody.IsEmpty || secondVisibleBody.IsEmpty) return false;
        var dx = firstVisibleBody.X + firstVisibleBody.Width / 2 - (secondVisibleBody.X + secondVisibleBody.Width / 2);
        var dy = firstVisibleBody.Y + firstVisibleBody.Height / 2 - (secondVisibleBody.Y + secondVisibleBody.Height / 2);
        return dx * dx + dy * dy <= range * range;
    }
}
