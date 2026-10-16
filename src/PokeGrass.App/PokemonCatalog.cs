using System.IO;
using System.Text.Json;

namespace PokeGrass;

public sealed class PokemonEntry
{
    public required string Id { get; init; }
    public required string EncounterNormal { get; init; }
    public required string EncounterShiny { get; init; }
    public required string WalkingNormal { get; init; }
    public required string WalkingShiny { get; init; }
    public required string IconNormal { get; init; }
    public required string IconShiny { get; init; }
    public bool EncounterPoolEntry { get; init; } = true;
    public bool CanMegaEvolve { get; internal set; }
}

public sealed class PokemonCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<PokemonEntry> Entries { get; }
    public IReadOnlyDictionary<string, PokemonEntry> ById { get; }
    internal IReadOnlyDictionary<string, PokemonEntry> FemaleEncounterVariants { get; }

    // Resolve legacy captured IDs to the base species assets without registering
    // them in ById or any encounter pool.
    internal PokemonEntry GetForCapture(string id)
    {
        if (ById.TryGetValue(id, out var entry)) return entry;
        if (id.EndsWith("_female", StringComparison.OrdinalIgnoreCase)
            && ById.TryGetValue(id[..^"_female".Length], out var baseEntry))
            return new PokemonEntry
            {
                Id = id,
                EncounterNormal = baseEntry.EncounterNormal,
                EncounterShiny = baseEntry.EncounterShiny,
                WalkingNormal = baseEntry.WalkingNormal,
                WalkingShiny = baseEntry.WalkingShiny,
                IconNormal = baseEntry.IconNormal,
                IconShiny = baseEntry.IconShiny,
                EncounterPoolEntry = false,
                CanMegaEvolve = baseEntry.CanMegaEvolve
            };
        throw new KeyNotFoundException($"No hay recursos Pokémon para {id}.");
    }

    private PokemonCatalog(IReadOnlyList<PokemonEntry> entries)
    {
        Entries = entries;
        var byId = entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        var femaleVariants = new Dictionary<string, PokemonEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(entry => entry.Id.EndsWith("_female", StringComparison.OrdinalIgnoreCase)))
        {
            var baseId = entry.Id[..^"_female".Length];
            if (byId.TryGetValue(baseId, out var baseEntry)) femaleVariants[baseEntry.Id] = entry;
        }

        ById = byId;
        FemaleEncounterVariants = femaleVariants;
    }

    public static PokemonCatalog Load(string path)
    {
        var entries = JsonSerializer.Deserialize<List<PokemonEntry>>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("El catálogo Pokémon está vacío o no es válido.");
        if (entries.Count == 0 || entries.Any(entry => string.IsNullOrWhiteSpace(entry.Id)))
            throw new InvalidDataException("El catálogo no contiene IDs válidos.");
        var duplicate = entries.GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"ID duplicado en catálogo: {duplicate.Key}");
        var dataDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var assetsRoot = Directory.GetParent(dataDirectory)?.FullName
            ?? throw new InvalidDataException("No se pudo determinar la raíz de assets para el catálogo Pokémon.");
        var resolver = new PokemonAssetResolver(assetsRoot);
        var resolvedEntries = entries.Select(resolver.Resolve).ToArray();
        var megaPath = Path.Combine(dataDirectory, "mega-evolutions.json");
        var megaIds = File.Exists(megaPath)
            ? (JsonSerializer.Deserialize<List<MegaEvolutionDefinition>>(File.ReadAllText(megaPath), JsonOptions)
                ?? []).Select(form => form.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        resolvedEntries = AddFemaleEncounterFallbacks(resolvedEntries,
            Path.Combine(dataDirectory, "pokemon-species-metadata.json"), megaIds);
        return new PokemonCatalog(resolvedEntries);
    }

    private static PokemonEntry[] AddFemaleEncounterFallbacks(PokemonEntry[] entries, string metadataPath,
        IReadOnlySet<string> megaIds)
    {
        if (!File.Exists(metadataPath)) return entries;
        var metadata = JsonSerializer.Deserialize<List<PokemonSpeciesMetadata>>(File.ReadAllText(metadataPath), JsonOptions)
            ?? throw new InvalidDataException("El catálogo de metadatos de especie está vacío o no es válido.");
        var metadataById = metadata.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        var entriesById = entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        var fallbacks = new List<PokemonEntry>();

        foreach (var baseEntry in entries.Where(entry => !entry.Id.EndsWith("_female", StringComparison.OrdinalIgnoreCase)
                     && !megaIds.Contains(entry.Id)))
        {
            var femaleId = $"{baseEntry.Id}_female";
            if (entriesById.ContainsKey(femaleId)
                || !metadataById.TryGetValue(baseEntry.Id, out var baseMetadata)
                || !string.IsNullOrWhiteSpace(baseMetadata.Sex)
                || baseMetadata.SpeciesId.StartsWith("nidoran", StringComparison.OrdinalIgnoreCase))
                continue;

            // Blank sex metadata means the species/form supports either sex. Curated
            // male-only, female-only and genderless entries have explicit Sex values.
            fallbacks.Add(new PokemonEntry
            {
                Id = femaleId,
                EncounterNormal = baseEntry.EncounterNormal,
                EncounterShiny = baseEntry.EncounterShiny,
                WalkingNormal = baseEntry.WalkingNormal,
                WalkingShiny = baseEntry.WalkingShiny,
                IconNormal = baseEntry.IconNormal,
                IconShiny = baseEntry.IconShiny,
                EncounterPoolEntry = false,
                CanMegaEvolve = baseEntry.CanMegaEvolve
            });
        }

        return [.. entries, .. fallbacks];
    }

    public IReadOnlyList<PokemonEntry> GetTier(string rarity, RarityConfiguration configuration)
    {
        if (!configuration.Species.TryGetValue(rarity, out var ids))
            return Array.Empty<PokemonEntry>();
        return ids.Where(ById.ContainsKey).Select(id => ById[id]).ToArray();
    }

    public (string Rarity, PokemonEntry Entry) Choose(RarityConfiguration configuration, Random random)
    {
        configuration.ValidateComplete(Entries);
        var roll = random.Next(1, 101);
        var selectedTier = configuration.TierOrder.First(tier => roll <= configuration.CumulativeWeightThrough(tier));
        var pool = GetTier(selectedTier, configuration);
        return (selectedTier, pool[random.Next(pool.Count)]);
    }
}

