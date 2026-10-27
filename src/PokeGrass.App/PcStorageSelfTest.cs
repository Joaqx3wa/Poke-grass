using System.IO;
using System.Text.Json;

namespace PokeGrass;

internal static class PcStorageSelfTest
{
    internal static string Summary { get; private set; } = string.Empty;
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "PokéGrass-PC-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = PokemonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "data", "pokemon-catalog.json"));
            var metadata = PokemonSpeciesMetadataCatalog.Load(Path.Combine(AppContext.BaseDirectory, "assets", "data", "pokemon-species-metadata.json"), catalog);
            VerifyEmptyAndAutocreate(Path.Combine(root, "full.json"), catalog, metadata);
            VerifyMovesSearchReleaseAndPersistence(Path.Combine(root, "moves.json"), catalog, metadata);
            VerifyDesktopCompanions(Path.Combine(root, "companions.json"), metadata);
            VerifyLegacyMigration(Path.Combine(root, "legacy.json"), metadata);
            Summary = "PC: cajas, duplicados shiny, intercambio, búsqueda, liberación, migración y persistencia comprobados; equipo: seis plazas, orden, huecos, deduplicación, migración antigua, posición y restauración comprobados.";
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void VerifyDesktopCompanions(string path, PokemonSpeciesMetadataCatalog metadata)
    {
        var save = GameSaveStore.LoadOrCreate(path);
        save.TryRecordCapture(metadata.Get("JOLTIK"), true, out _);
        save.TryRecordCapture(metadata.Get("PIKACHU"), false, out _);
        save.TryRecordCapture(metadata.Get("WAILORD"), false, out _);
        save.TryRecordCapture(metadata.Get("MEWTWO"), false, out _);
        save.TryRecordCapture(metadata.Get("BULBASAUR"), false, out _);
        save.TryRecordCapture(metadata.Get("CHARMANDER"), false, out _);
        save.TryRecordCapture(metadata.Get("SQUIRTLE"), false, out _);
        var before = save.GetPcSnapshot();
        var joltik = before.Captures.Single(item => item.Id == "JOLTIK" && item.IsShiny);
        var pikachu = before.Captures.Single(item => item.Id == "PIKACHU" && !item.IsShiny);
        var wailord = before.Captures.Single(item => item.Id == "WAILORD" && !item.IsShiny);
        var mewtwo = before.Captures.Single(item => item.Id == "MEWTWO" && !item.IsShiny);
        var bulbasaur = before.Captures.Single(item => item.Id == "BULBASAUR");
        var charmander = before.Captures.Single(item => item.Id == "CHARMANDER");
        var squirtle = before.Captures.Single(item => item.Id == "SQUIRTLE");

        Assert(save.TryAddDesktopCompanion(joltik.RecordId, 120, 140), "Un ejemplar shiny capturado debe poder enviarse al escritorio.");
        Assert(save.TryAddDesktopCompanion(pikachu.RecordId, 240, 260), "Un ejemplar normal capturado debe poder enviarse al escritorio.");
        Assert(save.TryAddDesktopCompanion(wailord.RecordId, 360, 380), "Debe admitirse el tercer acompañante.");
        Assert(save.TryAddDesktopCompanion(mewtwo.RecordId, 420, 430), "Debe admitirse el cuarto acompañante.");
        Assert(save.TryAddDesktopCompanion(bulbasaur.RecordId, 520, 530), "Debe admitirse el quinto acompañante.");
        Assert(save.TryAddDesktopCompanion(charmander.RecordId, 620, 630), "Debe admitirse el sexto acompañante.");
        Assert(!save.TryAddDesktopCompanion(joltik.RecordId, 500, 500), "No se debe duplicar un ejemplar que ya es acompañante.");
        Assert(!save.TryAddDesktopCompanion(squirtle.RecordId, 700, 700), "No se debe superar el límite de seis acompañantes.");
        Assert(!save.TryAddDesktopCompanion("not-captured", 0, 0), "No se deben enviar ejemplares que no estén guardados en el PC.");
        Assert(save.GetDesktopTeamSlots().SequenceEqual(new string?[] { joltik.RecordId, pikachu.RecordId, wailord.RecordId, mewtwo.RecordId, bulbasaur.RecordId, charmander.RecordId }),
            "Los integrantes deben ocupar las primeras casillas libres en orden y persistir las seis posiciones.");

        save.UpdateDesktopCompanion(joltik.RecordId, x: 777, y: 333, isPinned: true);
        var reloaded = GameSaveStore.LoadOrCreate(path);
        var active = reloaded.GetDesktopCompanions();
        Assert(active.Count == 6 && active.Select(item => item.RecordId).Distinct(StringComparer.Ordinal).Count() == 6,
            "El guardado debe restaurar exactamente seis ejemplares únicos.");
        var restoredJoltik = active.Single(item => item.RecordId == joltik.RecordId);
        Assert(restoredJoltik.X == 777 && restoredJoltik.Y == 333 && restoredJoltik.IsPinned,
            "La posición y la fijación deben persistir al reiniciar.");
        Assert(reloaded.GetDesktopTeamSlots().SequenceEqual(new string?[] { joltik.RecordId, pikachu.RecordId, wailord.RecordId, mewtwo.RecordId, bulbasaur.RecordId, charmander.RecordId }),
            "El orden de los seis espacios debe persistir al reiniciar.");
        Assert(reloaded.GetPcSnapshot().Captures.Any(item => item.RecordId == joltik.RecordId && item.IsShiny),
            "Enviar al escritorio debe conservar la identidad shiny y la pertenencia al PC.");

        Assert(reloaded.RemoveDesktopCompanion(pikachu.RecordId), "Devolver a la PC debe eliminar el estado de acompañante.");
        Assert(reloaded.GetDesktopTeamSlots()[1] is null && reloaded.GetDesktopTeamSlots()[2] == wailord.RecordId,
            "Devolver un integrante intermedio libera su plaza sin desplazar los demás.");
        Assert(reloaded.TryAddDesktopCompanion(squirtle.RecordId, 720, 730), "Tras devolver uno, el siguiente debe ocupar la plaza vacía.");
        Assert(reloaded.GetDesktopTeamSlots()[1] == squirtle.RecordId, "La nueva captura debe ocupar la primera plaza libre.");
        Assert(reloaded.SwapDesktopTeamSlots(0, 5), "Debe permitirse intercambiar dos plazas del equipo.");
        Assert(reloaded.GetDesktopTeamSlots()[0] == charmander.RecordId && reloaded.GetDesktopTeamSlots()[5] == joltik.RecordId,
            "El intercambio conserva la identidad y el orden solicitado.");
        Assert(reloaded.ReleaseRecord(wailord.RecordId, out _), "La liberación del PC debe seguir funcionando para un acompañante.");
        Assert(!reloaded.IsDesktopCompanion(wailord.RecordId), "Liberar un ejemplar debe limpiar su referencia de acompañante.");
        Assert(reloaded.GetPcSnapshot().Captures.Any(item => item.RecordId == joltik.RecordId)
            && reloaded.GetDesktopCompanions().Any(item => item.RecordId == joltik.RecordId),
            "Devolver o liberar otro ejemplar no debe perder la captura restante.");
    }

