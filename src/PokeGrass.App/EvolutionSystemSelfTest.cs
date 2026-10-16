using System.IO;

namespace PokeGrass;

internal static class EvolutionSystemSelfTest
{
    internal static string Summary { get; private set; } = string.Empty;

    internal static void Run(string assets)
    {
        var data = Path.Combine(assets, "data");
        var pokemon = PokemonCatalog.Load(Path.Combine(data, "pokemon-catalog.json"));
        var metadata = PokemonSpeciesMetadataCatalog.Load(Path.Combine(data, "pokemon-species-metadata.json"), pokemon);
        var mega = MegaEvolutionCatalog.Load(assets, pokemon);
        var evolutions = PokemonEvolutionCatalog.Load(Path.Combine(data, "pokemon-evolutions.json"), pokemon, metadata, mega);

        var eligible = new FixedRoll(4);
        Assert(PokemonEvolutionCatalog.RollForEvolution(eligible.Next) && eligible.Calls == 1 && eligible.Range == 100,
            "La tirada debe ser una sola llamada uniforme de 0 a 99 y 4 debe representar 5 %.");
        var ineligible = new FixedRoll(5);
        Assert(!PokemonEvolutionCatalog.RollForEvolution(ineligible.Next) && ineligible.Calls == 1,
            "El umbral 5 debe quedar fuera del 5 %.");

        Assert(Targets(evolutions, metadata, pokemon, "CHARMELEON").Any(x => x.Id == "CHARIZARD"),
            "Charmeleon debe poder evolucionar a Charizard.");
        var maleSource = Capture(metadata, pokemon, "CHARMELEON", "charmeleon-male");
        var ownedFemaleCharizard = Capture(metadata, pokemon, "CHARIZARD_female", "charizard-female");
        Assert(evolutions.GetAvailableTargets(maleSource, [maleSource, ownedFemaleCharizard]).Any(x => x.Id == "CHARIZARD"),
            "Tener Charizard hembra no debe bloquear Charizard macho.");
        var ownedMaleCharizard = Capture(metadata, pokemon, "CHARIZARD", "charizard-male", shiny: true, alpha: true);
        Assert(!evolutions.GetAvailableTargets(maleSource, [maleSource, ownedMaleCharizard]).Any(x => x.Id == "CHARIZARD"),
            "Charizard shiny Alfa debe contar como macho poseído.");

        Assert(Targets(evolutions, metadata, pokemon, "KIRLIA").Any(x => x.Id == "GALLADE"),
            "Kirlia macho debe poder evolucionar a Gallade.");
        Assert(!Targets(evolutions, metadata, pokemon, "KIRLIA_female").Any(x => x.Id == "GALLADE"),
            "Kirlia hembra no debe poder evolucionar a Gallade.");
        Assert(!Targets(evolutions, metadata, pokemon, "FARFETCHD").Any(),
            "Farfetch'd normal no debe evolucionar a Sirfetch'd.");
        Assert(Targets(evolutions, metadata, pokemon, "FARFETCHD_1").Any(x => x.Id == "SIRFETCHD"),
            "Farfetch'd de Galar debe poder evolucionar a Sirfetch'd.");
        Assert(Targets(evolutions, metadata, pokemon, "MEOWTH").Any(x => x.Id == "PERSIAN")
            && Targets(evolutions, metadata, pokemon, "MEOWTH_1").Any(x => x.Id == "PERSIAN_1")
            && Targets(evolutions, metadata, pokemon, "MEOWTH_2").Any(x => x.Id == "PERRSERKER"),
            "Meowth normal, de Alola y de Galar deben seguir cadenas distintas.");
        Assert(Targets(evolutions, metadata, pokemon, "WOOPER_1").Any(x => x.Id == "CLODSIRE"),
            "Wooper de Paldea debe evolucionar a Clodsire.");
        Assert(Targets(evolutions, metadata, pokemon, "SLOWPOKE_1").Any(x => x.Id == "SLOWBRO_1")
            && Targets(evolutions, metadata, pokemon, "SLOWPOKE_1").Any(x => x.Id == "SLOWKING_1"),
            "Slowpoke de Galar debe conservar ambas evoluciones de Galar.");
        Assert(Targets(evolutions, metadata, pokemon, "SLOWPOKE_1_female").Any(x => x.Id == "SLOWBRO_1_female")
            && Targets(evolutions, metadata, pokemon, "SLOWPOKE_1_female").Any(x => x.Id == "SLOWKING_1_female"),
            "Slowpoke hembra de Galar debe conservar sus dos destinos y el sexo.");
        Assert(Targets(evolutions, metadata, pokemon, "BASCULIN_2_female").Any(x => x.Id == "BASCULEGION_female"),
            "Basculin raya blanca hembra debe evolucionar manteniendo su sexo.");
        Assert(Targets(evolutions, metadata, pokemon, "COMBEE_female").Any(x => x.Id == "VESPIQUEN")
            && !Targets(evolutions, metadata, pokemon, "COMBEE").Any(x => x.Id == "VESPIQUEN"),
            "Solo Combee hembra debe poder evolucionar a Vespiquen.");
        Assert(Targets(evolutions, metadata, pokemon, "SALANDIT_female").Any(x => x.Id == "SALAZZLE")
            && !Targets(evolutions, metadata, pokemon, "SALANDIT").Any(x => x.Id == "SALAZZLE"),
            "Solo Salandit hembra debe poder evolucionar a Salazzle.");
        Assert(Targets(evolutions, metadata, pokemon, "SNORUNT_female").Any(x => x.Id == "FROSLASS")
            && !Targets(evolutions, metadata, pokemon, "SNORUNT").Any(x => x.Id == "FROSLASS"),
            "Solo Snorunt hembra debe poder evolucionar a Froslass.");
        Assert(Targets(evolutions, metadata, pokemon, "LECHONK").Any(x => x.Id == "OINKOLOGNE")
            && Targets(evolutions, metadata, pokemon, "LECHONK_female").Any(x => x.Id == "OINKOLOGNE_1"),
            "Lechonk debe conservar la forma de Oinkologne según su sexo.");
        Assert(Targets(evolutions, metadata, pokemon, "EEVEE").Count >= 2,
            "Eevee debe ofrecer destinos ramificados.");
        Assert(Targets(evolutions, metadata, pokemon, "TYROGUE").Count == 3,
            "Tyrogue debe conservar sus tres destinos válidos.");
        Assert(!Targets(evolutions, metadata, pokemon, "DITTO").Any(), "Ditto no debe evolucionar.");
        var megaCapture = Capture(metadata, pokemon, "CHARIZARD", "charizard-mega");
        megaCapture.MegaFormId = "CHARIZARD_1";
        Assert(!evolutions.GetAvailableTargets(megaCapture, [megaCapture]).Any(),
            "Un Pokémon con forma Mega activa no debe evolucionar permanentemente.");

        var saveDirectory = Path.Combine(Path.GetTempPath(), "PokéGrass-evolution-test", Guid.NewGuid().ToString("N"));
        try
        {
            var save = GameSaveStore.LoadOrCreate(Path.Combine(saveDirectory, "save.json"));
            save.TryRecordCapture(metadata.Get("CHARMELEON"), shiny: true, out _, alpha: true);
            var source = save.GetPcSnapshot().Captures.Single();
            Assert(save.TryAddDesktopCompanion(source.RecordId, 200, 300), "Fixture should be on desktop and PC.");
            var before = save.GetPcSnapshot().Captures.Single();
            Assert(save.TryEvolveCapture(before.RecordId, before.Id, metadata.Get("CHARIZARD"), metadata),
                "Debe guardarse la evolución en el mismo registro.");
            var after = save.GetPcSnapshot().Captures.Single();
            Assert(after.RecordId == before.RecordId && after.IsShiny == before.IsShiny && after.IsAlpha == before.IsAlpha
                && after.CapturedAt == before.CapturedAt && after.BoxId == before.BoxId && after.BoxSlot == before.BoxSlot
                && after.Id == "CHARIZARD" && save.GetDesktopCompanions().Single().RecordId == before.RecordId
                && save.GetDesktopTeamSlots().Contains(before.RecordId),
                "La evolución debe conservar instancia, shiny, Alfa, captura, caja y equipo/escritorio.");
            Assert(GameSaveStore.LoadOrCreate(Path.Combine(saveDirectory, "save.json")).GetPcSnapshot().Captures.Single().Id == "CHARIZARD",
                "La evolución debe persistir después de recargar el guardado.");
        }
        finally
        {
            if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, recursive: true);
        }