/// <summary>Single runtime source of truth for catalog validation and sprite paths.</summary>
internal sealed class PokemonAssetResolver
{
    private static readonly (string Property, string Directory, bool FemaleFallback, bool GenesectIconFallback)[] Assets =
    [
        (nameof(PokemonEntry.EncounterNormal), "pokemon/encounter/normal", false, false),
        (nameof(PokemonEntry.EncounterShiny), "pokemon/encounter/shiny", false, false),
        (nameof(PokemonEntry.WalkingNormal), "pokemon/walking/normal", true, false),
        (nameof(PokemonEntry.WalkingShiny), "pokemon/walking/shiny", true, false),
        (nameof(PokemonEntry.IconNormal), "pokemon/icons/normal", true, true),
        (nameof(PokemonEntry.IconShiny), "pokemon/icons/shiny", true, true)
    ];

    private static readonly HashSet<string> GenesectDriveForms =
        ["GENESECT_1", "GENESECT_2", "GENESECT_3", "GENESECT_4"];

    private static readonly Dictionary<string, string> FemaleEncounterShinyFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CAMERUPT_1_female"] = "CAMERUPT"
    };

    private readonly Dictionary<string, Dictionary<string, string>> _filesByDirectory = new(StringComparer.OrdinalIgnoreCase);

    internal PokemonAssetResolver(string assetsRoot)
    {
        foreach (var directoryPath in Assets.Select(asset => asset.Directory).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var absoluteDirectory = Path.Combine(assetsRoot, directoryPath.Replace('/', Path.DirectorySeparatorChar));
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(absoluteDirectory))
            {
                foreach (var path in Directory.EnumerateFiles(absoluteDirectory)
                             .Where(candidate => Path.GetExtension(candidate).Equals(".png", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
                {
                    var id = Path.GetFileNameWithoutExtension(path);
                    if (!files.ContainsKey(id)) files.Add(id, Path.GetRelativePath(assetsRoot, path).Replace('\\', '/'));
                }
            }
            _filesByDirectory.Add(directoryPath, files);
        }
    }

    internal PokemonEntry Resolve(PokemonEntry entry)
    {
        var isFemale = entry.Id.EndsWith("_female", StringComparison.OrdinalIgnoreCase);
        var baseId = isFemale ? entry.Id[..^"_female".Length] : entry.Id;
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var asset in Assets)
        {
            var path = FindAsset(asset.Directory, entry.Id);
            if (path is null && isFemale && asset.FemaleFallback)
                path = FindAsset(asset.Directory, baseId);
            if (path is null && asset.GenesectIconFallback && GenesectDriveForms.Contains(entry.Id))
                path = FindAsset(asset.Directory, "GENESECT");
            if (path is null && asset.Property == nameof(PokemonEntry.EncounterShiny)
                && FemaleEncounterShinyFallbacks.TryGetValue(entry.Id, out var shinyFallbackId))
                path = FindAsset(asset.Directory, shinyFallbackId);
            if (path is null) missing.Add(asset.Directory);
            else resolved[asset.Property] = path;
        }

        if (missing.Count > 0)
            throw new InvalidDataException($"El catálogo incluye {entry.Id}, pero faltan recursos válidos: {string.Join(", ", missing)}.");

        return new PokemonEntry
        {
            Id = entry.Id,
            EncounterNormal = resolved[nameof(PokemonEntry.EncounterNormal)],
            EncounterShiny = resolved[nameof(PokemonEntry.EncounterShiny)],
            WalkingNormal = resolved[nameof(PokemonEntry.WalkingNormal)],
            WalkingShiny = resolved[nameof(PokemonEntry.WalkingShiny)],
            IconNormal = resolved[nameof(PokemonEntry.IconNormal)],
            IconShiny = resolved[nameof(PokemonEntry.IconShiny)],
            EncounterPoolEntry = entry.EncounterPoolEntry,
            CanMegaEvolve = entry.CanMegaEvolve
        };
    }

    private string? FindAsset(string relativeDirectory, string id) =>
        _filesByDirectory[relativeDirectory].TryGetValue(id, out var relativePath) ? relativePath : null;
}

