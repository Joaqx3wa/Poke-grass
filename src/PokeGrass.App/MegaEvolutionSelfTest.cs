using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace PokeGrass;

internal static class MegaEvolutionSelfTest
{
    internal static string Summary { get; private set; } = string.Empty;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "assets");
        var pokemon = PokemonCatalog.Load(Path.Combine(assets, "data", "pokemon-catalog.json"));
        var metadata = PokemonSpeciesMetadataCatalog.Load(Path.Combine(assets, "data", "pokemon-species-metadata.json"), pokemon);
        var mega = MegaEvolutionCatalog.Load(assets, pokemon);
        var definitions = JsonSerializer.Deserialize<List<MegaEvolutionDefinition>>(
            File.ReadAllText(Path.Combine(assets, "data", "mega-evolutions.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var root = Path.Combine(Path.GetTempPath(), "PokeGrass-mega-menu-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var application = Application.Current;
        var shutdownMode = application.ShutdownMode;
        var audioPath = Environment.GetEnvironmentVariable("POKEGRASS_AUDIO_SETTINGS_PATH");
        var enabled = GameSoundSettings.Enabled;
        var volume = GameSoundSettings.Volume;
        var cases = 0;
        var specimenCount = 0;
        try
        {
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Environment.SetEnvironmentVariable("POKEGRASS_AUDIO_SETTINGS_PATH", Path.Combine(root, "audio.json"));
            GameSoundSettings.Update(enabled: false);
            foreach (var group in definitions.GroupBy(form => form.BasePokemonId))
            {
                var ids = new List<string> { group.Key };
                if (pokemon.FemaleEncounterVariants.TryGetValue(group.Key, out var female)) ids.Add(female.Id);
                foreach (var id in ids)
                foreach (var shiny in new[] { false, true })
                foreach (var alpha in new[] { false, true })
                {
                    var path = Path.Combine(root, $"specimen-{++specimenCount}.json");
                    var save = GameSaveStore.LoadOrCreate(path);
                    save.TryRecordCapture(metadata.Get(id), shiny, out _, alpha: alpha);
                    var capture = save.GetPcSnapshot().Captures.Single();
                    var identity = capture.RecordId;
                    var window = new DesktopCompanionWindow(save, capture, pokemon.GetForCapture(id), 0, 0,
                        false, _ => { }, megaEvolutions: mega, speciesMetadata: metadata, pokemonCatalog: pokemon,
                        changeMegaForm: formId => (formId is null || mega.CanApplyForm(capture, formId))
                            && save.SetMegaForm(identity, formId));
                    try
                    {
                        // All alternatives are selected in sequence on the same captured specimen.
                        foreach (var form in group)
                        {
                            var context = $"{id} -> {form.FormId}, shiny={shiny}, Alfa={alpha}";
                            Invoke(window, "RefreshMegaMenu");
                            var menu = Read<ContextMenu>(window, "_contextMenu");
                            var choices = menu.Items.OfType<MenuItem>()
                                .Where(item => item.Visibility == Visibility.Visible && item.Tag is string).ToArray();
                            Assert(choices.Length == group.Count() && choices.All(item => item.Items.Count == 0 && item.IsEnabled),
                                $"Todas las formas deben poder seleccionarse desde filas visibles del menú: {context}.");
                            var choice = choices.Single(item => Equals(item.Tag, form.FormId));
                            choice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, choice));
                            Assert(capture.MegaFormId == form.FormId && Read<bool>(window, "_megaTransformationActive"),
                                $"El clic real del menú no inició la transformación: {context}.");
                            var frames = Read<BitmapSource[,]>(window, "_megaTransitionFrames");
                            Assert(frames.Length == 16 && frames.Cast<BitmapSource>().All(frame => frame.PixelWidth > 0 && frame.PixelHeight > 0),
                                $"No se cargaron los 16 sprites de transición: {context}.");
                            Invoke(window, "CompleteMegaEvolutionAnimation");
                            Assert(window.IsMegaEvolved && window.BattleFormId == form.FormId
                                && Read<PokemonEntry>(window, "_entry").Id == form.FormId,
                                $"No se mostró la forma Mega elegida: {context}.");
                            var restored = GameSaveStore.LoadOrCreate(path).GetPcSnapshot().Captures.Single();
                            Assert(restored.MegaFormId == form.FormId && restored.Id == id
                                && restored.RecordId == identity && restored.IsShiny == shiny && restored.IsAlpha == alpha,
                                $"La forma Mega o la identidad no persistieron: {context}.");
                            Invoke(window, "RefreshMegaMenu");
                            var undo = menu.Items.OfType<MenuItem>().Single(item => item.Visibility == Visibility.Visible
                                && Equals(item.Header, "Desmegaevolucionar"));
                            undo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, undo));
                            Assert(Read<bool>(window, "_megaTransformationActive") && Read<bool>(window, "_megaTransformationReversing"),
                                $"El menú no inició la vuelta a la forma normal: {context}.");
                            Invoke(window, "CompleteMegaDevolutionAnimation");
                            restored = GameSaveStore.LoadOrCreate(path).GetPcSnapshot().Captures.Single();
                            Assert(!window.IsMegaEvolved && restored.MegaFormId is null && restored.Id == id
                                && restored.RecordId == identity && restored.IsShiny == shiny && restored.IsAlpha == alpha,
                                $"La desmegaevolución alteró la identidad: {context}.");
                            cases++;
                        }
                    }
                    finally
                    {
                        window.SuppressPersistenceOnClose();
                        window.Close();
                    }
                }
            }
            var multi = definitions.GroupBy(form => form.BasePokemonId).Where(group => group.Count() > 1)
                .Select(group => $"{group.Key}: {string.Join('/', group.Select(form => form.DisplayName))}");
            Summary = $"Mega menu integration: PASS ({definitions.Count} registered forms; {specimenCount} isolated specimens; "
                + $"{cases} menu/transition/save/reload/devolution cycles; normal/shiny, normal/Alfa, female variants). "
                + string.Join("; ", multi);
        }
        finally
        {
            GameSoundSettings.Update(enabled, volume);
            Environment.SetEnvironmentVariable("POKEGRASS_AUDIO_SETTINGS_PATH", audioPath);
            application.ShutdownMode = shutdownMode;
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static T Read<T>(DesktopCompanionWindow window, string name) =>
        (T)typeof(DesktopCompanionWindow).GetField(name, PrivateInstance)!.GetValue(window)!;

    private static void Invoke(DesktopCompanionWindow window, string method)
    {
        try { typeof(DesktopCompanionWindow).GetMethod(method, PrivateInstance)!.Invoke(window, null); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
