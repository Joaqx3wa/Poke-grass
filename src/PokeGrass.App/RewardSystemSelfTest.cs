using System.IO;
using System.Text.Json;

namespace PokeGrass;

internal static class RewardSystemSelfTest
{
    internal static string Summary { get; private set; } = string.Empty;

    internal static void Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "assets");
        var catalog = PokemonCatalog.Load(Path.Combine(root, "data", "pokemon-catalog.json"));
        var metadata = PokemonSpeciesMetadataCatalog.Load(Path.Combine(root, "data", "pokemon-species-metadata.json"), catalog);
        var balance = RewardBalance.Load(Path.Combine(root, "data", "reward-balance.json"));
        var normalRequestGift = balance.MakeBerryRequestGift("razz-berry", new FixedRandom());
        var silverRequestGift = balance.MakeBerryRequestGift("silver-razz-berry", new FixedRandom());
        var goldenExceptionalGift = balance.MakeBerryRequestGift("golden-nanab-berry", new FixedRandom());
        var goldenRegularGift = balance.MakeBerryRequestGift("golden-razz-berry", new NonZeroRandom());
        Assert(normalRequestGift.Id != "master-ball",
            "Las peticiones normales deben poder entregar objetos mejorados sin conceder Master Ball.");
        Assert(silverRequestGift.Id != "master-ball" && goldenRegularGift.Id != "master-ball",
            "La Master Ball debe quedar excluida de las categorías normal y plateada y del resultado dorado común.");
        Assert(goldenExceptionalGift.Id == "master-ball",
            "El resultado excepcional del 0,1 % de las peticiones doradas debe poder dar una Master Ball.");
        var tempRoot = Path.Combine(Path.GetTempPath(), "PokéGrass-rewards-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var capturePath = Path.Combine(tempRoot, "captures.json");
        var save = GameSaveStore.LoadOrCreate(capturePath);
        var pikachu = metadata.Get("PIKACHU");
        save.TryRecordCapture(pikachu, false, out _);

        var normalDuplicate = save.TryRecordCaptureWithRewards(pikachu, false, "common", balance, "capture:normal-duplicate", new Random(17));
        Assert(normalDuplicate.Duplicate && normalDuplicate.Rewards.Count > 0, "Un duplicado normal común debe entregar objetos.");
        Assert(normalDuplicate.Rewards.All(item => item.Id != "master-ball"), "Los duplicados normales nunca entregan Master Ball.");
        var afterDuplicate = save.Count("poke-ball") + save.Count("razz-berry") + save.Count("nanab-berry") + save.Count("silver-razz-berry")
            + save.Count("super-ball") + save.Count("ultra-ball") + save.Count("incense-full");
        var replay = save.TryRecordCaptureWithRewards(pikachu, false, "common", balance, "capture:normal-duplicate", new Random(17));
        var afterReplay = save.Count("poke-ball") + save.Count("razz-berry") + save.Count("nanab-berry") + save.Count("silver-razz-berry")
            + save.Count("super-ball") + save.Count("ultra-ball") + save.Count("incense-full");
        Assert(replay.Rewards.SequenceEqual(normalDuplicate.Rewards) && afterReplay == afterDuplicate,
            "Repetir el mismo evento de captura no debe duplicar sus recompensas.");

        var firstShiny = save.TryRecordCaptureWithRewards(pikachu, true, "common", balance, "capture:shiny-first", new Random(19));
        var secondShiny = save.TryRecordCaptureWithRewards(pikachu, true, "common", balance, "capture:shiny-duplicate", new FixedRandom());
        Assert(!firstShiny.ShinyDuplicate && firstShiny.Rewards.Count == 0, "El primer shiny se guarda sin regalo de shiny duplicado.");
        Assert(secondShiny.ShinyDuplicate && secondShiny.Rewards.Count > 0 && secondShiny.Rewards.Any(item => item.Id == "master-ball"),
            "Un shiny duplicado puede producir la Master Ball excepcional de su tabla especial.");
        Assert(save.GetPcSnapshot().Captures.Count(record => record.Id == pikachu.Id && record.IsShiny) == 2
            && JsonSerializer.Deserialize<GameSaveData>(File.ReadAllText(capturePath))!.DuplicateCaptureRecords
                .All(record => record.Id != pikachu.Id || !record.IsShiny),
            "Los shiny duplicados deben permanecer en la PC y no en el registro oculto de duplicados.");

        var supplyPath = Path.Combine(tempRoot, "supply.json");
        var supplySave = GameSaveStore.LoadOrCreate(supplyPath);
        supplySave.SaveRewardProgress(1);
        var today = new DateOnly(2026, 10, 12);
        using (var service = new RewardService(supplySave, balance, startTimer: false, dateProvider: () => today, random: new Random(23)))
        {
            Assert(supplySave.GetRewardProgress().LastDailyLocalDate == today.ToString("yyyy-MM-dd"), "El regalo diario debe registrarse con la fecha local.");
            service.TickForTest();
            Assert(supplySave.GetRewardProgress().SupplySecondsRemaining == balance.SupplyIntervalSeconds,
                "Al simular el vencimiento del reloj debe reiniciarse una hora.");
            Assert(supplySave.GetRewardProgress().PendingNotifications.Any(item => item.Title == "Suministros periódicos"),
                "El paquete periódico debe dejar una notificación pendiente.");
            Assert(service.TryGetPending(out var pendingDaily) && pendingDaily?.Title == "Regalo diario"
                && supplySave.GetRewardProgress().PendingNotifications.Count == 2,
                "Leer una notificación para presentarla no debe borrarla antes de que se cierre.");
            service.CompletePending(pendingDaily!.EventId);
            Assert(service.TryGetPending(out var pendingSupply) && pendingSupply?.Title == "Suministros periódicos",
                "La notificación periódica debe mantenerse pendiente hasta mostrarse.");
            Assert(supplySave.Count("poke-ball") > 10, "El suministro simulado debe añadir Balls al inventario.");
            today = today.AddDays(1);
            service.TickForTest();
            Assert(supplySave.GetRewardProgress().LastDailyLocalDate == today.ToString("yyyy-MM-dd"),
                "El cambio de día mientras la app está abierta debe entregar un nuevo regalo diario.");
        }
        var reloaded = GameSaveStore.LoadOrCreate(supplyPath);
        Assert(reloaded.GetRewardProgress().PendingNotifications.Any(item => item.Title == "Suministros periódicos")
            && reloaded.Count("poke-ball") == supplySave.Count("poke-ball"), "Los objetos, avisos y temporizador deben persistir al recargar.");

        var feedPath = Path.Combine(tempRoot, "feeding.json");
        var feedSave = GameSaveStore.LoadOrCreate(feedPath);
        var gift = balance.MakeCompanionGift(new Random(29));
        Assert(gift.Id != "master-ball", "Los regalos de acompañantes nunca deben incluir Master Ball.");
        Assert(feedSave.TryFeedCompanionBerry("razz-berry", "companion-feed:one", "¡REGALO DE TU POKÉMON!", []),
            "Una baya disponible debe poder alimentar al acompañante.");
        Assert(feedSave.Count("razz-berry") == 2, "Al alimentar se debe consumir exactamente una baya.");
        Assert(!feedSave.TryFeedCompanionBerry("razz-berry", "companion-feed:one", "¡REGALO DE TU POKÉMON!", []),
            "Repetir la misma operación no debe volver a consumir la baya.");
        Assert(feedSave.Count("razz-berry") == 2, "Una operación repetida no debe descontar una segunda baya.");
        var giftCountBefore = feedSave.Count(gift.Id);
        Assert(feedSave.TryFeedCompanionBerry("nanab-berry", "companion-feed:gift", "¡REGALO DE TU POKÉMON!", [gift]),
            "La alimentación con regalo debe guardarse como una transacción.");
        Assert(feedSave.Count("nanab-berry") == 2 && feedSave.Count(gift.Id) == giftCountBefore + gift.Quantity,
            "El regalo debe guardarse junto con el consumo de una sola baya.");
        Assert(feedSave.GetRewardProgress().PendingNotifications.Any(item => item.EventId == "companion-feed:gift"
            && item.Title == "¡REGALO DE TU POKÉMON!" && item.Items.Single().Id == gift.Id),
            "El regalo debe quedar en cola para el panel amarillo existente.");
        var feedReload = GameSaveStore.LoadOrCreate(feedPath);
        Assert(feedReload.Count("nanab-berry") == 2 && feedReload.Count(gift.Id) == feedSave.Count(gift.Id)
            && feedReload.GetRewardProgress().PendingNotifications.Any(item => item.EventId == "companion-feed:gift"),
            "El consumo, el objeto recibido y la notificación deben persistir al reiniciar.");

        var exportPath = Path.Combine(tempRoot, "exported-save.json");
        feedSave.ExportToFile(exportPath);
        var importPath = Path.Combine(tempRoot, "import-target", "save.json");
        var importSave = GameSaveStore.LoadOrCreate(importPath);
        Assert(importSave.Count("razz-berry") == 3, "El guardado de destino aislado debe conservar el inventario inicial.");
        var backupPath = importSave.ImportFromFile(exportPath);
        Assert(File.Exists(backupPath) && importSave.Count("razz-berry") == 2
            && importSave.Count(gift.Id) == feedSave.Count(gift.Id),
            "La importación debe reemplazar los datos y crear una copia de seguridad.");
        Assert(GameSaveStore.LoadOrCreate(backupPath).Count("razz-berry") == 3,
            "La copia automática debe conservar los datos anteriores a la importación.");
        var invalidImport = Path.Combine(tempRoot, "invalid-save.json");
        File.WriteAllText(invalidImport, "{ invalid json }");
        var countBeforeInvalidImport = importSave.Count("razz-berry");
        try { importSave.ImportFromFile(invalidImport); throw new InvalidOperationException("Un JSON inválido no debe importarse."); }
        catch (JsonException) { }
        Assert(importSave.Count("razz-berry") == countBeforeInvalidImport,
            "Un archivo inválido no debe tocar la partida actual.");

        var persisted = JsonSerializer.Deserialize<GameSaveData>(File.ReadAllText(capturePath))!;
        Assert(persisted.AppliedRewardEvents.Any(entry => entry.EventId == "capture:normal-duplicate")
            && persisted.Captures.Count(record => record.Id == pikachu.Id && record.IsShiny) == 2,
            "Las transacciones de duplicados y su identidad shiny deben persistir.");
        VerifyRewardDistribution(balance);
        Summary = "Recompensas y datos: regalos por categoría de baya, Master Ball dorada excepcional, idempotencia, exportación, importación validada, copia de seguridad y rechazo de archivos inválidos verificados con guardados temporales aislados.";
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyRewardDistribution(RewardBalance balance)
    {
        const int samples = 20000;
        var random = new Random(27102026);
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < samples; i++)
        {
            var supply = balance.MakeSupply(random);
            Assert(supply.Any(item => item.Id == "super-ball"),
                "Cada suministro debe aportar Super Balls para evitar depender de duplicados raros.");
            foreach (var item in supply) totals[item.Id] = totals.GetValueOrDefault(item.Id) + item.Quantity;
            var daily = balance.MakeDailyGift(random);
            Assert(daily.Any(item => item.Id == "ultra-ball") && daily.Any(item => item.Id == "super-ball"),
                "El regalo diario debe asegurar acceso a Super Balls y Ultra Balls.");
            Assert(supply.Concat(daily).All(item => item.Id != "master-ball"),
                "Los suministros y regalos diarios no deben conceder Master Balls.");
        }
        Assert(totals["poke-ball"] < 3 * totals["super-ball"]
            && totals["poke-ball"] < 8 * totals["ultra-ball"],
            "El suministro no debe volver a concentrar casi todas las Balls en Poké Balls.");
        foreach (var id in new[] { "silver-razz-berry", "silver-nanab-berry", "golden-razz-berry", "golden-nanab-berry" })
            Assert(totals.GetValueOrDefault(id) > samples / 5,
                $"La baya {id} debe aparecer regularmente en los suministros.");
        foreach (var id in balance.Items.Keys.Where(id => id.StartsWith("incense-", StringComparison.OrdinalIgnoreCase)))
            Assert(totals.GetValueOrDefault(id) > samples / 25,
                $"El incienso {id} debe ser accesible mediante suministros ordinarios.");
        var gifts = Enumerable.Range(0, samples).Select(_ => balance.MakeBerryRequestGift("razz-berry", random)).ToArray();
        Assert(gifts.Count(item => item.Id is "super-ball" or "ultra-ball") > samples / 5,
            "Las peticiones normales también deben ofrecer Balls mejoradas.");
    }

    private sealed class FixedRandom : Random
    {
        public override double NextDouble() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private sealed class NonZeroRandom : Random
    {
        public override double NextDouble() => 1;
        public override int Next(int maxValue) => maxValue > 1 ? 1 : 0;
        public override int Next(int minValue, int maxValue) => minValue + Math.Min(1, maxValue - minValue - 1);
    }
}