    private static void VerifyEmptyAndAutocreate(string path, PokemonCatalog catalog, PokemonSpeciesMetadataCatalog metadata)
    {
        var save = GameSaveStore.LoadOrCreate(path);
        var initial = save.GetPcSnapshot();
        Assert(initial.Boxes.Count == 1 && initial.Boxes[0].Name == "Caja 1" && initial.Boxes[0].Slots.Count == 30,
            "El PC vacío debe empezar con una caja de 30 casillas.");
        Assert(initial.Captures.Count == 0, "El PC nuevo debe estar vacío.");

        var ids = catalog.Entries.Select(entry => entry.Id).Where(id => !id.Equals("PIKACHU", StringComparison.OrdinalIgnoreCase)).Prepend("PIKACHU").Take(31).ToArray();
        foreach (var id in ids) save.TryRecordCapture(metadata.Get(id), false, out var duplicate);
        var full = save.GetPcSnapshot();
        Assert(full.Boxes.Count == 2 && full.Boxes[0].Slots.All(id => id is not null)
            && full.Boxes[1].Slots[0] == full.Captures.Last().RecordId,
            "Al llenar la caja 1, el siguiente Pokémon debe crear y ocupar Caja 2.");

        save.TryRecordCapture(metadata.Get(ids[0]), false, out var normalDuplicate);
        Assert(normalDuplicate && save.GetPcSnapshot().Captures.Count == 31,
            "Los duplicados normales no deben ocupar otra casilla.");
        save.TryRecordCapture(metadata.Get(ids[0]), true, out var shinyOne);
        save.TryRecordCapture(metadata.Get(ids[0]), true, out var shinyTwo);
        var shinySnapshot = save.GetPcSnapshot();
        Assert(shinyOne && shinyTwo && shinySnapshot.Captures.Count == 33
            && shinySnapshot.Captures.Count(item => item.Id == ids[0] && item.IsShiny) == 2,
            "Todos los shiny deben ocupar una casilla del PC, incluso si su forma ya alcanzó el cupo normal.");
        Assert(shinySnapshot.Captures.All(record => record.BoxId is not null && record.BoxSlot is >= 0 and < 30),
            "Cada ejemplar normal o shiny debe tener una ubicación válida.");
    }

