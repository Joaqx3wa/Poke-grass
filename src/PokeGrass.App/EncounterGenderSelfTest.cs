using System.IO;

namespace PokeGrass;

internal static class EncounterGenderSelfTest
{
    internal static string Summary { get; private set; } = string.Empty;

    internal static void Run()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "assets");
        var catalog = PokemonCatalog.Load(Path.Combine(assets, "data", "pokemon-catalog.json"));
        var metadata = PokemonSpeciesMetadataCatalog.Load(
            Path.Combine(assets, "data", "pokemon-species-metadata.json"), catalog);
        var megaCatalog = MegaEvolutionCatalog.Load(assets, catalog);
        var rarity = RarityConfiguration.Load(Path.Combine(assets, "data", "encounter-rarity.json"));
        var initialTierSizes = rarity.TierOrder.ToDictionary(tier => tier, tier => catalog.GetTier(tier, rarity).Count);

        Assert(catalog.FemaleEncounterVariants.Count >= 98,
            "Los sprites femeninos y las variantes con fallback deben asociarse a sus especies.");
        VerifyResolvedFemale(catalog, "HYPNO_female", "HYPNO_female", "HYPNO_female", "HYPNO", "HYPNO", "HYPNO", "HYPNO");
        VerifyResolvedFemale(catalog, "GARCHOMP_female", "GARCHOMP_female", "GARCHOMP_female", "GARCHOMP_female", "GARCHOMP_female", "GARCHOMP", "GARCHOMP");
        VerifyResolvedFemale(catalog, "VENUSAUR_female", "VENUSAUR_female", "VENUSAUR_female", "VENUSAUR_female", "VENUSAUR_female", "VENUSAUR", "VENUSAUR");
        VerifyResolvedFemale(catalog, "RAICHU_female", "RAICHU_female", "RAICHU_female", "RAICHU", "RAICHU", "RAICHU_female", "RAICHU_female");
        VerifyResolvedFemale(catalog, "BASCULEGION_female", "BASCULEGION_female", "BASCULEGION_female", "BASCULEGION_female", "BASCULEGION_female", "BASCULEGION_female", "BASCULEGION_female");
        Assert(metadata.Get("PIKACHU_female").Form.Equals("female", StringComparison.OrdinalIgnoreCase),
            "La variante femenina creada desde el sprite debe conservar el sexo al capturarse.");

        var pikachu = catalog.ById["PIKACHU"];
        var pikachuFemale = catalog.ById["PIKACHU_female"];
        var maleCapture = CreateCapture("male", "pikachu", "", shiny: true, alpha: true);
        var femaleCapture = CreateCapture("female", "pikachu", "female", shiny: true, alpha: true);
        Assert(PokemonSex.Get(metadata.Get(Select(pikachu, [maleCapture], catalog, metadata, 1).Id)) == "female",
            "Si la colección solo tiene un macho (aunque sea shiny y Alfa), debe aparecer la hembra.");
        Assert(PokemonSex.Get(metadata.Get(Select(pikachuFemale, [femaleCapture], catalog, metadata, 2).Id)) == "male",
            "Si la colección solo tiene una hembra, debe aparecer el macho.");

        var neitherResults = Enumerable.Range(0, 128)
            .Select(seed => PokemonSex.Get(metadata.Get(Select(pikachu, [], catalog, metadata, seed).Id)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bothResults = Enumerable.Range(0, 128)
            .Select(seed => PokemonSex.Get(metadata.Get(Select(pikachu, [maleCapture, femaleCapture], catalog, metadata, seed).Id)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(neitherResults.SetEquals(["male", "female"]),
            "Sin ejemplares previos, ambos sexos deben poder aparecer.");
        Assert(bothResults.SetEquals(["male", "female"]),
            "Con ambos sexos capturados, ambos deben seguir pudiendo aparecer.");

        var hypno = catalog.ById["HYPNO"];
        var hypnoFemale = catalog.ById["HYPNO_female"];
        Assert(Select(hypno, [CreateSpeciesCapture("hypno-male", metadata.Get("HYPNO"))], catalog, metadata, 10).Id == hypnoFemale.Id,
            "La prioridad debe elegir Hypno hembra si el jugador solo tiene un macho.");
        Assert(Select(hypnoFemale, [CreateSpeciesCapture("hypno-female", metadata.Get("HYPNO_female"))], catalog, metadata, 11).Id == hypno.Id,
            "La prioridad debe elegir Hypno macho si el jugador solo tiene una hembra.");
        var poochyena = catalog.ById["POOCHYENA"];
        var poochyenaFemale = catalog.ById["POOCHYENA_female"];
        Assert(poochyenaFemale.EncounterNormal == poochyena.EncounterNormal
            && poochyenaFemale.EncounterShiny == poochyena.EncounterShiny
            && poochyenaFemale.WalkingNormal == poochyena.WalkingNormal
            && poochyenaFemale.IconNormal == poochyena.IconNormal
            && !poochyenaFemale.EncounterPoolEntry,
            "Poochyena debe usar el arte base para su variante hembra sin añadir peso al encuentro.");
        Assert(Select(poochyena, [CreateSpeciesCapture("poochyena-male", metadata.Get("POOCHYENA"))],
                catalog, metadata, 12).Id == poochyenaFemale.Id,
            "Si solo se tiene Poochyena macho, el siguiente debe ser hembra aunque use el sprite base.");
        Assert(PokemonSex.GetDisplayName(metadata.Get("HYPNO_female")).EndsWith(" F", StringComparison.Ordinal),
            "El nombre de las capturas femeninas debe incluir el sufijo F.");

        foreach (var number in Enumerable.Range(3, 5))
            Assert(PokemonSex.Get(metadata.Get($"PIKACHU_{number}")) == "female",
                $"Pikachu {number} debe estar marcado únicamente como hembra.");
        foreach (var number in Enumerable.Range(8, 8))
            Assert(PokemonSex.Get(metadata.Get($"PIKACHU_{number}")) == "male",
                $"Pikachu {number} debe estar marcado únicamente como macho.");
        Assert(PokemonSex.Get(metadata.Get("DITTO")) == "genderless"
            && PokemonSex.Get(metadata.Get("GARDEVOIR")) == "female"
            && PokemonSex.Get(metadata.Get("GALLADE")) == "male",
            "Ditto debe ser asexual y Gardevoir/Gallade deben conservar sus sexos configurados.");

        var nidoran = catalog.ById["NIDORANfE"];
        Assert(Select(nidoran, [], catalog, metadata, 3).Id == nidoran.Id,
            "Nidoran hembra debe conservar su identidad de especie, sin tratarse como variante de sexo.");
        Assert(PokemonSex.Get(metadata.Get("ARCEUS")) == "male"
            && !catalog.FemaleEncounterVariants.ContainsKey("ARCEUS"),
            "Arceus debe respetar la lista de especies exclusivamente masculinas.");
        Assert(PokemonSex.Get(metadata.Get("MEW")) == "male"
            && PokemonSex.Get(metadata.Get("MEWTWO")) == "male"
            && !catalog.FemaleEncounterVariants.ContainsKey("MEW")
            && !catalog.FemaleEncounterVariants.ContainsKey("MEWTWO"),
            "Mew y Mewtwo deben respetar la lista de especies exclusivamente masculinas.");
        Assert(!catalog.FemaleEncounterVariants.ContainsKey("DITTO"),
            "Ditto no debe recibir una variante femenina porque no tiene sexo.");
        Assert(!catalog.FemaleEncounterVariants.ContainsKey("NIDORANmA"),
            "Las identidades de Nidoran no deben convertirse en variantes de sexo.");
        Assert(rarity.TierOrder.All(tier => catalog.GetTier(tier, rarity).Count == initialTierSizes[tier]),
            "La incorporación de selección por sexo no debe cambiar los grupos de rareza ni el peso de especies.");
        Assert(catalog.GetTier("uncommon", rarity).Count(entry => entry.Id.StartsWith("PIKACHU", StringComparison.OrdinalIgnoreCase)) == 8,
            "Las nuevas formas de Pikachu no deben aumentar su peso en los encuentros.");
        Assert(megaCatalog.TryGetForm("CAMERUPT_1", out var megaCamerupt)
            && MegaEvolutionCatalog.GetDetailsEncounterPath(megaCamerupt, isShiny: false, isFemale: true, assets)
                .Equals("pokemon/encounter/normal/CAMERUPT_1_female.png", StringComparison.OrdinalIgnoreCase)
            && !rarity.TierOrder.SelectMany(tier => rarity.Species[tier]).Contains("CAMERUPT_1", StringComparer.OrdinalIgnoreCase),
            "La ficha del PC debe usar Camerupt Mega hembra sin incluir esa forma Mega en encuentros salvajes.");
        var venusaurFemaleCapture = CreateSpeciesCapture("venusaur-female", metadata.Get("VENUSAUR_female"));
        Assert(megaCatalog.GetFormsFor(venusaurFemaleCapture).Count > 0
            && catalog.ById["VENUSAUR_female"].CanMegaEvolve
            && megaCatalog.TryGetForm("VENUSAUR_1", out var megaVenusaur)
            && !megaVenusaur.EncounterNormal.Contains("_female", StringComparison.OrdinalIgnoreCase),
            "Venusaur hembra debe poder megaevolucionar con sprites Mega normales cuando no exista variante femenina.");

        foreach (var id in new[] { "MAWILE", "MAWILE_female", "VENUSAUR_female", "GARCHOMP_female" })
        {
            foreach (var shiny in new[] { false, true })
            foreach (var alpha in new[] { false, true })
            {
                var capture = CreateSpeciesCapture($"mega-{id}", metadata.Get(id), shiny, alpha);
                var forms = megaCatalog.GetFormsFor(capture);
                Assert(forms.Count > 0 && forms.All(form => megaCatalog.CanApplyForm(capture, form.FormId)),
                    $"La validación efectiva de Mega debe aceptar {id}, shiny={shiny}, Alfa={alpha}.");
                Assert(!megaCatalog.CanApplyForm(capture, "CHARIZARD_1") && capture.Id == id,
                    "La Mega debe conservar el ID/sexo y rechazar formas de otra especie.");
            }
        }

        Summary = $"Encounter gender selection: PASS ({catalog.FemaleEncounterVariants.Count} female sprite/fallback variants mapped; Pikachu forms unweighted; male/female Mega eligibility verified)";
    }

    private static PokemonEntry Select(PokemonEntry candidate, IReadOnlyList<CapturedPokemon> captures,
        PokemonCatalog catalog, PokemonSpeciesMetadataCatalog metadata, int seed) =>
        EncounterGenderSelector.Select(candidate, catalog, metadata, captures, new Random(seed));

    private static CapturedPokemon CreateCapture(string recordId, string speciesId, string form, bool shiny, bool alpha) => new()
    {
        RecordId = recordId,
        Id = speciesId.Equals("pikachu", StringComparison.OrdinalIgnoreCase) && form == "female" ? "PIKACHU_female" : "PIKACHU",
        SpeciesId = speciesId,
        Form = form,
        IsShiny = shiny,
        IsAlpha = alpha,
        CapturedAt = DateTimeOffset.UtcNow
    };

    private static CapturedPokemon CreateSpeciesCapture(string recordId, PokemonSpeciesMetadata metadata,
        bool shiny = true, bool alpha = true) => new()
    {
        RecordId = recordId,
        Id = metadata.Id,
        SpeciesId = metadata.SpeciesId,
        Form = metadata.Form,
        IsShiny = shiny,
        IsAlpha = alpha,
        CapturedAt = DateTimeOffset.UtcNow
    };

    private static void VerifyResolvedFemale(PokemonCatalog catalog, string id, string encounterNormal,
        string encounterShiny, string walkingNormal, string walkingShiny, string iconNormal, string iconShiny)
    {
        var entry = catalog.ById[id];
        Assert(Path.GetFileNameWithoutExtension(entry.EncounterNormal).Equals(encounterNormal, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(entry.EncounterShiny).Equals(encounterShiny, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(entry.WalkingNormal).Equals(walkingNormal, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(entry.WalkingShiny).Equals(walkingShiny, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(entry.IconNormal).Equals(iconNormal, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(entry.IconShiny).Equals(iconShiny, StringComparison.OrdinalIgnoreCase),
            $"Los recursos de {id} deben usar sus sprites femeninos y fallback independiente.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