public sealed class RarityConfiguration
{
    private static readonly string[] Order = ["common", "uncommon", "rare", "epic", "legendary"];

    public Dictionary<string, int> Weights { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Species { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> TierOrder => Order;

    public static RarityConfiguration Load(string path)
    {
        var result = JsonSerializer.Deserialize<RarityConfiguration>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result is null)
            throw new InvalidDataException("El archivo de rarezas no es válido.");
        result.ValidateWeights();
        return result;
    }

    public bool IsCompleteFor(IReadOnlyList<PokemonEntry> catalog)
    {
        try { ValidateComplete(catalog); return true; }
        catch (InvalidDataException) { return false; }
    }

    public void ValidateComplete(IReadOnlyList<PokemonEntry> catalog)
    {
        ValidateWeights();
        var catalogIds = catalog.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var weightedIds = catalog.Where(entry => entry.EncounterPoolEntry)
            .Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tier in Order)
        {
            if (!Species.TryGetValue(tier, out var members) || members.Count == 0)
                throw new InvalidDataException($"La categoría '{tier}' no tiene especies asignadas.");
            foreach (var id in members)
            {
                if (!catalogIds.Contains(id))
                    throw new InvalidDataException($"'{id}' no pertenece al catálogo válido.");
                if (!ids.Add(id))
                    throw new InvalidDataException($"'{id}' aparece en más de una categoría de rareza.");
            }
        }
        var unclassified = weightedIds.Except(ids, StringComparer.OrdinalIgnoreCase).ToArray();
        var unexpectedlyClassified = ids.Except(weightedIds, StringComparer.OrdinalIgnoreCase).ToArray();
        if (unclassified.Length > 0 || unexpectedlyClassified.Length > 0)
            throw new InvalidDataException($"La tabla de rareza debe incluir solo las formas ponderadas; faltan {unclassified.Length} e incluye {unexpectedlyClassified.Length} formas sin peso.");
    }

    public int CumulativeWeightThrough(string tier)
    {
        var total = 0;
        foreach (var item in Order)
        {
            total += Weights[item];
            if (item.Equals(tier, StringComparison.OrdinalIgnoreCase)) return total;
        }
        return total;
    }

    private void ValidateWeights()
    {
        if (Order.Any(tier => !Weights.TryGetValue(tier, out var weight) || weight < 0)
            || Order.Sum(tier => Weights[tier]) != 100)
            throw new InvalidDataException("Los pesos deben sumar 100 y definir las cinco rarezas acordadas.");
    }
}
