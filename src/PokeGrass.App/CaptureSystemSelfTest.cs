using System.IO;
using System.Text;
using System.Text.Json;

namespace PokeGrass;

internal static class CaptureSystemSelfTest
{
    public static string Summary { get; private set; } = string.Empty;
    public static string BalanceReport { get; private set; } = string.Empty;

    public static void Run(string assets, PokemonCatalog pokemonCatalog)
    {
        var metadata = PokemonSpeciesMetadataCatalog.Load(
            Path.Combine(assets, "data", "pokemon-species-metadata.json"), pokemonCatalog);
        Assert(File.Exists(Path.Combine(assets, "items", "balls", "poke.png")), "Debe existir la imagen real de Poké Ball.");
        Assert(File.Exists(Path.Combine(assets, "items", "balls", "great.png")), "Debe existir la imagen real de Super Ball.");
        Assert(File.Exists(Path.Combine(assets, "items", "balls", "ultra.png")), "Debe existir la imagen real de Ultra Ball.");
        Assert(File.Exists(Path.Combine(assets, "items", "balls", "master.png")), "Debe existir la imagen real de Master Ball.");
        Assert(File.Exists(Path.Combine(assets, "items", "berries", "razz-gen7.png")), "Debe existir la baya Frambu.");
        Assert(File.Exists(Path.Combine(assets, "items", "berries", "nanab-gen7.png")), "Debe existir la baya Latano.");
        Assert(File.Exists(Path.Combine(assets, "items", "berries", "golden-razz.png")), "Debe existir la baya Frambu Dorada.");
        Assert(File.Exists(Path.Combine(assets, "items", "berries", "silver-razz.png")), "Debe existir la baya Frambu Plateada.");
        Assert(File.Exists(Path.Combine(assets, "items", "berries", "silver-nanab.png")), "Debe existir la baya Latano Plateada.");
        Assert(File.Exists(Path.Combine(assets, "items", "berries", "golden-nanab.png")), "Debe existir la baya Latano Dorada.");
        Assert(File.Exists(Path.Combine(assets, "fonts", "Minecraft.ttf")), "Debe estar empaquetada la fuente pixel art Minecraft.");
        Assert(pokemonCatalog.Entries.All(entry => metadata.Get(entry.Id).PokedexNumber > 0),
            "Cada forma del catálogo debe tener un número de Pokédex.");
        Assert(pokemonCatalog.Entries.All(entry => metadata.Get(entry.Id).Types.Count is 1 or 2),
            "Cada forma válida debe tener uno o dos tipos.");
        Assert(metadata.Get("MRMIME").Types.SequenceEqual(["psychic", "fairy"]),
            "Mr. Mime debe conservar ambos tipos oficiales.");
        Assert(metadata.Get("WORMADAM_1").Types.SequenceEqual(["bug", "ground"])
            && metadata.Get("WORMADAM_2").Types.SequenceEqual(["bug", "steel"]),
            "Wormadam debe conservar tipos distintos en sus formas.");
        Assert(metadata.Get("SLOWBRO_1").Types.SequenceEqual(["poison", "psychic"])
            && metadata.Get("SLOWBRO_1").Form == "1",
            "Slowbro_1 debe ser la forma de Galar capturable y usar sus tipos propios.");
        Assert(metadata.Get("OGERPON_1").Types.SequenceEqual(["grass", "water"])
            && metadata.Get("OGERPON_2").Types.SequenceEqual(["grass", "fire"])
            && metadata.Get("OGERPON_3").Types.SequenceEqual(["grass", "rock"]),
            "Las máscaras de Ogerpon deben conservar los tipos de cada forma.");
        Assert(metadata.Get("IRONJUGULIS").Types.SequenceEqual(["dark", "flying"]),
            "La ficha de Iron Jugulis debe incluir ambos tipos.");

        var common = metadata.Get("PIKACHU");
        var lowRate = metadata.Get("MEWTWO");
        Assert(CaptureMechanics.Chance(lowRate, CaptureBall.Poke, null)
            < CaptureMechanics.Chance(lowRate, CaptureBall.Great, null)
            && CaptureMechanics.Chance(lowRate, CaptureBall.Great, null)
            < CaptureMechanics.Chance(lowRate, CaptureBall.Ultra, null),
            "Las Balls deben mejorar la probabilidad en orden.");
        Assert(CaptureMechanics.Chance(lowRate, CaptureBall.Poke, CaptureBerry.Razz)
            > CaptureMechanics.Chance(lowRate, CaptureBall.Poke, null), "Frambu debe mejorar la captura.");
        Assert(CaptureMechanics.Chance(lowRate, CaptureBall.Poke, CaptureBerry.SilverRazz)
            > CaptureMechanics.Chance(lowRate, CaptureBall.Poke, CaptureBerry.Razz), "Frambu Plateada debe mejorar más que Frambu.");
        var goldenRazzChance = CaptureMechanics.Chance(lowRate, CaptureBall.Poke, CaptureBerry.GoldenRazz);
        Assert(goldenRazzChance > CaptureMechanics.Chance(lowRate, CaptureBall.Poke, CaptureBerry.SilverRazz)
            && goldenRazzChance < 1,
            $"Frambu Dorada debe ayudar mucho, pero no garantizar la captura ({goldenRazzChance:F3}).");
        Assert(CaptureMechanics.FleeChance(lowRate, "common", null) > CaptureMechanics.FleeChance(common, "common", null),
            "La probabilidad de huida debe variar según la especie.");
        Assert(CaptureMechanics.FleeChance(common, "common", CaptureBerry.Nanab)
            < CaptureMechanics.FleeChance(common, "common", null), "Latano debe reducir la huida.");
        Assert(CaptureMechanics.FleeChance(common, "common", CaptureBerry.SilverNanab)
            < CaptureMechanics.FleeChance(common, "common", CaptureBerry.Nanab)
            && CaptureMechanics.FleeChance(common, "common", CaptureBerry.GoldenNanab)
            < CaptureMechanics.FleeChance(common, "common", CaptureBerry.SilverNanab)
            && CaptureMechanics.FleeChance(common, "common", CaptureBerry.GoldenNanab) > 0,
            "Las variantes Latano deben reducir la huida progresivamente sin anularla.");
        Assert(CaptureMechanics.Roll(common, CaptureBall.Master, null, false, new FixedCaptureRandom(0.999)).Success,
            "Master Ball debe garantizar la captura.");
        Assert(CaptureMechanics.Roll(common, CaptureBall.Poke, null, true, new FixedCaptureRandom(0.999)).Success,
            "Un shiny debe capturarse con cualquier Ball válida.");
        Assert(!CaptureMechanics.RollFlee(common, "legendary", null, true, new FixedCaptureRandom(0.999)),
            "Un shiny no puede huir.");
        Assert(!CaptureMechanics.Roll(common, CaptureBall.Poke, null, false, new FixedCaptureRandom(0.999)).Success,
            "La prueba debe cubrir un intento fallido.");

        BalanceReport = BuildBalanceReport(metadata);
        var reportPath = Environment.GetEnvironmentVariable("POKEGRASS_BALANCE_REPORT_PATH");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, BalanceReport, Encoding.UTF8);
        }

        var savePath = Path.Combine(Path.GetTempPath(), "PokéGrass-capture-test", Guid.NewGuid().ToString("N"), "save.json");
        var save = GameSaveStore.LoadOrCreate(savePath);
        Assert(save.Count(CaptureBall.Poke) == 10 && save.Count(CaptureBall.Great) == 5
            && save.Count(CaptureBerry.Razz) == 3 && save.Count(CaptureBerry.Nanab) == 3,
            "La partida nueva debe entregar solo el kit inicial acordado.");
        Assert(save.Count(CaptureBall.Ultra) == 0 && save.Count(CaptureBall.Master) == 0
            && Enum.GetValues<CaptureBerry>().Where(berry => berry is not CaptureBerry.Razz and not CaptureBerry.Nanab)
                .All(berry => save.Count(berry) == 0), "No se deben otorgar Balls ni bayas adicionales al inicio.");
        Assert(save.TryConsume(CaptureBall.Poke), "Debe poder gastarse una Ball válida.");
        Assert(!save.TryConsume(CaptureBall.Master), "Una Ball agotada no debe gastarse ni pasar a negativo.");
        Assert(save.TryConsume(CaptureBerry.Razz), "Debe poder descontarse una baya del inventario al aplicarla.");
        var attemptSavePath = Path.Combine(Path.GetTempPath(), "PokéGrass-berry-attempt-test", Guid.NewGuid().ToString("N"), "save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(attemptSavePath)!);
        File.WriteAllText(attemptSavePath, JsonSerializer.Serialize(new GameSaveData
        {
            StarterKitGranted = true,
            Items = Enum.GetValues<CaptureBerry>().ToDictionary(berry => GameSaveStore.ItemId(berry), _ => 3,
                StringComparer.OrdinalIgnoreCase)
        }));
        var attemptSave = GameSaveStore.LoadOrCreate(attemptSavePath);
        var slot = new EncounterBerrySlot();
        Assert(slot.TryUse(attemptSave, CaptureBerry.Razz), "Debe poder aplicarse una baya disponible.");
        Assert(!slot.TryUse(attemptSave, CaptureBerry.Nanab), "No se puede aplicar una segunda baya antes de resolver el lanzamiento.");
        Assert(slot.ActiveBerry == CaptureBerry.Razz, "Un lanzamiento cancelado debe conservar la baya pendiente.");
        Assert(!CaptureMechanics.Roll(common, CaptureBall.Poke, slot.ActiveBerry, false, new FixedCaptureRandom(0.999)).Success,
            "La primera Ball puede fallar aunque una baya Frambu esté activa.");
        Assert(slot.ResolveAttempt() == CaptureBerry.Razz && !slot.Used,
            "Al resolverse un lanzamiento fallido se consume el efecto pendiente.");
        Assert(slot.TryUse(attemptSave, CaptureBerry.Nanab), "Se debe poder usar otra baya antes del siguiente lanzamiento.");
        Assert(!CaptureMechanics.Roll(common, CaptureBall.Poke, slot.ActiveBerry, false, new FixedCaptureRandom(0.999)).Success,
            "Latano no debe mejorar la captura de la Ball.");
        Assert(CaptureMechanics.FleeChance(common, "common", slot.ActiveBerry)
            < CaptureMechanics.FleeChance(common, "common", null), "Latano solo modifica la huida tras fallar la captura.");
        Assert(CaptureMechanics.RollFlee(lowRate, "common", null, false, new FixedCaptureRandom(0.04))
            && !CaptureMechanics.RollFlee(lowRate, "common", slot.ActiveBerry, false, new FixedCaptureRandom(0.04)),
            "Tras fallar la captura, Latano debe reducir la huida del encuentro con el multiplicador actual.");
        slot.ResolveAttempt();
        Assert(slot.TryUse(attemptSave, CaptureBerry.Nanab), "Otra baya debe poder activarse tras resolver el lanzamiento anterior.");
        Assert(CaptureMechanics.Roll(common, CaptureBall.Poke, slot.ActiveBerry, false, new FixedCaptureRandom(0)).Success,
            "La captura exitosa recibe el efecto de la baya de ese lanzamiento.");
        Assert(slot.ResolveAttempt() == CaptureBerry.Nanab && !slot.Used,
            "La captura exitosa también limpia el estado de la baya.");
        Assert(slot.TryUse(attemptSave, CaptureBerry.Razz), "Un encuentro posterior puede usar bayas tras cerrar el actual.");
        slot.Clear();
        Assert(!slot.Used, "Cerrar el encuentro debe limpiar cualquier efecto pendiente.");
        Assert(slot.TryUse(attemptSave, CaptureBerry.Razz), "Un shiny puede tener una baya pendiente antes de su lanzamiento garantizado.");
        Assert(CaptureMechanics.Roll(common, CaptureBall.Poke, slot.ActiveBerry, true, new FixedCaptureRandom(0.999)).Success,
            "La captura shiny sigue garantizada con cualquier Ball aunque haya una baya activa.");
        slot.ResolveAttempt();
        Assert(save.TryConsume(CaptureBerry.SilverRazz) == false, "Una variante sin existencias no debe consumirse.");
        var reloaded = GameSaveStore.LoadOrCreate(savePath);
        Assert(reloaded.Count(CaptureBall.Poke) == 9 && reloaded.Count(CaptureBerry.Razz) == 2
            && reloaded.Count(CaptureBerry.Nanab) == 3, "Inventario y kit deben persistir sin duplicarse al reabrir.");
        Assert(reloaded.Count(CaptureBall.Poke) != 20, "El kit no debe duplicarse al reiniciar.");

        var berrySavePath = Path.Combine(Path.GetTempPath(), "PokéGrass-berry-test", Guid.NewGuid().ToString("N"), "save.json");
        var berryItems = Enum.GetValues<CaptureBerry>().ToDictionary(berry => GameSaveStore.ItemId(berry), _ => 2,
            StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(berrySavePath)!);
        File.WriteAllText(berrySavePath, JsonSerializer.Serialize(new GameSaveData
        {
            StarterKitGranted = true,
            Items = berryItems
        }));
        var berrySave = GameSaveStore.LoadOrCreate(berrySavePath);
        foreach (var berry in Enum.GetValues<CaptureBerry>())
            Assert(berrySave.TryConsume(berry), $"Debe poder consumirse {berry}.");
        var berryReloaded = GameSaveStore.LoadOrCreate(berrySavePath);
        Assert(Enum.GetValues<CaptureBerry>().All(berry => berryReloaded.Count(berry) == 1),
            "Las seis bayas deben guardar y restaurar sus cantidades por separado.");

        var legacySavePath = Path.Combine(Path.GetTempPath(), "PokéGrass-legacy-save-test", Guid.NewGuid().ToString("N"), "save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacySavePath)!);
        File.WriteAllText(legacySavePath, JsonSerializer.Serialize(new GameSaveData
        {
            StarterKitGranted = true,
            Items = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [GameSaveStore.ItemId(CaptureBall.Poke)] = 7,
                [GameSaveStore.ItemId(CaptureBerry.Razz)] = 2,
                [GameSaveStore.ItemId(CaptureBerry.Nanab)] = 1,
                [GameSaveStore.ItemId(CaptureBerry.GoldenRazz)] = 0
            }
        }));
        var migratedSave = GameSaveStore.LoadOrCreate(legacySavePath);
        var migratedData = JsonSerializer.Deserialize<GameSaveData>(File.ReadAllText(legacySavePath))!;
        Assert(migratedSave.Count(CaptureBall.Poke) == 7 && migratedSave.Count(CaptureBerry.Razz) == 2
            && migratedSave.Count(CaptureBerry.Nanab) == 1
            && Enum.GetValues<CaptureBerry>().Where(berry => berry is not CaptureBerry.Razz and not CaptureBerry.Nanab)
                .All(berry => migratedSave.Count(berry) == 0)
            && Enum.GetValues<CaptureBerry>().All(berry => migratedData.Items.ContainsKey(GameSaveStore.ItemId(berry))),
            "Los guardados antiguos deben migrar IDs de bayas a cero sin alterar cantidades ni entregar el kit otra vez.");

        var pikachu = metadata.Get("PIKACHU");
        reloaded.TryRecordCapture(pikachu, false, out var duplicateOne);
        reloaded.TryRecordCapture(pikachu, false, out var duplicateTwo);
        reloaded.TryRecordCapture(pikachu, true, out var shinyOne);
        reloaded.TryRecordCapture(pikachu, true, out var shinyTwo);
        Assert(!duplicateOne && duplicateTwo && shinyOne && shinyTwo,
            "Los shiny duplicados conservan el indicador de duplicado.");
        var persisted = JsonSerializer.Deserialize<GameSaveData>(File.ReadAllText(savePath))!;
        Assert(persisted.Captures.Count == 3 && persisted.DuplicateCaptureRecords.Count == 1,
            "Las capturas y duplicados deben persistir con la identidad correcta.");
        Assert(persisted.Captures.All(record => !string.IsNullOrWhiteSpace(record.Id)
            && record.CapturedAt != default && !string.IsNullOrWhiteSpace(record.SpeciesId)),
            "Cada captura guardada debe registrar ID, especie y fecha.");
        Assert(persisted.Captures.Count(record => record.IsShiny) == 2
            && persisted.DuplicateCaptureRecords.All(record => !record.IsShiny),
            "Todos los shiny, incluidos los duplicados, deben permanecer en la PC.");

        var megaSavePath = Path.Combine(Path.GetTempPath(), "PokéGrass-mega-test", Guid.NewGuid().ToString("N"), "save.json");
        var megaSave = GameSaveStore.LoadOrCreate(megaSavePath);
        megaSave.TryRecordCapture(metadata.Get("CHARIZARD"), true, out _);
        var charizard = megaSave.GetPcSnapshot().Captures.Single();
        Assert(megaSave.SetMegaForm(charizard.RecordId, "CHARIZARD_1"), "Un ejemplar capturado debe poder adoptar una forma Mega.");
        var megaReloaded = GameSaveStore.LoadOrCreate(megaSavePath).GetPcSnapshot().Captures.Single();
        Assert(megaReloaded.RecordId == charizard.RecordId && megaReloaded.Id == "CHARIZARD"
            && megaReloaded.IsShiny && megaReloaded.MegaFormId == "CHARIZARD_1",
            "La forma Mega debe persistir sin duplicar ni cambiar la identidad ni el estado shiny.");
        Assert(GameSaveStore.LoadOrCreate(megaSavePath).SetMegaForm(charizard.RecordId, null)
            && GameSaveStore.LoadOrCreate(megaSavePath).GetPcSnapshot().Captures.Single().MegaFormId is null,
            "Desmegaevolucionar debe limpiar solo el estado de forma.");

        var legacySlowbroPath = Path.Combine(Path.GetTempPath(), "PokéGrass-legacy-mega-slowbro", Guid.NewGuid().ToString("N"), "save.json");
        var legacySlowbroSave = GameSaveStore.LoadOrCreate(legacySlowbroPath);
        legacySlowbroSave.TryRecordCapture(metadata.Get("SLOWBRO_1"), true, out _);
        var legacySlowbroData = JsonSerializer.Deserialize<GameSaveData>(File.ReadAllText(legacySlowbroPath))!;
        var legacySlowbro = legacySlowbroData.Captures.Single();
        var legacySlowbroRecordId = legacySlowbro.RecordId;
        legacySlowbroData.Version = 5;
        legacySlowbro.Id = "SLOWBRO_2";
        legacySlowbro.Form = "2";
        legacySlowbro.MegaFormId = null;
        File.WriteAllText(legacySlowbroPath, JsonSerializer.Serialize(legacySlowbroData));
        var migratedSlowbro = GameSaveStore.LoadOrCreate(legacySlowbroPath).GetPcSnapshot().Captures.Single();
        Assert(migratedSlowbro.RecordId == legacySlowbroRecordId && migratedSlowbro.Id == "SLOWBRO"
            && migratedSlowbro.MegaFormId == "SLOWBRO_2" && migratedSlowbro.IsShiny,
            "La migración Slowbro_2 debe conservar el ejemplar y registrarlo como Mega sin perder su estado shiny.");

        Summary = "Captura: baya aplicada a un único lanzamiento, nueva baya tras cada fallo, Ball cancelada conserva el efecto, huida con Latano, shiny/Master garantizados e inventario persistente verificados; megaevolución shiny permanente/desmegaevolución conserva identidad y persiste; migración de Slowbro_2 a Mega conserva el ejemplar; simulación de seis perfiles completada.";
    }

    private static string BuildBalanceReport(PokemonSpeciesMetadataCatalog metadata)
    {
        var scenarios = new (string Label, string Id, string Rarity, bool Shiny)[]
        {
            ("Común fácil", "ZUBAT", "common", false),
            ("Común difícil", "RELICANTH", "common", false),
            ("Raro", "METANG", "rare", false),
            ("Épico", "METAGROSS", "epic", false),
            ("Legendario", "ARCEUS", "legendary", false),
            ("Shiny legendario", "ARCEUS", "legendary", true)
        };
        var variants = new (string Label, CaptureBall Ball, CaptureBerry? Berry)[]
        {
            ("Poké Ball", CaptureBall.Poke, null),
            ("Super Ball", CaptureBall.Great, null),
            ("Ultra Ball", CaptureBall.Ultra, null),
            ("Poké + Frambu", CaptureBall.Poke, CaptureBerry.Razz),
            ("Poké + Frambu Plateada", CaptureBall.Poke, CaptureBerry.SilverRazz),
            ("Poké + Frambu Dorada", CaptureBall.Poke, CaptureBerry.GoldenRazz),
            ("Ultra + Frambu Dorada", CaptureBall.Ultra, CaptureBerry.GoldenRazz),
            ("Poké + Latano", CaptureBall.Poke, CaptureBerry.Nanab),
            ("Poké + Latano Plateada", CaptureBall.Poke, CaptureBerry.SilverNanab),
            ("Poké + Latano Dorada", CaptureBall.Poke, CaptureBerry.GoldenNanab),
            ("Master Ball", CaptureBall.Master, null)
        };
        const int trials = 50000;
        var output = new StringBuilder();
        output.AppendLine("# Auditoría de probabilidades de captura y huida");
        output.AppendLine();
        output.AppendLine("## Fórmulas vigentes");
        output.AppendLine();
        output.AppendLine("- Captura base sin combate, con Pokémon a salud completa: `p = 0,35 + 0,50 × (captureRate / 255)`. Conserva el orden por especie; el intervalo es aprox. 35,2–85 %.");
        output.AppendLine("- Multiplicadores sobre el fallo restante: `pFinal = 1 - (1-p)^m`, donde `m = multiplicador Ball × multiplicador baya`. Poké ×1; Super ×1,5; Ultra ×2; Master garantiza. No Master se limita a 99 %; shiny garantiza con cualquier Ball válida.");
        output.AppendLine("- Frambu: ×1,5; Frambu Plateada: ×1,9; Frambu Dorada: ×2,5. Se multiplican con el multiplicador de la Ball; solo una baya por encuentro.");
        output.AppendLine("- Huida: solo se comprueba después de una captura fallida. Base por rareza común/poco común/raro/épico/legendario: 5/8/12/16/20 %. Variación por especie ×0,75–×1,25 según captureRate, con máximo de 25 %.");
        output.AppendLine("- Latano: ×0,35; Latano Plateada: ×0,20; Latano Dorada: ×0,10 sobre huida. Shiny nunca huye.");
        output.AppendLine("- Rarezas 55/28/12/4/1 y shiny 1/512 no se alteran.");
        output.AppendLine();
        output.AppendLine("## Auditoría antes del ajuste");
        output.AppendLine();
        output.AppendLine("- Captura anterior: `0,08 + 0,72 × captureRate/255`, multiplicada directamente por Ball y baya y limitada a 99 %. captureRate solo incrementaba la base; no se aplicaban PS ni estados.");
        output.AppendLine("- Multiplicadores anteriores: Poké ×1, Super ×1,5, Ultra ×2, Master garantiza; Frambu ×1,5, Plateada ×1,9, Dorada ×2,5.");
        output.AppendLine("- Huida anterior: `0,05 + 0,50 × (255 - captureRate)/255`; podía llegar a ~54,8 % después de cada fallo. Latano ×0,35, Plateada ×0,20, Dorada ×0,10.");
        output.AppendLine("- No existía una huida por temporizador después de mostrar un Pokémon. Shiny garantizaba captura con cualquier Ball y no podía huir; esas reglas se conservan.");
        output.AppendLine();
        output.AppendLine("La baya afecta solo al primer lanzamiento válido posterior a usarla; los intentos siguientes no tienen baya hasta que se aplique otra. La esperanza teórica contempla ese primer lanzamiento distinto y luego la tasa sin baya. Latano solo modifica la probabilidad de huida tras el fallo de ese primer lanzamiento.");
        output.AppendLine();
        output.AppendLine($"## Simulación ({trials:N0} encuentros por combinación)");
        output.AppendLine();
        output.AppendLine("| Perfil | Opción | p por lanzamiento | Huida tras fallo | E[Ball] teórica | Captura simulada | Huida simulada | Balls medias |");
        output.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var scenario in scenarios)
        {
            var pokemon = metadata.Get(scenario.Id);
            foreach (var variant in variants)
            {
                var firstThrowChance = scenario.Shiny || variant.Ball == CaptureBall.Master
                    ? 1d
                    : CaptureMechanics.Chance(pokemon, variant.Ball, variant.Berry);
                var subsequentChance = scenario.Shiny || variant.Ball == CaptureBall.Master
                    ? 1d
                    : CaptureMechanics.Chance(pokemon, variant.Ball, null);
                var firstThrowFlee = scenario.Shiny ? 0d : CaptureMechanics.FleeChance(pokemon, scenario.Rarity, variant.Berry);
                var subsequentFlee = scenario.Shiny ? 0d : CaptureMechanics.FleeChance(pokemon, scenario.Rarity, null);
                var subsequentStopChance = subsequentChance + (1d - subsequentChance) * subsequentFlee;
                var theoreticalBalls = 1d + (1d - firstThrowChance) * (1d - firstThrowFlee) / subsequentStopChance;
                var random = new Random(HashCode.Combine(scenario.Id, scenario.Shiny, variant.Ball, variant.Berry, 52731));
                var captures = 0;
                var fled = 0;
                long usedBalls = 0;
                for (var i = 0; i < trials; i++)
                {
                    for (var throwNumber = 1; throwNumber <= 100; throwNumber++)
                    {
                        usedBalls++;
                        var berryForThrow = throwNumber == 1 ? variant.Berry : null;
                        if (CaptureMechanics.Roll(pokemon, variant.Ball, berryForThrow, scenario.Shiny, random).Success)
                        {
                            captures++;
                            break;
                        }
                        if (CaptureMechanics.RollFlee(pokemon, scenario.Rarity, berryForThrow, scenario.Shiny, random))
                        {
                            fled++;
                            break;
                        }
                        if (throwNumber == 100) throw new InvalidDataException("La simulación excedió el límite de lanzamientos.");
                    }
                }
                output.AppendLine($"| {scenario.Label} ({scenario.Id}, captureRate {pokemon.CaptureRate}) | {variant.Label} | {firstThrowChance:P2} | {firstThrowFlee:P2} | {theoreticalBalls:F3} | {captures / (double)trials:P2} | {fled / (double)trials:P2} | {usedBalls / (double)trials:F3} |");
            }
        }
        output.AppendLine();
        output.AppendLine("## Criterio de equilibrio");
        output.AppendLine();
        output.AppendLine("La fórmula anterior, `0,08 + 0,72 × captureRate/255`, daba aproximadamente 8,8 % para captureRate 3; combinada con huida de hasta 55 % podía dejar menos de una captura de cada cinco encuentros. La nueva base eleva el lanzamiento de especies difíciles a alrededor del 35 % con Poké Ball, conserva diferencias de especie y aplica las mejoras a la probabilidad de no fallar para evitar que el tope iguale varias opciones. La huida rara/épica/legendaria sigue la propuesta de tier con variación moderada y nunca es automática.");
        return output.ToString();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