        var sheet = Path.Combine(assets, EvolutionAnimationFrames.AssetPath.Replace('/', Path.DirectorySeparatorChar));
        var sheetSummary = EvolutionAnimationFrames.ValidateAndSummarize(sheet);
        Summary = $"Evolution: 5% roll, sex/region branches, ownership, Ditto/Mega exclusion, persistent identity; {sheetSummary}";

        IReadOnlyList<PokemonSpeciesMetadata> Targets(PokemonEvolutionCatalog catalog,
            PokemonSpeciesMetadataCatalog species, PokemonCatalog entries, string id)
        {
            var capture = Capture(species, entries, id, $"fixture-{id}");
            return catalog.GetAvailableTargets(capture, [capture]);
        }
    }

    private static CapturedPokemon Capture(PokemonSpeciesMetadataCatalog metadata, PokemonCatalog pokemon,
        string id, string recordId, bool shiny = false, bool alpha = false)
    {
        var species = metadata.Get(id);
        return new CapturedPokemon
        {
            RecordId = recordId, Id = id, SpeciesId = species.SpeciesId, Form = species.Form,
            IsShiny = shiny, IsAlpha = alpha, CapturedAt = DateTimeOffset.UnixEpoch
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed class FixedRoll(int value)
    {
        internal int Calls { get; private set; }
        internal int Range { get; private set; }
        internal int Next(int max)
        {
            Calls++;
            Range = max;
            return value;
        }
    }
}