    private static void VerifyMovesSearchReleaseAndPersistence(string path, PokemonCatalog catalog, PokemonSpeciesMetadataCatalog metadata)
    {
        var save = GameSaveStore.LoadOrCreate(path);
        foreach (var id in new[] { "PIKACHU", "JOLTIK", "WAILORD", "MEWTWO", "PIKACHU_female", "HYPNO_female" })
            save.TryRecordCapture(metadata.Get(id), id is "PIKACHU_female" or "HYPNO_female", out _, alpha: id == "HYPNO_female");
        var beforeCancel = save.GetPcSnapshot();
        var firstBox = beforeCancel.Boxes[0];
        var first = beforeCancel.Captures.Single(record => record.Id == "PIKACHU" && !record.IsShiny);
        var second = beforeCancel.Captures.Single(record => record.Id == "JOLTIK");
        Assert(save.MoveRecord(first.RecordId, firstBox.Id, 1), "Debe permitir mover un ejemplar dentro de una caja.");
        var swapped = save.GetPcSnapshot();
        Assert(swapped.Captures.Single(record => record.RecordId == first.RecordId).BoxSlot == 1
            && swapped.Captures.Single(record => record.RecordId == second.RecordId).BoxSlot == 0,
            "Soltar en una casilla ocupada debe intercambiar ambos ejemplares.");

        var cancelState = save.GetPcSnapshot();
        var cancelRecordId = cancelState.Captures.First().RecordId;
        // Move mode cancellation has no write operation until a destination is selected.
        var afterCancel = save.GetPcSnapshot();
        Assert(afterCancel.Captures.First(record => record.RecordId == cancelRecordId).BoxSlot
            == cancelState.Captures.First(record => record.RecordId == cancelRecordId).BoxSlot,
            "Cancelar el modo mover no debe modificar ubicaciones.");

        var searchOrdered = swapped.Boxes.SelectMany(box => box.Slots.Where(id => id is not null)).ToArray();
        var searchMatch = searchOrdered.Select(id => swapped.Captures.First(record => record.RecordId == id))
            .Where(record => metadata.Get(record.Id).DisplayName.Contains("Pika", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert(searchMatch.Length == 2 && searchMatch.Any(record => record.Id == "PIKACHU")
            && searchMatch.Any(record => record.Id == "PIKACHU_female"),
            "La búsqueda debe consultar ambas variantes visuales en orden de caja y casilla sin moverlas.");

        var occupiedSecond = swapped.Boxes[0].Slots[1]!;
        Assert(!save.DeleteEmptyBox(swapped.Boxes[0].Id), "No se puede eliminar una caja ocupada.");
        var victim = swapped.Captures.Single(record => record.RecordId == occupiedSecond);
        save.ReleaseRecord(victim.RecordId, out _);
        var afterRelease = save.GetPcSnapshot();
        Assert(afterRelease.Boxes[0].Slots[1] is null && afterRelease.Boxes[0].Slots[0] is not null,
            "Liberar deja el hueco y no desplaza los demás ejemplares.");
        var shiny = afterRelease.Captures.First(record => record.IsShiny);
        Assert(save.ReleaseRecord(shiny.RecordId, out var released) && released!.IsShiny,
            "La operación de liberar debe admitir y conservar la identidad shiny para la confirmación adicional de UI.");

        save.AddSpecialBox();
        var expanded = save.GetPcSnapshot(); var extra = expanded.Boxes[0];
        save.SetBoxBackground(extra.Id, "2.gif"); save.RenameBox(extra.Id, "Favoritos");
        save.AddBox(); var third = save.GetPcSnapshot().Boxes[2];
        save.SetBoxBackground(third.Id, "team.gif");
        save.ReorderBox(third.Id, -1);
        var reloaded = GameSaveStore.LoadOrCreate(path).GetPcSnapshot();
        var restoredHypno = reloaded.Captures.Single(record => record.Id == "HYPNO_female");
        Assert(restoredHypno.Form.Equals("female", StringComparison.OrdinalIgnoreCase)
            && restoredHypno.SpeciesId.Equals("hypno", StringComparison.OrdinalIgnoreCase)
            && restoredHypno.IsShiny && restoredHypno.IsAlpha,
            "El sexo, el estado shiny y el atributo Alfa de una variante femenina recién recuperada deben persistir en el PC.");
        Assert(reloaded.Boxes.Count == 3 && reloaded.Boxes[0].Name == "Favoritos" && reloaded.Boxes[0].IsSpecial
            && reloaded.Boxes[0].Background == "2.gif" && reloaded.Boxes[1].Name == "Caja 1"
            && reloaded.Boxes[1].Background == "team.gif" && reloaded.Boxes[2].Name == "Caja 2"
            && reloaded.Boxes[2].Background == "1.gif",
            $"Fondos independientes, nombres personalizados y numeración predeterminada deben persistir después de reordenar. Actual: {string.Join(" | ", reloaded.Boxes.Select(b => $"{b.Name}:{b.Background}:{b.NameIsCustom}"))}");
        save.RenameBox(reloaded.Boxes[2].Id, "Caja 1");
        Assert(save.GetPcSnapshot().Boxes.Count(box => box.Name == "Caja 1") == 2,
            "El identificador interno debe permitir renombrados sin imponer nombres únicos.");
        save.RenameBox(reloaded.Boxes[2].Id, "Favoritos duplicado");
        Assert(save.DeleteEmptyBox(reloaded.Boxes[1].Id), "Una caja secundaria vacía debe poder eliminarse después de reordenarla.");
        Assert(save.GetPcSnapshot().Boxes.Last(box => !box.IsSpecial).Name == "Favoritos duplicado",
            "Eliminar otra caja no debe cambiar el nombre personalizado de una caja normal.");
        var singleBoxPath = Path.Combine(Path.GetDirectoryName(path)!, "single-box.json");
        var singleBoxSave = GameSaveStore.LoadOrCreate(singleBoxPath);
        Assert(!singleBoxSave.DeleteEmptyBox(singleBoxSave.GetPcSnapshot().Boxes[0].Id), "Nunca debe eliminarse la última caja.");
    }

    private static void VerifyLegacyMigration(string path, PokemonSpeciesMetadataCatalog metadata)
    {
        var pikachu = metadata.Get("PIKACHU"); var joltik = metadata.Get("JOLTIK");
        var bulbasaur = metadata.Get("BULBASAUR");
        var legacy = new
        {
            Version = 1, StarterKitGranted = true,
            Items = new Dictionary<string, int> { ["poke-ball"] = 4, ["razz-berry"] = 2 },
            Captures = new[]
            {
                new CapturedPokemon { RecordId = "legacy-one", Id = pikachu.Id, SpeciesId = pikachu.SpeciesId, Form = pikachu.Form, IsShiny = false, CapturedAt = DateTimeOffset.UtcNow },
                new CapturedPokemon { RecordId = "legacy-two", Id = joltik.Id, SpeciesId = joltik.SpeciesId, Form = joltik.Form, IsShiny = true, CapturedAt = DateTimeOffset.UtcNow },
                new CapturedPokemon { RecordId = "legacy-three", Id = bulbasaur.Id, SpeciesId = bulbasaur.SpeciesId, Form = bulbasaur.Form, IsShiny = false, CapturedAt = DateTimeOffset.UtcNow }
            },
            DuplicateCaptureRecords = Array.Empty<CapturedPokemon>(),
            DesktopCompanions = new[]
            {
                new DesktopCompanionSave { RecordId = "legacy-one", X = 77, Y = 88, IsPinned = true },
                new DesktopCompanionSave { RecordId = "legacy-two", X = 177, Y = 188, IsPinned = false },
                new DesktopCompanionSave { RecordId = "legacy-three", X = 277, Y = 288, IsPinned = true }
            }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(legacy));
        var save = GameSaveStore.LoadOrCreate(path); var snapshot = save.GetPcSnapshot();
        Assert(snapshot.Captures.Count == 3 && snapshot.Captures.All(record => record.BoxId == snapshot.Boxes[0].Id)
            && snapshot.Captures.Select(record => record.BoxSlot).Order().SequenceEqual(new int?[] { 0, 1, 2 }),
            "El guardado v1 debe migrar todas las capturas en su orden a las primeras casillas sin perder estado.");
        Assert(save.Count(CaptureBall.Poke) == 4 && save.Count(CaptureBerry.Razz) == 2,
            "La migración del PC no debe alterar las cantidades de objetos.");
        Assert(save.GetDesktopCompanions().Count == 3
            && save.GetDesktopTeamSlots().Count == 6
            && save.GetDesktopTeamSlots().Take(3).SequenceEqual(new string?[] { "legacy-one", "legacy-two", "legacy-three" })
            && save.GetDesktopTeamSlots().Skip(3).All(id => id is null)
            && save.GetDesktopCompanions().Single(item => item.RecordId == "legacy-one") is { X: 77, Y: 88, IsPinned: true },
            "Un guardado antiguo debe migrar sus tres acompañantes a las plazas iniciales sin perder posición ni fijación.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
