using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace PokeGrass;

public partial class App : System.Windows.Application
{
    private static string[] _entryArguments = [];

    [STAThread]
    public static int Main(string[] args)
    {
        if (!args.Contains("--self-test", StringComparer.OrdinalIgnoreCase)
            && !args.Contains("--mega-evolution-self-test", StringComparer.OrdinalIgnoreCase))
            Velopack.VelopackApp.Build().Run();
        _entryArguments = args;
        var application = new App();
        application.InitializeComponent();
        return application.Run();
    }

    private Forms.NotifyIcon? _trayIcon;
    private GardenWindow? _gardenWindow;
    private CapturePanelWindow? _capturePanelWindow;
    private GardenActionButtonsWindow? _actionButtonsWindow;
    private GardenCornerButtonsWindow? _gardenCornerButtonsWindow;
    private SettingsWindow? _settingsWindow;
    private BagWindow? _bagWindow;
    private PokemonPcWindow? _pcWindow;
    private GameSaveStore? _gameSave;
    private PokemonCatalog? _pokemonCatalog;
    private PokemonSpeciesMetadataCatalog? _speciesMetadata;
    private MegaEvolutionCatalog? _megaEvolutions;
    private PokemonEvolutionCatalog? _pokemonEvolutions;
    private EvolutionPromptWindow? _evolutionPrompt;
    private bool _evolutionAssetsAvailable;
    private DesktopPokemonBattle? _desktopBattle;
    private DesktopPokemonCouples? _desktopCouples;
    private TimedItemEffects? _timedEffects;
    private TimedEffectToastWindow? _timedNotice;
    private RewardBalance? _rewardBalance;
    private RewardService? _rewardService;
    private RewardToastWindow? _rewardToast;
    private DesktopToyWindow? _desktopToy;
    private System.Windows.Threading.DispatcherTimer? _companionActivityTimer;
    private System.Windows.Threading.DispatcherTimer? _toyActivityTimer;
    private System.Windows.Threading.DispatcherTimer? _overlayTopmostTimer;
    private System.Windows.Threading.DispatcherTimer? _backupTimer;
    private DateTime _nextBerryRequestAt;
    private DateTime _nextPlayRequestAt;
    private DateTime _berryRequestExpiresAt;
    private DateTime _playRequestExpiresAt;
    private DateTime _lastToyInteractionAt;
    private DateTime _nextToyParticipantAt;
    private DateTime _toyPassReceiverWaitSince;
    private DateTime _toyPassKickAt;
    private DateTime _toyAtTopSince;
    private DateTime _teamRallyExpiresAt;
    private DateTime _teamRallyGatherDeadline;
    private DateTime _teamRallyNextReactionAt;
    private DateTime _teamRallyNextCryAt;
    private DateTime _teamRallyFinalizeAt;
    private string? _berryRequestRecordId;
    private string? _berryRequestItemId;
    private string? _playRequestRecordId;
    private string? _toyParticipantRecordId;
    private string? _lastToyParticipantRecordId;
    private string? _preferredToyParticipantRecordId;
    private string? _toyPassReceiverRecordId;
    private string? _toyManualThrowChaserRecordId;
    private string? _locatedCompanionRecordId;
    private DateTime _locatedCompanionUntil;
    private string? _teamRallyLeaderRecordId;
    private System.Windows.Point? _lastSoloToyPushDirection;
    private int _soloToyPushDirectionStreak;
    private readonly Dictionary<string, DateTime> _toyPlayerSince = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _toyBoredUntil = new(StringComparer.Ordinal);
    private const double ToyInterestRangePixels = 320d;
    private const double ToyGroupInviteRangePixels = 480d;
    private bool _teamRallyActive;
    private bool _teamRallyCelebrating;
    private bool _teamRallyFinalizing;
    private bool _toyPausedForTeamRally;
    private int _teamRallyReactionIndex;
    private DateTime _rewardNotificationNotBefore = DateTime.MinValue;
    private bool _restartingAfterDataImport;
    private bool _externalSystemUiLayerActive;
    private IntPtr _externalSystemUiHandle;
    private string? _debugBattlePreviewSaveDirectory;
#if DEBUG
    private bool _suppressRewardNotificationsForPreview;
#endif
    private Forms.ToolStripMenuItem? _closeEncounterItem;
    private readonly Dictionary<string, DesktopCompanionWindow> _desktopCompanions = new(StringComparer.Ordinal);

    protected override void OnStartup(StartupEventArgs e)
    {
        var startupArgs = _entryArguments;
        base.OnStartup(e);
        DragPreviewWindow.ActivePreviewsChanged += OnDragPreviewWindowsChanged;
        DispatcherUnhandledException += (_, args) =>
        {
            EncounterDiagnostics.WriteException("dispatcher-unhandled", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                EncounterDiagnostics.WriteException("appdomain-unhandled", exception);
            else
                EncounterDiagnostics.Write("appdomain-unhandled", $"Unhandled object: {args.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            EncounterDiagnostics.WriteException("task-unobserved", args.Exception);
            args.SetObserved();
        };
        EncounterDiagnostics.Write("startup", $"PokéGrass starting; version={typeof(App).Assembly.GetName().Version}; base={AppContext.BaseDirectory}");

        if (startupArgs.Contains("--mega-evolution-self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                MegaEvolutionSelfTest.Run();
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PokéGrass-mega-evolution-self-test.log"),
                    $"PASS{Environment.NewLine}{MegaEvolutionSelfTest.Summary}");
                Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PokéGrass-mega-evolution-self-test.log"), exception.ToString());
                Shutdown(1);
            }
            return;
        }

        if (startupArgs.Contains("--ditto-transform-self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var summary = DittoTransformFrames.ValidateAndSummarize(Path.Combine(AppContext.BaseDirectory,
                    DittoTransformFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
                var megaEvolutionSound = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "effects", "mega-evolution.mp3");
                if (!File.Exists(megaEvolutionSound)) throw new FileNotFoundException("Falta el efecto mega-evolution requerido para las transformaciones de Ditto.", megaEvolutionSound);
                summary += "; sound mega-evolution.mp3 present";
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PokéGrass-ditto-transform-self-test.log"), $"PASS{Environment.NewLine}{summary}");
                Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PokéGrass-ditto-transform-self-test.log"), exception.ToString());
                Shutdown(1);
            }
            return;
        }

        if (startupArgs.Contains("--couples-self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                DesktopPokemonCouplesSelfTest.Run();
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PokéGrass-self-test.log"),
                    $"PASS{Environment.NewLine}{DesktopPokemonCouplesSelfTest.Summary}");
                Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PokéGrass-self-test.log"), exception.ToString());
                Shutdown(1);
            }
            return;
        }

        if (startupArgs.Contains("--battle-background-self-test", StringComparer.OrdinalIgnoreCase))
        {
            var selfTestPath = Path.Combine(Path.GetTempPath(), "PokéGrass-battle-background-self-test.log");
            try
            {
                BattleBackgroundSelectorSelfTest.Run();
                File.WriteAllText(selfTestPath, $"PASS{Environment.NewLine}{BattleBackgroundSelectorSelfTest.Summary}");
                Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(selfTestPath, exception.ToString());
                Shutdown(1);
            }
            return;
        }

        if (startupArgs.Contains("--evolution-self-test", StringComparer.OrdinalIgnoreCase))
        {
            var selfTestPath = Path.Combine(Path.GetTempPath(), "PokéGrass-evolution-self-test.log");
            try
            {
                EvolutionSystemSelfTest.Run(Path.Combine(AppContext.BaseDirectory, "assets"));
                File.WriteAllText(selfTestPath, $"PASS{Environment.NewLine}{EvolutionSystemSelfTest.Summary}");
                Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(selfTestPath, exception.ToString());
                Shutdown(1);
            }
            return;
        }

