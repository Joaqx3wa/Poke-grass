using System.IO;
using System.Text.Json;

namespace PokeGrass;

internal sealed class MegaEvolutionDefinition
{
    public required string BasePokemonId { get; init; }
    public required string FormId { get; init; }
    public required string DisplayName { get; init; }
    public required string EncounterNormal { get; init; }
    public required string EncounterShiny { get; init; }
    public required string WalkingNormal { get; init; }
    public required string WalkingShiny { get; init; }
    public required string IconNormal { get; init; }
    public required string IconShiny { get; init; }
}

internal sealed class MegaEvolutionCatalog
{
    internal static MegaEvolutionCatalog Empty { get; } = new(Array.Empty<MegaEvolutionDefinition>());
    private readonly Dictionary<string, MegaEvolutionDefinition> _byForm;
    private readonly Dictionary<string, IReadOnlyList<MegaEvolutionDefinition>> _byBase;
    private readonly Dictionary<string, PokemonEntry> _entries;
    private readonly IReadOnlyDictionary<string, PokemonEntry> _baseEntries;

    private MegaEvolutionCatalog(IEnumerable<MegaEvolutionDefinition> forms, PokemonCatalog? catalog = null)
    {
        var array = forms.ToArray();
        _byForm = array.ToDictionary(form => form.FormId, StringComparer.OrdinalIgnoreCase);
        _byBase = array.GroupBy(form => form.BasePokemonId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MegaEvolutionDefinition>)group.ToArray(), StringComparer.OrdinalIgnoreCase);
        _entries = array.ToDictionary(form => form.FormId, form => new PokemonEntry
        {
            Id = form.FormId,
            EncounterNormal = form.EncounterNormal,
            EncounterShiny = form.EncounterShiny,
            WalkingNormal = form.WalkingNormal,
            WalkingShiny = form.WalkingShiny,
            IconNormal = form.IconNormal,
            IconShiny = form.IconShiny
        }, StringComparer.OrdinalIgnoreCase);
        _baseEntries = catalog?.ById ?? new Dictionary<string, PokemonEntry>(StringComparer.OrdinalIgnoreCase);
    }

    internal static MegaEvolutionCatalog Load(string assetsRoot, PokemonCatalog catalog)
    {
        var dataPath = Path.Combine(assetsRoot, "data", "mega-evolutions.json");
        var forms = JsonSerializer.Deserialize<List<MegaEvolutionDefinition>>(File.ReadAllText(dataPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("La tabla de megaevoluciones no es válida.");
        var declaredMegaIds = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetsRoot, "data", "mega-form-exclusions.json")));
        using (declaredMegaIds)
        {
            var declared = declaredMegaIds.RootElement.GetProperty("ids").EnumerateArray()
                .Select(value => value.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var form in forms)
            {
                if (string.IsNullOrWhiteSpace(form.BasePokemonId) || string.IsNullOrWhiteSpace(form.FormId)
                    || string.IsNullOrWhiteSpace(form.DisplayName))
                    throw new InvalidDataException("La tabla contiene una forma Mega incompleta.");
                if (!catalog.ById.ContainsKey(form.BasePokemonId))
                    throw new InvalidDataException($"La especie base '{form.BasePokemonId}' no existe en el catálogo.");
                if (!declared.Contains(form.FormId))
                    throw new InvalidDataException($"'{form.FormId}' no está identificada explícitamente como Mega.");
                foreach (var relative in new[] { form.EncounterNormal, form.EncounterShiny,
                    form.WalkingNormal, form.WalkingShiny, form.IconNormal, form.IconShiny })
                    if (!File.Exists(Path.Combine(assetsRoot, relative.Replace('/', Path.DirectorySeparatorChar))))
                        throw new InvalidDataException($"Falta un sprite requerido para {form.DisplayName}: {relative}");
            }
        }
        if (forms.Select(form => form.FormId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != forms.Count)
            throw new InvalidDataException("La tabla contiene formas Mega duplicadas.");

        foreach (var baseId in forms.Select(form => form.BasePokemonId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            catalog.ById[baseId].CanMegaEvolve = true;
            if (catalog.FemaleEncounterVariants.TryGetValue(baseId, out var femaleEntry))
                femaleEntry.CanMegaEvolve = true;
        }
        return new MegaEvolutionCatalog(forms, catalog);
    }

    internal IReadOnlyList<MegaEvolutionDefinition> GetFormsFor(CapturedPokemon capture)
    {
        var baseId = capture.Id.EndsWith("_female", StringComparison.OrdinalIgnoreCase)
            ? capture.Id[..^"_female".Length]
            : capture.Id;
        return _byBase.TryGetValue(baseId, out var forms) ? forms : Array.Empty<MegaEvolutionDefinition>();
    }

    internal int GetCaptureLimit(string basePokemonId) => Math.Max(1,
        _byBase.TryGetValue(basePokemonId, out var forms) ? forms.Count : 0);

    internal bool CanApplyForm(CapturedPokemon capture, string formId) => GetFormsFor(capture)
        .Any(form => form.FormId.Equals(formId, StringComparison.OrdinalIgnoreCase));

    internal bool TryGetForm(string formId, out MegaEvolutionDefinition form) => _byForm.TryGetValue(formId, out form!);

    internal string GetBasePokemonIdForForm(string formId) => _byForm.TryGetValue(formId, out var form)
        ? form.BasePokemonId
        : throw new KeyNotFoundException($"No existe la forma Mega '{formId}'.");

    internal PokemonEntry GetEntry(string formId) => _entries.TryGetValue(formId, out var entry)
        ? entry : throw new KeyNotFoundException($"No existe la forma Mega '{formId}'.");

    internal PokemonEntry GetBaseEntryForForm(string formId) => _byForm.TryGetValue(formId, out var form)
        && _baseEntries.TryGetValue(form.BasePokemonId, out var entry)
            ? entry : throw new KeyNotFoundException($"No existe la forma base para '{formId}'.");

    internal static string GetDetailsEncounterPath(MegaEvolutionDefinition form, bool isShiny, bool isFemale,
        string assetsRoot)
    {
        var relative = isShiny ? form.EncounterShiny : form.EncounterNormal;
        if (!isFemale) return relative;
        var directory = Path.GetDirectoryName(relative.Replace('/', Path.DirectorySeparatorChar));
        var femaleRelative = Path.Combine(directory ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(relative)}_female{Path.GetExtension(relative)}");
        return File.Exists(Path.Combine(assetsRoot, femaleRelative)) ? femaleRelative.Replace('\\', '/') : relative;
    }
}
