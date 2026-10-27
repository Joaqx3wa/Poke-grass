using System.IO;

namespace PokeGrass;

internal static class CaptureLimitSelfTest
{
    internal static string Summary { get; private set; } = string.Empty;

    internal static void Run()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "assets");
        var pokemon = PokemonCatalog.Load(Path.Combine(assets, "data", "pokemon-catalog.json"));
        var metadata = PokemonSpeciesMetadataCatalog.Load(Path.Combine(assets, "data", "pokemon-species-metadata.json"), pokemon);
        var mega = MegaEvolutionCatalog.Load(assets, pokemon);
        var policy = new CaptureLimitPolicy(metadata, pokemon, mega, assets);
        var root = Path.Combine(Path.GetTempPath(), "PokéGrass-capture-limits", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var charizardPath = Path.Combine(root, "charizard.json");
            var charizardSave = GameSaveStore.LoadOrCreate(charizardPath, policy);
            charizardSave.TryRecordCapture(metadata.Get("CHARIZARD"), false, out var charizardOneDuplicate);
            var first = charizardSave.GetPcSnapshot().Captures.Single();
            Assert(charizardSave.SetMegaForm(first.RecordId, "CHARIZARD_1"), "El primer Charizard debe poder megaevolucionar.");
            charizardSave.TryRecordCapture(metadata.Get("CHARIZARD"), true, out var charizardTwoDuplicate);
            charizardSave.TryRecordCapture(metadata.Get("CHARIZARD"), false, out var charizardThreeDuplicate);
            Assert(!charizardOneDuplicate && !charizardTwoDuplicate && charizardThreeDuplicate,
                "Charizard debe permitir dos capturas compartidas entre normal, Mega y shiny, y rechazar la tercera del PC.");
            Assert(charizardSave.GetPcSnapshot().Captures.Count == 2
                && charizardSave.GetPcSnapshot().Captures.Any(capture => capture.MegaFormId == "CHARIZARD_1")
                && charizardSave.GetPcSnapshot().Captures.Any(capture => capture.IsShiny),
                "Mega y shiny deben conservar su identidad sin ampliar el cupo.");
            Assert(GameSaveStore.LoadOrCreate(charizardPath, policy).GetPcSnapshot().Captures.Count == 2,
                "El cupo y los ejemplares deben persistir al reiniciar.");

            var genderSave = GameSaveStore.LoadOrCreate(Path.Combine(root, "gender.json"), policy);
            var wobbuffet = metadata.Get("WOBBUFFET");
            var femaleWobbuffet = metadata.Get("WOBBUFFET_female");
            Assert(policy.GetCaptureForm(wobbuffet) != policy.GetCaptureForm(femaleWobbuffet),
                "Los sprites y metadatos disponibles deben reconocer la variante femenina visual de Wobbuffet.");
            genderSave.TryRecordCapture(wobbuffet, false, out var maleOneDuplicate);
            genderSave.TryRecordCapture(femaleWobbuffet, false, out var femaleOneDuplicate);
            genderSave.TryRecordCapture(wobbuffet, true, out var maleTwoDuplicate);
            genderSave.TryRecordCapture(femaleWobbuffet, true, out var femaleTwoDuplicate);
            Assert(!maleOneDuplicate && !femaleOneDuplicate && maleTwoDuplicate && femaleTwoDuplicate
                && genderSave.GetPcSnapshot().Captures.Count == 4,
                "Wobbuffet conserva macho y hembra, y guarda los shiny aunque superen el cupo.");

            var alphaSavePath = Path.Combine(root, "alpha.json");
            var alphaSave = GameSaveStore.LoadOrCreate(alphaSavePath, policy);
            var pikachu = metadata.Get("PIKACHU");
            alphaSave.TryRecordCapture(pikachu, false, out var normalDuplicate);
            alphaSave.TryRecordCapture(pikachu, false, out var alphaDuplicate, alpha: true);
            alphaSave.TryRecordCapture(pikachu, true, out var alphaShinyDuplicate, alpha: true);
            Assert(!normalDuplicate && !alphaDuplicate && alphaShinyDuplicate,
                "Los cupos normales y Alfa deben ser independientes, y el shiny Alfa debe marcarse duplicado.");
            var persistedAlpha = GameSaveStore.LoadOrCreate(alphaSavePath, policy).GetPcSnapshot().Captures;
            Assert(persistedAlpha.Count == 3 && persistedAlpha.Count(capture => capture.IsAlpha) == 2
                && persistedAlpha.Any(capture => capture.IsAlpha && capture.IsShiny),
                "El shiny Alfa debe permanecer en la PC al superar el cupo y al reiniciar.");

            var alphaCharizardSave = GameSaveStore.LoadOrCreate(Path.Combine(root, "alpha-charizard.json"), policy);
            alphaCharizardSave.TryRecordCapture(metadata.Get("CHARIZARD"), false, out var alphaCharizardOneDuplicate, alpha: true);
            alphaCharizardSave.TryRecordCapture(metadata.Get("CHARIZARD"), true, out var alphaCharizardTwoDuplicate, alpha: true);
            alphaCharizardSave.TryRecordCapture(metadata.Get("CHARIZARD"), false, out var alphaCharizardThreeDuplicate, alpha: true);
            Assert(!alphaCharizardOneDuplicate && !alphaCharizardTwoDuplicate && alphaCharizardThreeDuplicate,
                "El cupo Alfa debe respetar las dos plazas de Charizard por sus dos Megaevoluciones.");
            var alphaCharizards = alphaCharizardSave.GetPcSnapshot().Captures;
            Assert(alphaCharizards.All(capture => capture.IsAlpha) && alphaCharizards.Any(capture => capture.IsShiny),
                "La combinación Alfa + Shiny debe conservar ambos atributos dentro del cupo Alfa.");
            Assert(alphaCharizardSave.SetMegaForm(alphaCharizards[0].RecordId, "CHARIZARD_1"),
                "Una captura Alfa debe poder megaevolucionar sin perder su atributo Alfa.");
            var restoredAlphaCharizards = GameSaveStore.LoadOrCreate(Path.Combine(root, "alpha-charizard.json"), policy)
                .GetPcSnapshot().Captures;
            Assert(restoredAlphaCharizards.Any(capture => capture.IsAlpha && capture.MegaFormId == "CHARIZARD_1")
                && restoredAlphaCharizards.Any(capture => capture.IsAlpha && capture.IsShiny),
                "Alfa, Mega y Shiny deben mantenerse al guardar y recargar la colección.");

            var ordinarySave = GameSaveStore.LoadOrCreate(Path.Combine(root, "ordinary.json"), policy);
            ordinarySave.TryRecordCapture(metadata.Get("PIKACHU"), false, out var pikachuOneDuplicate);
            ordinarySave.TryRecordCapture(metadata.Get("PIKACHU"), true, out var pikachuShinyDuplicate);
            Assert(!pikachuOneDuplicate && pikachuShinyDuplicate && ordinarySave.GetPcSnapshot().Captures.Count == 2
                && ordinarySave.GetPcSnapshot().Captures.Any(capture => capture.IsShiny),
                "Una especie sin Mega debe guardar también su shiny aunque supere el cupo.");

            var formsSave = GameSaveStore.LoadOrCreate(Path.Combine(root, "forms.json"), policy);
            formsSave.TryRecordCapture(metadata.Get("SLOWBRO"), false, out var baseDuplicate);
            formsSave.TryRecordCapture(metadata.Get("SLOWBRO_1"), false, out var regionalDuplicate);
            formsSave.TryRecordCapture(metadata.Get("SLOWBRO"), true, out var baseSecondDuplicate);
            formsSave.TryRecordCapture(metadata.Get("SLOWBRO_1"), true, out var regionalSecondDuplicate);
            Assert(!baseDuplicate && !regionalDuplicate && baseSecondDuplicate && regionalSecondDuplicate
                && formsSave.GetPcSnapshot().Captures.Count == 4,
                "Las formas conservan cupos independientes y guardan los shiny aunque superen el cupo.");

            Summary = "Cupos: dos Charizard por clase normal/Alfa; todos los shiny se guardan también al superar el cupo; Alfa y variantes de género/regionales mantienen cupos independientes.";
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