        if (startupArgs.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                EncounterSelfTest.Run();
                EncounterGenderSelfTest.Run();
                MegaEvolutionSelfTest.Run();
                PcStorageSelfTest.Run();
                CaptureLimitSelfTest.Run();
                RewardSystemSelfTest.Run();
                SaveBackupSelfTest.Run();
                ShinySparkSelfTest.Run();
                var battleEffectSummary = BattleEffectCatalog.ValidateAndSummarize();
                BattleBackgroundSelectorSelfTest.Run();
                var battleAssetsPath = Path.Combine(AppContext.BaseDirectory, "assets");
                var battlePokemonCatalog = PokemonCatalog.Load(Path.Combine(battleAssetsPath, "data", "pokemon-catalog.json"));
                var battleMegaCatalog = MegaEvolutionCatalog.Load(battleAssetsPath, battlePokemonCatalog);
                var battleMetadata = PokemonSpeciesMetadataCatalog.Load(
                    Path.Combine(battleAssetsPath, "data", "pokemon-species-metadata.json"), battlePokemonCatalog);
                EvolutionSystemSelfTest.Run(battleAssetsPath);
                var heracrossBaseId = battleMegaCatalog.GetBasePokemonIdForForm("HERACROSS_1");
                var megaHeracrossTypes = BattleEffectCatalog.GetTypesForForm("HERACROSS_1", battleMetadata.Get(heracrossBaseId).Types);
                if (!megaHeracrossTypes.SequenceEqual(["bug", "fighting"], StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("Mega Heracross no está usando sus tipos Bug/Lucha para elegir el ataque.");
                var alphaEffectSummary = AlphaEffectFrames.ValidateAndSummarize(
                    Path.Combine(AppContext.BaseDirectory, AlphaEffectFrames.EncounterAssetPath.Replace('/', Path.DirectorySeparatorChar)),
                    Path.Combine(AppContext.BaseDirectory, AlphaEffectFrames.CompanionAssetPath.Replace('/', Path.DirectorySeparatorChar)));
                var megaAuraSummary = MegaAuraFrames.ValidateAndSummarize(Path.Combine(AppContext.BaseDirectory,
                    MegaAuraFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
                var megaBubbleSummary = MegaBubbleFrames.ValidateAndSummarize(Path.Combine(AppContext.BaseDirectory,
                    MegaBubbleFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
                var dittoTransformSummary = DittoTransformFrames.ValidateAndSummarize(Path.Combine(AppContext.BaseDirectory,
                    DittoTransformFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
                CompanionBehaviorSelfTest.Run();
                DesktopPokemonCouplesSelfTest.Run();
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PokéGrass-self-test.log"),
                    $"PASS{Environment.NewLine}{EncounterSelfTest.SimulationSummary}{Environment.NewLine}{EncounterGenderSelfTest.Summary}{Environment.NewLine}{MegaEvolutionSelfTest.Summary}{Environment.NewLine}{PcStorageSelfTest.Summary}{Environment.NewLine}{CaptureLimitSelfTest.Summary}{Environment.NewLine}{RewardSystemSelfTest.Summary}{Environment.NewLine}{SaveBackupSelfTest.Summary}{Environment.NewLine}{ShinySparkSelfTest.Summary}{Environment.NewLine}{battleEffectSummary}{Environment.NewLine}{BattleBackgroundSelectorSelfTest.Summary}{Environment.NewLine}Mega Heracross battle types: {string.Join('/', megaHeracrossTypes)}{Environment.NewLine}{alphaEffectSummary}{Environment.NewLine}{megaAuraSummary}{Environment.NewLine}{megaBubbleSummary}{Environment.NewLine}{dittoTransformSummary}{Environment.NewLine}{EvolutionSystemSelfTest.Summary}{Environment.NewLine}{CompanionBehaviorSelfTest.Summary}{Environment.NewLine}{DesktopPokemonCouplesSelfTest.Summary}");
                Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PokéGrass-self-test.log"), exception.ToString());
                Shutdown(1);
            }
            return;
        }

        var assetRoot = Path.Combine(AppContext.BaseDirectory, "assets");
        var catalog = PokemonCatalog.Load(Path.Combine(assetRoot, "data", "pokemon-catalog.json"));
        _pokemonCatalog = catalog;
        _megaEvolutions = MegaEvolutionCatalog.Load(assetRoot, catalog);
        var rarity = RarityConfiguration.Load(Path.Combine(assetRoot, "data", "encounter-rarity.json"));
        var speciesMetadata = PokemonSpeciesMetadataCatalog.Load(Path.Combine(assetRoot, "data", "pokemon-species-metadata.json"), catalog);
        _speciesMetadata = speciesMetadata;
        _pokemonEvolutions = PokemonEvolutionCatalog.Load(Path.Combine(assetRoot, "data", "pokemon-evolutions.json"),
            catalog, speciesMetadata, _megaEvolutions);
        try
        {
            var evolutionSheet = Path.Combine(assetRoot, EvolutionAnimationFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar));
            EncounterDiagnostics.Write("pokemon-evolution-assets", EvolutionAnimationFrames.ValidateAndSummarize(evolutionSheet));
            _evolutionAssetsAvailable = true;
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException("pokemon-evolution-assets", exception);
        }
        var evolutionSound = Path.Combine(assetRoot, "sounds", "effects", "evolve.mp3");
        if (!File.Exists(evolutionSound)) EncounterDiagnostics.Write("pokemon-evolution-sound", $"Missing evolve effect: {evolutionSound}");
        _desktopBattle = new DesktopPokemonBattle(
            () => _desktopCompanions.Values.ToArray(),
            () => _teamRallyActive || _desktopToy?.IsVisible == true
                || _berryRequestRecordId is not null || _playRequestRecordId is not null
                || _desktopCouples?.IsCouplesActive == true,
            (first, second) => _desktopCouples?.HasRecentCouplesAffinity(first, second) == true,
            RestoreDesktopOverlayZOrder,
            () => _gardenWindow?.IsVisible == true ? _gardenWindow.GetPhysicalBounds() : Rectangle.Empty,
            OfferEvolutionAfterVictory);
        _desktopCouples = new DesktopPokemonCouples(
            () => _desktopCompanions.Values.ToArray(),
            () => _teamRallyActive || _desktopToy?.IsVisible == true
                || _desktopBattle?.IsBattleActive == true
                || _berryRequestRecordId is not null || _playRequestRecordId is not null);
        _rewardBalance = RewardBalance.Load(Path.Combine(assetRoot, "data", "reward-balance.json"));
        var pcCaptureDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-pc-captures=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var uiCaptureDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-ui-captures=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
#if DEBUG
        var visualScaleCaptureDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-visual-scale-captures=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
#endif
        var companionSparkDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-companion-spark-capture=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var dittoTransformCaptureDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-ditto-transform-captures=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var megaEvolutionCaptureDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-mega-evolution-capture=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var megaAuraCaptureDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-mega-aura-capture=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var evolutionCaptureDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-evolution-captures=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var regionalEvolutionComparisonDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-regional-evolution-comparison=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var alphaPreviewDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-alpha-preview=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var alphaSizeComparisonDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-alpha-size-comparison=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var battlePoke1CapturePath = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-battle-poke1-capture=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var isolatedDirectory = pcCaptureDirectory ?? uiCaptureDirectory ?? companionSparkDirectory ?? dittoTransformCaptureDirectory ?? megaEvolutionCaptureDirectory ?? megaAuraCaptureDirectory ?? evolutionCaptureDirectory ?? regionalEvolutionComparisonDirectory ?? alphaPreviewDirectory ?? alphaSizeComparisonDirectory
#if DEBUG
            ?? visualScaleCaptureDirectory
#endif
            ;
        _debugBattlePreviewSaveDirectory = battlePoke1CapturePath is null
            ? null : Path.Combine(Path.GetTempPath(), $"PokéGrass-battle-preview-{Guid.NewGuid():N}");
        var savePath = _debugBattlePreviewSaveDirectory is not null
            ? Path.Combine(_debugBattlePreviewSaveDirectory, "save.json")
            : evolutionCaptureDirectory is not null || regionalEvolutionComparisonDirectory is not null
                ? Path.Combine(Path.GetTempPath(), $"PokéGrass-evolution-preview-{Guid.NewGuid():N}", "save.json")
            : isolatedDirectory is null ? GameSaveStore.DefaultSavePath : Path.Combine(isolatedDirectory, "ui-preview-save.json");
        var captureLimitPolicy = new CaptureLimitPolicy(speciesMetadata, catalog, _megaEvolutions, assetRoot);
        var diagnosticAlert = startupArgs.FirstOrDefault(argument => argument.StartsWith("--diagnostic-alert-zone=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var diagnosticPokemon = startupArgs.FirstOrDefault(argument => argument.StartsWith("--diagnostic-pokemon=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var diagnosticShiny = startupArgs.Contains("--diagnostic-shiny", StringComparer.OrdinalIgnoreCase);
        if (diagnosticAlert is not null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POKEGRASS_SAVE_PATH")))
            throw new InvalidOperationException("El modo --diagnostic-alert-zone requiere POKEGRASS_SAVE_PATH para proteger la partida habitual.");
        try
        {
            _gameSave = GameSaveStore.LoadOrCreate(savePath, captureLimitPolicy);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            var backups = SaveBackupService.ListValid(savePath);
            if (backups.Count == 0)
            {
                EncounterDiagnostics.WriteException("save-recovery-no-valid-backup", exception);
                System.Windows.MessageBox.Show(
                    "PokéGrass no pudo leer la partida y no encontró copias válidas. El archivo original se conserva sin cambios.\n\n"
                    + exception.Message,
                    "No se pudo abrir la partida", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var latest = backups[0];
            var choice = System.Windows.MessageBox.Show(
                $"PokéGrass no pudo leer la partida actual. El archivo original se conservará para diagnóstico.\n\n"
                + $"Se encontró una copia válida del {latest.CreatedUtc.ToLocalTime():g} (formato {latest.SaveFormatVersion}).\n"
                + "¿Quieres restaurar esta copia? La partida activa será reemplazada y la aplicación se reiniciará.",
                "Recuperar partida", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes)
            {
                EncounterDiagnostics.Write("save-recovery", "User declined recovery; original save left untouched.");
                Shutdown(1);
                return;
            }

            try
            {
                SaveBackupService.Restore(savePath, latest);
                EncounterDiagnostics.Write("save-recovery", $"Restored verified backup from {latest.CreatedUtc:O}; original preserved.");
                _gameSave = GameSaveStore.LoadOrCreate(savePath, captureLimitPolicy);
            }
            catch (Exception recoveryException)
            {
                EncounterDiagnostics.WriteException("save-recovery-failed", recoveryException);
                System.Windows.MessageBox.Show(
                    "No se pudo restaurar la copia. El archivo dañado y la copia seleccionada se conservaron.\n\n"
                    + recoveryException.Message,
                    "Error al recuperar la partida", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
        }
        try { SaveBackupService.CreateAutomatic(savePath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        { EncounterDiagnostics.WriteException("save-startup-backup", exception); }
        _backupTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        { Interval = TimeSpan.FromMinutes(30) };
        _backupTimer.Tick += (_, _) =>
        {
            try { SaveBackupService.CreateAutomatic(_gameSave.SavePath, force: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            { EncounterDiagnostics.WriteException("save-periodic-backup", exception); }
        };
        _backupTimer.Start();
        Exit += (_, _) => _backupTimer.Stop();
        _timedEffects = new TimedItemEffects(_gameSave);
        _rewardService = new RewardService(_gameSave, _rewardBalance);
        _timedEffects.Expired += id =>
        {
            var message = id == "repel" ? "El efecto del repelente terminó." : "El efecto del incienso terminó.";
            _timedNotice?.Close();
            var area = Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            _timedNotice = new TimedEffectToastWindow(message, area.Right - 316, area.Bottom - 94);
            _timedNotice.Closed += (_, _) => _timedNotice = null;
            _timedNotice.Show();
        };
        var gardenWindow = new GardenWindow(GardenSettings.Load(), catalog, rarity, speciesMetadata, _timedEffects,
            _rewardBalance, _gameSave);
        gardenWindow.SetDiagnosticEncounter(diagnosticPokemon, diagnosticShiny ? true : null);
        gardenWindow.Activated += (_, _) => RestoreDesktopOverlayZOrder();
        _gardenWindow = gardenWindow;
        gardenWindow.Show();
        _actionButtonsWindow = new GardenActionButtonsWindow(gardenWindow,
            () => ShowSettings(gardenWindow),
            () => ShowBag(_gameSave!), () => ShowPc(_gameSave!, catalog, speciesMetadata));
        _actionButtonsWindow.Show();
        _gardenCornerButtonsWindow = new GardenCornerButtonsWindow(gardenWindow, () => Shutdown());
        _gardenCornerButtonsWindow.Show();
        RestoreDesktopOverlayZOrder();
        _overlayTopmostTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _overlayTopmostTimer.Tick += (_, _) => RestoreDesktopOverlayZOrder();
        _overlayTopmostTimer.Start();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar / ocultar jardín", null, (_, _) => ToggleGarden());
        menu.Items.Add("Abrir Bolsa", null, (_, _) => ShowBag(_gameSave!));
        menu.Items.Add("Abrir PC Pokémon", null, (_, _) => ShowPc(_gameSave!, catalog, speciesMetadata));

        var scaleMenu = new Forms.ToolStripMenuItem("Escala");
        var scaleItems = new Dictionary<int, Forms.ToolStripMenuItem>();
        foreach (var scale in GardenSettings.AllowedScales)
        {
            var capturedScale = scale;
            var item = new Forms.ToolStripMenuItem($"{scale}×")
            {
                Checked = gardenWindow.Scale == scale
            };
            item.Click += (_, _) =>
            {
                gardenWindow.SetScale(capturedScale);
                foreach (var (value, scaleItem) in scaleItems)
                    scaleItem.Checked = value == capturedScale;
            };
            scaleItems.Add(scale, item);
            scaleMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(scaleMenu);

        var lockItem = new Forms.ToolStripMenuItem("Bloquear posición")
        {
            CheckOnClick = true,
            Checked = gardenWindow.IsLocked
        };
        lockItem.CheckedChanged += (_, _) => gardenWindow.SetLocked(lockItem.Checked);
        gardenWindow.LockedChanged += locked => lockItem.Checked = locked;
        menu.Items.Add(lockItem);

        _closeEncounterItem = new Forms.ToolStripMenuItem("Cerrar encuentro actual")
        {
            Enabled = false
        };
        _closeEncounterItem.Click += (_, _) => gardenWindow.CloseEncounter();
        menu.Items.Add(_closeEncounterItem);

#if DEBUG
        var developmentMenu = new Forms.ToolStripMenuItem("Desarrollo (solo Debug)");
        developmentMenu.DropDownItems.Add("Provocar agitación ahora", null, (_, _) => gardenWindow.TriggerShake());
        developmentMenu.DropDownItems.Add("Forzar encuentro normal", null, (_, _) => gardenWindow.ForceEncounter(shiny: false));
        developmentMenu.DropDownItems.Add("Forzar encuentro shiny", null, (_, _) => gardenWindow.ForceEncounter(shiny: true));
        developmentMenu.DropDownItems.Add("Cerrar encuentro actual", null, (_, _) => gardenWindow.CloseEncounter());
        menu.Items.Add(developmentMenu);
#endif

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir de PokéGrass", null, (_, _) => Shutdown());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "PokéGrass",
            ContextMenuStrip = menu,
            Visible = true
        };
        gardenWindow.EncounterStateChanged += active =>
        {
            if (_closeEncounterItem is not null)
                _closeEncounterItem.Enabled = active;
        };
        gardenWindow.CaptureReady += (pokemon, shiny, alpha) =>
        {
            EncounterDiagnostics.Write("capture-panel", $"Opening capture panel for {pokemon.Id}; shiny={shiny}; alpha={alpha}");
            ShowCapturePanel(gardenWindow, _gameSave!, speciesMetadata, pokemon, shiny, alpha);
        };
        gardenWindow.CollectionChanged += () => { _pcWindow?.Refresh(); _bagWindow?.Refresh(); ReconcileDesktopCompanions(); };
        gardenWindow.EncounterEnded += () =>
        {
            if (_capturePanelWindow is not null)
            {
                _capturePanelWindow.Close();
                _capturePanelWindow = null;
            }
        };
        gardenWindow.ItemDroppedOnPokemon += item =>
        {
            if (_capturePanelWindow is not null)
                _ = _capturePanelWindow.UseDroppedItemAsync(item);
        };
        gardenWindow.LocationChanged += (_, _) => { _capturePanelWindow?.RepositionBesideGarden(); _actionButtonsWindow?.Reposition(); };
        gardenWindow.ScaleChanged += _ => { _capturePanelWindow?.RepositionBesideGarden(); _actionButtonsWindow?.Reposition(); };
        gardenWindow.IsVisibleChanged += (_, _) =>
        {
            if (gardenWindow.IsVisible)
            {
                _actionButtonsWindow?.Show();
                _gardenCornerButtonsWindow?.Show();
                _gardenCornerButtonsWindow?.Reposition();
                PresentPendingRewardNotifications();
            }
            else
            {
                _actionButtonsWindow?.Hide();
                _gardenCornerButtonsWindow?.Hide();
            }
        };
        gardenWindow.EncounterEnded += PresentPendingRewardNotifications;
        _rewardService.PendingRewardsChanged += () =>
        {
            _bagWindow?.Refresh();
            PresentPendingRewardNotifications();
        };
        gardenWindow.EncounterNotice += message =>
        {
            EncounterDiagnostics.Write("notice", message);
            if (_trayIcon is null) return;
            _trayIcon.BalloonTipTitle = "PokéGrass: encuentro no disponible";
            _trayIcon.BalloonTipText = message;
            _trayIcon.ShowBalloonTip(6000);
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                ToggleGarden();
        };
        RestoreDesktopCompanions(_gameSave!, catalog);
        StartCompanionActivityTimers();
#if DEBUG
        Dispatcher.BeginInvoke(() =>
        {
            if (alphaSizeComparisonDirectory is not null)
            {
                _ = CaptureAlphaSizeComparisonAsync(_gameSave!, catalog, speciesMetadata, alphaSizeComparisonDirectory);
                return;
            }
            if (alphaPreviewDirectory is not null)
            {
                _ = CaptureAlphaPreviewAsync(gardenWindow, _gameSave!, catalog, speciesMetadata, alphaPreviewDirectory);
                return;
            }
            if (pcCaptureDirectory is not null)
            {
                _ = CapturePcScreenshotsAsync(_gameSave!, catalog, speciesMetadata, pcCaptureDirectory);
                return;
            }
            if (uiCaptureDirectory is not null)
            {
                _ = CaptureUiScreenshotsAsync(gardenWindow, _gameSave!, catalog, speciesMetadata, uiCaptureDirectory);
                return;
            }
#if DEBUG
            if (visualScaleCaptureDirectory is not null)
            {
                _ = CaptureVisualScaleScreenshotsAsync(_gameSave!, catalog, speciesMetadata, visualScaleCaptureDirectory);
                return;
            }
#endif
            if (battlePoke1CapturePath is not null)
            {
                _ = CaptureBattlePoke1ScreenshotAsync(catalog, speciesMetadata, battlePoke1CapturePath);
                return;
            }
            var toolbarDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-toolbar-captures=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (toolbarDirectory is not null)
            {
                _ = CaptureToolbarScaleScreenshotsAsync(gardenWindow, toolbarDirectory);
                return;
            }
            var panelOnlyDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-panel-captures=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (panelOnlyDirectory is not null)
            {
                _ = CapturePanelScaleScreenshotsAsync(gardenWindow, panelOnlyDirectory);
                return;
            }
            var shinySparkDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-shiny-spark-captures=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (shinySparkDirectory is not null)
            {
                _ = CaptureShinySparkScreenshotsAsync(gardenWindow, shinySparkDirectory);
                return;
            }
            if (companionSparkDirectory is not null)
            {
                _ = CaptureCompanionSparkScreenshotAsync(_gameSave!, catalog, speciesMetadata, companionSparkDirectory);
                return;
            }
            if (dittoTransformCaptureDirectory is not null)
            {
                _ = CaptureDittoTransformScreenshotsAsync(_gameSave!, catalog, speciesMetadata, dittoTransformCaptureDirectory);
                return;
            }
            if (megaEvolutionCaptureDirectory is not null)
            {
                _ = CaptureMegaEvolutionScreenshotAsync(_gameSave!, catalog, speciesMetadata, megaEvolutionCaptureDirectory);
                return;
            }
            if (evolutionCaptureDirectory is not null)
            {
                _ = CaptureEvolutionScreenshotsAsync(_gameSave!, catalog, speciesMetadata, evolutionCaptureDirectory);
                return;
            }
            if (regionalEvolutionComparisonDirectory is not null)
            {
                _ = CaptureRegionalEvolutionComparisonAsync(_gameSave!, catalog, speciesMetadata, regionalEvolutionComparisonDirectory);
                return;
            }
            if (megaAuraCaptureDirectory is not null)
            {
                _ = CaptureMegaAuraScreenshotAsync(_gameSave!, catalog, speciesMetadata, megaAuraCaptureDirectory);
                return;
            }
            var alertDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-alert-captures=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (alertDirectory is not null)
            {
                _ = CaptureAlertGridAsync(gardenWindow, alertDirectory);
                return;
            }
            var captureAnimationDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-capture-animation-sequence=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (captureAnimationDirectory is not null)
            {
                _ = CaptureDevelopmentAnimationSequenceAsync(gardenWindow, startupArgs, captureAnimationDirectory);
                return;
            }
            var sequenceDirectory = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-capture-sequence=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (sequenceDirectory is not null)
            {
                _ = CaptureDevelopmentSequenceAsync(gardenWindow, startupArgs, sequenceDirectory);
                return;
            }
            var capturePath = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-capture=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (capturePath is not null)
            {
                _ = CaptureDevelopmentSceneAsync(gardenWindow, startupArgs, capturePath);
                return;
            }
            var shakeZoneText = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-shake-zone=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (int.TryParse(shakeZoneText, out var shakeZone)) gardenWindow.TriggerShake(shakeZone);
            else if (startupArgs.Contains("--dev-shake", StringComparer.OrdinalIgnoreCase)) gardenWindow.TriggerShake();
            var normalId = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-normal-id=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            var shinyId = startupArgs.FirstOrDefault(argument => argument.StartsWith("--dev-shiny-id=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (normalId is not null) gardenWindow.ForceEncounter(shiny: false, pokemonId: normalId);
            else if (startupArgs.Contains("--dev-normal", StringComparer.OrdinalIgnoreCase)) gardenWindow.ForceEncounter(shiny: false);
            if (shinyId is not null) gardenWindow.ForceEncounter(shiny: true, pokemonId: shinyId);
            else if (startupArgs.Contains("--dev-shiny", StringComparer.OrdinalIgnoreCase)) gardenWindow.ForceEncounter(shiny: true);
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
#endif
        if (int.TryParse(diagnosticAlert, out var diagnosticZone))
        {
            Dispatcher.BeginInvoke(() =>
            {
                EncounterDiagnostics.Write("diagnostic", $"Triggering alert in grass zone {diagnosticZone}; isolated save={savePath}");
                gardenWindow.TriggerShake(diagnosticZone);
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        Dispatcher.BeginInvoke(PresentPendingRewardNotifications, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        _ = CheckForUpdateNotificationAsync();
    }

    private async Task CheckForUpdateNotificationAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(12));
        try
        {
            var check = await AppUpdateService.CheckAsync();
            if (!check.IsInstalled || check.Update is null || _gameSave is null) return;
            var version = check.Update.TargetFullRelease.Version.ToString();
            var answer = System.Windows.MessageBox.Show(
                $"Hay una versión nueva de PokéGrass ({version}).\n\n¿Quieres descargarla e instalarla ahora? Puedes posponerla desde Configuración.",
                "Actualización disponible", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;

            SaveBackupService.CreateAutomatic(_gameSave.SavePath, force: true);
            await AppUpdateService.DownloadAsync(check.Manager, check.Update,
                progress => EncounterDiagnostics.Write("update-download", $"Download progress: {progress}%"));
            AppUpdateService.ApplyAndRestart(check.Manager, check.Update);
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException("update-startup-check", exception);
        }
    }

#if DEBUG
    private async Task CaptureVisualScaleScreenshotsAsync(GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        var originalScale = DesktopVisualScaleService.Current;
        try
        {
            Directory.CreateDirectory(directory);
            if (!catalog.ById.ContainsKey("PIKACHU")) throw new InvalidDataException("Falta Pikachu para la comparación de escala.");
            save.TryRecordCapture(metadata.Get("PIKACHU"), shiny: true, out _);
            var capture = save.GetPcSnapshot().Captures.Single(item => item.Id == "PIKACHU" && item.IsShiny);
            if (!SendToDesktop(save, catalog, capture))
                throw new InvalidOperationException("No se pudo mostrar Pikachu para la comparación de escala.");
            foreach (var other in _desktopCompanions.Values.Where(item => item.RecordId != capture.RecordId)) other.Hide();

            var companion = _desktopCompanions[capture.RecordId];
            companion.Left = 320;
            companion.Top = 180;
            companion.ShowSparklePreviewForDebug();
            foreach (var scale in DesktopVisualScaleService.AllowedScales)
            {
                DesktopVisualScaleService.Set(scale);
                await Task.Delay(350);
                var name = scale switch { 1d => "escala_x1.png", 1.5d => "escala_x1_5.png", _ => "escala_x2.png" };
                SaveWindowScreenshot(companion, Path.Combine(directory, name));
                var handle = new WindowInteropHelper(companion).Handle;
                if (GetWindowRect(handle, out var rect))
                    File.AppendAllText(Path.Combine(directory, "visual-scale-captures.txt"),
                        $"{scale}x: window={rect.Right - rect.Left}x{rect.Bottom - rect.Top}; WPF={companion.Width:F1}x{companion.Height:F1}; actual={companion.ActualWidth:F1}x{companion.ActualHeight:F1}{Environment.NewLine}");
            }
            File.AppendAllText(Path.Combine(directory, "visual-scale-captures.txt"),
                "PASS; the same live shiny Pikachu window was captured at x1, x1.5 and x2 after changing the persisted global setting without restarting. Isolated preview save.");
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "visual-scale-captures-error.txt"), exception.ToString());
        }
        finally
        {
            DesktopVisualScaleService.Set(originalScale);
            Shutdown();
        }
    }

    private async Task CaptureBattlePoke1ScreenshotAsync(PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog speciesMetadata, string capturePath)
    {
        try
        {
            var save = _gameSave ?? throw new InvalidOperationException("No se cargó la partida temporal para la captura de prueba.");
            foreach (var id in new[] { "PIKACHU", "ABOMASNOW" })
            {
                if (!catalog.ById.TryGetValue(id, out var entry))
                    throw new InvalidDataException($"Falta el Pokémon de prueba {id} en el catálogo.");
                save.TryRecordCapture(speciesMetadata.Get(id), shiny: false, out _);
                var capture = save.GetPcSnapshot().Captures.Last(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (!SendToDesktop(save, catalog, capture))
                    throw new InvalidOperationException($"No se pudo mostrar {id} para la captura de combate.");
            }

            var work = (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.First()).WorkingArea;
            var first = _desktopCompanions.Values.OrderBy(item => item.RecordId, StringComparer.Ordinal).First();
            var second = _desktopCompanions.Values.OrderBy(item => item.RecordId, StringComparer.Ordinal).Last();
            var centerX = work.Left + work.Width * .5d;
            var centerY = work.Top + work.Height * .47d;
            first.Left = centerX - first.Width * 1.55d;
            first.Top = centerY - first.Height / 2d;
            second.Left = centerX + second.Width * .55d;
            second.Top = centerY - second.Height / 2d;
            _gardenWindow?.Hide();
            _actionButtonsWindow?.Hide();
            _gardenCornerButtonsWindow?.Hide();
            RestoreDesktopOverlayZOrder();
            if (_desktopBattle?.StartForDebug(first, second) != true)
                throw new InvalidOperationException("No se pudo iniciar el combate de prueba.");

            var attackDeadline = DateTime.UtcNow.AddSeconds(10);
            var sawBattleEffect = false;
            while (DateTime.UtcNow < attackDeadline)
            {
                if (_desktopBattle?.IsBattleEffectVisible == true)
                {
                    sawBattleEffect = true;
                    await Task.Delay(40);
                    continue;
                }
                if (sawBattleEffect) break;
                await Task.Delay(40);
            }
            if (!sawBattleEffect)
                throw new TimeoutException("El combate de prueba no llegó a mostrar un ataque o escudo.");
            await Task.Delay(180);
            var bounds = (_desktopBattle ?? throw new InvalidOperationException("No se encontró el gestor del combate."))
                .GetArenaBounds();
            if (bounds.IsEmpty) throw new InvalidOperationException("El fondo no tiene límites visibles para capturar.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capturePath))!);
            using (var screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(screenshot))
            {
                var destinationDc = graphics.GetHdc();
                var screenDc = GetDC(IntPtr.Zero);
                try
                {
                    if (screenDc == IntPtr.Zero || !BitBlt(destinationDc, 0, 0, bounds.Width, bounds.Height,
                            screenDc, bounds.Left, bounds.Top, 0x00CC0020 | 0x40000000))
                        throw new InvalidOperationException("Windows no pudo capturar la composición visible del combate.");
                }
                finally
                {
                    if (screenDc != IntPtr.Zero) _ = ReleaseDC(IntPtr.Zero, screenDc);
                    graphics.ReleaseHdc(destinationDc);
                }
                screenshot.Save(capturePath, ImageFormat.Png);
            }

            EncounterDiagnostics.Write("battle-preview", $"Captured poke1 arena screenshot: {Path.GetFullPath(capturePath)}");
            var finishDeadline = DateTime.UtcNow.AddSeconds(45);
            while ((_desktopBattle?.IsBattleActive == true || _desktopBattle?.IsArenaVisible == true)
                && DateTime.UtcNow < finishDeadline)
                await Task.Delay(80);
            if (_desktopBattle?.IsBattleActive == true || _desktopBattle?.IsArenaVisible == true)
                throw new TimeoutException("El combate de prueba no terminó de celebrar y ocultar el fondo.");
            EncounterDiagnostics.Write("battle-preview", "Battle finished; winner celebration and arena fade-out completed.");
            Shutdown();
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException("battle-preview", exception);
            Shutdown(1);
        }
    }

    private async Task CaptureAlertGridAsync(GardenWindow garden, string directory)
    {
        var originalScale = garden.Scale;
        var originalPosition = garden.GetPhysicalBounds().Location;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var scale in GardenSettings.AllowedScales)
            {
                garden.SetScale(scale);
                await Task.Delay(400);
                for (var row = 0; row < 3; row++)
                {
                    garden.CloseEncounter();
                    garden.TriggerShake(row * 4 + 1);
                    await Task.Delay(240);
                    garden.SaveSceneCapture(Path.Combine(directory, $"alert-{scale}x-row{row + 1}.png"));
                }
            }
            garden.SetScale(4);
            await Task.Delay(350);
            var edgeNotes = new List<string>();
            var gardenWidth = 128 * garden.Scale;
            var gardenHeight = 80 * garden.Scale;
            var edgePositions = new[] { "top-left", "top-right", "bottom-left", "bottom-right" };
            var edgeScreen = Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1024, 768);
            var positions = new[]
            {
                new System.Drawing.Point(edgeScreen.Left, edgeScreen.Top),
                new System.Drawing.Point(edgeScreen.Right - gardenWidth, edgeScreen.Top),
                new System.Drawing.Point(edgeScreen.Left, edgeScreen.Bottom - gardenHeight),
                new System.Drawing.Point(edgeScreen.Right - gardenWidth, edgeScreen.Bottom - gardenHeight)
            };
            for (var edge = 0; edge < positions.Length; edge++)
            {
                garden.SetDebugPosition(positions[edge].X, positions[edge].Y);
                garden.CloseEncounter();
                garden.TriggerShake(edge < 2 ? 1 : 9);
                await Task.Delay(240);
                var renderBounds = garden.GetRenderBounds();
                var region = Rectangle.Intersect(renderBounds, edgeScreen);
                SaveRegionScreenshot(renderBounds, edgeScreen, Path.Combine(directory, $"alert-edge-{edgePositions[edge]}.png"));
                edgeNotes.Add($"{edgePositions[edge]}: garden={garden.GetPhysicalBounds()}; render={renderBounds}; screenCapture={region}");
            }
            File.WriteAllLines(Path.Combine(directory, "alert-edge-placement.txt"), edgeNotes);
            File.WriteAllText(Path.Combine(directory, "alert-capture-readme.txt"),
                "12 capturas del lienzo completo en filas 1-3 y escalas 1x-4x, más capturas reales del escritorio en las cuatro esquinas a 4x. El lienzo conserva un margen transparente de 24px fuente a cada lado; los efectos se mantienen dentro de los límites físicos del monitor cuando la ventana está junto a un borde.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "alert-capture-error.txt"), exception.ToString());
        }
        finally
        {
            garden.SetDebugPosition(originalPosition.X, originalPosition.Y);
            garden.SetScale(originalScale);
            await Task.Delay(350);
            Shutdown();
        }
    }

    private async Task CaptureToolbarScaleScreenshotsAsync(GardenWindow garden, string directory)
    {
        Directory.CreateDirectory(directory);
        var originalScale = garden.Scale;
        var placement = new List<string>();
        try
        {
            foreach (var scale in GardenSettings.AllowedScales)
            {
                garden.SetScale(scale);
                await Task.Delay(450);
                _actionButtonsWindow?.Reposition();
                await Task.Delay(150);
                var gardenBounds = garden.GetPhysicalBounds();
                var toolbarBounds = _actionButtonsWindow?.GetPhysicalBounds() ?? Rectangle.Empty;
                var area = Forms.Screen.FromRectangle(gardenBounds).WorkingArea;
                placement.Add($"{scale}x: garden={gardenBounds}; toolbar={toolbarBounds}; workArea={area}; toolbarFullyInside={area.Contains(toolbarBounds)}; toolbarSide={(toolbarBounds.Right <= gardenBounds.Left ? "left" : toolbarBounds.Left >= gardenBounds.Right ? "right" : "overlap")}");
                SaveRegionScreenshot(Rectangle.Union(gardenBounds, toolbarBounds), area,
                    Path.Combine(directory, $"garden-actions-{scale}x.png"));
            }
            File.WriteAllLines(Path.Combine(directory, "toolbar-captures.txt"), ["PASS; captured garden and action icons at 1x, 2x, 3x, 4x; original garden scale restored.", .. placement]);
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(directory, "toolbar-captures-error.txt"), exception.ToString()); }
        finally { garden.SetScale(originalScale); await Task.Delay(250); Shutdown(); }
    }

    private async Task CapturePanelScaleScreenshotsAsync(GardenWindow garden, string directory)
    {
        Directory.CreateDirectory(directory);
        var originalScale = garden.Scale;
        var placement = new List<string>();
        try
        {
            foreach (var scale in GardenSettings.AllowedScales)
            {
                garden.SetScale(scale);
                await Task.Delay(450);
                garden.ForceEncounter(shiny: false, pokemonId: "PIKACHU", playAudio: false, zoneIndex: 5);
                await Task.Delay(900);
                if (_capturePanelWindow is null) throw new InvalidOperationException("No apareció el panel del encuentro.");
                var scaleDirectory = Path.Combine(directory, $"{scale}x"); Directory.CreateDirectory(scaleDirectory);
                _capturePanelWindow.SaveCaptureScreenshot(Path.Combine(scaleDirectory, "capture-panel.png"));
                var gardenBounds = garden.GetPhysicalBounds(); var panelBounds = _capturePanelWindow.LastPhysicalBounds;
                var workArea = _capturePanelWindow.LastWorkArea;
                var toolbarBounds = _actionButtonsWindow?.GetPhysicalBounds() ?? Rectangle.Empty;
                placement.Add($"{scale}x: garden={gardenBounds}; panel={panelBounds}; toolbar={toolbarBounds}; workArea={workArea}; panelFullyInside={workArea.Contains(panelBounds)}; overlapsGarden={panelBounds.IntersectsWith(gardenBounds)}; overlapsToolbar={!toolbarBounds.IsEmpty && panelBounds.IntersectsWith(toolbarBounds)}; uiScale={_capturePanelWindow.ActualUiScale:F3}");
                SaveRegionScreenshot(Rectangle.Union(gardenBounds, panelBounds), _capturePanelWindow.LastWorkArea,
                    Path.Combine(scaleDirectory, "capture-panel-desktop.png"));
                garden.CloseEncounter();
                await Task.Delay(250);
            }
            File.WriteAllLines(Path.Combine(directory, "panel-captures.txt"), ["PASS; captured the updated encounter panel at 1x, 2x, 3x, 4x without launching items or changing inventory.", .. placement]);
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(directory, "panel-captures-error.txt"), exception.ToString()); }
        finally { _capturePanelWindow?.Close(); _capturePanelWindow = null; garden.CloseEncounter(); garden.SetScale(originalScale); await Task.Delay(250); Shutdown(); }
    }

    private static void SaveRegionScreenshot(Rectangle bounds, Rectangle workArea, string path)
    {
        var region = Rectangle.Intersect(bounds, workArea);
        if (region.Width <= 0 || region.Height <= 0) throw new InvalidOperationException("El conjunto no intersecta el área visible del monitor.");
        using var bitmap = new Bitmap(region.Width, region.Height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(region.Location, System.Drawing.Point.Empty, region.Size);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bitmap.Save(path, ImageFormat.Png);
    }

    private async Task CaptureDevelopmentSceneAsync(GardenWindow gardenWindow, string[] args, string capturePath)
    {
        var scaleText = args.FirstOrDefault(argument => argument.StartsWith("--dev-scale=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        if (int.TryParse(scaleText, out var scale))
            gardenWindow.SetScale(scale);
        await Task.Delay(350);
        var id = args.FirstOrDefault(argument => argument.StartsWith("--dev-capture-id=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1]
            ?? "PIKACHU";
        gardenWindow.ForceEncounter(shiny: false, pokemonId: id, playAudio: false, zoneIndex: 5);
        await Task.Delay(1500);
        gardenWindow.SaveSceneCapture(capturePath);
        Shutdown();
    }

    private async Task CaptureDevelopmentAnimationSequenceAsync(GardenWindow gardenWindow, string[] args, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var scaleText = args.FirstOrDefault(argument => argument.StartsWith("--dev-scale=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            if (int.TryParse(scaleText, out var scale)) gardenWindow.SetScale(scale);
            await Task.Delay(350);
            gardenWindow.SetDebugCaptureSequenceDirectory(directory);
            var pokemonId = args.FirstOrDefault(argument => argument.StartsWith("--dev-pokemon=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1] ?? "PIKACHU";
            var shiny = args.Contains("--dev-shiny", StringComparer.OrdinalIgnoreCase);
            gardenWindow.ForceEncounter(shiny, pokemonId, playAudio: false, zoneIndex: 5);
            await Task.Delay(900);
            if (_capturePanelWindow is null)
                throw new InvalidOperationException("La ventana de captura no apareció tras el encuentro de desarrollo.");
            _capturePanelWindow.SaveCaptureScreenshot(Path.Combine(directory, "capture-panel.png"));
            var gardenBounds = gardenWindow.GetPhysicalBounds();
            var panelBounds = _capturePanelWindow.LastPhysicalBounds;
            var workArea = _capturePanelWindow.LastWorkArea;
            SaveEncounterDesktopScreenshot(directory, gardenBounds, panelBounds, workArea);
            File.WriteAllText(Path.Combine(directory, "capture-panel-placement.txt"),
                $"garden={gardenBounds}; panel={panelBounds}; workArea={workArea}; uiScale={_capturePanelWindow.ActualUiScale:F3}; fullyInsideWorkArea={workArea.Contains(panelBounds)}; overlapsGarden={panelBounds.IntersectsWith(gardenBounds)}");
            var resultText = args.FirstOrDefault(argument => argument.StartsWith("--dev-outcome=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            var random = new FixedCaptureRandom(resultText?.Equals("fail", StringComparison.OrdinalIgnoreCase) == true ? 0.999 : 0d);
            var ballText = args.FirstOrDefault(argument => argument.StartsWith("--dev-ball=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
            var ball = Enum.TryParse<CaptureBall>(ballText, true, out var parsedBall) ? parsedBall : CaptureBall.Poke;
            var captured = await _capturePanelWindow.ThrowBallAsync(ball, _capturePanelWindow.GetBallOriginScreen(ball), random);
            if (!captured) throw new InvalidOperationException($"La Ball de desarrollo {ball} no produjo un lanzamiento válido.");
            await Task.Delay(120);
            _capturePanelWindow.SaveCaptureScreenshot(Path.Combine(directory, "capture-result.png"));
            gardenWindow.SaveSceneCapture(Path.Combine(directory, "garden-final.png"));
            var actualOutcome = _capturePanelWindow.LastResolution?.Success == true ? "success" : "fail";
            File.WriteAllText(Path.Combine(directory, "capture-sequence.log"), $"PASS; outcome={actualOutcome}; pokemon={pokemonId}; ball={ball}; shiny={shiny}");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "capture-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
    }

    private static void SaveEncounterDesktopScreenshot(string directory, Rectangle gardenBounds,
        Rectangle panelBounds, Rectangle workArea)
    {
        var visibleArea = Rectangle.Intersect(Rectangle.Union(gardenBounds, panelBounds), workArea);
        if (visibleArea.Width <= 0 || visibleArea.Height <= 0) return;
        using var bitmap = new Bitmap(visibleArea.Width, visibleArea.Height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(visibleArea.Location, System.Drawing.Point.Empty, visibleArea.Size);
        bitmap.Save(Path.Combine(directory, "capture-panel-desktop.png"), ImageFormat.Png);
    }

    private async Task CaptureDevelopmentSequenceAsync(GardenWindow gardenWindow, string[] args, string sequenceDirectory)
    {
        var scaleText = args.FirstOrDefault(argument => argument.StartsWith("--dev-scale=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        if (int.TryParse(scaleText, out var scale))
            gardenWindow.SetScale(scale);
        await Task.Delay(350);
        Directory.CreateDirectory(sequenceDirectory);

        foreach (var id in new[] { "JOLTIK", "PIKACHU", "WAILORD" })
        {
            gardenWindow.ForceEncounter(shiny: false, pokemonId: id, playAudio: false, zoneIndex: 5);
            for (var frame = 0; frame <= 5; frame++)
            {
                gardenWindow.SaveSceneCapture(Path.Combine(sequenceDirectory, $"{id}-{frame:D2}.png"));
                if (frame < 5) await Task.Delay(100);
            }
            gardenWindow.CloseEncounter();
        }

        Shutdown();
    }

    private async Task CaptureShinySparkScreenshotsAsync(GardenWindow gardenWindow, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var frameSamples = new[] { 0, 4, 8, 12, 15 };
            foreach (var scale in new[] { 1, 2, 3, 4 })
            {
                gardenWindow.SetScale(scale);
                await Task.Delay(350);
                foreach (var id in new[] { "JOLTIK", "PIKACHU", "WAILORD" })
                {
                    gardenWindow.ForceEncounter(shiny: false, pokemonId: id, playAudio: false, zoneIndex: 5);
                    await Task.Delay(550);
                    gardenWindow.SaveSceneCapture(Path.Combine(directory, $"{id}-{scale}x-normal.png"));
                    if (scale == 3 && id == "PIKACHU" && _capturePanelWindow is not null)
                    {
                        _capturePanelWindow.ShowCaptureResultDebug();
                        _capturePanelWindow.SaveCaptureScreenshot(Path.Combine(directory, "capture-result-normal.png"));
                    }
                    gardenWindow.CloseEncounter();

                    gardenWindow.ForceEncounter(shiny: true, pokemonId: id, playAudio: false, zoneIndex: 5);
                    await Task.Delay(550);
                    if (scale == 3 && id == "JOLTIK")
                    {
                        var beforeHide = gardenWindow.ShinySparkFrameDebug;
                        gardenWindow.Hide();
                        await Task.Delay(180);
                        var whileHidden = gardenWindow.ShinySparkFrameDebug;
                        gardenWindow.Show();
                        await Task.Delay(110);
                        var afterShow = gardenWindow.ShinySparkFrameDebug;
                        if (beforeHide < 0 || whileHidden != beforeHide || afterShow <= whileHidden)
                            throw new InvalidOperationException($"La secuencia shiny no pausó/reanudó: antes={beforeHide}, oculto={whileHidden}, visible={afterShow}.");
                        File.WriteAllText(Path.Combine(directory, "visibility-pause-test.txt"), "PASS; frame paused while the garden was hidden and advanced after it was shown again.");
                    }
                    foreach (var frame in frameSamples)
                    {
                        gardenWindow.SetDebugShinySparkFrame(frame);
                        gardenWindow.SaveSceneCapture(Path.Combine(directory, $"{id}-{scale}x-shiny-frame-{frame:D2}.png"));
                    }

                    if (scale == 3 && id == "PIKACHU" && _capturePanelWindow is not null)
                    {
                        _capturePanelWindow.ShowCaptureResultDebug(new RewardItemGrant("incense-rock", 2));
                        foreach (var frame in new[] { 0, 7, 15, -1 })
                        {
                            _capturePanelWindow.SetResultSparkFrameDebug(frame);
                            var suffix = frame < 0 ? "complete" : $"frame-{frame:D2}";
                            _capturePanelWindow.SaveCaptureScreenshot(Path.Combine(directory, $"capture-result-shiny-{suffix}.png"));
                        }
                    }
                    gardenWindow.CloseEncounter();
                }
            }
            File.WriteAllText(Path.Combine(directory, "shiny-sparks-validation.txt"),
                "PASS; 16 frames; 8x2 row-major source; 640ms sequence; normal/shiny Joltik, Pikachu, Wailord; scales 1x-4x; result card with reward samples captured.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "shiny-sparks-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
    }
#endif

    private void ToggleGarden()
    {
        if (_gardenWindow is null)
            return;

        if (_gardenWindow.IsVisible)
        {
            _capturePanelWindow?.Hide();
            _actionButtonsWindow?.Hide();
            _gardenCornerButtonsWindow?.Hide();
            _gardenWindow.Hide();
        }
        else
        {
            _gardenWindow.Show();
            _actionButtonsWindow?.Show();
            _actionButtonsWindow?.Reposition();
            _gardenCornerButtonsWindow?.Show();
            _gardenCornerButtonsWindow?.Reposition();
            if (_capturePanelWindow is not null) _capturePanelWindow.Show();
            RestoreDesktopOverlayZOrder();
        }
    }

    private void ShowCapturePanel(GardenWindow garden, GameSaveStore save,
        PokemonSpeciesMetadataCatalog metadata, PokemonEntry pokemon, bool shiny, bool alpha = false)
    {
        if (!garden.IsVisible) return;
        _capturePanelWindow?.Close();
        _capturePanelWindow = new CapturePanelWindow(garden, save, pokemon, metadata.Get(pokemon.Id), shiny,
            _actionButtonsWindow, _rewardBalance, alpha);
        _capturePanelWindow.Show();
    }

    private void ShowSettings(GardenWindow garden)
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.IsVisible && _settingsWindow.WindowState != WindowState.Minimized) _settingsWindow.Close();
            else ShowOrActivate(_settingsWindow);
            return;
        }
        _settingsWindow = new SettingsWindow(garden, _gameSave!, RestartAfterDataImport);
        var window = _settingsWindow;
        window.Activated += (_, _) => RestoreDesktopOverlayZOrder();
        window.IsVisibleChanged += (_, _) => _actionButtonsWindow?.SetWindowActive(0, window.IsVisible);
        window.Closed += (_, _) => { _settingsWindow = null; _actionButtonsWindow?.SetWindowActive(0, false); };
        _settingsWindow.Show();
        RestoreDesktopOverlayZOrder();
    }

    private void RestartAfterDataImport(string backupPath)
    {
        _restartingAfterDataImport = true;
        _timedEffects?.StopForDataReload();
        _rewardService?.StopForDataReload();
        foreach (var companion in _desktopCompanions.Values) companion.SuppressPersistenceOnClose();
        _rewardToast?.Close();

        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("No se encontró el ejecutable para reiniciar PokéGrass.");
            _ = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            });
            Shutdown();
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"data-import-restart;backup={backupPath}", exception);
            Shutdown();
        }
    }

    private void ShowBag(GameSaveStore save)
    {
        if (_bagWindow is not null)
        {
            if (_bagWindow.IsVisible && _bagWindow.WindowState != WindowState.Minimized) _bagWindow.Close();
            else ShowOrActivate(_bagWindow);
            return;
        }
        _bagWindow = new BagWindow(save, _timedEffects!);
        var window = _bagWindow;
        window.Activated += (_, _) => RestoreDesktopOverlayZOrder();
        window.IsVisibleChanged += (_, _) => _actionButtonsWindow?.SetWindowActive(1, window.IsVisible);
        window.Closed += (_, _) => { _bagWindow = null; _actionButtonsWindow?.SetWindowActive(1, false); };
        _bagWindow.Show();
        RestoreDesktopOverlayZOrder();
    }

    private void ShowPc(GameSaveStore save, PokemonCatalog catalog, PokemonSpeciesMetadataCatalog metadata)
    {
        if (_pcWindow is not null)
        {
            if (_pcWindow.IsVisible && _pcWindow.WindowState != WindowState.Minimized) _pcWindow.Close();
            else ShowOrActivate(_pcWindow);
            return;
        }
        _pcWindow = new PokemonPcWindow(save, catalog, metadata,
            (capture, dropCenter) => SendToDesktop(save, catalog, capture, dropCenter),
            LocateDesktopCompanion, ReturnDesktopCompanion, _megaEvolutions!);
        var window = _pcWindow;
        window.Activated += (_, _) => RestoreDesktopOverlayZOrder();
        window.IsVisibleChanged += (_, _) => _actionButtonsWindow?.SetWindowActive(2, window.IsVisible);
        window.Closed += (_, _) =>
        {
            SetDesktopCompanionsTopmost(true);
            _pcWindow = null;
            _actionButtonsWindow?.SetWindowActive(2, false);
            RestoreDesktopOverlayZOrder();
        };
        _pcWindow.Show();
        RestoreDesktopOverlayZOrder();
    }

    private bool SendToDesktop(GameSaveStore save, PokemonCatalog catalog, CapturedPokemon capture, System.Windows.Point? dropCenter = null)
    {
        var teamSlots = save.GetDesktopTeamSlots();
        var teamSlot = teamSlots.ToList().FindIndex(id => id is null);
        if (_desktopCompanions.ContainsKey(capture.RecordId) || _desktopCompanions.Count >= 6 || teamSlot < 0
            || !TryGetCaptureEntry(catalog, capture.Id, out var entry)) return false;

        var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.First();
        var offset = teamSlot;
        var x = screen.WorkingArea.Left + 48 + offset * 210;
        var y = screen.WorkingArea.Top + 72 + offset * 48;
        if (!save.TryAddDesktopCompanion(capture.RecordId, x, y)) return false;
        try
        {
            CreateDesktopCompanion(save, capture, entry, x, y, isPinned: false, dropCenter);
            _pcWindow?.Refresh();
            return true;
        }
        catch (Exception exception)
        {
            save.RemoveDesktopCompanion(capture.RecordId);
            EncounterDiagnostics.WriteException($"desktop-companion-create:{capture.Id}", exception);
            return false;
        }
    }

    private void RestoreDesktopCompanions(GameSaveStore save, PokemonCatalog catalog)
    {
        var captures = save.GetPcSnapshot().Captures.ToDictionary(capture => capture.RecordId, StringComparer.Ordinal);
        foreach (var companion in save.GetDesktopCompanions())
        {
            if (!captures.TryGetValue(companion.RecordId, out var capture)
                || !TryGetCaptureEntry(catalog, capture.Id, out var entry))
            {
                save.RemoveDesktopCompanion(companion.RecordId);
                continue;
            }
            try { CreateDesktopCompanion(save, capture, entry, companion.X, companion.Y, companion.IsPinned); }
            catch (Exception exception) { EncounterDiagnostics.WriteException($"desktop-companion-restore:{capture.Id}", exception); }
        }
    }

    private static bool TryGetCaptureEntry(PokemonCatalog catalog, string id, out PokemonEntry entry)
    {
        try { entry = catalog.GetForCapture(id); return true; }
        catch (KeyNotFoundException) { entry = null!; return false; }
    }

    private void CreateDesktopCompanion(GameSaveStore save, CapturedPokemon capture, PokemonEntry entry,
        double x, double y, bool isPinned, System.Windows.Point? dropCenter = null)
    {
        if (capture.MegaFormId is not null && _megaEvolutions is not null
            && _megaEvolutions.TryGetForm(capture.MegaFormId, out var megaForm))
            entry = _megaEvolutions.GetEntry(megaForm.FormId);
        var window = new DesktopCompanionWindow(save, capture, entry, x, y, isPinned, OnCompanionReturned,
            dropCenter, () => _desktopCompanions.Values.ToArray(),
            (berryId, requestedBerryId) => FeedCompanionBerry(capture.RecordId, berryId, requestedBerryId),
            _megaEvolutions!, formId => ChangeCompanionMegaForm(save, capture.RecordId, formId),
            StartTeamRally, ToggleDesktopToy, () => _desktopToy?.IsVisible == true,
            _ => CancelTeamRally(), window => CancelToyChase(window.RecordId), OnToyPushed,
            () => _desktopToy?.GetCollisionBounds() ?? System.Windows.Rect.Empty,
            () => GetPokemonGroupSpacingPixels(), _speciesMetadata,
            (angryPokemon, otherPokemon) => _desktopBattle?.ReportAnger(angryPokemon, otherPokemon),
            companion => _desktopBattle?.CancelFor(companion),
            (first, second) => _desktopCouples?.TryStartFromCollision(first, second) == true,
            TryReturnDesktopCompanionToPcOnDrop, RestoreDesktopOverlayZOrder, _pokemonCatalog,
            targetId => ApplyCompanionEvolution(save, capture.RecordId, targetId));
        _desktopCompanions[capture.RecordId] = window;
        window.Activated += (_, _) => RestoreDesktopOverlayZOrder();
        window.Closed += (_, _) => _desktopCompanions.Remove(capture.RecordId);
        window.Show();
        if (_desktopToy?.IsVisible == true) window.RaiseAboveToy();
        RestoreDesktopOverlayZOrder();
        // Companion windows stay in the topmost band so a topmost garden window
        // can never cover them while they walk across it. An active PC window is
        // still raised by normal activation ordering.
    }

    private void OfferEvolutionAfterVictory(DesktopCompanionWindow winner)
    {
        if (!_evolutionAssetsAvailable || _pokemonEvolutions is null || _speciesMetadata is null
            || _gameSave is null || _evolutionPrompt is not null
            || !_desktopCompanions.TryGetValue(winner.RecordId, out var active) || !ReferenceEquals(active, winner)
            || !winner.IsVisible || winner.IsDitto || winner.IsEvolutionPendingOrActive)
            return;

        var collection = _gameSave.GetPcSnapshot().Captures;
        var source = collection.FirstOrDefault(capture => capture.RecordId == winner.RecordId);
        if (source is null) return;
        var targets = _pokemonEvolutions.GetAvailableTargets(source, collection);
        if (targets.Count == 0 || !PokemonEvolutionCatalog.RollForEvolution(_ => Random.Shared.Next(100))) return;

        var target = targets[Random.Shared.Next(targets.Count)];
        if (!winner.TryBeginEvolutionPrompt()) return;
        var currentName = PokemonSex.GetDisplayName(_speciesMetadata.GetForCapture(source.Id));
        var targetName = PokemonSex.GetDisplayName(target);
        var prompt = new EvolutionPromptWindow(winner, currentName, targetName, accepted =>
        {
            if (!accepted)
            {
                winner.CancelEvolutionPrompt();
                return;
            }

            if (!_desktopCompanions.TryGetValue(winner.RecordId, out var stillActive)
                || !ReferenceEquals(stillActive, winner) || !winner.IsVisible)
            {
                winner.CancelEvolutionPrompt();
                return;
            }

            var latestCollection = _gameSave?.GetPcSnapshot().Captures;
            if (latestCollection is null || !latestCollection.Any(capture => capture.RecordId == winner.RecordId)
                || !_pokemonEvolutions.GetAvailableTargets(
                    latestCollection.First(capture => capture.RecordId == winner.RecordId), latestCollection)
                    .Any(candidate => candidate.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase)))
            {
                winner.CancelEvolutionPrompt();
                return;
            }

            if (!winner.BeginPermanentEvolution(target.Id)) winner.CancelEvolutionPrompt();
        });
        _evolutionPrompt = prompt;
        prompt.Closed += (_, _) =>
        {
            if (ReferenceEquals(_evolutionPrompt, prompt)) _evolutionPrompt = null;
            winner.CancelEvolutionPrompt();
        };
        try { prompt.Show(); }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"pokemon-evolution-prompt:{winner.RecordId}", exception);
            _evolutionPrompt = null;
            winner.CancelEvolutionPrompt();
        }
    }

    private bool ApplyCompanionEvolution(GameSaveStore save, string recordId, string targetId)
    {
        if (_pokemonCatalog is null || _speciesMetadata is null
            || !_desktopCompanions.TryGetValue(recordId, out var companion) || !companion.IsVisible
            || !_pokemonCatalog.ById.TryGetValue(targetId, out var targetEntry))
            return false;
        var state = save.GetDesktopCompanions().FirstOrDefault(item => item.RecordId == recordId);
        if (state is null) return false;
        var previous = save.GetPcSnapshot().Captures.FirstOrDefault(item => item.RecordId == recordId);
        if (previous is null || !save.TryEvolveCapture(recordId, previous.Id,
                _speciesMetadata.Get(targetId), _speciesMetadata))
            return false;

        var evolved = save.GetPcSnapshot().Captures.First(item => item.RecordId == recordId);
        companion.SuppressPersistenceOnClose();
        companion.Close();
        try
        {
            CreateDesktopCompanion(save, evolved, targetEntry, state.X, state.Y, state.IsPinned);
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"pokemon-evolution-desktop:{targetId}", exception);
        }
        _pcWindow?.Refresh();
        return true;
    }

    private bool ChangeCompanionMegaForm(GameSaveStore save, string recordId, string? megaFormId)
    {
        if (_pokemonCatalog is null || _megaEvolutions is null) return false;
        var capture = save.GetPcSnapshot().Captures.FirstOrDefault(item => item.RecordId == recordId);
        if (capture is null) return false;
        if (megaFormId is not null
            && !_megaEvolutions.CanApplyForm(capture, megaFormId)) return false;
        if (!save.SetMegaForm(recordId, megaFormId)) return false;

        var companionState = save.GetDesktopCompanions().FirstOrDefault(item => item.RecordId == recordId);
        if (megaFormId is not null)
        {
            _pcWindow?.Refresh();
            return true;
        }

        if (companionState is not null && _desktopCompanions.TryGetValue(recordId, out var oldWindow))
        {
            oldWindow.SuppressPersistenceOnClose();
            oldWindow.Close();
            var currentCapture = save.GetPcSnapshot().Captures.First(item => item.RecordId == recordId);
            CreateDesktopCompanion(save, currentCapture, _pokemonCatalog.ById[currentCapture.Id],
                companionState.X, companionState.Y, companionState.IsPinned);
        }
        _pcWindow?.Refresh();
        return true;
    }

    private void StartCompanionActivityTimers()
    {
        var now = DateTime.UtcNow;
        _nextBerryRequestAt = now.AddMinutes(Random.Shared.Next(20, 41));
        _nextPlayRequestAt = now.AddMinutes(Random.Shared.Next(20, 41));
        _companionActivityTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _companionActivityTimer.Tick += (_, _) => TickCompanionRequests();
        _companionActivityTimer.Start();
    }

    private void TickCompanionRequests()
    {
        var now = DateTime.UtcNow;
        if (_teamRallyActive) UpdateTeamRally(now);
        if (_berryRequestRecordId is not null && now >= _berryRequestExpiresAt) ClearBerryRequest();
        if (_playRequestRecordId is not null && now >= _playRequestExpiresAt) ClearPlayRequest();
        if (_desktopCompanions.Count == 0 || _teamRallyActive || _desktopToy?.IsVisible == true
            || _desktopBattle?.IsBattleActive == true
            || _desktopCouples?.IsCouplesActive == true
            || _berryRequestRecordId is not null || _playRequestRecordId is not null) return;

        var berryReady = now >= _nextBerryRequestAt;
        var playReady = now >= _nextPlayRequestAt;
        if (!berryReady && !playReady) return;
        var tryBerry = berryReady && (!playReady || Random.Shared.Next(2) == 0);
        if (tryBerry && StartBerryRequest()) return;
        if (playReady && StartPlayRequest()) return;
        if (berryReady) _nextBerryRequestAt = now.AddMinutes(Random.Shared.Next(20, 41));
        if (playReady) _nextPlayRequestAt = now.AddMinutes(Random.Shared.Next(20, 41));
    }

    private bool StartBerryRequest()
    {
        if (_gameSave is null || _rewardBalance is null || _desktopCompanions.Count == 0) return false;
        string[] berryIds = ["razz-berry", "silver-razz-berry", "golden-razz-berry", "nanab-berry", "silver-nanab-berry", "golden-nanab-berry"];
        var available = berryIds.Where(id => _gameSave.Count(id) > 0 && _rewardBalance.Items.ContainsKey(id)).ToArray();
        if (available.Length == 0)
        {
            _nextBerryRequestAt = DateTime.UtcNow.AddMinutes(Random.Shared.Next(20, 41));
            return false;
        }

        var companion = _desktopCompanions.Values.ElementAt(Random.Shared.Next(_desktopCompanions.Count));
        var berryId = available[Random.Shared.Next(available.Length)];
        _berryRequestRecordId = companion.RecordId;
        _berryRequestItemId = berryId;
        _berryRequestExpiresAt = DateTime.UtcNow.AddSeconds(60);
        companion.ShowBerryRequest(berryId, _rewardBalance.Items[berryId].Icon, TimeSpan.FromSeconds(60));
        var now = DateTime.UtcNow;
        _nextBerryRequestAt = now.AddMinutes(Random.Shared.Next(20, 41));
        if (now >= _nextPlayRequestAt) _nextPlayRequestAt = now.AddMinutes(Random.Shared.Next(20, 41));
        return true;
    }

    private bool StartPlayRequest()
    {
        if (_desktopCompanions.Count == 0) return false;
        var companion = _desktopCompanions.Values.ElementAt(Random.Shared.Next(_desktopCompanions.Count));
        _playRequestRecordId = companion.RecordId;
        _playRequestExpiresAt = DateTime.UtcNow.AddSeconds(60);
        companion.ShowPlayRequest(TimeSpan.FromSeconds(60));
        companion.PlayAttentionCry();
        var now = DateTime.UtcNow;
        _nextPlayRequestAt = now.AddMinutes(Random.Shared.Next(20, 41));
        if (now >= _nextBerryRequestAt) _nextBerryRequestAt = now.AddMinutes(Random.Shared.Next(20, 41));
        return true;
    }

    private void ClearBerryRequest()
    {
        if (_berryRequestRecordId is not null
            && _desktopCompanions.TryGetValue(_berryRequestRecordId, out var companion))
            companion.ClearBerryRequest();
        _berryRequestRecordId = null;
        _berryRequestItemId = null;
        _berryRequestExpiresAt = DateTime.MinValue;
    }

    private void ClearPlayRequest()
    {
        if (_playRequestRecordId is not null
            && _desktopCompanions.TryGetValue(_playRequestRecordId, out var companion))
            companion.ClearPlayRequest();
        _playRequestRecordId = null;
        _playRequestExpiresAt = DateTime.MinValue;
    }

    private void StartTeamRally(DesktopCompanionWindow leader)
    {
        if (!_desktopCompanions.ContainsKey(leader.RecordId)) return;
        _desktopCouples?.CancelActive();
        _desktopBattle?.CancelActiveBattle();
        CancelTeamRally();
        ClearBerryRequest();
        ClearPlayRequest();
        var now = DateTime.UtcNow;
        var leaderBounds = leader.VisibleBodyScreenBounds;
        if (leaderBounds.IsEmpty) return;
        var leaderCenter = new System.Windows.Point(leaderBounds.X + leaderBounds.Width / 2d, leaderBounds.Y + leaderBounds.Height / 2d);
        var others = _desktopCompanions.Values.Where(item => item.RecordId != leader.RecordId
            && item.CanJoinTeamRally).ToArray();
        // Scale the gap to actual bodies. The old fixed three-grass gap made
        // the ring too wide for a natural gathering on many desktop layouts.
        var largestBody = _desktopCompanions.Values.Select(item => item.VisibleBodyScreenBounds)
            .Where(bounds => !bounds.IsEmpty).Select(bounds => Math.Max(bounds.Width, bounds.Height))
            .DefaultIfEmpty(48d).Max();
        var spacing = Math.Min(GetPokemonGroupSpacingPixels(), Math.Max(48d, largestBody * .85d));
        _toyPausedForTeamRally = _desktopToy?.IsVisible == true;
        if (_toyPausedForTeamRally)
        {
            _desktopToy!.PauseForTeamRally();
            _toyActivityTimer?.Stop();
        }

        // Wait for every reachable participant instead of starting the
        // celebration after an arbitrary gather timeout.
        _teamRallyGatherDeadline = DateTime.MaxValue;
        _teamRallyExpiresAt = _teamRallyGatherDeadline;
        _teamRallyCelebrating = false;
        _teamRallyFinalizing = false;
        _teamRallyReactionIndex = 0;
        _teamRallyLeaderRecordId = leader.RecordId;
        _teamRallyActive = true;
        foreach (var companion in _desktopCompanions.Values)
            companion.SetRallyEventActive(true);
        leader.BeginTeamRallyLeader(_teamRallyGatherDeadline);

        var stationaryParticipants = new List<DesktopCompanionWindow> { leader };
        foreach (var pinned in others.Where(item => item.IsPinned))
        {
            pinned.JoinTeamRally(default, leaderCenter, _teamRallyGatherDeadline);
            var pinnedBounds = pinned.VisibleBodyScreenBounds;
            if (!pinned.IsRallyParticipant || pinnedBounds.IsEmpty
                || stationaryParticipants.Any(item => !RectanglesAreSeparated(
                    item.VisibleBodyScreenBounds, pinnedBounds, spacing)))
            {
                pinned.CancelTeamRally();
                continue;
            }
            stationaryParticipants.Add(pinned);
        }

        // Preserve each Pokémon's side of the leader when choosing a destination.
        // Assigning slots by collection order made paths cross through the group,
        // so participants repeatedly blocked one another on the way in.
        var movers = others.Where(item => !item.IsPinned && !item.VisibleBodyScreenBounds.IsEmpty)
            .OrderBy(item =>
            {
                var body = item.VisibleBodyScreenBounds;
                return Math.Atan2(body.Y + body.Height / 2d - leaderCenter.Y,
                    body.X + body.Width / 2d - leaderCenter.X);
            }).ToArray();
        var maxHalfDiagonal = stationaryParticipants.Concat(movers)
            .Select(item => item.VisibleBodyScreenBounds)
            .Select(bounds => Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) / 2d)
            .DefaultIfEmpty(0d).Max();
        var minimumCenterDistance = spacing + 2d * maxHalfDiagonal;
        var radius = Math.Max(minimumCenterDistance,
            movers.Length > 1 ? minimumCenterDistance / (2d * Math.Sin(Math.PI / movers.Length)) : 0d);
        var monitor = Forms.Screen.FromPoint(new System.Drawing.Point((int)leaderCenter.X, (int)leaderCenter.Y));
        var work = monitor.WorkingArea;
        var assigned = stationaryParticipants.Select(item => item.VisibleBodyScreenBounds).ToList();
        var accepted = new List<(DesktopCompanionWindow Companion, System.Windows.Point Target)>();
        for (var index = 0; index < movers.Length; index++)
        {
            var mover = movers[index];
            var body = mover.VisibleBodyScreenBounds;
            var originAngle = Math.Atan2(body.Y + body.Height / 2d - leaderCenter.Y,
                body.X + body.Width / 2d - leaderCenter.X);
            var found = false;
            for (var ring = 0; ring < 4 && !found; ring++)
            for (var offset = 0; offset < Math.Max(8, movers.Length * 4) && !found; offset++)
            {
                var slotOffset = offset == 0 ? 0d
                    : ((offset + 1) / 2d) * (offset % 2 == 1 ? 1d : -1d);
                var angle = originAngle + slotOffset * (2d * Math.PI / Math.Max(1, movers.Length));
                var distance = radius * (1d + ring * .32d);
                var halfWidth = body.Width / 2d;
                var halfHeight = body.Height / 2d;
                var centerX = Math.Clamp(leaderCenter.X + Math.Cos(angle) * distance,
                    work.Left + halfWidth, work.Right - halfWidth);
                var centerY = Math.Clamp(leaderCenter.Y + Math.Sin(angle) * distance,
                    work.Top + halfHeight, work.Bottom - halfHeight);
                var targetBody = new System.Windows.Rect(centerX - halfWidth, centerY - halfHeight, body.Width, body.Height);
                if (assigned.Any(existing => !RectanglesAreSeparated(existing, targetBody, spacing))) continue;
                assigned.Add(targetBody);
                accepted.Add((mover, new System.Windows.Point(centerX, centerY)));
                found = true;
            }

            // If the full preferred spacing does not fit around a leader near
            // a screen edge, relax only the formation gap. This keeps everyone
            // in the event instead of leaving them to collide in normal roaming.
            if (!found)
            {
                const double compactGap = 14d;
                var compactCenterDistance = compactGap + 2d * maxHalfDiagonal;
                var compactRadius = Math.Max(compactCenterDistance,
                    movers.Length > 1 ? compactCenterDistance / (2d * Math.Sin(Math.PI / movers.Length)) : 0d);
                for (var ring = 0; ring < 6 && !found; ring++)
                for (var offset = 0; offset < Math.Max(16, movers.Length * 8) && !found; offset++)
                {
                    var slotOffset = offset == 0 ? 0d
                        : ((offset + 1) / 2d) * (offset % 2 == 1 ? 1d : -1d);
                    var angle = originAngle + slotOffset * (2d * Math.PI / Math.Max(1, movers.Length));
                    var distance = compactRadius * (1d + ring * .2d);
                    var halfWidth = body.Width / 2d;
                    var halfHeight = body.Height / 2d;
                    var centerX = Math.Clamp(leaderCenter.X + Math.Cos(angle) * distance,
                        work.Left + halfWidth, work.Right - halfWidth);
                    var centerY = Math.Clamp(leaderCenter.Y + Math.Sin(angle) * distance,
                        work.Top + halfHeight, work.Bottom - halfHeight);
                    var targetBody = new System.Windows.Rect(centerX - halfWidth, centerY - halfHeight,
                        body.Width, body.Height);
                    if (assigned.Any(existing => !RectanglesAreSeparated(existing, targetBody, compactGap))) continue;
                    assigned.Add(targetBody);
                    accepted.Add((mover, new System.Windows.Point(centerX, centerY)));
                    found = true;
                }
            }
        }

        foreach (var (companion, target) in accepted)
            companion.JoinTeamRally(target, leaderCenter, _teamRallyGatherDeadline);
    }

    private void UpdateTeamRally(DateTime now)
    {
        if (!_teamRallyActive) return;
        if (_teamRallyFinalizing)
        {
            if (now >= _teamRallyFinalizeAt) CancelTeamRally();
            return;
        }

        var participants = _desktopCompanions.Values.Where(item => item.IsRallyParticipant).ToArray();
        if (participants.Length == 0) { CancelTeamRally(); return; }
        if (!_teamRallyCelebrating)
        {
            foreach (var companion in participants)
                if (!companion.IsRallyArrived && IsTeamRallyParticipantReady(companion))
                    companion.CompleteTeamRallyApproach();
            if (participants.Any(item => !item.IsRallyArrived)) return;
            _teamRallyCelebrating = true;
            _teamRallyExpiresAt = now.AddSeconds(Random.Shared.Next(8, 13));
            _teamRallyNextReactionAt = now.AddMilliseconds(450);
            _teamRallyNextCryAt = now.AddSeconds(2.5);
            var groupBodies = participants.Select(item => item.VisibleBodyScreenBounds).Where(item => !item.IsEmpty).ToArray();
            var groupCenter = groupBodies.Length == 0 ? new System.Windows.Point(0, 0)
                : new System.Windows.Point(groupBodies.Average(item => item.X + item.Width / 2d),
                    groupBodies.Average(item => item.Y + item.Height / 2d));
            var positiveProtectionUntil = now.AddSeconds(60);
            for (var i = 0; i < participants.Length; i++)
            for (var j = i + 1; j < participants.Length; j++)
                participants[i].ProtectFromNegativeInteractionWith(participants[j], positiveProtectionUntil);
            foreach (var companion in participants) companion.BeginTeamRallyCelebration(_teamRallyExpiresAt, groupCenter);
        }

        if (now >= _teamRallyExpiresAt)
        {
            foreach (var companion in participants.Where(_ => Random.Shared.Next(2) == 0))
                companion.SetTeamRallyReaction(CompanionEmotion.Happy, TimeSpan.FromSeconds(1.8));
            _teamRallyFinalizing = true;
            _teamRallyFinalizeAt = now.AddSeconds(2);
            foreach (var companion in participants) companion.SetTeamRallyEnd(_teamRallyFinalizeAt);
            return;
        }

        if (now < _teamRallyNextReactionAt) return;
        var target = participants[_teamRallyReactionIndex++ % participants.Length];
        var emotion = Random.Shared.Next(3) switch
        {
            0 => CompanionEmotion.Happy,
            1 => CompanionEmotion.Heart,
            _ => CompanionEmotion.Music
        };
        target.SetTeamRallyReaction(emotion, TimeSpan.FromSeconds(1.25));
        if (emotion == CompanionEmotion.Music && now >= _teamRallyNextCryAt)
        {
            target.PlayTeamRallyCry();
            _teamRallyNextCryAt = now.AddSeconds(3.5);
        }
        _teamRallyNextReactionAt = now.AddMilliseconds(1400);
    }

    private void CancelTeamRally()
    {
        _teamRallyActive = false;
        _teamRallyCelebrating = false;
        _teamRallyFinalizing = false;
        _teamRallyLeaderRecordId = null;
        _teamRallyExpiresAt = DateTime.MinValue;
        foreach (var companion in _desktopCompanions.Values.ToArray())
        {
            companion.CancelTeamRally();
            companion.SetRallyEventActive(false);
        }
        if (_toyPausedForTeamRally)
        {
            _toyPausedForTeamRally = false;
            if (_desktopToy?.IsVisible == true)
            {
                _desktopToy.ResumeAfterTeamRally();
                _lastToyInteractionAt = DateTime.UtcNow;
                if (_toyActivityTimer is not null) _toyActivityTimer.Start();
            }
        }
    }

    private static bool IsTeamRallyParticipantReady(DesktopCompanionWindow participant)
        => participant.IsNearRallyTarget;

    private void ToggleDesktopToy(DesktopCompanionWindow caller)
    {
        _desktopCouples?.CancelActive();
        _desktopBattle?.CancelActiveBattle();
        if (_teamRallyActive) CancelTeamRally();
        if (_desktopToy?.IsVisible == true)
        {
            StoreDesktopToy();
            return;
        }

        CancelTeamRally();
        ClearBerryRequest();
        DesktopCompanionWindow? requester = null;
        if (_playRequestRecordId is not null) _desktopCompanions.TryGetValue(_playRequestRecordId, out requester);
        var playRequested = requester is not null;
        var host = playRequested ? requester! : caller;
        if (playRequested)
        {
            ClearPlayRequest();
            host.ActivateReaction(CompanionEmotion.Happy, TimeSpan.FromSeconds(2.5));
            _toyPassReceiverWaitSince = DateTime.UtcNow;
        }
        var body = host.VisibleBodyScreenBounds;
        if (body.IsEmpty) return;
        _desktopToy = new DesktopToyWindow(OnToyDragStarted, OnToyDragEnded, FindToyCollision, OnToyPassReceived);
        _desktopToy.Closed += (_, _) =>
        {
            _desktopToy = null;
            _toyParticipantRecordId = null;
            _toyPassKickAt = DateTime.MinValue;
            _toyPassReceiverRecordId = null;
            _toyPlayerSince.Clear();
            _toyBoredUntil.Clear();
            foreach (var companion in _desktopCompanions.Values) companion.StopToyChase();
        };
        _desktopToy.Show();
        var toyRadius = Math.Max(1d, _desktopToy.GetCollisionBounds().Width / 2d);
        // Place the ball beside the Pokémon with its lower edge on the same
        // visual ground line, rather than floating at mid-body height.
        _desktopToy.MoveCenter(new System.Windows.Point(body.Right + toyRadius + 4d, body.Bottom - toyRadius));
        foreach (var companion in _desktopCompanions.Values) companion.RaiseAboveToy();
        _lastToyInteractionAt = DateTime.UtcNow;
        _toyAtTopSince = DateTime.MinValue;
        _preferredToyParticipantRecordId = playRequested ? host.RecordId : null;
        _nextToyParticipantAt = playRequested ? DateTime.UtcNow.AddMilliseconds(2600) : DateTime.MinValue;
        _lastToyParticipantRecordId = null;
        _toyActivityTimer ??= new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _toyActivityTimer.Tick -= OnToyActivityTick;
        _toyActivityTimer.Tick += OnToyActivityTick;
        _toyActivityTimer.Start();
    }

    private void StoreDesktopToy()
    {
        _toyActivityTimer?.Stop();
        _toyParticipantRecordId = null;
        _toyPlayerSince.Clear();
        _toyBoredUntil.Clear();
        _toyPassKickAt = DateTime.MinValue;
        _toyPassReceiverRecordId = null;
        _preferredToyParticipantRecordId = null;
        _toyManualThrowChaserRecordId = null;
        _toyAtTopSince = DateTime.MinValue;
        _lastSoloToyPushDirection = null;
        _soloToyPushDirectionStreak = 0;
        foreach (var companion in _desktopCompanions.Values) companion.StopToyChase();
        var toy = _desktopToy;
        _desktopToy = null;
        if (toy is not null) toy.Close();
    }

    private void OnToyActivityTick(object? sender, EventArgs e)
    {
        if (_desktopToy?.IsVisible != true)
        {
            _toyActivityTimer?.Stop();
            return;
        }
        if (_desktopToy.IsBeingDragged)
        {
            _lastToyInteractionAt = DateTime.UtcNow;
            return;
        }
        if (DateTime.UtcNow - _lastToyInteractionAt >= TimeSpan.FromMinutes(3))
        {
            StoreDesktopToy();
            return;
        }
        var now = DateTime.UtcNow;
        var center = _desktopToy.GetScreenCenter();
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
        var topThreshold = screen.WorkingArea.Top + screen.WorkingArea.Height * .2d;
        if (center.Y <= topThreshold)
        {
            if (_toyAtTopSince == DateTime.MinValue) _toyAtTopSince = now;
        }
        else
        {
            _toyAtTopSince = DateTime.MinValue;
        }
        var currentPlayer = _toyParticipantRecordId is not null
            && _desktopCompanions.TryGetValue(_toyParticipantRecordId, out var playing)
            ? playing : null;
        if (currentPlayer is not null)
        {
            if (currentPlayer.IsChasingToy)
                currentPlayer.UpdateToyChaseTarget(center);
            else if (DistanceTo(currentPlayer.VisibleBodyScreenBounds, center) > ToyInterestRangePixels)
            {
                currentPlayer.StopToyChase();
                _toyPlayerSince.Remove(currentPlayer.RecordId);
                _toyParticipantRecordId = null;
            }
        }

        foreach (var (recordId, startedAt) in _toyPlayerSince.ToArray())
        {
            if (!_desktopCompanions.TryGetValue(recordId, out var boredPlayer))
            {
                _toyPlayerSince.Remove(recordId);
                continue;
            }
            var elapsed = now - startedAt;
            if (elapsed < TimeSpan.FromSeconds(30)
                || (elapsed < TimeSpan.FromSeconds(90) && Random.Shared.Next(100) != 0)) continue;
            boredPlayer.StopToyChase();
            _toyPlayerSince.Remove(recordId);
            _toyBoredUntil[recordId] = now.AddSeconds(Random.Shared.Next(45, 91));
            if (_toyParticipantRecordId == recordId) _toyParticipantRecordId = null;
            if (_preferredToyParticipantRecordId == recordId) _preferredToyParticipantRecordId = null;
        }

        foreach (var (recordId, boredUntil) in _toyBoredUntil.ToArray())
            if (now >= boredUntil) _toyBoredUntil.Remove(recordId);

        if (_toyPassKickAt != DateTime.MinValue)
        {
            if (now < _toyPassKickAt) return;
            var receiverId = _toyPassReceiverRecordId;
            _toyPassKickAt = DateTime.MinValue;
            _toyPassReceiverRecordId = null;
            KickToyPass(receiverId);
            return;
        }

        if (currentPlayer is not null && currentPlayer.IsChasingToy)
        {
            if (_desktopToy.IsMoving) currentPlayer.UpdateToyChaseTarget(center);
            return;
        }
        _toyParticipantRecordId = null;
        if (_desktopToy.IsMoving || now < _nextToyParticipantAt) return;

        if (_preferredToyParticipantRecordId is not null
            && _toyPlayerSince.ContainsKey(_preferredToyParticipantRecordId)
            && _desktopCompanions.TryGetValue(_preferredToyParticipantRecordId, out var preferredReceiver)
            && !preferredReceiver.CanPlayWithToy)
        {
            if (now - _toyPassReceiverWaitSince < TimeSpan.FromSeconds(3))
            {
                _nextToyParticipantAt = now.AddMilliseconds(200);
                return;
            }
            _preferredToyParticipantRecordId = null;
        }

        var ordered = _desktopCompanions.Values
            .Where(item => item.CanPlayWithToy
                && DistanceTo(item.VisibleBodyScreenBounds, center) <= ToyInterestRangePixels
                && CanJoinToyGroupAtCurrentPosition(item)
                && (!_toyBoredUntil.TryGetValue(item.RecordId, out var boredUntil) || now >= boredUntil))
            .OrderBy(item => _preferredToyParticipantRecordId == item.RecordId ? 0 : 1)
            .ThenBy(item => DistanceTo(item.VisibleBodyScreenBounds, center))
            .ToArray();
        if (ordered.Length == 0) return;
        var available = ordered.Length > 1 && _lastToyParticipantRecordId is not null
            ? ordered.Where(item => item.RecordId != _lastToyParticipantRecordId).ToArray() : ordered;
        var player = available.FirstOrDefault() ?? ordered[0];
        if (player.RecordId == _preferredToyParticipantRecordId) _preferredToyParticipantRecordId = null;
        _toyPlayerSince.TryAdd(player.RecordId, now);
        player.StartToyChase(center);
        if (player.IsChasingToy) _toyParticipantRecordId = player.RecordId;
        else _nextToyParticipantAt = now.AddMilliseconds(700);
    }

    private static double DistanceTo(System.Windows.Rect rect, System.Windows.Point point)
    {
        if (rect.IsEmpty) return double.MaxValue;
        var dx = point.X - (rect.X + rect.Width / 2d);
        var dy = point.Y - (rect.Y + rect.Height / 2d);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private bool CanJoinToyGroupAtCurrentPosition(DesktopCompanionWindow candidate)
    {
        if (_toyPlayerSince.ContainsKey(candidate.RecordId)) return true;
        var spacing = GetPokemonGroupSpacingPixels();
        return _toyPlayerSince.Keys
            .Where(id => id != candidate.RecordId && _desktopCompanions.ContainsKey(id))
            .Select(id => _desktopCompanions[id])
            .Where(item => item.IsVisible && item.IsToyPlayParticipant)
            .All(item => RectanglesAreSeparated(candidate.VisibleBodyScreenBounds,
                item.VisibleBodyScreenBounds, spacing));
    }

    private string? FindToyCollision(System.Windows.Rect toyCircle, string? ignoredRecordId)
    {
        var centerX = toyCircle.X + toyCircle.Width / 2d;
        var centerY = toyCircle.Y + toyCircle.Height / 2d;
        var radius = Math.Min(toyCircle.Width, toyCircle.Height) / 2d;
        return _desktopCompanions.Values.FirstOrDefault(item =>
        {
            if (item.RecordId == ignoredRecordId) return false;
            if (!item.IsVisible) return false;
            var body = item.ToyContactScreenBounds;
            if (body.IsEmpty) return false;
            var closestX = Math.Clamp(centerX, body.Left, body.Right);
            var closestY = Math.Clamp(centerY, body.Top, body.Bottom);
            var dx = centerX - closestX;
            var dy = centerY - closestY;
            return dx * dx + dy * dy <= radius * radius;
        })?.RecordId;
    }

    private void OnToyPushed(DesktopCompanionWindow companion, System.Windows.Point direction)
    {
        if (_desktopToy?.IsVisible != true) return;
        var now = DateTime.UtcNow;
        _toyPlayerSince.TryAdd(companion.RecordId, now);
        companion.JoinToyGame();
        var center = _desktopToy.GetScreenCenter();
        var spacing = GetPokemonGroupSpacingPixels();
        var activePlayers = _toyPlayerSince.Keys
            .Where(id => _desktopCompanions.ContainsKey(id)
                && (!_toyBoredUntil.TryGetValue(id, out var boredUntil) || now >= boredUntil))
            .Select(id => _desktopCompanions[id])
            .Where(item => item.IsVisible && !item.IsPinned)
            .ToArray();

        // Invite a second nearby Pokémon only when it can keep the four-tile group gap.
        if (activePlayers.Length == 1)
        {
            var invite = _desktopCompanions.Values
                .Where(item => item.RecordId != companion.RecordId && item.CanPlayWithToy
                    && DistanceTo(item.VisibleBodyScreenBounds, center) <= ToyGroupInviteRangePixels
                    && (!_toyBoredUntil.TryGetValue(item.RecordId, out var boredUntil) || now >= boredUntil)
                    && activePlayers.All(player => RectanglesAreSeparated(
                        item.VisibleBodyScreenBounds, player.VisibleBodyScreenBounds, spacing)))
                .OrderBy(item => DistanceTo(item.VisibleBodyScreenBounds, center))
                .FirstOrDefault();
            if (invite is not null)
            {
                _toyPlayerSince.TryAdd(invite.RecordId, now);
                invite.JoinToyGame();
                activePlayers = activePlayers.Append(invite).ToArray();
            }
        }

        // A solo player keeps the organic chase. A group member stays put while
        // the ball travels to the next player.
        var recipient = activePlayers.Length < 2
            ? null
            : activePlayers.Where(item => item.RecordId != companion.RecordId)
                .OrderBy(item => DistanceTo(item.VisibleBodyScreenBounds, center))
                .FirstOrDefault();
        string? soloChaserId = null;
        if (recipient is null)
        {
            StartSoloToyChase(companion, direction, now);
            soloChaserId = companion.RecordId;
        }
        else
        {
            companion.WaitForToyPass();
            recipient.JoinToyGame();
            companion.FaceToward(GetBodyCenter(recipient.VisibleBodyScreenBounds));
            recipient.FaceToward(GetBodyCenter(companion.VisibleBodyScreenBounds));
            var reception = GetToyReceptionPoint(recipient.VisibleBodyScreenBounds, center,
                _desktopToy.GetCollisionBounds().Width / 2d + 4d);
            if (!_desktopToy.PassTo(reception, companion.RecordId, recipient.RecordId))
                ReleaseToyPlayers(activePlayers);
        }

        _lastToyInteractionAt = now;
        _lastToyParticipantRecordId = companion.RecordId;
        _toyParticipantRecordId = soloChaserId;
        _nextToyParticipantAt = now.AddMilliseconds(500);
    }

    private static System.Windows.Point GetBodyCenter(System.Windows.Rect bounds)
        => bounds.IsEmpty ? new System.Windows.Point() : new System.Windows.Point(
            bounds.X + bounds.Width / 2d, bounds.Y + bounds.Height / 2d);

    private void StartSoloToyChase(DesktopCompanionWindow player, System.Windows.Point direction, DateTime now)
    {
        if (_desktopToy?.IsVisible != true) return;
        var center = _desktopToy.GetScreenCenter();
        var preferredDirection = Math.Abs(direction.X) >= Math.Abs(direction.Y)
            ? new System.Windows.Point(Math.Sign(direction.X), 0)
            : new System.Windows.Point(0, Math.Sign(direction.Y));
        if (preferredDirection.X == 0 && preferredDirection.Y == 0)
            preferredDirection = new System.Windows.Point(1, 0);
        var stayedAtTop = _toyAtTopSince != DateTime.MinValue
            && now - _toyAtTopSince >= TimeSpan.FromSeconds(3);
        if (stayedAtTop) preferredDirection = new System.Windows.Point(0, 1);
        var cardinalDirection = ChooseSoloToyPushDirection(player, preferredDirection, forceDown: stayedAtTop);
        player.FaceToward(new System.Windows.Point(center.X + cardinalDirection.X, center.Y + cardinalDirection.Y));
        _desktopToy.Push(cardinalDirection, player.RecordId, soloPlay: true);
        // Do not treat the ball's initial overlap with the Pokémon as a second
        // kick. Wait until it has actually rolled clear, then pursue it.
        player.StartToyPursuit(_desktopToy.GetScreenCenter(), run: true, waitForBallSeparation: true);
        _toyPlayerSince.TryAdd(player.RecordId, now);
        _toyParticipantRecordId = player.RecordId;
        _lastToyInteractionAt = now;
    }

    private System.Windows.Point ChooseSoloToyPushDirection(DesktopCompanionWindow player,
        System.Windows.Point preferredDirection, bool forceDown = false)
    {
        var directions = new[]
        {
            new System.Windows.Point(0, -1), new System.Windows.Point(0, 1),
            new System.Windows.Point(-1, 0), new System.Windows.Point(1, 0)
        };
        var clearance = directions.ToDictionary(direction => direction,
            direction => _desktopToy?.GetAvailableRollDistance(direction) ?? 0d);
        var allowed = directions.Where(direction => clearance[direction] >= 240d).ToArray();
        if (allowed.Length == 0) allowed = directions;

        // Prefer kicks that move the ball away from the Pokémon. Kicking back
        // into its body can keep the overlap alive and look like a double hit.
        var toyCenter = _desktopToy?.GetScreenCenter() ?? new System.Windows.Point();
        var playerCenter = GetBodyCenter(player.VisibleBodyScreenBounds);
        var awayFromPlayer = allowed.Where(direction =>
            direction.X * (playerCenter.X - toyCenter.X)
            + direction.Y * (playerCenter.Y - toyCenter.Y) <= 0d).ToArray();
        if (awayFromPlayer.Length > 0) allowed = awayFromPlayer;

        if (_soloToyPushDirectionStreak >= 2 && _lastSoloToyPushDirection is { } previous)
            allowed = allowed.Where(direction => !direction.Equals(previous)).ToArray();
        if (allowed.Length == 0)
            allowed = directions.Where(direction => !_lastSoloToyPushDirection.HasValue
                || !direction.Equals(_lastSoloToyPushDirection.Value)).ToArray();

        // If the toy has lingered near the top of the monitor, return it into
        // the play area on the next solo kick. Keep the established rule that
        // three consecutive kicks cannot use the same direction.
        var down = new System.Windows.Point(0, 1);
        var downWouldRepeatTooOften = _soloToyPushDirectionStreak >= 2
            && _lastSoloToyPushDirection is { } previousPush && down.Equals(previousPush);
        if (forceDown && clearance[down] >= 120d && !downWouldRepeatTooOften)
            allowed = new[] { down };

        System.Windows.Point chosen;
        if (allowed.Any(direction => direction.Equals(preferredDirection))
            && clearance[preferredDirection] >= 240d)
        {
            chosen = preferredDirection;
        }
        else
        {
            var maximumClearance = allowed.Max(direction => clearance[direction]);
            var roomy = allowed.Where(direction => clearance[direction] >= maximumClearance - 160d).ToArray();
            chosen = roomy
                .OrderByDescending(direction =>
                {
                    var towardsPlayer = direction.X * (playerCenter.X - toyCenter.X)
                        + direction.Y * (playerCenter.Y - toyCenter.Y);
                    return clearance[direction] - Math.Max(0d, towardsPlayer) * .25d;
                })
                .ThenBy(_ => Random.Shared.Next())
                .First();
        }

        if (_lastSoloToyPushDirection is { } last && chosen.Equals(last))
            _soloToyPushDirectionStreak++;
        else
        {
            _lastSoloToyPushDirection = chosen;
            _soloToyPushDirectionStreak = 1;
        }
        return chosen;
    }

    private void ReleaseToyPlayers(IEnumerable<DesktopCompanionWindow> players)
    {
        foreach (var player in players.DistinctBy(item => item.RecordId).ToArray())
        {
            _toyPlayerSince.Remove(player.RecordId);
            _toyBoredUntil.Remove(player.RecordId);
            player.StopToyChase();
        }
        _toyParticipantRecordId = null;
        _toyPassReceiverRecordId = null;
        _toyPassKickAt = DateTime.MinValue;
        _preferredToyParticipantRecordId = null;
        _nextToyParticipantAt = DateTime.UtcNow.AddMilliseconds(700);
    }

    private double GetPokemonGroupSpacingPixels() => 16d * 4d * (_gardenWindow?.Scale ?? 3)
        * DesktopVisualScaleService.Current;

    private static bool RectanglesAreSeparated(System.Windows.Rect first, System.Windows.Rect second, double gap)
    {
        if (first.IsEmpty || second.IsEmpty) return false;
        var dx = Math.Max(0d, Math.Max(second.Left - first.Right, first.Left - second.Right));
        var dy = Math.Max(0d, Math.Max(second.Top - first.Bottom, first.Top - second.Bottom));
        return Math.Sqrt(dx * dx + dy * dy) >= gap;
    }

    private static System.Windows.Point GetToyReceptionPoint(System.Windows.Rect playerBounds,
        System.Windows.Point from, double padding)
    {
        if (playerBounds.IsEmpty) return from;
        var center = new System.Windows.Point(playerBounds.X + playerBounds.Width / 2d,
            playerBounds.Y + playerBounds.Height / 2d);
        var dx = from.X - center.X;
        var dy = from.Y - center.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < .001d) dx = 1d;
        var paddedHalfWidth = playerBounds.Width / 2d + padding;
        var paddedHalfHeight = playerBounds.Height / 2d + padding;
        var tx = Math.Abs(dx) < .001d ? double.PositiveInfinity : paddedHalfWidth / Math.Abs(dx);
        var ty = Math.Abs(dy) < .001d ? double.PositiveInfinity : paddedHalfHeight / Math.Abs(dy);
        var scale = Math.Min(tx, ty);
        return new System.Windows.Point(center.X + dx * scale, center.Y + dy * scale);
    }

    private void OnToyPassReceived(string pusherRecordId, string receiverRecordId)
    {
        if (!_desktopCompanions.TryGetValue(receiverRecordId, out var receiver)) return;
        var now = DateTime.UtcNow;
        _toyPlayerSince.TryAdd(receiverRecordId, now);
        if (_desktopCompanions.TryGetValue(pusherRecordId, out var pusher))
            receiver.FaceToward(GetBodyCenter(pusher.VisibleBodyScreenBounds));
        receiver.WaitForToyPass();
        _lastToyInteractionAt = now;
        _lastToyParticipantRecordId = pusherRecordId;
        _toyParticipantRecordId = null;
        _preferredToyParticipantRecordId = null;
        _toyPassReceiverRecordId = receiverRecordId;
        _toyPassKickAt = now.AddMilliseconds(750);
    }

    private void KickToyPass(string? requestedPusherId)
    {
        if (_desktopToy?.IsVisible != true) return;
        var now = DateTime.UtcNow;
        var players = _toyPlayerSince.Keys
            .Where(id => _desktopCompanions.ContainsKey(id)
                && (!_toyBoredUntil.TryGetValue(id, out var boredUntil) || now >= boredUntil))
            .Select(id => _desktopCompanions[id])
            .Where(item => item.IsVisible && !item.IsPinned)
            .ToList();
        var pusher = players.FirstOrDefault(item => item.RecordId == requestedPusherId)
            ?? players.FirstOrDefault();
        if (pusher is null) return;
        var center = _desktopToy.GetScreenCenter();

        if (players.Count == 1)
        {
            // When the group thins out, the remaining player switches to an
            // energetic solo kick-and-chase instead of abandoning the toy.
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
            var area = screen.WorkingArea;
            var dx = area.Left + area.Width / 2d - center.X;
            var dy = area.Top + area.Height / 2d - center.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var direction = length < 1d
                ? new System.Windows.Point(1d, 0d)
                : new System.Windows.Point(dx / length, dy / length);
            StartSoloToyChase(pusher, direction, now);
            return;
        }

        var spacing = GetPokemonGroupSpacingPixels();
        var invite = _desktopCompanions.Values
            .Where(item => !players.Any(player => player.RecordId == item.RecordId)
                && item.CanPlayWithToy
                && DistanceTo(item.VisibleBodyScreenBounds, center) <= ToyGroupInviteRangePixels
                && (!_toyBoredUntil.TryGetValue(item.RecordId, out var boredUntil) || now >= boredUntil)
                && players.All(player => RectanglesAreSeparated(
                    item.VisibleBodyScreenBounds, player.VisibleBodyScreenBounds, spacing)))
            .OrderBy(item => DistanceTo(item.VisibleBodyScreenBounds, center))
            .FirstOrDefault();
        if (invite is not null)
        {
            _toyPlayerSince.TryAdd(invite.RecordId, now);
            invite.JoinToyGame();
            players.Add(invite);
        }

        var recipient = players.Where(item => item.RecordId != pusher.RecordId)
            .OrderBy(item => DistanceTo(item.VisibleBodyScreenBounds, center))
            .FirstOrDefault();
        if (recipient is null)
        {
            ReleaseToyPlayers(players);
            return;
        }

        pusher.WaitForToyPass();
        recipient.JoinToyGame();
        var reception = GetToyReceptionPoint(recipient.VisibleBodyScreenBounds, center,
            _desktopToy.GetCollisionBounds().Width / 2d + 4d);
        pusher.FaceToward(GetBodyCenter(recipient.VisibleBodyScreenBounds));
        recipient.FaceToward(GetBodyCenter(pusher.VisibleBodyScreenBounds));
        if (!_desktopToy.PassTo(reception, pusher.RecordId, recipient.RecordId))
        {
            ReleaseToyPlayers(players);
            return;
        }
        _lastToyParticipantRecordId = pusher.RecordId;
        _lastToyInteractionAt = now;
    }

    private void OnToyDragStarted()
    {
        if (_teamRallyActive) CancelTeamRally();
        _toyAtTopSince = DateTime.MinValue;
        var activePlayers = _toyPlayerSince.Keys
            .Where(id => _desktopCompanions.TryGetValue(id, out var player)
                && player.IsVisible && !player.IsPinned && player.IsToyPlayParticipant
                && (!_toyBoredUntil.TryGetValue(id, out var boredUntil) || DateTime.UtcNow >= boredUntil))
            .ToArray();
        _toyManualThrowChaserRecordId = activePlayers.Length == 1 ? activePlayers[0] : null;
        _lastToyInteractionAt = DateTime.UtcNow;
        _toyParticipantRecordId = _toyManualThrowChaserRecordId;
        _toyPassKickAt = DateTime.MinValue;
        _toyPassReceiverRecordId = null;
        _preferredToyParticipantRecordId = null;
        foreach (var companion in _desktopCompanions.Values) companion.StopToyChase();
    }

    private void OnToyDragEnded()
    {
        var now = DateTime.UtcNow;
        _toyAtTopSince = DateTime.MinValue;
        _lastToyInteractionAt = now;
        var chaserId = _toyManualThrowChaserRecordId;
        _toyManualThrowChaserRecordId = null;
        if (chaserId is not null && _desktopCompanions.TryGetValue(chaserId, out var chaser)
            && chaser.IsVisible && !chaser.IsPinned
            && (!_toyBoredUntil.TryGetValue(chaserId, out var boredUntil) || now >= boredUntil))
        {
            _toyPlayerSince.TryAdd(chaserId, now);
            chaser.StartToyPursuit(_desktopToy?.GetScreenCenter() ?? chaser.VisibleBodyScreenBounds.Location, run: true);
            _toyParticipantRecordId = chaserId;
        }
        _nextToyParticipantAt = now.AddMilliseconds(500);
    }

    private void CancelToyChase(string recordId)
    {
        if (_toyManualThrowChaserRecordId == recordId) _toyManualThrowChaserRecordId = null;
        _toyPlayerSince.Remove(recordId);
        _toyBoredUntil.Remove(recordId);
        if (!_desktopCompanions.TryGetValue(recordId, out var companion)) return;
        companion.StopToyChase();
        if (_toyParticipantRecordId != recordId) return;
        _toyParticipantRecordId = null;
        _nextToyParticipantAt = DateTime.UtcNow.AddMilliseconds(700);
    }

    private bool FeedCompanionBerry(string recordId, string berryId, string? requestedBerryId)
    {
        if (_gameSave is null || _rewardBalance is null) return false;
        var matchesRequest = _berryRequestRecordId == recordId
            && _berryRequestItemId is not null
            && _berryRequestItemId.Equals(berryId, StringComparison.OrdinalIgnoreCase)
            && requestedBerryId is not null
            && requestedBerryId.Equals(berryId, StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow < _berryRequestExpiresAt;
        _desktopBattle?.CancelForRecord(recordId);
        CancelTeamRally();
        var getsGift = matchesRequest || Random.Shared.Next(100) < 5;
        var eventId = $"companion-feed:{Guid.NewGuid():N}";
        IReadOnlyList<RewardItemGrant> gift = matchesRequest
            ? [_rewardBalance.MakeBerryRequestGift(berryId, Random.Shared)]
            : getsGift ? [_rewardBalance.MakeCompanionGift(Random.Shared)] : Array.Empty<RewardItemGrant>();
        const string title = "¡REGALO DE TU POKÉMON!";
        if (!_gameSave.TryFeedCompanionBerry(berryId, eventId, title, gift)) return false;

        if (matchesRequest) ClearBerryRequest();

        _bagWindow?.Refresh();
        if (getsGift)
        {
            _rewardNotificationNotBefore = DateTime.UtcNow.AddMilliseconds(2600);
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(2600)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _rewardNotificationNotBefore = DateTime.MinValue;
                PresentPendingRewardNotifications();
            };
            timer.Start();
        }
        return true;
    }

    private void OnCompanionReturned(DesktopCompanionWindow window)
    {
        _desktopCouples?.CancelFor(window);
        _desktopBattle?.CancelFor(window);
        CancelTeamRally();
        CancelToyChase(window.RecordId);
        if (_preferredToyParticipantRecordId == window.RecordId) _preferredToyParticipantRecordId = null;
        if (_berryRequestRecordId == window.RecordId) ClearBerryRequest();
        if (_playRequestRecordId == window.RecordId) ClearPlayRequest();
        _desktopCompanions.Remove(window.RecordId);
        _pcWindow?.Refresh();
    }

    private bool LocateDesktopCompanion(string recordId)
    {
        if (!_desktopCompanions.TryGetValue(recordId, out var window)) return false;
        _locatedCompanionRecordId = recordId;
        _locatedCompanionUntil = DateTime.UtcNow.AddSeconds(5);
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Topmost = true;
        window.Activate();
        RestoreDesktopOverlayZOrder();
        return true;
    }

    private void SetDesktopCompanionsTopmost(bool topmost)
    {
        foreach (var companion in _desktopCompanions.Values)
            if (companion.IsVisible) companion.Topmost = topmost;
    }

    private bool ReturnDesktopCompanion(string recordId)
    {
        _desktopCouples?.CancelForRecord(recordId);
        _desktopBattle?.CancelForRecord(recordId);
        if (_gameSave is null || !_gameSave.RemoveDesktopCompanion(recordId)) return false;
        CancelTeamRally();
        CancelToyChase(recordId);
        if (_preferredToyParticipantRecordId == recordId) _preferredToyParticipantRecordId = null;
        if (_berryRequestRecordId == recordId) ClearBerryRequest();
        if (_playRequestRecordId == recordId) ClearPlayRequest();
        if (_desktopCompanions.TryGetValue(recordId, out var window)) window.Close();
        _pcWindow?.Refresh();
        return true;
    }

    private bool TryReturnDesktopCompanionToPcOnDrop(DesktopCompanionWindow companion, System.Windows.Point point)
    {
        if (_pcWindow?.ContainsScreenPoint((int)Math.Round(point.X), (int)Math.Round(point.Y)) != true) return false;
        return ReturnDesktopCompanion(companion.RecordId);
    }

    private void ReconcileDesktopCompanions()
    {
        if (_gameSave is null) return;
        var activeIds = _gameSave.GetDesktopCompanions().Select(companion => companion.RecordId).ToHashSet(StringComparer.Ordinal);
        foreach (var (recordId, window) in _desktopCompanions.ToArray())
        {
            if (activeIds.Contains(recordId)) continue;
            _desktopCouples?.CancelFor(window);
            _desktopBattle?.CancelFor(window);
            CancelTeamRally();
            CancelToyChase(recordId);
            if (_preferredToyParticipantRecordId == recordId) _preferredToyParticipantRecordId = null;
            if (_berryRequestRecordId == recordId) ClearBerryRequest();
            if (_playRequestRecordId == recordId) ClearPlayRequest();
            _desktopCompanions.Remove(recordId);
            window.Close();
        }
    }

    private void ShowOrActivate(Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        RestoreDesktopOverlayZOrder();
    }

    private void RestoreDesktopOverlayZOrder() => RestoreDesktopOverlayZOrder(null);

    private void RestoreDesktopOverlayZOrder(DesktopCompanionWindow? preferredCompanion)
    {
        if (_overlayTopmostTimer is { } overlayTimer)
            overlayTimer.Interval = TimeSpan.FromMilliseconds(_desktopBattle?.IsBattleActive == true ? 50 : 350);
        UpdateExternalSystemUiLayer();
        DesktopCompanionWindow? locatedCompanion = null;
        if (_locatedCompanionRecordId is { } locatedId && DateTime.UtcNow < _locatedCompanionUntil
            && _desktopCompanions.TryGetValue(locatedId, out var visibleLocated) && visibleLocated.IsVisible)
            locatedCompanion = visibleLocated;
        else
        {
            _locatedCompanionRecordId = null;
            _locatedCompanionUntil = DateTime.MinValue;
        }

        // Apply the full topmost stack at once. Moving each transparent Pokémon
        // window individually exposes it above the management panels for a frame.
        var orderedHandles = new List<IntPtr>();
        var queuedHandles = new HashSet<IntPtr>();
        void QueueHandle(IntPtr handle)
        {
            if (handle != IntPtr.Zero && queuedHandles.Add(handle)) orderedHandles.Add(handle);
        }
        void Queue(Window? window)
        {
            if (window?.IsVisible != true) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            QueueHandle(handle);
        }

        Queue(_gardenWindow);
        if (_externalSystemUiLayerActive) Queue(_capturePanelWindow);
        Queue(_desktopToy);
        foreach (var companion in _desktopCompanions.Values)
            if (!ReferenceEquals(companion, preferredCompanion) && !ReferenceEquals(companion, locatedCompanion)) Queue(companion);
        if (!ReferenceEquals(preferredCompanion, locatedCompanion)) Queue(preferredCompanion);
        // Attack and shield effects use their own layered window. Keep them
        // above the fighters when the active-battle stack is refreshed.
        QueueHandle(_desktopBattle?.GetEffectHandle() ?? IntPtr.Zero);
        Queue(_timedNotice);
        Queue(_rewardToast);
        Queue(_actionButtonsWindow);
        Queue(_gardenCornerButtonsWindow);
        foreach (var companion in _desktopCompanions.Values)
        {
            var contextMenu = companion.GetOpenContextMenuHandle();
            if (contextMenu != IntPtr.Zero) orderedHandles.Add(contextMenu);
        }
        // Keep the management screens above the desktop sprites.
        Queue(_settingsWindow);
        Queue(_bagWindow);
        Queue(_pcWindow);
        if (Current is { } application)
        {
            var managementRoots = new HashSet<Window>();
            if (_settingsWindow is not null) managementRoots.Add(_settingsWindow);
            if (_bagWindow is not null) managementRoots.Add(_bagWindow);
            if (_pcWindow is not null) managementRoots.Add(_pcWindow);

            int GetManagementOwnerDepth(Window window)
            {
                var owner = window.Owner;
                var visited = new HashSet<Window>();
                var depth = 1;
                while (owner is not null && visited.Add(owner))
                {
                    if (managementRoots.Contains(owner)) return depth;
                    owner = owner.Owner;
                    depth++;
                }
                return -1;
            }

            // Include every owned dialog from the management screens. Box
            // settings, background selection, rename prompts, and future
            // child windows must stay above the desktop Pokémon too.
            foreach (var window in application.Windows.Cast<Window>()
                         .Select(window => (Window: window, OwnerDepth: GetManagementOwnerDepth(window)))
                         .Where(item => item.OwnerDepth > 0)
                         .OrderBy(item => item.OwnerDepth))
                Queue(window.Window);

            // Keep confirmations owned by other game windows in the same
            // overlay stack as well, including the Pokémon release prompt.
            foreach (Window dialog in application.Windows)
                if (dialog is PixelConfirmWindow) Queue(dialog);
            Queue(_evolutionPrompt);
            if (_externalSystemUiLayerActive)
                foreach (Window window in application.Windows)
                    Queue(window);
        }
        // Dragged Pokémon and items must stay above every game window. This
        // includes previews created by the PC, capture panel, and bag.
        foreach (var previewHandle in DragPreviewWindow.GetActiveWindowHandles())
            QueueHandle(previewHandle);
        // Temporarily keep a located Pokémon above every game window so it is
        // easy to spot; the regular refresh restores the normal stack on expiry.
        Queue(locatedCompanion);

        if (orderedHandles.Count == 0) return;
        var insertAfter = _externalSystemUiLayerActive
            ? _externalSystemUiHandle != IntPtr.Zero ? _externalSystemUiHandle : HwndNotTopmost
            : HwndTopmost;
        var deferredPosition = BeginDeferWindowPos(orderedHandles.Count);
        if (deferredPosition != IntPtr.Zero)
        {
            foreach (var handle in orderedHandles)
            {
                deferredPosition = DeferWindowPos(deferredPosition, handle, insertAfter, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
                if (deferredPosition == IntPtr.Zero) break;
            }

            if (deferredPosition != IntPtr.Zero && EndDeferWindowPos(deferredPosition))
                return;
        }

        // Keep the previous behavior as a fallback if Windows cannot batch the stack.
        foreach (var handle in orderedHandles)
            _ = SetWindowPos(handle, insertAfter, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    internal static void PositionBattleArenaBehindOverlayStack(IntPtr arenaHandle)
    {
        if (arenaHandle == IntPtr.Zero || Current is not App app) return;

        // Keep the arena in the topmost band so ordinary windows from other
        // applications remain below it. Place it directly behind the garden
        // once, when the arena is created. Reapplying this z-order on every
        // overlay refresh can flash layered battle backgrounds over fighters.
        var anchor = app._gardenWindow?.IsVisible == true
            ? new WindowInteropHelper(app._gardenWindow).Handle
            : IntPtr.Zero;
        if (anchor == IntPtr.Zero || anchor == arenaHandle) return;

        _ = SetWindowPos(arenaHandle, anchor, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    private void UpdateExternalSystemUiLayer()
    {
        var popupHandle = FindExternalSystemPopup();
        var shouldYield = popupHandle != IntPtr.Zero;
        if (shouldYield == _externalSystemUiLayerActive && popupHandle == _externalSystemUiHandle) return;
        var layerChanged = shouldYield != _externalSystemUiLayerActive;
        _externalSystemUiLayerActive = shouldYield;
        _externalSystemUiHandle = popupHandle;
        if (!layerChanged) return;
        if (Current is not { } application) return;
        foreach (Window window in application.Windows)
            if (window.Topmost == shouldYield) window.Topmost = !shouldYield;
    }

    private static IntPtr FindExternalSystemPopup()
    {
        var foreground = GetForegroundWindow();
        var foregroundProcess = foreground == IntPtr.Zero ? null : GetWindowProcessName(foreground);
        var foregroundClass = foreground == IntPtr.Zero ? string.Empty : GetWindowClassName(foreground);
        if (foreground != IntPtr.Zero
            && (IsExternalPopupClass(foregroundClass)
                || string.Equals(foregroundProcess, "EarTrumpet", StringComparison.OrdinalIgnoreCase))) return foreground;

        var foundPopup = IntPtr.Zero;
        EnumWindows((handle, lParam) =>
        {
            if (!IsWindowVisible(handle)) return true;
            _ = GetWindowThreadProcessId(handle, out var processId);
            if (processId == Environment.ProcessId) return true;
            var name = GetWindowClassName(handle);
            if (IsExternalPopupClass(name))
            {
                foundPopup = handle;
                return false;
            }

            if (name == "Xaml_WindowedPopupClass"
                && IsExternalPopupProcess(GetWindowProcessName(handle)))
            {
                foundPopup = handle;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return foundPopup;
    }

    private static string GetWindowClassName(IntPtr handle)
    {
        var className = new StringBuilder(128);
        _ = GetClassName(handle, className, className.Capacity);
        return className.ToString();
    }

    private static bool IsExternalPopupClass(string className) => className is "#32768"
        or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland";

    private static bool IsExternalPopupProcess(string? processName) => processName is not null
        && (processName.Equals("EarTrumpet", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase));

    private static string? GetWindowProcessName(IntPtr handle)
    {
        _ = GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr BeginDeferWindowPos(int windowCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DeferWindowPos(IntPtr deferHandle, IntPtr windowHandle,
        IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDeferWindowPos(IntPtr deferHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y,
        int cx, int cy, uint flags);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    internal static IntPtr OverlayInsertAfter => Current is App app && app._externalSystemUiLayerActive
        ? app._externalSystemUiHandle != IntPtr.Zero ? app._externalSystemUiHandle : HwndNotTopmost
        : HwndTopmost;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destinationDc, int x, int y, int width, int height,
        IntPtr sourceDc, int sourceX, int sourceY, int rasterOperation);

#if DEBUG
    private async Task CaptureDittoTransformScreenshotsAsync(GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            if (save.GetPcSnapshot().Captures.Count != 0)
                throw new InvalidOperationException("Las capturas de Ditto requieren el guardado aislado y vacío de la prueba.");
            save.TryRecordCapture(metadata.Get("DITTO"), shiny: false, out var dittoDuplicate);
            save.TryRecordCapture(metadata.Get("PIKACHU"), shiny: false, out var pikachuDuplicate);
            if (dittoDuplicate || pikachuDuplicate) throw new InvalidOperationException("No se pudieron preparar Ditto y Pikachu para la prueba.");
            var ditto = save.GetPcSnapshot().Captures.Single(item => item.Id == "DITTO");
            var pikachu = save.GetPcSnapshot().Captures.Single(item => item.Id == "PIKACHU");
            if (!SendToDesktop(save, catalog, ditto) || !SendToDesktop(save, catalog, pikachu))
                throw new InvalidOperationException("No se pudieron mostrar los Pokémon de prueba en el escritorio.");
            foreach (var other in _desktopCompanions.Values.Where(item => item.RecordId != ditto.RecordId && item.RecordId != pikachu.RecordId)) other.Hide();
            var dittoWindow = _desktopCompanions[ditto.RecordId];
            var pikachuWindow = _desktopCompanions[pikachu.RecordId];
            var area = Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            dittoWindow.Left = area.Left + area.Width / 2d - 180;
            dittoWindow.Top = area.Top + area.Height / 2d - 110 - DesktopCompanionWindow.DittoTransformCanvasTopPadding;
            pikachuWindow.Left = area.Left + area.Width / 2d + 80;
            pikachuWindow.Top = area.Top + area.Height / 2d - 110;
            await Task.Delay(250);
            if (!dittoWindow.BeginDittoTransformPreviewForDebug(pikachuWindow))
                throw new InvalidOperationException("No se pudo activar la transformación de prueba de Ditto.");
            for (var frameIndex = 0; frameIndex < DittoTransformFrames.Columns * DittoTransformFrames.Rows; frameIndex++)
            {
                dittoWindow.SetDittoTransformPreviewFrameForDebug(frameIndex);
                await Task.Delay(40);
                SaveWindowScreenshot(dittoWindow, Path.Combine(directory, $"ditto_transform_frame_{frameIndex + 1:00}.png"));
            }
            File.WriteAllText(Path.Combine(directory, "preview.txt"),
                "27 capturas reales, una por cada fotograma de la animación Ditto. Guardado de prueba aislado.");
            await Task.Delay(600);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "preview-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
    }

    private async Task CaptureAlphaSizeComparisonAsync(GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var species = metadata.Get("PIKACHU");
            save.TryRecordCapture(species, shiny: false, out var normalDuplicate);
            if (normalDuplicate)
                throw new InvalidOperationException("No se pudo crear el Pikachu normal para la comparación.");
            save.TryRecordCapture(species, shiny: false, out var alphaDuplicate, alpha: true);
            if (alphaDuplicate)
                throw new InvalidOperationException("No se pudo crear el Pikachu Alfa para la comparación.");

            var captures = save.GetPcSnapshot().Captures;
            var normalCapture = captures.Single(item => item.Id == "PIKACHU" && !item.IsAlpha);
            var alphaCapture = captures.Single(item => item.Id == "PIKACHU" && item.IsAlpha);
            if (!SendToDesktop(save, catalog, normalCapture) || !SendToDesktop(save, catalog, alphaCapture))
                throw new InvalidOperationException("No se pudieron crear los dos acompañantes de comparación.");

            foreach (var companion in _desktopCompanions.Values) companion.Hide();
            var normalCompanion = _desktopCompanions[normalCapture.RecordId];
            var alphaCompanion = _desktopCompanions[alphaCapture.RecordId];
            normalCompanion.PrepareSizeComparisonForDebug();
            alphaCompanion.PrepareSizeComparisonForDebug();
            normalCompanion.SaveAlphaPreviewForDebug(Path.Combine(directory, "pokemon-normal.png"));
            alphaCompanion.SaveAlphaPreviewForDebug(Path.Combine(directory, "pokemon-alpha.png"));
            File.WriteAllText(Path.Combine(directory, "preview.txt"),
                "Comparación de dos Pikachu de la misma especie y dirección: tamaño normal frente a Alfa (1,25×). Guardado aislado de prueba.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "preview-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
        await Task.CompletedTask;
    }

    private async Task CaptureAlphaPreviewAsync(GardenWindow garden, GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _suppressRewardNotificationsForPreview = true;
            _rewardToast?.Close();
            _rewardToast = null;
            if (save.GetPcSnapshot().Captures.Count != 0)
                throw new InvalidOperationException("La vista previa Alfa requiere el guardado aislado y vacío creado junto a las capturas.");

            save.TryRecordCapture(metadata.Get("CHARIZARD"), shiny: true, out var duplicate, alpha: true);
            if (duplicate) throw new InvalidOperationException("No se pudo crear el Charizard Alfa shiny de vista previa.");
            var charizard = save.GetPcSnapshot().Captures.Single(item => item.Id == "CHARIZARD" && item.IsAlpha);
            if (!save.SetMegaForm(charizard.RecordId, "CHARIZARD_1"))
                throw new InvalidOperationException("No se pudo activar Mega Charizard X en la vista previa.");
            charizard = save.GetPcSnapshot().Captures.Single(item => item.RecordId == charizard.RecordId);

            ShowPc(save, catalog, metadata);
            if (_pcWindow is null) throw new InvalidOperationException("No se pudo abrir el PC para la vista previa Alfa.");
            _pcWindow.Left = 50; _pcWindow.Top = 40;
            _pcWindow.SelectDebug("CHARIZARD"); _pcWindow.Refresh();
            await Task.Delay(350);
            SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-alpha-shiny-mega.png"));

            if (!SendToDesktop(save, catalog, charizard))
                throw new InvalidOperationException("No se pudo enviar el Charizard Alfa a la vista previa del escritorio.");
            _pcWindow.SelectTeamViewForDebug(true); _pcWindow.Refresh();
            foreach (var companion in _desktopCompanions.Values) companion.Hide();
            await Task.Delay(250);
            SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-equipo-alpha.png"));
            _pcWindow.Hide();

            var desktopCharizard = _desktopCompanions[charizard.RecordId];
            desktopCharizard.Left = 330; desktopCharizard.Top = 180; desktopCharizard.Show();
            desktopCharizard.ShowAlphaPreviewForDebug(includeOtherEffects: false);
            await Task.Delay(275);
            desktopCharizard.SaveAlphaPreviewForDebug(Path.Combine(directory, "escritorio-alpha-heart.png"));
            desktopCharizard.ShowAlphaPreviewForDebug(includeOtherEffects: true);
            await Task.Delay(275);
            desktopCharizard.SaveAlphaPreviewForDebug(Path.Combine(directory, "escritorio-alpha-shiny-mega.png"));
            desktopCharizard.Hide();

            garden.ForceEncounter(shiny: true, pokemonId: "JOLTIK", playAudio: false, zoneIndex: 5, alpha: true);
            var effectWait = 0;
            while (garden.AlphaEffectFrameDebug < 0 && effectWait < 3500)
            {
                await Task.Delay(20);
                effectWait += 20;
            }
            if (garden.AlphaEffectFrameDebug < 0)
                throw new InvalidOperationException("No comenzó la animación Alfa del encuentro de vista previa.");
            garden.SaveSceneCapture(Path.Combine(directory, "encuentro-alpha-shiny.png"));
            await Task.Delay(200);
            if (_capturePanelWindow is null)
                throw new InvalidOperationException("No se abrió el panel del encuentro Alfa de vista previa.");
            _capturePanelWindow.SaveCaptureScreenshot(Path.Combine(directory, "panel-captura-alpha-shiny.png"));
            var captured = await _capturePanelWindow.ThrowBallAsync(CaptureBall.Poke,
                _capturePanelWindow.GetBallOriginScreen(CaptureBall.Poke), new FixedCaptureRandom(0));
            if (!captured) throw new InvalidOperationException("No se pudo completar la captura Alfa de vista previa.");
            await Task.Delay(120);
            _capturePanelWindow.SaveCaptureScreenshot(Path.Combine(directory, "resultado-alpha-shiny.png"));
            File.WriteAllText(Path.Combine(directory, "preview.txt"),
                "Vistas generadas con guardado temporal aislado: marca Alfa en PC/ficha/equipo, Alfa+Shiny+Mega en escritorio con corazón y efectos simultáneos, animación Alfa del encuentro shiny y paneles de captura. Ningún dato de la partida habitual fue usado.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "preview-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
    }

    private async Task CaptureMegaAuraScreenshotAsync(GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            save.TryRecordCapture(metadata.Get("CHARIZARD"), shiny: false, out var duplicate);
            if (duplicate) throw new InvalidOperationException("El guardado de vista previa ya contiene un Charizard normal.");
            var original = save.GetPcSnapshot().Captures.Single(item => item.Id == "CHARIZARD");
            if (!save.SetMegaForm(original.RecordId, "CHARIZARD_1"))
                throw new InvalidOperationException("No se pudo megaevolucionar el Charizard de vista previa.");
            var capture = save.GetPcSnapshot().Captures.Single(item => item.RecordId == original.RecordId);
            if (!SendToDesktop(save, catalog, capture))
                throw new InvalidOperationException("No se pudo mostrar el Pokémon Mega como acompañante.");

            foreach (var other in _desktopCompanions.Values.Where(item => item.RecordId != capture.RecordId)) other.Hide();
            var companion = _desktopCompanions[capture.RecordId];
            companion.Left = 320;
            companion.Top = 180;
            companion.ShowMegaAuraPreviewForDebug();
            await Task.Delay(275);
            companion.SaveMegaAuraPreviewForDebug(Path.Combine(directory, "mega-companion-aura.png"));
            File.WriteAllText(Path.Combine(directory, "preview.txt"),
                "Captura real de un acompañante Mega con su aura y la reacción de corazón simultáneas. Guardado de prueba aislado.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "preview-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
    }

    private async Task CaptureMegaEvolutionScreenshotAsync(GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            save.TryRecordCapture(metadata.Get("CHARIZARD"), shiny: false, out var duplicate);
            if (duplicate) throw new InvalidOperationException("El guardado temporal ya contiene un Charizard.");
            var capture = save.GetPcSnapshot().Captures.Single(item => item.Id == "CHARIZARD");
            if (!SendToDesktop(save, catalog, capture))
                throw new InvalidOperationException("No se pudo mostrar Charizard para la captura de megaevolución.");

            foreach (var other in _desktopCompanions.Values.Where(item => item.RecordId != capture.RecordId)) other.Hide();
            var companion = _desktopCompanions[capture.RecordId];
            var area = Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            companion.Left = area.Left + area.Width / 2d - companion.Width / 2d;
            companion.Top = area.Top + area.Height / 2d - companion.Height / 2d;
            if (!companion.BeginMegaEvolutionPreviewForDebug("CHARIZARD_1"))
                throw new InvalidOperationException("No se pudo iniciar la animación de megaevolución de Charizard.");

            for (var frameIndex = 0; frameIndex < MegaBubbleFrames.Count; frameIndex++)
            {
                companion.SetMegaEvolutionPreviewFrameForDebug(frameIndex);
                await Task.Delay(35);
                companion.SaveMegaEvolutionPreviewForDebug(Path.Combine(directory,
                    $"mega-evolution-frame_{frameIndex + 1:00}.png"));
            }
            File.WriteAllText(Path.Combine(directory, "preview.txt"),
                "27 capturas reales, una por cada fotograma de la animación de megaevolución de Charizard. Guardado temporal aislado.");
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "preview-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
    }

    private async Task CaptureEvolutionScreenshotsAsync(GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            save.TryRecordCapture(metadata.Get("CHARMELEON"), shiny: false, out var duplicate);
            if (duplicate) throw new InvalidOperationException("El guardado aislado ya contiene un Charmeleon.");
            var capture = save.GetPcSnapshot().Captures.Single(item => item.Id == "CHARMELEON");
            if (!SendToDesktop(save, catalog, capture))
                throw new InvalidOperationException("No se pudo mostrar Charmeleon en el escritorio.");

            foreach (var other in _desktopCompanions.Values.Where(item => item.RecordId != capture.RecordId)) other.Hide();
            var companion = _desktopCompanions[capture.RecordId];
            var area = Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            companion.Left = area.Left + area.Width / 2d - companion.Width / 2d;
            companion.Top = area.Top + area.Height / 2d - companion.Height / 2d;

            var prompt = new EvolutionPromptWindow(companion, "Charmeleon", "Charizard", _ => { });
            _evolutionPrompt = prompt;
            prompt.Show();
            await Task.Delay(250);
            SaveWindowScreenshot(prompt, Path.Combine(directory, "evolution_aviso.png"));
            prompt.Close();
            _evolutionPrompt = null;
            await Task.Delay(80);

            if (!companion.TryBeginEvolutionPrompt() || !companion.BeginPermanentEvolution("CHARIZARD"))
                throw new InvalidOperationException("No se pudo iniciar la animación permanente de evolución.");
            for (var frame = 0; frame < EvolutionAnimationFrames.Count; frame++)
            {
                companion.SetEvolutionPreviewFrameForDebug(frame);
                await Task.Delay(35);
                companion.SaveEvolutionPreviewForDebug(Path.Combine(directory,
                    $"evolution-frame_{frame + 1:00}.png"));
            }

            companion.CompleteEvolutionPreviewForDebug();
            await Task.Delay(180);
            if (!_desktopCompanions.TryGetValue(capture.RecordId, out var evolved)
                || !save.GetPcSnapshot().Captures.Any(item => item.RecordId == capture.RecordId && item.Id == "CHARIZARD"))
                throw new InvalidOperationException("La vista previa no completó y guardó la evolución de Charizard.");
            evolved.SaveEvolutionPreviewForDebug(Path.Combine(directory, "evolution_final.png"));
            File.WriteAllText(Path.Combine(directory, "preview.txt"),
                "Capturas reales de PokéGrass (ventanas WPF ejecutadas): aviso y los 27 fotogramas de la evolución Charmeleon → Charizard, más estado final guardado. Guardado aislado de prueba.");
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "preview-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
    }

    private async Task CaptureRegionalEvolutionComparisonAsync(GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            save.TryRecordCapture(metadata.Get("GROWLITHE_1"), shiny: false, out var growlitheDuplicate);
            save.TryRecordCapture(metadata.Get("ARCANINE_1"), shiny: false, out var arcanineDuplicate);
            if (growlitheDuplicate || arcanineDuplicate)
                throw new InvalidOperationException("El guardado aislado ya contiene una de las formas de Hisui.");

            var growlithe = save.GetPcSnapshot().Captures.Single(item => item.Id == "GROWLITHE_1");
            var arcanine = save.GetPcSnapshot().Captures.Single(item => item.Id == "ARCANINE_1");
            if (!SendToDesktop(save, catalog, growlithe) || !SendToDesktop(save, catalog, arcanine))
                throw new InvalidOperationException("No se pudieron mostrar Growlithe y Arcanine de Hisui en el escritorio.");

            foreach (var other in _desktopCompanions.Values.Where(item => item.RecordId != growlithe.RecordId
                         && item.RecordId != arcanine.RecordId)) other.Hide();
            var evolving = _desktopCompanions[growlithe.RecordId];
            var reference = _desktopCompanions[arcanine.RecordId];
            evolving.PrepareSizeComparisonForDebug();
            reference.PrepareSizeComparisonForDebug();
            var area = Forms.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            evolving.Left = area.Left + area.Width / 2d - evolving.Width - 48d;
            reference.Left = area.Left + area.Width / 2d + 48d;
            evolving.Top = reference.Top = area.Top + area.Height / 2d - Math.Max(evolving.Height, reference.Height) / 2d;

            if (!evolving.TryBeginEvolutionPrompt() || !evolving.BeginPermanentEvolution("ARCANINE_1"))
                throw new InvalidOperationException("No se pudo iniciar la evolución Growlithe de Hisui → Arcanine de Hisui.");

            evolving.SetEvolutionPreviewFrameForDebug(26);
            reference.SaveEvolutionPreviewForDebug(Path.Combine(directory, "arcanine-hisui.png"));
            evolving.SaveEvolutionPreviewForDebug(Path.Combine(directory, "growlithe-to-arcanine-hisui-frame27.png"));
            File.WriteAllText(Path.Combine(directory, "preview.txt"),
                "Comparación a escala real de Growlithe de Hisui durante el fotograma 27 de su evolución a Arcanine de Hisui junto a un Arcanine de Hisui sin efectos.");
            await Task.Delay(20);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "preview-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
    }

    private async Task CaptureCompanionSparkScreenshotAsync(GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            save.TryRecordCapture(metadata.Get("PIKACHU"), shiny: true, out var duplicate);
            if (duplicate) throw new InvalidOperationException("El guardado de vista previa ya contiene ese Pikachu shiny.");
            var capture = save.GetPcSnapshot().Captures.Single(item => item.Id == "PIKACHU" && item.IsShiny);
            if (!SendToDesktop(save, catalog, capture))
                throw new InvalidOperationException("No se pudo mostrar el Pikachu shiny como acompañante.");
            foreach (var other in _desktopCompanions.Values.Where(item => item.RecordId != capture.RecordId)) other.Hide();

            var companion = _desktopCompanions[capture.RecordId];
            companion.Left = 320;
            companion.Top = 180;
            companion.ShowSparklePreviewForDebug();
            await Task.Delay(100);
            SaveWindowScreenshot(companion, Path.Combine(directory, "shiny-companion-heart-sparkles.png"));
            File.WriteAllText(Path.Combine(directory, "preview.txt"),
                "Captura real del acompañante shiny con la reacción de corazón y los destellos simultáneos. Guardado de prueba aislado.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "preview-error.txt"), exception.ToString());
        }
        finally { Shutdown(); }
    }

    private async Task CapturePcScreenshotsAsync(GameSaveStore save, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var existingCaptureCount = save.GetPcSnapshot().Captures.Count;
            var interactiveBoxTest = string.Equals(Environment.GetEnvironmentVariable("POKEGRASS_INTERACTIVE_BOX_TEST"), "1", StringComparison.Ordinal);
            if (existingCaptureCount != 0 && !interactiveBoxTest)
                throw new InvalidOperationException("La ruta de pruebas del PC debe apuntar a un guardado aislado y vacío.");
            _actionButtonsWindow?.ClickActionDebug(2);
            if (_pcWindow is null) throw new InvalidOperationException("No se pudo abrir el PC.");
            _pcWindow.Left = 60; _pcWindow.Top = 60; _pcWindow.Activate();
            await Task.Delay(700);
            if (existingCaptureCount == 0)
            {
                SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-empty.png"));
                _pcWindow.SelectTeamViewForDebug(true); _pcWindow.Refresh(); await Task.Delay(300);
                SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-team-empty.png"));
                _pcWindow.SelectTeamViewForDebug(false); _pcWindow.Refresh(); await Task.Delay(200);
                foreach (var id in new[] { "PIKACHU", "JOLTIK", "WAILORD", "MEWTWO", "IRONJUGULIS", "EEVEE", "BULBASAUR", "CHARMANDER", "SQUIRTLE" })
                    save.TryRecordCapture(metadata.Get(id), id == "JOLTIK", out _);
                var teamScreens = new Dictionary<int, string> { [1] = "pc-team-one.png", [3] = "pc-team-three.png", [6] = "pc-team-six.png" };
                var captures = save.GetPcSnapshot().Captures;
                var teamIds = new[] { "PIKACHU", "JOLTIK", "WAILORD", "MEWTWO", "IRONJUGULIS", "EEVEE", "BULBASAUR" };
                for (var index = 0; index < teamIds.Length; index++)
                {
                    var capture = captures.First(item => item.Id.Equals(teamIds[index], StringComparison.OrdinalIgnoreCase));
                    var sent = SendToDesktop(save, catalog, capture);
                    if (index < 6 && !sent) throw new InvalidOperationException($"No se pudo añadir el acompañante {index + 1} a las seis plazas.");
                    if (index == 6 && sent) throw new InvalidOperationException("Se permitió enviar un séptimo acompañante.");
                    if (teamScreens.TryGetValue(index + 1, out var screenshotName))
                    {
                        _pcWindow.SelectTeamViewForDebug(true); _pcWindow.Refresh(); await Task.Delay(350);
                        foreach (var companionWindow in _desktopCompanions.Values) companionWindow.Hide();
                        SaveWindowScreenshot(_pcWindow, Path.Combine(directory, screenshotName));
                        foreach (var companionWindow in _desktopCompanions.Values) companionWindow.Show();
                        _pcWindow.SelectTeamViewForDebug(false); _pcWindow.Refresh(); await Task.Delay(160);
                    }
                }
                _pcWindow.SelectTeamViewForDebug(true); _pcWindow.Width = 980; _pcWindow.Height = 650; _pcWindow.Refresh(); await Task.Delay(350);
                foreach (var companionWindow in _desktopCompanions.Values) companionWindow.Hide();
                SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-team-small.png"));
                _pcWindow.Width = 1400; _pcWindow.Height = 900; _pcWindow.Refresh(); await Task.Delay(350);
                SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-team-large.png"));
                foreach (var companionWindow in _desktopCompanions.Values) companionWindow.Show();
                _pcWindow.Width = 1220; _pcWindow.Height = 820; _pcWindow.SelectTeamViewForDebug(false); _pcWindow.Refresh(); await Task.Delay(250);
                _pcWindow.SelectTeamPokemonForDebug("JOLTIK"); await Task.Delay(300);
                SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-team-selected-shiny.png"));
                _pcWindow.SelectTeamViewForDebug(false);

                save.TryRecordCapture(metadata.Get("CHARIZARD"), false, out _);
                var megaPreviewCapture = save.GetPcSnapshot().Captures.First(item => item.Id == "CHARIZARD");
                save.SetMegaForm(megaPreviewCapture.RecordId, "CHARIZARD_1");
                foreach (var companionWindow in _desktopCompanions.Values) companionWindow.Hide();
                _pcWindow.SelectDebug("CHARIZARD"); _pcWindow.Refresh(); await Task.Delay(350);
                SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-mega-charizard-x.png"));
                save.SetMegaForm(megaPreviewCapture.RecordId, "CHARIZARD_2"); _pcWindow.Refresh(); await Task.Delay(350);
                SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-mega-charizard-y.png"));
                foreach (var companionWindow in _desktopCompanions.Values) companionWindow.Show();
            }
            _pcWindow.SelectDebug(null);
            _pcWindow.Refresh(); await Task.Delay(300);
            SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-populated.png"));
            _pcWindow.SelectDebug("PIKACHU");
            _pcWindow.Refresh(); await Task.Delay(350);
            SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-selected.png"));
            var releaseConfirmation = PixelConfirmWindow.CreatePreview(_pcWindow);
            releaseConfirmation.Show(); await Task.Delay(250);
            SaveWindowScreenshot(releaseConfirmation, Path.Combine(directory, "pc-release-confirmation.png"));
            releaseConfirmation.Close(); await Task.Delay(150);
            _pcWindow.SelectDebug("JOLTIK");
            _pcWindow.Refresh(); await Task.Delay(250);
            SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-selected-joltik.png"));
            _pcWindow.SelectDebug("WAILORD");
            _pcWindow.Refresh(); await Task.Delay(250);
            SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-selected-wailord.png"));
            _pcWindow.SelectDebug("IRONJUGULIS");
            _pcWindow.Refresh(); await Task.Delay(250);
            SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-selected-long-name.png"));
            _pcWindow.SelectDebug("PIKACHU");
            var options = _pcWindow.ShowBoxOptionsForDebug(); await Task.Delay(350);
            SaveWindowScreenshot(options, Path.Combine(directory, "pc-box-options.png"));
            options.Width = 460; options.Height = 320; await Task.Delay(300);
            SaveWindowScreenshot(options, Path.Combine(directory, "pc-box-options-small.png"));
            options.Width = 650; options.Height = 430; await Task.Delay(300);
            SaveWindowScreenshot(options, Path.Combine(directory, "pc-box-options-large.png"));
            options.Close(); await Task.Delay(200);
            var gallery = _pcWindow.ShowBackgroundGalleryForDebug(); await Task.Delay(500);
            SaveWindowScreenshot(gallery, Path.Combine(directory, "pc-background-gallery.png")); gallery.Close(); await Task.Delay(200);
            _pcWindow.Width = 980; _pcWindow.Height = 650; _pcWindow.Left = 100; _pcWindow.Top = 60;
            await Task.Delay(300); SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-minimum-size.png"));
            _pcWindow.Width = 1400; _pcWindow.Height = 900; _pcWindow.Left = 100; _pcWindow.Top = 60;
            await Task.Delay(300); SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-resized.png"));
            _pcWindow.Width = 980; _pcWindow.Height = 650;
            await Task.Delay(300); SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-resized-back-to-minimum.png"));
            _pcWindow.Close(); await Task.Delay(250);
            var settingsPath = Environment.GetEnvironmentVariable("POKEGRASS_PC_WINDOW_SETTINGS_PATH");
            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath)) throw new InvalidOperationException("No se guardaron los ajustes de la ventana PC.");
            using var settingsDocument = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var storedBounds = settingsDocument.RootElement;
            var expectedBounds = (Left: storedBounds.GetProperty("Left").GetDouble(), Top: storedBounds.GetProperty("Top").GetDouble(),
                Width: storedBounds.GetProperty("Width").GetDouble(), Height: storedBounds.GetProperty("Height").GetDouble());
            ShowPc(save, catalog, metadata);
            if (_pcWindow is null) throw new InvalidOperationException("No se pudo reabrir el PC para validar sus dimensiones guardadas.");
            await Task.Delay(500);
            var workArea = SystemParameters.WorkArea;
            var restoredPositionIsVisible = _pcWindow.Left >= workArea.Left - 2 && _pcWindow.Top >= workArea.Top - 2
                && _pcWindow.Left + _pcWindow.Width <= workArea.Right + 2 && _pcWindow.Top + _pcWindow.Height <= workArea.Bottom + 2;
            if (!restoredPositionIsVisible || Math.Abs(_pcWindow.Width - expectedBounds.Width) > 2 || Math.Abs(_pcWindow.Height - expectedBounds.Height) > 2)
                throw new InvalidOperationException($"La ventana PC no restauró bounds: esperado {expectedBounds}; actual ({_pcWindow.Left}, {_pcWindow.Top}, {_pcWindow.Width}, {_pcWindow.Height}).");
            SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-restored.png"));
            _pcWindow.SelectDebug("PIKACHU");
            if (!_pcWindow.CanReleaseDebug || !_pcWindow.ClickReleaseAndConfirmForDebug())
                throw new InvalidOperationException("El botón Liberar no ejecutó la liberación confirmada en la prueba aislada.");
            SaveWindowScreenshot(_pcWindow, Path.Combine(directory, "pc-after-release.png"));
            var firstCompanion = _desktopCompanions.Values.FirstOrDefault();
            if (firstCompanion is not null)
            {
                firstCompanion.SuppressPersistenceOnClose();
                save.RemoveDesktopCompanion(firstCompanion.RecordId);
                _desktopCompanions.Remove(firstCompanion.RecordId);
                firstCompanion.Close();
                _pcWindow?.Refresh();
                await Task.Delay(600); // deja que WPF procese el cierre y libere el cupo antes de la captura de paseo.
            }
            var megaWalker = save.GetPcSnapshot().Captures.FirstOrDefault(item => item.Id == "CHARIZARD");
            var megaReady = megaWalker is not null
                && (megaWalker.MegaFormId == "CHARIZARD_2" || save.SetMegaForm(megaWalker.RecordId, "CHARIZARD_2"));
            var megaSent = megaReady && SendToDesktop(save, catalog, megaWalker!);
            if (megaSent && megaWalker is not null)
            {
                foreach (var companion in _desktopCompanions.Values)
                    if (companion.RecordId != megaWalker.RecordId) companion.Hide();
                _pcWindow?.Hide();
                var megaWalkerWindow = _desktopCompanions[megaWalker.RecordId];
                megaWalkerWindow.Left = 300; megaWalkerWindow.Top = 180;
                await Task.Delay(700);
                SaveWindowScreenshot(megaWalkerWindow, Path.Combine(directory, "mega-charizard-walking.png"));
            }
            else File.WriteAllText(Path.Combine(directory, "mega-walking-preview-error.txt"),
                $"No se pudo colocar a Mega Charizard en pantalla. capture={megaWalker is not null}; formReady={megaReady}; desktopWindows={_desktopCompanions.Count}; savedCompanions={save.GetDesktopCompanions().Count}; freeTeamSlots={save.GetDesktopTeamSlots().Count(id => id is null)}");
            File.WriteAllText(Path.Combine(directory, "pc-preview.txt"), "PC: vacío, poblado, selección, Mega Charizard X/Y con insignia, opciones de caja, galería, tamaños normal/pequeño/grande y restauración. Equipo: vacío, 1/6, 3/6, 6/6, seleccionado shiny y redimensionado; se admitieron seis acompañantes y se rechazó el séptimo. Se verificaron Liberar, el guardado aislado y el sprite animado de Mega Charizard al pasear; no se modificó la partida del usuario.");
            if (interactiveBoxTest)
            {
                var interactivePc = _pcWindow ?? throw new InvalidOperationException("No se pudo reabrir el PC para la vista interactiva.");
                interactivePc.SelectDebug(null);
                var interactiveOptions = interactivePc.ShowBoxOptionsForDebug();
                await Task.Delay(400);
                SaveWindowScreenshot(interactiveOptions, Path.Combine(directory, "pc-box-options-interactive.png"));
                await Task.Delay(Timeout.Infinite);
            }
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(directory, "pc-preview-error.txt"), exception.ToString()); }
        finally { Shutdown(); }
    }

    private async Task CaptureUiScreenshotsAsync(GardenWindow garden, GameSaveStore save,
        PokemonCatalog catalog, PokemonSpeciesMetadataCatalog metadata, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            SaveWindowScreenshot(garden, Path.Combine(directory, "garden-context.png"));
            foreach (var id in new[] { "ultra-ball", "master-ball", "silver-razz-berry", "golden-razz-berry", "silver-nanab-berry", "golden-nanab-berry", "repel", "super-repel", "max-repel", "incense-full", "incense-lax", "incense-luck", "incense-odd", "incense-pure", "incense-rock", "incense-rose", "incense-sea", "incense-wave" })
                save.SetItemCountForDebug(id, 2);
            // Exercise the persistent zero-quantity catalog entries in the real bag view.
            foreach (var id in new[] { "ultra-ball", "silver-razz-berry", "max-repel" }) save.SetItemCountForDebug(id, 0);
            _actionButtonsWindow?.ClickActionDebug(1); await Task.Delay(600);
            if (_bagWindow is null) throw new InvalidOperationException("No se pudo abrir la Bolsa.");
            foreach (var (tab, file) in new[] { (0, "bag-balls.png"), (1, "bag-berries.png"), (2, "bag-repels.png"), (3, "bag-incense.png") })
            {
                _bagWindow!.SelectTabDebug(tab);
                if (tab == 2)
                {
                    if (!_timedEffects!.ActivateRepel("repel") || !_timedEffects.ActivateRepel("super-repel") || _timedEffects.RepelSeconds < 2699)
                        throw new InvalidOperationException("Falló la acumulación de duraciones de repelentes.");
                    var originalGardenScale = garden.Scale;
                    var bagWasVisible = _bagWindow?.IsVisible == true;
                    var actionButtonsWereVisible = _actionButtonsWindow?.IsVisible == true;
                    _bagWindow?.Hide(); _actionButtonsWindow?.Hide();
                    foreach (var scale in GardenSettings.AllowedScales)
                    {
                        garden.SetScale(scale); await Task.Delay(180);
                        garden.SaveSceneCapture(Path.Combine(directory, $"timed-effect-indicator-repel-{scale}x.png"));
                    }
                    garden.SetScale(originalGardenScale); await Task.Delay(180);
                    if (actionButtonsWereVisible) _actionButtonsWindow?.Show();
                    if (bagWasVisible) _bagWindow?.Show();
                }
                if (tab == 3)
                {
                    if (!_timedEffects!.ActivateIncense("incense-pure") || _timedEffects.ShinyDenominator != 256
                        || !_timedEffects.ActivateIncense("incense-pure") || _timedEffects.IncenseSeconds < 1199)
                        throw new InvalidOperationException("Falló el shiny mejorado o la extensión del mismo incienso.");
                    if (!_timedEffects.ActivateIncense("incense-wave") || _timedEffects.IncenseId != "incense-wave"
                        || !_timedEffects.ActivateIncense("incense-lax") || _timedEffects.AlertSeconds != 30)
                        throw new InvalidOperationException("Falló el reemplazo de incienso o la alerta prolongada de Lax.");
                    var persisted = GameSaveStore.LoadOrCreate(Path.Combine(directory, "ui-preview-save.json")).GetTimedItems();
                    if (persisted.IncenseId != "incense-lax" || persisted.IncenseSeconds < 1799)
                        throw new InvalidOperationException("No persistió el estado y tiempo restante del incienso.");
                }
                if (tab is 2 or 3) _bagWindow!.SelectFirstAvailableDebug();
                await Task.Delay(180);
                SaveWindowScreenshot(_bagWindow!, Path.Combine(directory, file));
                if (tab is 0 or 1)
                {
                    _bagWindow!.SelectFirstAvailableDebug(); await Task.Delay(150);
                    SaveWindowScreenshot(_bagWindow!, Path.Combine(directory, tab == 0 ? "bag-ball-selected.png" : "bag-berry-selected.png"));
                }
            }
            save.SetItemCountForDebug("incense-wave", 0);
            _bagWindow!.SelectTabDebug(3); await Task.Delay(160);
            SaveWindowScreenshot(_bagWindow!, Path.Combine(directory, "bag-zero-quantity.png"));
            _bagWindow.SelectItemDebug("incense-wave"); await Task.Delay(120);
            if (_bagWindow.CanUseSelectedDebug || !_bagWindow.SelectedDescriptionDebug.Contains("Favorece tipo Agua", StringComparison.Ordinal))
                throw new InvalidOperationException("Un objeto agotado se puede usar o no muestra su descripción.");
            SaveWindowScreenshot(_bagWindow, Path.Combine(directory, "bag-zero-quantity-selected.png"));
            _bagWindow.Width = 760; _bagWindow.Height = 500; _bagWindow.Left = 80; _bagWindow.Top = 80;
            await Task.Delay(250); SaveWindowScreenshot(_bagWindow, Path.Combine(directory, "bag-minimum-size.png"));
            _bagWindow.Width = 960; _bagWindow.Height = 630; _bagWindow.Left = 80; _bagWindow.Top = 60;
            await Task.Delay(250); SaveWindowScreenshot(_bagWindow, Path.Combine(directory, "bag-intermediate-size.png"));
            _bagWindow.Width = 1240; _bagWindow.Height = 800; _bagWindow.Left = 80; _bagWindow.Top = 60;
            await Task.Delay(250); SaveWindowScreenshot(_bagWindow, Path.Combine(directory, "bag-resized.png"));
            _bagWindow.Width = 760; _bagWindow.Height = 500;
            await Task.Delay(250); SaveWindowScreenshot(_bagWindow, Path.Combine(directory, "bag-resized-back-to-minimum.png"));
            _timedEffects!.CancelIncense();
            _bagWindow.Close(); _bagWindow = null; await Task.Delay(150);
            var bagBoundsPath = Environment.GetEnvironmentVariable("POKEGRASS_BAG_WINDOW_SETTINGS_PATH");
            if (bagBoundsPath is null || !File.Exists(bagBoundsPath)) throw new InvalidOperationException("No se guardaron los límites de la Bolsa.");
            var bagBounds = JsonDocument.Parse(File.ReadAllText(bagBoundsPath)).RootElement;
            var expectedBagSize = (Width: bagBounds.GetProperty("Width").GetDouble(), Height: bagBounds.GetProperty("Height").GetDouble());
            _actionButtonsWindow?.ClickActionDebug(1); await Task.Delay(350);
            if (_bagWindow is null || Math.Abs(_bagWindow.Width - expectedBagSize.Width) > 2 || Math.Abs(_bagWindow.Height - expectedBagSize.Height) > 2)
                throw new InvalidOperationException("La Bolsa no restauró el tamaño elegido al reabrirse.");
            SaveWindowScreenshot(_bagWindow, Path.Combine(directory, "bag-restored.png"));
            _bagWindow.Close(); _bagWindow = null; await Task.Delay(150);

            _actionButtonsWindow?.ClickActionDebug(0); await Task.Delay(400);
            if (_settingsWindow is null) throw new InvalidOperationException("No se pudo abrir Configuración.");
            foreach (var (tab, file) in new[] { (0, "settings-general.png"), (1, "settings-garden.png"), (2, "settings-sound.png") })
            {
                _settingsWindow.SelectTabDebug(tab); await Task.Delay(180);
                SaveWindowScreenshot(_settingsWindow, Path.Combine(directory, file));
            }
            _settingsWindow.Width = 520; _settingsWindow.Height = 370; await Task.Delay(200);
            SaveWindowScreenshot(_settingsWindow, Path.Combine(directory, "settings-minimum-size.png"));
            _settingsWindow.Width = 680; _settingsWindow.Height = 480; await Task.Delay(200);
            SaveWindowScreenshot(_settingsWindow, Path.Combine(directory, "settings-intermediate-size.png"));
            _settingsWindow.Width = 850; _settingsWindow.Height = 600; await Task.Delay(200);
            SaveWindowScreenshot(_settingsWindow, Path.Combine(directory, "settings-resized.png"));
            _settingsWindow.Width = 520; _settingsWindow.Height = 370; await Task.Delay(200);
            SaveWindowScreenshot(_settingsWindow, Path.Combine(directory, "settings-resized-back-to-minimum.png"));
            GameSoundSettings.Update(enabled: false, volume: 0);
            if (GameSoundSettings.Enabled || GameSoundSettings.Volume != 0) throw new InvalidOperationException("No se guardó el estado de silencio/volumen en las pruebas.");
            GameSoundSettings.Update(enabled: true, volume: 70);
            var audioPath = Environment.GetEnvironmentVariable("POKEGRASS_AUDIO_SETTINGS_PATH");
            var savedAudio = audioPath is null ? null : JsonSerializer.Deserialize<SoundSettings>(File.ReadAllText(audioPath));
            if (savedAudio is not { Enabled: true, Volume: 70 }) throw new InvalidOperationException("No se guardó correctamente el archivo aislado de ajustes de sonido.");
            _settingsWindow.Close(); _settingsWindow = null; await Task.Delay(150);
            var settingsBoundsPath = Environment.GetEnvironmentVariable("POKEGRASS_SETTINGS_WINDOW_PATH");
            if (settingsBoundsPath is null || !File.Exists(settingsBoundsPath)) throw new InvalidOperationException("No se guardaron los límites de Configuración.");
            using (var settingsBoundsDoc = JsonDocument.Parse(File.ReadAllText(settingsBoundsPath)))
            {
                var stored = settingsBoundsDoc.RootElement;
                var expected = (Width: stored.GetProperty("Width").GetDouble(), Height: stored.GetProperty("Height").GetDouble());
                _actionButtonsWindow?.ClickActionDebug(0); await Task.Delay(350);
                if (_settingsWindow is null || Math.Abs(_settingsWindow.Width - expected.Width) > 2 || Math.Abs(_settingsWindow.Height - expected.Height) > 2)
                    throw new InvalidOperationException("Configuración no restauró el tamaño elegido al reabrirse.");
                SaveWindowScreenshot(_settingsWindow, Path.Combine(directory, "settings-restored.png"));
                _settingsWindow.Close(); _settingsWindow = null; await Task.Delay(150);
            }

            ShowCapturePanel(garden, save, metadata, catalog.ById["PIKACHU"], shiny: false);
            await Task.Delay(500);
            if (_capturePanelWindow is null) throw new InvalidOperationException("No se pudo abrir el panel de captura.");
            _capturePanelWindow.SelectTabDebug(0); await Task.Delay(180);
            SaveWindowScreenshot(_capturePanelWindow, Path.Combine(directory, "capture-balls.png"));
            _capturePanelWindow.SelectTabDebug(1); await Task.Delay(180);
            SaveWindowScreenshot(_capturePanelWindow, Path.Combine(directory, "capture-berries.png"));
            _capturePanelWindow.Close(); _capturePanelWindow = null;

            ShowCapturePanel(garden, save, metadata, catalog.ById["PIKACHU"], shiny: false);
            await Task.Delay(450);
            if (_capturePanelWindow is null) throw new InvalidOperationException("No se pudo abrir la vista previa del resultado.");
            _capturePanelWindow.ShowCaptureResultDebug();
            await Task.Delay(80);
            SaveWindowScreenshot(_capturePanelWindow, Path.Combine(directory, "capture-result-normal.png"));
            _capturePanelWindow.Close(); _capturePanelWindow = null;

            ShowCapturePanel(garden, save, metadata, catalog.ById["PIKACHU"], shiny: false);
            await Task.Delay(450);
            _capturePanelWindow!.ShowCaptureResultDebug(new RewardItemGrant("incense-rock", 2));
            await Task.Delay(80);
            SaveWindowScreenshot(_capturePanelWindow, Path.Combine(directory, "capture-result-one-reward.png"));
            _capturePanelWindow.Close(); _capturePanelWindow = null;

            ShowCapturePanel(garden, save, metadata, catalog.ById["IRONJUGULIS"], shiny: true);
            await Task.Delay(450);
            if (_capturePanelWindow is null) throw new InvalidOperationException("No se pudo abrir la vista previa del resultado shiny.");
            _capturePanelWindow.ShowCaptureResultDebug(new RewardItemGrant("poke-ball", 3),
                new RewardItemGrant("razz-berry", 1), new RewardItemGrant("incense-rock", 2));
            await Task.Delay(80);
            SaveWindowScreenshot(_capturePanelWindow, Path.Combine(directory, "capture-result-three-rewards.png"));
            foreach (var scale in GardenSettings.AllowedScales)
            {
                garden.SetScale(scale); await Task.Delay(180);
                _capturePanelWindow.SaveCaptureScreenshot(Path.Combine(directory, $"capture-result-{scale}x.png"));
            }
            _capturePanelWindow.Close(); _capturePanelWindow = null;
            await CaptureRewardNoticePreviewAsync(garden, directory, new RewardNotificationSave
            {
                EventId = "preview:supply", Title = "Suministros periódicos", Items =
                [new("poke-ball", 5), new("razz-berry", 3), new("incense-rock", 1), new("repel", 1)]
            }, "reward-notice-supplies.png");
            await CaptureRewardNoticePreviewAsync(garden, directory, new RewardNotificationSave
            {
                EventId = "preview:daily", Title = "Regalo diario", Items =
                [new("poke-ball", 10), new("razz-berry", 4), new("nanab-berry", 3)]
            }, "reward-notice-daily.png");
            File.WriteAllText(Path.Combine(directory, "ui-preview.txt"), "Capturas de resultado azul sin recompensas, con un objeto y con tres objetos; cuatro escalas; y avisos amarillos de suministros y regalo diario. Vistas previas generadas sin consumir ni conceder objetos. Las demás capturas de Bolsa, Configuración y panel de captura proceden del guardado de UI aislado.");
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(directory, "ui-preview-error.txt"), exception.ToString()); }
        finally { Shutdown(); }
    }


    private static void SaveWindowScreenshot(Window window, string path)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var rect)) throw new InvalidOperationException("No se pudo medir la ventana del PC.");
        var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
        bitmap.Save(path, ImageFormat.Png);
    }

    private async Task CaptureRewardNoticePreviewAsync(GardenWindow garden, string directory,
        RewardNotificationSave notification, string filename)
    {
        var gardenBounds = garden.GetPhysicalBounds();
        var area = Forms.Screen.FromRectangle(gardenBounds).WorkingArea;
        var dpi = garden.GetDpiScale();
        var actionBounds = _actionButtonsWindow?.GetPhysicalBounds() ?? Rectangle.Empty;
        var preview = new RewardToastWindow(notification, _rewardBalance!, garden.Scale / 3d,
            gardenBounds, actionBounds, area, dpi.X, dpi.Y);
        preview.Show(); await Task.Delay(200);
        SaveWindowScreenshot(preview, Path.Combine(directory, filename));
        preview.Close(); await Task.Delay(80);
    }
#endif

    protected override void OnExit(ExitEventArgs e)
    {
        DragPreviewWindow.ActivePreviewsChanged -= OnDragPreviewWindowsChanged;
        _desktopCouples?.Dispose();
        _desktopCouples = null;
        _desktopBattle?.Dispose();
        _desktopBattle = null;
        _companionActivityTimer?.Stop();
        _toyActivityTimer?.Stop();
        _overlayTopmostTimer?.Stop();
        _desktopToy?.Close();
        _desktopToy = null;
        foreach (var window in _desktopCompanions.Values.ToArray()) window.Close();
        _desktopCompanions.Clear();
        _gardenWindow?.SaveSettings();
        if (!_restartingAfterDataImport)
        {
            _rewardService?.Dispose();
            _timedEffects?.Dispose();
        }

        if (_trayIcon is not null)
        {
            var icon = _trayIcon.Icon;
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            icon?.Dispose();
        }

        if (_debugBattlePreviewSaveDirectory is { } previewDirectory && Directory.Exists(previewDirectory))
        {
            try { Directory.Delete(previewDirectory, recursive: true); }
            catch (IOException exception) { EncounterDiagnostics.WriteException("battle-preview-save-cleanup", exception); }
            catch (UnauthorizedAccessException exception) { EncounterDiagnostics.WriteException("battle-preview-save-cleanup", exception); }
        }

        base.OnExit(e);
    }

    private void OnDragPreviewWindowsChanged() => RestoreDesktopOverlayZOrder();

    private void PresentPendingRewardNotifications()
    {
#if DEBUG
        if (_suppressRewardNotificationsForPreview) return;
#endif
        if (UiTestMode.Enabled || _rewardService is null || _rewardBalance is null || _gardenWindow is null
            || !_gardenWindow.IsVisible || _gardenWindow.HasActiveEncounter || _rewardToast is not null) return;
        if (DateTime.UtcNow < _rewardNotificationNotBefore) return;
        if (!_rewardService.TryGetPending(out var notification) || notification is null) return;
        var gardenBounds = _gardenWindow.GetPhysicalBounds();
        var area = Forms.Screen.FromRectangle(gardenBounds).WorkingArea;
        var dpi = _gardenWindow.GetDpiScale();
        var actionBounds = _actionButtonsWindow?.GetPhysicalBounds() ?? Rectangle.Empty;
        _rewardToast = new RewardToastWindow(notification, _rewardBalance, _gardenWindow.Scale / 3d,
            gardenBounds, actionBounds, area, dpi.X, dpi.Y);
        _rewardToast.Closed += (_, _) =>
        {
            if (!_restartingAfterDataImport) _rewardService.CompletePending(notification.EventId);
            _rewardToast = null;
            if (!_restartingAfterDataImport)
                Dispatcher.BeginInvoke(PresentPendingRewardNotifications, System.Windows.Threading.DispatcherPriority.Background);
        };
        _rewardToast.Show();
    }

    private static Icon CreateTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "items", "balls", "master.png");
        using var source = new Bitmap(path);
        using var iconBitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(iconBitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            var bounds = GetOpaqueBounds(source);
            const int artSize = 28;
            var destination = new Rectangle((32 - artSize) / 2, (32 - artSize) / 2, artSize, artSize);
            graphics.DrawImage(source, destination, bounds, GraphicsUnit.Pixel);
        }

        var handle = iconBitmap.GetHicon();
        try
        {
            using var borrowedIcon = Icon.FromHandle(handle);
            return (Icon)borrowedIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Rectangle GetOpaqueBounds(Bitmap bitmap)
    {
        var left = bitmap.Width; var top = bitmap.Height; var right = -1; var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).A == 0) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        return right < left || bottom < top
            ? new Rectangle(0, 0, bitmap.Width, bitmap.Height)
            : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
}
