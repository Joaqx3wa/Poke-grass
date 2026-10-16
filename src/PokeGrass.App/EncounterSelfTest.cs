using System.Drawing;
using System.IO;
using System.Text.Json;

namespace PokeGrass;

internal static class EncounterSelfTest
{
    public static string SimulationSummary { get; private set; } = string.Empty;

    public static void Run()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "assets");
        var catalog = PokemonCatalog.Load(Path.Combine(assets, "data", "pokemon-catalog.json"));
        var megaEvolutions = MegaEvolutionCatalog.Load(assets, catalog);
        var rarity = RarityConfiguration.Load(Path.Combine(assets, "data", "encounter-rarity.json"));
        Assert(catalog.Entries.Count > 1000, "El catálogo debe conservar las formas completas.");
        Assert(rarity.Weights["common"] == 55 && rarity.Weights["uncommon"] == 28
            && rarity.Weights["rare"] == 12 && rarity.Weights["epic"] == 4 && rarity.Weights["legendary"] == 1,
            "Los porcentajes de rareza deben permanecer 55/28/12/4/1.");
        Assert(catalog.Entries.Count >= 1446 && catalog.Entries.Count(entry => entry.EncounterPoolEntry) == 1340,
            "El catálogo debe incluir las variantes femeninas resueltas sin alterar las 1.340 entradas ponderadas.");
        Assert(GardenSettings.AllowedScales.SequenceEqual(new[] { 1, 2, 3, 4 }),
            "Las opciones de escala deben permanecer en 1×, 2×, 3× y 4×.");
        var alphaRandom = new Random(0xA17A);
        const int alphaTrials = 100_000;
        var alphaHits = Enumerable.Range(0, alphaTrials).Count(_ => EncounterRules.RollAlpha(alphaRandom));
        Assert(alphaHits is >= 1800 and <= 2200,
            $"La tirada Alfa debe ser independiente y cercana al 2%; se obtuvo {alphaHits}/{alphaTrials}.");
        Assert(GardenWindow.EffectBleedX >= 24 && GardenWindow.EffectBleedY >= 24,
            "El lienzo transparente debe conservar margen suficiente para el signo y las hojas fuera del jardín.");
        foreach (var scale in GardenSettings.AllowedScales)
            Assert((128 + GardenWindow.EffectBleedX * 2) * scale == 176 * scale
                && (80 + GardenWindow.EffectBleedY * 2) * scale == 128 * scale,
                "El margen de efectos debe ampliar el host de forma proporcional, sin deformar el jardín.");
        rarity.ValidateComplete(catalog.Entries);
        Assert(rarity.IsCompleteFor(catalog.Entries), "Las rarezas deben asignar cada forma válida exactamente una vez.");
        Assert(rarity.TierOrder.All(tier => catalog.GetTier(tier, rarity).Count > 0), "Las cinco categorías deben tener Pokémon.");
        Assert(megaEvolutions.GetFormsFor(new CapturedPokemon
            { RecordId = "test", Id = "CHARIZARD", SpeciesId = "charizard", Form = "", IsShiny = false, CapturedAt = DateTimeOffset.UtcNow }).Count == 2,
            "Charizard debe ofrecer sus dos formas Mega explícitas.");
        Assert(catalog.ById.ContainsKey("SLOWBRO_1") && !catalog.ById.ContainsKey("SLOWBRO_2")
            && megaEvolutions.GetFormsFor(new CapturedPokemon
                { RecordId = "test-slowbro", Id = "SLOWBRO", SpeciesId = "slowbro", Form = "", IsShiny = false, CapturedAt = DateTimeOffset.UtcNow })
                .Any(form => form.FormId == "SLOWBRO_2"),
            "Slowbro_1 debe seguir siendo Galar capturable y Slowbro_2 debe estar disponible como Mega.");
        Assert(catalog.ById.ContainsKey("RAICHU_1")
            && !catalog.ById.ContainsKey("RAICHU_2") && !catalog.ById.ContainsKey("RAICHU_3")
            && megaEvolutions.GetFormsFor(new CapturedPokemon
                { RecordId = "test-raichu", Id = "RAICHU", SpeciesId = "raichu", Form = "", IsShiny = false, CapturedAt = DateTimeOffset.UtcNow })
                .Select(form => form.FormId).Order().SequenceEqual(new[] { "RAICHU_2", "RAICHU_3" }),
            "Raichu_1 debe seguir siendo Alola capturable y Raichu_2/_3 deben ser sus dos formas Mega.");
        Assert(catalog.ById["CHARIZARD"].CanMegaEvolve && !catalog.ById["RAICHU_1"].CanMegaEvolve,
            "CanMegaEvolve debe activarse solo en especies/formas declaradas, nunca por el sufijo.");
        Assert(megaEvolutions.TryGetForm("CHARIZARD_1", out var megaCharizard)
            && megaCharizard.EncounterNormal.StartsWith("pokemon/encounter/normal/", StringComparison.OrdinalIgnoreCase)
            && megaCharizard.EncounterShiny.StartsWith("pokemon/encounter/shiny/", StringComparison.OrdinalIgnoreCase),
            "Los retratos Mega de la ficha de PC deben cargar sprites de encounter, con variantes shiny.");
        foreach (var (baseId, megaId) in new[]
                 {
                     ("BANETTE", "BANETTE_1"), ("PIDGEOT", "PIDGEOT_1"), ("GENGAR", "GENGAR_1")
                 })
            Assert(megaEvolutions.GetFormsFor(new CapturedPokemon
                { RecordId = $"test-{baseId}", Id = baseId, SpeciesId = baseId.ToLowerInvariant(), Form = "", IsShiny = false, CapturedAt = DateTimeOffset.UtcNow })
                .Any(form => form.FormId == megaId), $"{megaId} debe estar registrado como Mega de {baseId}.");

        var excludedPath = Path.Combine(assets, "data", "pokemon-excluded.json");
        var megaPath = Path.Combine(assets, "data", "mega-form-exclusions.json");
        using var excludedDocument = JsonDocument.Parse(File.ReadAllText(excludedPath));
        using var megaDocument = JsonDocument.Parse(File.ReadAllText(megaPath));
        var mega = megaDocument.RootElement.GetProperty("ids").EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(!catalog.Entries.Any(entry => mega.Contains(entry.Id)), "Ninguna forma en la lista explícita Mega puede quedar en el catálogo.");
        var rarityIds = rarity.TierOrder.SelectMany(tier => rarity.Species[tier]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(!rarityIds.Overlaps(mega), "La clasificación no puede incluir IDs Mega explícitos.");
        var excludedIds = excludedDocument.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(!rarityIds.Overlaps(excludedIds), "La clasificación no puede incluir formas excluidas del catálogo.");
        Assert(catalog.ById.ContainsKey("TERAPAGOS_2")
            && catalog.ById["TERAPAGOS_2"].WalkingShiny.EndsWith("walking/shiny/TERAPAGOS_2.png", StringComparison.OrdinalIgnoreCase),
            "El sprite shiny de caminata de Terapagos_2 debe resolverse por su ID corregido.");
        Assert(catalog.ById.ContainsKey("CAMERUPT_1_female")
            && catalog.ById["CAMERUPT_1_female"].EncounterNormal.EndsWith("encounter/normal/CAMERUPT_1_female.png", StringComparison.OrdinalIgnoreCase)
            && catalog.ById["CAMERUPT_1_female"].EncounterShiny.EndsWith("encounter/shiny/CAMERUPT.png", StringComparison.OrdinalIgnoreCase),
            "Camerupt Mega hembra debe usar su encounter normal femenino y el shiny de Camerupt base.");
        foreach (var number in Enumerable.Range(1, 4))
        {
            var id = $"GENESECT_{number}";
            Assert(catalog.ById.TryGetValue(id, out var genesectForm)
                && genesectForm.IconNormal.EndsWith("pokemon/icons/normal/GENESECT.png", StringComparison.OrdinalIgnoreCase)
                && genesectForm.IconShiny.EndsWith("pokemon/icons/shiny/GENESECT.png", StringComparison.OrdinalIgnoreCase),
                $"{id} debe conservar sus sprites propios de encuentro/caminata y usar los iconos base de Genesect.");
        }
        Assert(excludedDocument.RootElement.GetArrayLength() > 0, "Debe existir un informe con exclusiones.");
        foreach (var (id, missing) in new[]
                 {
                     ("MEOWSTIC_female", new[] { "pokemon\\encounter\\normal", "pokemon\\encounter\\shiny" }),
                     ("TORCHIC_female", new[] { "pokemon\\encounter\\normal", "pokemon\\encounter\\shiny" })
                 })
        {
            Assert(excludedDocument.RootElement.EnumerateArray().Any(item =>
                    item.GetProperty("id").GetString()!.Equals(id, StringComparison.OrdinalIgnoreCase)
                    && item.GetProperty("missing").EnumerateArray().Select(value => value.GetString()!)
                        .SequenceEqual(missing, StringComparer.OrdinalIgnoreCase)),
                $"{id} debe seguir excluida por sus encounter femeninos exactos faltantes.");
            Assert(catalog.ById.TryGetValue(id, out var fallbackEntry)
                && catalog.ById.TryGetValue(id[..^"_female".Length], out var baseEntry)
                && fallbackEntry.EncounterNormal == baseEntry.EncounterNormal
                && fallbackEntry.EncounterShiny == baseEntry.EncounterShiny,
                $"{id} debe conservar su sexo y usar el encounter base cuando falta el sprite femenino.");
        }

        var relativeAssets = catalog.Entries.SelectMany(entry => new[] { entry.EncounterNormal, entry.EncounterShiny,
            entry.WalkingNormal, entry.WalkingShiny, entry.IconNormal, entry.IconShiny });
        Assert(relativeAssets.All(path => File.Exists(Path.Combine(assets, path.Replace('/', Path.DirectorySeparatorChar)))),
            "Todos los recursos citados por catálogo deben existir.");
        var overlayLayers = new[] { "Garden/OverlayCesped 3.png", "Garden/OverlayCesped2.png", "Garden/OverlayCesped1.png" };
        Assert(overlayLayers.All(path => File.Exists(Path.Combine(assets, path.Replace('/', Path.DirectorySeparatorChar)))),
            "Las tres capas originales de hierba deben estar empaquetadas.");
        Assert(overlayLayers.All(path => PngSize(Path.Combine(assets, path.Replace('/', Path.DirectorySeparatorChar))) == (128, 80)),
            "Las capas de hierba deben conservar la cuadrícula nativa de 128x80.");
        var expectedRows = new[] { (6, 21), (26, 41), (46, 61) };
        var actualRows = overlayLayers.Select(path => AlphaBounds(Path.Combine(assets, path.Replace('/', Path.DirectorySeparatorChar)))).OrderBy(bounds => bounds.Top).ToArray();
        Assert(actualRows.Select(bounds => (bounds.Top, bounds.Bottom)).SequenceEqual(expectedRows),
            "Los tres overlays deben conservar sus filas de hierba independientes y alineadas.");
        Assert(overlayLayers.All(path => HasTransparentCorner(Path.Combine(assets, path.Replace('/', Path.DirectorySeparatorChar)))),
            "Los tres overlays deben tener transparencia alfa real fuera de sus arbustos.");
        foreach (var entry in catalog.Entries)
            VerifyPokemonPlacement(assets, entry);
        VerifyPokemonShadowResources(assets);
        Assert(PngSize(Path.Combine(assets, "ui/effects/leaves.png")) == (1024, 896), "La cuadrícula de hojas debe ser 8x7 de 128px.");
        Assert(PngSize(Path.Combine(assets, "ui/effects/exclamation.png")) == (1440, 160), "La cuadrícula de exclamación debe ser 9x1 de 160px.");
        Assert(PngSize(Path.Combine(assets, "ui/effects/grass_rustle.png")) == (1600, 160), "La cuadrícula de hierba debe ser 10x1 de 160px.");
        Assert(PngSize(Path.Combine(assets, "ui/effects/stars.png")) == (416, 276), "El efecto shiny debe ser la extracción aprobada.");
        var starSheet = new Bitmap(Path.Combine(assets, "ui/effects/stars.png"));
        using (starSheet)
        {
            Assert(starSheet.GetPixel(0, 0).A == 0, "El sprite de estrellas debe conservar transparencia real.");
            Assert(starSheet.GetPixel(35, 40).A > 0, "El sprite de estrellas debe conservar los destellos.");
        }
        var cryPath = Directory.GetFiles(Path.Combine(assets, "sounds/pokemon"), "*.ogg").FirstOrDefault()
            ?? throw new InvalidDataException("No se encontró ningún grito OGG local.");
        var wav = EncounterAudio.DecodeToWav(cryPath);
        Assert(wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) && wav.AsSpan(8, 4).SequenceEqual("WAVE"u8), "El OGG debe decodificarse a WAV para Windows.");

        var random = new Random(5872);
        for (var i = 0; i < 10000; i++)
        {
            var interval = EncounterRules.NextIntervalSeconds(random);
            Assert(interval is >= 180 and <= 480, "El intervalo debe permanecer entre 3 y 8 minutos.");
        }

        VerifyRaritySelection(catalog, rarity);
        CaptureSystemSelfTest.Run(assets, catalog);
        SimulationSummary += Environment.NewLine + CaptureSystemSelfTest.Summary;
    }

    private static void VerifyRaritySelection(PokemonCatalog catalog, RarityConfiguration rarity)
    {
        var random = new Random(21613);
        var counts = rarity.TierOrder.ToDictionary(tier => tier, _ => 0, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 100000; i++)
        {
            var selected = catalog.Choose(rarity, random);
            counts[selected.Rarity]++;
            Assert(!string.IsNullOrWhiteSpace(selected.Entry.EncounterNormal), "Un encuentro normal debe seleccionar un sprite normal.");
            Assert(catalog.ById.ContainsKey(selected.Entry.Id), "La selección debe apuntar a una forma válida del catálogo.");
        }
        var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["common"] = 55000, ["uncommon"] = 28000, ["rare"] = 12000, ["epic"] = 4000, ["legendary"] = 1000
        };
        foreach (var tier in rarity.TierOrder)
        {
            var probability = rarity.Weights[tier] / 100d;
            var tolerance = Math.Ceiling(4 * Math.Sqrt(100000 * probability * (1 - probability)) + 2);
            Assert(Math.Abs(counts[tier] - expected[tier]) <= tolerance,
                $"La simulación real de {tier} ({counts[tier]}) debe aproximarse al peso acordado ({expected[tier]} de 100.000).");
        }

        var shinyRolls = 512000;
        var shinies = 0;
        random = new Random(512);
        for (var i = 0; i < shinyRolls; i++)
            if (EncounterRules.RollShiny(random)) shinies++;
        Assert(shinies is > 850 and < 1150, "La prueba estadística shiny debe aproximarse a 1/512.");
        SimulationSummary = $"Rareza, 100.000 selecciones: {string.Join(", ", rarity.TierOrder.Select(tier => $"{tier} {counts[tier]} ({counts[tier] / 1000d:F2}%)"))}. Shiny, {shinyRolls} tiradas: {shinies} ({shinyRolls / (double)shinies:F1}:1).";
    }

    private static (int Width, int Height) PngSize(string path)
    {
        using var image = Image.FromFile(path);
        return (image.Width, image.Height);
    }

    private static (int Left, int Top, int Right, int Bottom) AlphaBounds(string path)
    {
        using var image = new Bitmap(path);
        var left = image.Width;
        var top = image.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            if (image.GetPixel(x, y).A > 0)
            {
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
        return (left, top, right, bottom);
    }

    private static bool HasTransparentCorner(string path)
    {
        using var image = new Bitmap(path);
        return image.GetPixel(0, 0).A == 0;
    }

    private static void VerifyPokemonPlacement(string assets, PokemonEntry entry)
    {
        var id = entry.Id;
        var path = Path.Combine(assets, entry.EncounterNormal.Replace('/', Path.DirectorySeparatorChar));
        var bounds = AlphaBounds(path);
        Assert(bounds.Right >= bounds.Left && bounds.Bottom >= bounds.Top, $"{id} debe tener píxeles visibles.");
        using var image = new Bitmap(path);
        Assert(image.Size == new Size(PokemonPlacement.EncounterCanvasSize, PokemonPlacement.EncounterCanvasSize),
            $"{id} debe conservar el lienzo completo de 192×192.");
        var supportLeft = image.Width;
        var supportRight = -1;
        for (var y = Math.Max(bounds.Top, bounds.Bottom - 2); y <= bounds.Bottom; y++)
        for (var x = bounds.Left; x <= bounds.Right; x++)
            if (image.GetPixel(x, y).A >= 128)
            {
                supportLeft = Math.Min(supportLeft, x);
                supportRight = Math.Max(supportRight, x);
            }
        var supportX = supportRight >= supportLeft ? (supportLeft + supportRight + 1) / 2d : image.Width / 2d;
        var support = new SpriteSupport(supportX, bounds.Bottom + 1, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
            bounds.Left, bounds.Right);
        Assert(Math.Abs(PokemonPlacement.Scale - 35d / 92d) < 0.0000001,
            "La escala compartida debe conservar la proporción de Pikachu de la referencia aprobada.");
        var validFootY = PokemonPlacement.ChooseFootY(support, new[] { 22d, 42d, 62d });
        Assert(double.IsFinite(validFootY), $"{id} debe disponer de una fila de apoyo válida a escala fija.");
        var candidateFootYs = new[] { 22d, 42d, 62d };
        var hasContainedRow = candidateFootYs.Any(footY => footY >= PokemonPlacement.GreenTop + support.VisibleHeight * PokemonPlacement.Scale
            && footY <= PokemonPlacement.GreenBottom);
        Assert(validFootY >= PokemonPlacement.GreenTop && validFootY <= PokemonPlacement.GreenBottom,
            $"Los pies de {id} deben quedar dentro del área verde.");
        if (hasContainedRow)
            Assert(validFootY >= PokemonPlacement.GreenTop + support.VisibleHeight * PokemonPlacement.Scale - 0.001,
                $"El cuerpo visible de {id} debe comenzar dentro del área verde cuando quepa.");
        else
            Assert(validFootY == candidateFootYs.Max(),
                $"El cuerpo grande de {id} puede sobresalir hacia arriba, conservando el apoyo en la fila inferior.");
        var referenceLayout = PokemonPlacement.Calculate(image.Width, image.Height, support, 56, validFootY);
        Assert(Math.Abs(referenceLayout.Width - image.Width * PokemonPlacement.Scale) < 0.0001
            && Math.Abs(referenceLayout.Height - image.Height * PokemonPlacement.Scale) < 0.0001,
            $"{id} debe dibujarse con el lienzo completo y la escala compartida, sin normalizar el alfa.");
        Assert(Math.Abs((referenceLayout.Left + support.X * PokemonPlacement.Scale) - 56) < 0.0001
            && Math.Abs((referenceLayout.Top + support.Y * PokemonPlacement.Scale) - validFootY) < 0.0001,
            $"El punto de apoyo de {id} debe permanecer en la posición elegida.");
        foreach (var scale in new[] { 1, 2, 3, 4 })
        foreach (var x in new[] { 40d, 56, 72, 88 })
        foreach (var footY in new[] { 22d, 42, 62 })
        {
            var layout = PokemonPlacement.Calculate(image.Width, image.Height, support, x, footY);
            Assert(x >= PokemonPlacement.GreenLeft && x <= PokemonPlacement.GreenRight,
                $"El apoyo de {id} debe permanecer en el área verde a escala {scale}×.");
            Assert(Math.Abs(layout.Width - referenceLayout.Width) < 0.0001
                && Math.Abs(layout.Height - referenceLayout.Height) < 0.0001,
                $"La posición no debe cambiar el tamaño de {id} a escala {scale}×.");
            Assert(Math.Abs(layout.FootY - footY) < 0.0001,
                $"La coordenada vertical de apoyo de {id} debe poder variar sin cambiar su tamaño.");
            Assert(Math.Abs(layout.Width / layout.Height - 1) < 0.0001,
                $"{id} debe conservar sus proporciones en escala {scale}×.");
            Assert(Math.Abs(layout.Width * scale / (image.Width * scale) - PokemonPlacement.Scale) < 0.0001,
                $"La proporción Pokémon/jardín debe mantenerse en escala {scale}×.");
        }
    }

    private static void VerifyPokemonShadowResources(string assets)
    {
        var shadowPaths = new[]
        {
            Path.Combine(assets, "pokemon", "pokemons_shadow", "1.png"),
            Path.Combine(assets, "pokemon", "pokemons_shadow", "2.png"),
            Path.Combine(assets, "pokemon", "pokemons_shadow", "3.png")
        };
        var dimensions = new[] { (72, 16), (100, 20), (136, 24) };
        Assert(shadowPaths.All(File.Exists), "Las tres sombras originales deben estar presentes.");
        for (var i = 0; i < shadowPaths.Length; i++)
        {
            Assert(PngSize(shadowPaths[i]) == dimensions[i], $"La sombra {i + 1} debe conservar sus dimensiones originales.");
            Assert(HasTransparentCorner(shadowPaths[i]), $"La sombra {i + 1} debe tener transparencia alfa real.");
        }

        var shadowWidths = dimensions.Select(item => item.Item1).ToArray();
        foreach (var (id, expectedIndex) in new[] { ("JOLTIK", 0), ("PIKACHU", 0), ("WAILORD", 2) })
        {
            var spritePath = Path.Combine(assets, "pokemon", "encounter", "normal", id + ".png");
            var bounds = AlphaBounds(spritePath);
            var visibleWidth = bounds.Right - bounds.Left + 1;
            var selectedIndex = PokemonPlacement.SelectShadowVariant(visibleWidth, shadowWidths);
            Assert(selectedIndex == expectedIndex, $"La sombra elegida para {id} debe ser la más cercana al ancho visible del sprite.");
            var supportLeft = bounds.Right;
            var supportRight = bounds.Left;
            var lowerLeft = bounds.Right;
            var lowerRight = bounds.Left;
            using (var image = new Bitmap(spritePath))
            {
                for (var y = Math.Max(bounds.Top, bounds.Bottom - 2); y <= bounds.Bottom; y++)
                for (var x = bounds.Left; x <= bounds.Right; x++)
                    if (image.GetPixel(x, y).A >= 128)
                    {
                        supportLeft = Math.Min(supportLeft, x);
                        supportRight = Math.Max(supportRight, x);
                    }
                for (var y = Math.Max(bounds.Top, bounds.Bottom - 24); y <= bounds.Bottom; y++)
                for (var x = bounds.Left; x <= bounds.Right; x++)
                    if (image.GetPixel(x, y).A >= 128)
                    {
                        lowerLeft = Math.Min(lowerLeft, x);
                        lowerRight = Math.Max(lowerRight, x);
                    }
            }
            var anchorX = supportRight >= supportLeft ? (supportLeft + supportRight + 1) / 2d : (bounds.Left + bounds.Right + 1) / 2d;
            var footY = PokemonPlacement.ChooseFootY(
                new SpriteSupport(anchorX, bounds.Bottom + 1, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, lowerLeft, lowerRight),
                new[] { 22d, 42, 62 });
            var (shadowWidth, shadowHeight) = dimensions[selectedIndex];
            var pokemonLayout = PokemonPlacement.Calculate(192, 192,
                new SpriteSupport(anchorX, bounds.Bottom + 1, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, lowerLeft, lowerRight),
                56, footY);
            var shadowCenterX = pokemonLayout.Left + (bounds.Left + bounds.Right + 1) / 2d * PokemonPlacement.Scale;
            var visibleBottomY = pokemonLayout.Top + (bounds.Bottom + 1) * PokemonPlacement.Scale;
            var layout = PokemonPlacement.CalculateShadow(shadowWidth, shadowHeight, shadowCenterX, visibleBottomY);
            Assert(Math.Abs(layout.Left + layout.Width / 2 - shadowCenterX) < 0.0001,
                $"La sombra de {id} debe centrarse bajo el cuerpo visible, no bajo el lienzo ni el punto de apoyo lateral.");
            Assert(layout.Left >= PokemonPlacement.GreenLeft - 0.001
                && layout.Left + layout.Width <= PokemonPlacement.GreenRight + 0.001
                && layout.Top >= PokemonPlacement.GreenTop - 0.001
                && layout.Top + layout.Height <= PokemonPlacement.GreenBottom + 0.001,
                $"La sombra de {id} debe quedar dentro del área verde.");
            Assert(Math.Abs(layout.Width / layout.Height - (double)shadowWidth / shadowHeight) < 0.0001,
                $"La sombra de {id} debe conservar sus proporciones.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
