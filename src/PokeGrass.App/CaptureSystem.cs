using System.IO;
using System.Text.Json;

namespace PokeGrass;

internal enum CaptureBall
{
    Poke,
    Great,
    Ultra,
    Master
}

internal enum CaptureBerry
{
    Razz,
    SilverRazz,
    GoldenRazz,
    Nanab,
    SilverNanab,
    GoldenNanab
}

internal sealed class PokemonSpeciesMetadata
{
    public required string Id { get; init; }
    public required string SpeciesId { get; init; }
    public required int PokedexNumber { get; init; }
    public required string DisplayName { get; init; }
    public required string Form { get; init; }
    public required int CaptureRate { get; init; }
    public required List<string> Types { get; init; }
    public required string TypeSourcePokemon { get; init; }
    public string Sex { get; init; } = string.Empty;
}

internal static class PokemonSex
{
    internal static string Get(PokemonSpeciesMetadata metadata)
    {
        if (metadata.Sex.Equals("male", StringComparison.OrdinalIgnoreCase)
            || metadata.Sex.Equals("female", StringComparison.OrdinalIgnoreCase)
            || metadata.Sex.Equals("genderless", StringComparison.OrdinalIgnoreCase))
            return metadata.Sex.ToLowerInvariant();
        if (metadata.Form.Equals("female", StringComparison.OrdinalIgnoreCase)
            || metadata.Form.StartsWith("female_", StringComparison.OrdinalIgnoreCase)
            || metadata.Form.EndsWith("_female", StringComparison.OrdinalIgnoreCase)) return "female";
        // Numbered local forms keep the species' usual sex. Explicit metadata
        // still marks single-sex and genderless species before this fallback.
        return string.IsNullOrWhiteSpace(metadata.Form) || int.TryParse(metadata.Form, out _) ? "male" : string.Empty;
    }

    internal static string GetDisplayName(PokemonSpeciesMetadata metadata) =>
        Get(metadata).Equals("female", StringComparison.OrdinalIgnoreCase)
            && !metadata.DisplayName.EndsWith(" F", StringComparison.OrdinalIgnoreCase)
            ? $"{metadata.DisplayName} F" : metadata.DisplayName;
}

internal sealed class PokemonSpeciesMetadataCatalog
{
    private readonly Dictionary<string, PokemonSpeciesMetadata> _byId;
    internal IReadOnlyCollection<PokemonSpeciesMetadata> Entries => _byId.Values;

    private PokemonSpeciesMetadataCatalog(IEnumerable<PokemonSpeciesMetadata> entries) =>
        _byId = entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);

    public static PokemonSpeciesMetadataCatalog Load(string path, PokemonCatalog catalog)
    {
        var entries = JsonSerializer.Deserialize<List<PokemonSpeciesMetadata>>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("El catálogo de metadatos de especie está vacío o no es válido.");
        var result = new PokemonSpeciesMetadataCatalog(entries);
        foreach (var (baseId, femaleEntry) in catalog.FemaleEncounterVariants)
        {
            if (result._byId.ContainsKey(femaleEntry.Id)
                || !result._byId.TryGetValue(baseId, out var baseMetadata)) continue;

            result._byId[femaleEntry.Id] = new PokemonSpeciesMetadata
            {
                Id = femaleEntry.Id,
                SpeciesId = baseMetadata.SpeciesId,
                PokedexNumber = baseMetadata.PokedexNumber,
                DisplayName = baseMetadata.DisplayName,
                Form = string.IsNullOrWhiteSpace(baseMetadata.Form) ? "female" : $"female_{baseMetadata.Form}",
                Sex = "female",
                CaptureRate = baseMetadata.CaptureRate,
                Types = baseMetadata.Types.ToList(),
                TypeSourcePokemon = baseMetadata.TypeSourcePokemon
            };
        }

        foreach (var entry in catalog.ById.Values)
        {
            if (!result._byId.TryGetValue(entry.Id, out var metadata))
                throw new InvalidDataException($"Faltan metadatos de captura para {entry.Id}.");
            if (metadata.PokedexNumber <= 0 || metadata.CaptureRate is < 1 or > 255)
                throw new InvalidDataException($"Metadatos inválidos para {entry.Id}.");
            if (metadata.Types is null || metadata.Types.Count is < 1 or > 2
                || metadata.Types.Distinct(StringComparer.OrdinalIgnoreCase).Count() != metadata.Types.Count)
                throw new InvalidDataException($"Tipos Pokémon inválidos para {entry.Id}.");
            foreach (var type in metadata.Types)
            {
                var typeAsset = Path.Combine(AppContext.BaseDirectory, "assets", "ui", "types_pokemon", $"{type}.png");
                if (!File.Exists(typeAsset))
                    throw new InvalidDataException($"Falta el icono del tipo '{type}' para {entry.Id}.");
            }
            if (string.IsNullOrWhiteSpace(metadata.TypeSourcePokemon))
                throw new InvalidDataException($"Falta el origen de tipos para {entry.Id}.");
        }
        if (result._byId.Count != catalog.ById.Count)
            throw new InvalidDataException("El catálogo de metadatos contiene IDs que no están en el catálogo Pokémon.");
        return result;
    }

    public PokemonSpeciesMetadata Get(string id) => _byId.TryGetValue(id, out var value)
        ? value
        : throw new KeyNotFoundException($"No hay metadatos para {id}.");

    // Older saves may contain a female-suffixed capture even when that variant
    // was later removed from the curated catalog. Resolve it for saved records
    // only; this does not add the variant to encounter selection.
    internal PokemonSpeciesMetadata GetForCapture(string id)
    {
        if (_byId.TryGetValue(id, out var value)) return value;
        if (!id.EndsWith("_female", StringComparison.OrdinalIgnoreCase)) return Get(id);

        var baseId = id[..^"_female".Length];
        if (!_byId.TryGetValue(baseId, out var baseMetadata)) return Get(id);
        return new PokemonSpeciesMetadata
        {
            Id = id,
            SpeciesId = baseMetadata.SpeciesId,
            PokedexNumber = baseMetadata.PokedexNumber,
            DisplayName = baseMetadata.DisplayName,
            Form = string.IsNullOrWhiteSpace(baseMetadata.Form) ? "female" : $"female_{baseMetadata.Form}",
            // Preserve curated single-sex/genderless metadata. If sex is not
            // curated, the legacy suffix is the saved record's female identity.
            Sex = string.IsNullOrWhiteSpace(baseMetadata.Sex) ? "female" : baseMetadata.Sex,
            CaptureRate = baseMetadata.CaptureRate,
            Types = baseMetadata.Types.ToList(),
            TypeSourcePokemon = baseMetadata.TypeSourcePokemon
        };
    }

    internal PokemonSpeciesMetadata? FindBaseForm(string speciesId) => _byId.Values.FirstOrDefault(entry =>
        entry.SpeciesId.Equals(speciesId, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(entry.Form));

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

internal static class EncounterGenderSelector
{
    internal static PokemonEntry Select(PokemonEntry candidate, PokemonCatalog catalog,
        PokemonSpeciesMetadataCatalog metadata, IReadOnlyList<CapturedPokemon> captures, Random random)
    {
        var candidateMetadata = metadata.Get(candidate.Id);
        if (candidateMetadata.SpeciesId.Equals("pikachu", StringComparison.OrdinalIgnoreCase))
            return SelectPikachuForm(catalog, metadata, captures, random);

        var baseId = candidate.Id.EndsWith("_female", StringComparison.OrdinalIgnoreCase)
            ? candidate.Id[..^"_female".Length]
            : candidate.Id;
        if (!catalog.ById.TryGetValue(baseId, out var baseEntry)
            || !catalog.FemaleEncounterVariants.TryGetValue(baseEntry.Id, out var femaleEntry)
            || !PokemonSex.Get(metadata.Get(femaleEntry.Id)).Equals("female", StringComparison.OrdinalIgnoreCase))
            return candidate;

        var baseMetadata = metadata.Get(baseEntry.Id);

        var hasFemale = HasCapturedSex(captures, metadata, baseMetadata.SpeciesId, "female");
        var hasMale = HasCapturedSex(captures, metadata, baseMetadata.SpeciesId, "male");

        if (hasMale && !hasFemale) return femaleEntry;
        if (hasFemale && !hasMale) return baseEntry;
        return random.Next(2) == 0 ? baseEntry : femaleEntry;
    }

    private static PokemonEntry SelectPikachuForm(PokemonCatalog catalog, PokemonSpeciesMetadataCatalog metadata,
        IReadOnlyList<CapturedPokemon> captures, Random random)
    {
        var variants = catalog.ById.Values
            .Where(entry => metadata.Get(entry.Id).SpeciesId.Equals("pikachu", StringComparison.OrdinalIgnoreCase))
            .Select(entry => (Entry: entry, Sex: PokemonSex.Get(metadata.Get(entry.Id))))
            .Where(item => item.Sex is "male" or "female")
            .ToArray();
        if (variants.Length == 0) return catalog.ById["PIKACHU"];

        var hasFemale = HasCapturedSex(captures, metadata, "pikachu", "female");
        var hasMale = HasCapturedSex(captures, metadata, "pikachu", "male");
        var preferredSex = hasMale && !hasFemale ? "female" : hasFemale && !hasMale ? "male" : null;
        var choices = preferredSex is null ? variants : variants.Where(item => item.Sex == preferredSex).ToArray();
        return choices.Length == 0 ? variants[random.Next(variants.Length)].Entry : choices[random.Next(choices.Length)].Entry;
    }

    private static bool HasCapturedSex(IReadOnlyList<CapturedPokemon> captures,
        PokemonSpeciesMetadataCatalog metadata, string speciesId, string sex) => captures.Any(capture =>
        capture.SpeciesId.Equals(speciesId, StringComparison.OrdinalIgnoreCase)
        && PokemonSex.Get(metadata.GetForCapture(capture.Id)).Equals(sex, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Capture slots per form and sex, including Mega capacity.</summary>
internal sealed class CaptureLimitPolicy
{
    private readonly PokemonSpeciesMetadataCatalog _metadata;
    private readonly MegaEvolutionCatalog _megaEvolutions;

    internal CaptureLimitPolicy(PokemonSpeciesMetadataCatalog metadata, PokemonCatalog pokemonCatalog,
        MegaEvolutionCatalog megaEvolutions, string assetsRoot)
    {
        _metadata = metadata;
        _megaEvolutions = megaEvolutions;
    }

    internal string GetCaptureForm(PokemonSpeciesMetadata metadata)
    {
        // Sex is part of the captured variant even when the female encounter reuses
        // the base sprite. A visual fallback must never make that Pokémon a duplicate.
        var form = metadata.Form ?? string.Empty;
        return PokemonSex.Get(metadata).Equals("female", StringComparison.OrdinalIgnoreCase)
            ? $"{form}|female" : form;
    }

    internal string GetCaptureForm(CapturedPokemon capture) =>
        GetCaptureForm(_metadata.GetForCapture(capture.Id));

    internal int GetLimit(PokemonSpeciesMetadata metadata)
    {
        // Mega capacity belongs to the base form. Gender variants share that capacity
        // independently; regional, costume, and other visual forms retain their own cap.
        if (!string.IsNullOrWhiteSpace(metadata.Form)
            && !metadata.Form.Equals("female", StringComparison.OrdinalIgnoreCase)) return 1;
        var baseForm = _metadata.FindBaseForm(metadata.SpeciesId);
        return baseForm is null ? 1 : _megaEvolutions.GetCaptureLimit(baseForm.Id);
    }

}

internal sealed class CapturedPokemon
{
    public required string RecordId { get; init; }
    public required string Id { get; set; }
    public required string SpeciesId { get; set; }
    public required string Form { get; set; }
    public required bool IsShiny { get; init; }
    public bool IsAlpha { get; set; }
    public required DateTimeOffset CapturedAt { get; init; }
    public string? MegaFormId { get; set; }
    public string? BoxId { get; set; }
    public int? BoxSlot { get; set; }
}

internal sealed class PokemonBox
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Caja";
    public bool NameIsCustom { get; set; }
    public bool IsSpecial { get; set; }
    public string Background { get; set; } = "1.gif";
    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, 30).ToList();
}

internal sealed record PcSnapshot(IReadOnlyList<PokemonBox> Boxes, IReadOnlyList<CapturedPokemon> Captures);

internal sealed class DesktopCompanionSave
{
    public string RecordId { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsPinned { get; set; }
}

internal sealed class GameSaveData
{
    public int Version { get; set; } = 6;
    public bool StarterKitGranted { get; set; }
    public Dictionary<string, int> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CapturedPokemon> Captures { get; set; } = [];
    public List<CapturedPokemon> DuplicateCaptureRecords { get; set; } = [];
    public List<PokemonBox> PcBoxes { get; set; } = [];
    public List<DesktopCompanionSave> DesktopCompanions { get; set; } = [];
    public List<string?> DesktopTeamSlots { get; set; } = Enumerable.Repeat<string?>(null, 6).ToList();
    public TimedItemSave TimedItems { get; set; } = new();
    public RewardProgressSave RewardProgress { get; set; } = new();
    public List<AppliedRewardEventSave> AppliedRewardEvents { get; set; } = [];
}

internal sealed class TimedItemSave
{
    public int RepelSeconds { get; set; }
    public string? IncenseId { get; set; }
    public int IncenseSeconds { get; set; }
}

internal sealed class GameSaveStore
{
    private static readonly HashSet<string> LegacyNamedSpecialBoxes = new(StringComparer.OrdinalIgnoreCase)
    { "Leyendas", "Megas", "Favoritos" };
    private readonly object _gate = new();
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly CaptureLimitPolicy? _captureLimitPolicy;
    private GameSaveData _data;

    private GameSaveStore(string path, GameSaveData data, CaptureLimitPolicy? captureLimitPolicy)
    {
        _path = path;
        _data = data;
        _captureLimitPolicy = captureLimitPolicy;
    }

    public static GameSaveStore LoadOrCreate(string path, CaptureLimitPolicy? captureLimitPolicy = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!File.Exists(path))
        {
            var initial = new GameSaveData
            {
                StarterKitGranted = true,
                Items = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    [ItemId(CaptureBall.Poke)] = 10,
                    [ItemId(CaptureBall.Great)] = 5,
                    [ItemId(CaptureBall.Ultra)] = 0,
                    [ItemId(CaptureBall.Master)] = 0,
                    [ItemId(CaptureBerry.Razz)] = 3,
                    [ItemId(CaptureBerry.Nanab)] = 3,
                    [ItemId(CaptureBerry.SilverRazz)] = 0,
                    [ItemId(CaptureBerry.GoldenRazz)] = 0,
                    [ItemId(CaptureBerry.SilverNanab)] = 0,
                    [ItemId(CaptureBerry.GoldenNanab)] = 0
                }
            };
            var store = new GameSaveStore(path, initial, captureLimitPolicy);
            store.Persist();
            return store;
        }

        var loaded = JsonSerializer.Deserialize<GameSaveData>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("El guardado local está vacío o no es válido.");
        loaded.Items ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        loaded.Captures ??= [];
        loaded.DuplicateCaptureRecords ??= [];
        loaded.PcBoxes ??= [];
        loaded.DesktopCompanions ??= [];
        loaded.DesktopTeamSlots ??= [];
        loaded.TimedItems ??= new TimedItemSave();
        loaded.RewardProgress ??= new RewardProgressSave();
        loaded.RewardProgress.PendingNotifications ??= [];
        loaded.AppliedRewardEvents ??= [];
        var migrateTeamSlots = loaded.Version < 5;
        var migrateMegaSlowbro = loaded.Version < 6;
        var migratedItems = false;
        foreach (var berry in Enum.GetValues<CaptureBerry>())
            migratedItems |= loaded.Items.TryAdd(ItemId(berry), 0);
        var migratedSlowbro = migrateMegaSlowbro && MigrateLegacySlowbroMega(loaded.Captures.Concat(loaded.DuplicateCaptureRecords));
        var loadedStore = new GameSaveStore(path, loaded, captureLimitPolicy);
        var migratedPc = loadedStore.EnsurePcStorage();
        var migratedShinyDuplicates = loadedStore.MigrateShinyDuplicateCaptures();
        var migratedDittoDuplicates = loadedStore.MigrateDittoDuplicateCaptures();
        var migratedCompanions = loadedStore.EnsureDesktopCompanions(migrateTeamSlots);
        if (migratedItems || migratedPc || migratedShinyDuplicates || migratedDittoDuplicates || migratedCompanions || migratedSlowbro || migrateMegaSlowbro)
        {
            try { SaveBackupService.CreateMigrationBackup(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                EncounterDiagnostics.WriteException("save-migration-backup", exception);
            }
            loadedStore.Persist();
        }
        return loadedStore;
    }

    public static string DefaultSavePath => Environment.GetEnvironmentVariable("POKEGRASS_SAVE_PATH") is { Length: > 0 } overridePath
        ? Path.GetFullPath(overridePath)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "save.json");

    internal string SavePath => _path;

    internal void ExportToFile(string destinationPath)
    {
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        lock (_gate)
        {
            var temporary = destination + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(_data, _options));
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal string ImportFromFile(string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("No se encontró el archivo de guardado.", source);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"PokéGrass-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var stagingPath = Path.Combine(stagingDirectory, "save.json");
            File.Copy(source, stagingPath);
            var stagedStore = LoadOrCreate(stagingPath); // Migra y valida el archivo sin tocar el original.
            stagedStore.ExportToFile(stagingPath);
            var imported = JsonSerializer.Deserialize<GameSaveData>(File.ReadAllText(stagingPath), _options)
                ?? throw new InvalidDataException("El archivo no contiene una partida válida.");
            if (imported.Version > 7 || imported.Items.Any(pair => pair.Value < 0))
                throw new InvalidDataException("La versión del guardado no es compatible o contiene cantidades inválidas.");

            lock (_gate)
            {
                var backupPath = Path.Combine(Path.GetDirectoryName(_path)!,
                    $"save.backup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}" + ".json");
                if (File.Exists(_path)) File.Copy(_path, backupPath, overwrite: false);
                else File.WriteAllText(backupPath, JsonSerializer.Serialize(_data, _options));

                var previous = _data;
                _data = imported;
                try { Persist(); }
                catch { _data = previous; throw; }
                return backupPath;
            }
        }
        finally
        {
            try { Directory.Delete(stagingDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public int Count(CaptureBall ball) => Count(ItemId(ball));
    public int Count(CaptureBerry berry) => Count(ItemId(berry));

    public int Count(string id)
    {
        lock (_gate) return _data.Items.GetValueOrDefault(id);
    }

#if DEBUG
    internal void SetItemCountForDebug(string id, int count)
    {
        lock (_gate) { _data.Items[id] = Math.Max(0, count); Persist(); }
    }
#endif

    public bool TryConsume(CaptureBall ball) => TryConsume(ItemId(ball));
    public bool TryConsume(CaptureBerry berry) => TryConsume(ItemId(berry));

    public bool TryConsume(string id) => TryConsumeItem(id);

    private bool TryConsumeItem(string id)
    {
        lock (_gate)
        {
            var count = _data.Items.GetValueOrDefault(id);
            if (count <= 0) return false;
            _data.Items[id] = count - 1;
            Persist();
            return true;
        }
    }

    internal TimedItemSave GetTimedItems()
    {
        lock (_gate) return new TimedItemSave { RepelSeconds = _data.TimedItems.RepelSeconds,
            IncenseId = _data.TimedItems.IncenseId, IncenseSeconds = _data.TimedItems.IncenseSeconds };
    }

    internal void SaveTimedItems(TimedItemSave effects)
    {
        lock (_gate)
        {
            _data.TimedItems = new TimedItemSave { RepelSeconds = Math.Max(0, effects.RepelSeconds),
                IncenseId = effects.IncenseSeconds > 0 ? effects.IncenseId : null,
                IncenseSeconds = Math.Max(0, effects.IncenseSeconds) };
            Persist();
        }
    }

    public void TryRecordCapture(PokemonSpeciesMetadata metadata, bool shiny, out bool duplicate, bool alpha = false)
    {
        lock (_gate)
        {
            EnsurePcStorage();
            var record = new CapturedPokemon
            {
                RecordId = Guid.NewGuid().ToString("N"),
                Id = metadata.Id,
                SpeciesId = metadata.SpeciesId,
                Form = metadata.Form,
                IsShiny = shiny,
                IsAlpha = alpha,
                CapturedAt = DateTimeOffset.UtcNow
            };
            duplicate = IsAtCaptureLimit(metadata, alpha);
            if (duplicate && !shiny)
                _data.DuplicateCaptureRecords.Add(record);
            else
            {
                PlaceInFirstFreeSlot(record);
                _data.Captures.Add(record);
            }
            _data.Version = 3;
            Persist();
        }
    }

    internal CaptureRecordResult TryRecordCaptureWithRewards(PokemonSpeciesMetadata metadata, bool shiny, string rarity,
        RewardBalance balance, string eventId, Random? random = null, bool alpha = false)
    {
        lock (_gate)
        {
            EnsurePcStorage();
            var previous = _data.AppliedRewardEvents.FirstOrDefault(entry => entry.EventId == eventId);
            if (previous is not null)
                return new CaptureRecordResult(previous.IsDuplicate, previous.IsShinyDuplicate, previous.Items.ToArray());

            var duplicate = IsAtCaptureLimit(metadata, alpha);
            var shinyDuplicate = shiny && _data.Captures.Concat(_data.DuplicateCaptureRecords).Any(capture => capture.IsShiny
                && IsSameCaptureVariant(capture, metadata, alpha));
            var record = new CapturedPokemon
            {
                RecordId = Guid.NewGuid().ToString("N"), Id = metadata.Id, SpeciesId = metadata.SpeciesId,
                Form = metadata.Form, IsShiny = shiny, IsAlpha = alpha, CapturedAt = DateTimeOffset.UtcNow
            };
            if (duplicate && !shiny) _data.DuplicateCaptureRecords.Add(record);
            else { PlaceInFirstFreeSlot(record); _data.Captures.Add(record); }

            var rewards = balance.MakeCaptureRewards(duplicate, shiny, shinyDuplicate, rarity, random ?? Random.Shared).ToList();
            ApplyItems(rewards);
            _data.AppliedRewardEvents.Add(new AppliedRewardEventSave
            {
                EventId = eventId, IsDuplicate = duplicate, IsShinyDuplicate = shinyDuplicate, Items = rewards
            });
            _data.Version = 3;
            Persist();
            return new CaptureRecordResult(duplicate, shinyDuplicate, rewards.ToArray());
        }
    }

    private bool IsAtCaptureLimit(PokemonSpeciesMetadata metadata, bool alpha)
    {
        // Ditto is intentionally repeatable: every capture goes into the PC,
        // whose regular boxes expand automatically when all slots are occupied.
        if (metadata.SpeciesId.Equals("ditto", StringComparison.OrdinalIgnoreCase)) return false;

        var existingCount = _data.Captures.Count(capture => IsSameCaptureVariant(capture, metadata, alpha));
        var limit = _captureLimitPolicy?.GetLimit(metadata) ?? 1;
        return existingCount >= limit;
    }

    private bool MigrateDittoDuplicateCaptures()
    {
        var dittoDuplicates = _data.DuplicateCaptureRecords
            .Where(capture => capture.SpeciesId.Equals("ditto", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (dittoDuplicates.Length == 0) return false;

        foreach (var capture in dittoDuplicates)
        {
            PlaceInFirstFreeSlot(capture);
            _data.Captures.Add(capture);
            _data.DuplicateCaptureRecords.Remove(capture);
        }
        return true;
    }

    private bool MigrateShinyDuplicateCaptures()
    {
        var shinyDuplicates = _data.DuplicateCaptureRecords.Where(capture => capture.IsShiny).ToArray();
        if (shinyDuplicates.Length == 0) return false;

        foreach (var capture in shinyDuplicates)
        {
            _data.DuplicateCaptureRecords.Remove(capture);
            if (_data.Captures.Any(existing => existing.RecordId == capture.RecordId)) continue;
            PlaceInFirstFreeSlot(capture);
            _data.Captures.Add(capture);
        }
        return true;
    }

    private bool IsSameCaptureVariant(CapturedPokemon capture, PokemonSpeciesMetadata metadata, bool alpha)
    {
        if (capture.IsAlpha != alpha || !capture.SpeciesId.Equals(metadata.SpeciesId, StringComparison.OrdinalIgnoreCase)) return false;
        var capturedForm = _captureLimitPolicy?.GetCaptureForm(capture) ?? capture.Form;
        var targetForm = _captureLimitPolicy?.GetCaptureForm(metadata) ?? metadata.Form;
        return capturedForm.Equals(targetForm, StringComparison.OrdinalIgnoreCase);
    }

    internal RewardProgressSave GetRewardProgress()
    {
        lock (_gate) return CloneRewardProgress(_data.RewardProgress);
    }

    internal void SaveRewardProgress(int supplySecondsRemaining)
    {
        lock (_gate) { _data.RewardProgress.SupplySecondsRemaining = Math.Max(1, supplySecondsRemaining); Persist(); }
    }

    internal bool TryGrantScheduledReward(string eventId, string title, IReadOnlyList<RewardItemGrant> items,
        int nextSupplySecondsRemaining, string? lastDailyLocalDate)
    {
        lock (_gate)
        {
            if (_data.AppliedRewardEvents.Any(entry => entry.EventId == eventId)) return false;
            var rewards = items.Where(item => item.Quantity > 0).Select(item => item with { }).ToList();
            ApplyItems(rewards);
            _data.AppliedRewardEvents.Add(new AppliedRewardEventSave { EventId = eventId, Items = rewards });
            _data.RewardProgress.SupplySecondsRemaining = Math.Max(1, nextSupplySecondsRemaining);
            if (lastDailyLocalDate is not null) _data.RewardProgress.LastDailyLocalDate = lastDailyLocalDate;
            if (rewards.Count > 0)
                _data.RewardProgress.PendingNotifications.Add(new RewardNotificationSave { EventId = eventId, Title = title, Items = rewards });
            _data.Version = 3;
            Persist();
            return true;
        }
    }

    internal bool TryFeedCompanionBerry(string berryId, string eventId, string title, IReadOnlyList<RewardItemGrant> gift)
    {
        string[] berryIds = ["razz-berry", "silver-razz-berry", "golden-razz-berry", "nanab-berry", "silver-nanab-berry", "golden-nanab-berry"];
        lock (_gate)
        {
            if (!berryIds.Contains(berryId, StringComparer.OrdinalIgnoreCase)
                || _data.Items.GetValueOrDefault(berryId) <= 0
                || _data.AppliedRewardEvents.Any(entry => entry.EventId == eventId)) return false;

            _data.Items[berryId]--;
            var rewards = gift.Where(item => item.Quantity > 0).Select(item => item with { }).ToList();
            if (rewards.Count > 0)
            {
                ApplyItems(rewards);
                _data.RewardProgress.PendingNotifications.Add(new RewardNotificationSave
                {
                    EventId = eventId, Title = title, Items = rewards.Select(item => item with { }).ToList()
                });
            }
            _data.AppliedRewardEvents.Add(new AppliedRewardEventSave { EventId = eventId, Items = rewards });
            _data.Version = 5;
            Persist();
            return true;
        }
    }

    internal bool TryGetRewardNotification(out RewardNotificationSave? notification)
    {
        lock (_gate)
        {
            if (_data.RewardProgress.PendingNotifications.Count == 0) { notification = null; return false; }
            var pending = _data.RewardProgress.PendingNotifications[0];
            notification = new RewardNotificationSave
            {
                EventId = pending.EventId,
                Title = pending.Title,
                Items = pending.Items.Select(item => item with { }).ToList()
            };
            return true;
        }
    }

    internal void CompleteRewardNotification(string eventId)
    {
        lock (_gate)
        {
            var index = _data.RewardProgress.PendingNotifications.FindIndex(item => item.EventId == eventId);
            if (index < 0) return;
            _data.RewardProgress.PendingNotifications.RemoveAt(index);
            Persist();
        }
    }

    private void ApplyItems(IEnumerable<RewardItemGrant> items)
    {
        foreach (var item in items)
        {
            if (item.Quantity <= 0) continue;
            _data.Items[item.Id] = checked(_data.Items.GetValueOrDefault(item.Id) + item.Quantity);
        }
    }

    private static RewardProgressSave CloneRewardProgress(RewardProgressSave value) => new()
    {
        SupplySecondsRemaining = value.SupplySecondsRemaining,
        LastDailyLocalDate = value.LastDailyLocalDate,
        PendingNotifications = value.PendingNotifications.Select(item => new RewardNotificationSave
        {
            EventId = item.EventId, Title = item.Title, Items = item.Items.Select(reward => reward with { }).ToList()
        }).ToList()
    };

    internal PcSnapshot GetPcSnapshot()
    {
        lock (_gate)
        {
            if (EnsurePcStorage()) Persist();
            var boxes = _data.PcBoxes.Select(box => new PokemonBox
            {
                Id = box.Id, Name = box.Name, NameIsCustom = box.NameIsCustom, IsSpecial = box.IsSpecial, Background = box.Background,
                Slots = box.Slots.ToList()
            }).ToList();
            var captures = _data.Captures.Select(CloneCapture).ToList();
            return new PcSnapshot(boxes, captures);
        }
    }

    internal IReadOnlyList<DesktopCompanionSave> GetDesktopCompanions()
    {
        lock (_gate)
            return _data.DesktopCompanions.Select(CloneDesktopCompanion).ToArray();
    }

    internal bool SetMegaForm(string recordId, string? megaFormId)
    {
        lock (_gate)
        {
            var record = _data.Captures.FirstOrDefault(capture => capture.RecordId == recordId);
            if (record is null || string.Equals(record.MegaFormId, megaFormId, StringComparison.OrdinalIgnoreCase)) return false;
            record.MegaFormId = megaFormId;
            Persist();
            return true;
        }
    }

    internal bool TryEvolveCapture(string recordId, string expectedId, PokemonSpeciesMetadata target,
        PokemonSpeciesMetadataCatalog metadata)
    {
        lock (_gate)
        {
            var capture = _data.Captures.FirstOrDefault(item => item.RecordId == recordId);
            if (capture is null || !capture.Id.Equals(expectedId, StringComparison.OrdinalIgnoreCase)
                || capture.MegaFormId is not null)
                return false;

            var currentSex = PokemonSex.Get(metadata.GetForCapture(capture.Id));
            if (!currentSex.Equals(PokemonSex.Get(target), StringComparison.OrdinalIgnoreCase)) return false;
            var targetKey = EvolutionIdentity.Key(target, metadata);
            if (_data.Captures.Any(item => item.RecordId != recordId
                && EvolutionIdentity.Key(item, metadata).Equals(targetKey, StringComparison.OrdinalIgnoreCase)))
                return false;

            var oldId = capture.Id;
            var oldSpeciesId = capture.SpeciesId;
            var oldForm = capture.Form;
            capture.Id = target.Id;
            capture.SpeciesId = target.SpeciesId;
            capture.Form = target.Form;
            try { Persist(); }
            catch
            {
                capture.Id = oldId;
                capture.SpeciesId = oldSpeciesId;
                capture.Form = oldForm;
                throw;
            }
            return true;
        }
    }

    internal IReadOnlyList<string?> GetDesktopTeamSlots()
    {
        lock (_gate) return _data.DesktopTeamSlots.ToArray();
    }

    internal bool TryAddDesktopCompanion(string recordId, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        lock (_gate)
        {
            if (_data.DesktopCompanions.Count >= 6
                || _data.DesktopCompanions.Any(companion => companion.RecordId == recordId)) return false;
            var capture = _data.Captures.FirstOrDefault(record => record.RecordId == recordId);
            if (capture is null || capture.BoxId is null || capture.BoxSlot is not >= 0 or >= 30) return false;
            var teamSlot = _data.DesktopTeamSlots.FindIndex(id => id is null);
            if (teamSlot < 0) return false;
            _data.DesktopCompanions.Add(new DesktopCompanionSave { RecordId = recordId, X = x, Y = y });
            _data.DesktopTeamSlots[teamSlot] = recordId;
            Persist();
            return true;
        }
    }

    internal bool SwapDesktopTeamSlots(int firstSlot, int secondSlot)
    {
        if (firstSlot is < 0 or >= 6 || secondSlot is < 0 or >= 6) return false;
        lock (_gate)
        {
            if (_data.DesktopTeamSlots.Count != 6) return false;
            (_data.DesktopTeamSlots[firstSlot], _data.DesktopTeamSlots[secondSlot]) =
                (_data.DesktopTeamSlots[secondSlot], _data.DesktopTeamSlots[firstSlot]);
            Persist();
            return true;
        }
    }

    internal bool UpdateDesktopCompanion(string recordId, double? x = null, double? y = null, bool? isPinned = null)
    {
        lock (_gate)
        {
            var companion = _data.DesktopCompanions.FirstOrDefault(item => item.RecordId == recordId);
            if (companion is null) return false;
            if (x is { } nextX && double.IsFinite(nextX)) companion.X = nextX;
            if (y is { } nextY && double.IsFinite(nextY)) companion.Y = nextY;
            if (isPinned is { } pinned) companion.IsPinned = pinned;
            Persist();
            return true;
        }
    }

    internal bool RemoveDesktopCompanion(string recordId)
    {
        lock (_gate)
        {
            var removed = _data.DesktopCompanions.RemoveAll(item => item.RecordId == recordId) > 0;
            if (removed)
            {
                for (var slot = 0; slot < _data.DesktopTeamSlots.Count; slot++)
                    if (_data.DesktopTeamSlots[slot] == recordId) _data.DesktopTeamSlots[slot] = null;
                Persist();
            }
            return removed;
        }
    }

    internal bool IsDesktopCompanion(string recordId)
    {
        lock (_gate) return _data.DesktopCompanions.Any(item => item.RecordId == recordId);
    }

    internal void AddBox()
    {
        lock (_gate)
        {
            EnsurePcStorage();
            var regularCount = _data.PcBoxes.Count(box => !box.IsSpecial);
            _data.PcBoxes.Add(NewBox(regularCount + 1));
            NormalizeDefaultBoxNames();
            Persist();
        }
    }

    internal void AddSpecialBox()
    {
        lock (_gate)
        {
            EnsurePcStorage();
            var specialCount = _data.PcBoxes.Count(box => box.IsSpecial);
            var firstSpecialBox = _data.PcBoxes.FindIndex(box => box.IsSpecial);
            if (firstSpecialBox < 0) firstSpecialBox = _data.PcBoxes.FindIndex(box => !box.IsSpecial);
            if (firstSpecialBox < 0) firstSpecialBox = _data.PcBoxes.Count;
            _data.PcBoxes.Insert(firstSpecialBox, NewBox(specialCount + 1, isSpecial: true));
            Persist();
        }
    }

    internal void RenameBox(string boxId, string name)
    {
        lock (_gate)
        {
            var box = FindBox(boxId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                box.Name = name.Trim();
                box.NameIsCustom = true;
            }
            Persist();
        }
    }

    internal void SetBoxBackground(string boxId, string background)
    {
        lock (_gate)
        {
            var box = FindBox(boxId);
            box.Background = Path.GetFileName(background);
            Persist();
        }
    }

    internal void ReorderBox(string boxId, int direction)
    {
        lock (_gate)
        {
            var index = _data.PcBoxes.FindIndex(box => box.Id == boxId);
            var next = index + Math.Sign(direction);
            if (index < 0 || next < 0 || next >= _data.PcBoxes.Count) return;
            if (_data.PcBoxes[index].IsSpecial != _data.PcBoxes[next].IsSpecial) return;
            (_data.PcBoxes[index], _data.PcBoxes[next]) = (_data.PcBoxes[next], _data.PcBoxes[index]);
            NormalizeDefaultBoxNames();
            Persist();
        }
    }

    internal bool DeleteEmptyBox(string boxId)
    {
        lock (_gate)
        {
            EnsurePcStorage();
            var box = FindBox(boxId);
            if (box.IsSpecial ? _data.PcBoxes.Count <= 1 : _data.PcBoxes.Count(candidate => !candidate.IsSpecial) <= 1) return false;
            if (box.Slots.Any(recordId => recordId is not null)) return false;
            _data.PcBoxes.Remove(box);
            NormalizeDefaultBoxNames();
            Persist();
            return true;
        }
    }

    internal bool MoveRecord(string recordId, string destinationBoxId, int destinationSlot)
    {
        if (destinationSlot is < 0 or >= 30) return false;
        lock (_gate)
        {
            EnsurePcStorage();
            var source = _data.Captures.FirstOrDefault(record => record.RecordId == recordId);
            if (source is null) return false;
            var sourceBox = FindBox(source.BoxId!);
            var destinationBox = FindBox(destinationBoxId);
            if (source.BoxSlot is not >= 0 or >= 30) return false;
            var sourceSlot = source.BoxSlot.Value;
            var otherId = destinationBox.Slots[destinationSlot];
            if (source.BoxId == destinationBoxId && sourceSlot == destinationSlot) return true;
            var other = otherId is null ? null : _data.Captures.FirstOrDefault(record => record.RecordId == otherId);
            sourceBox.Slots[sourceSlot] = other?.RecordId;
            destinationBox.Slots[destinationSlot] = source.RecordId;
            source.BoxId = destinationBox.Id;
            source.BoxSlot = destinationSlot;
            if (other is not null)
            {
                other.BoxId = sourceBox.Id;
                other.BoxSlot = sourceSlot;
            }
            Persist();
            return true;
        }
    }

    internal bool ReleaseRecord(string recordId, out CapturedPokemon? released)
    {
        lock (_gate)
        {
            var capture = _data.Captures.FirstOrDefault(record => record.RecordId == recordId);
            if (capture is null) { released = null; return false; }
            if (capture.BoxId is not null && capture.BoxSlot is >= 0 and < 30)
            {
                var box = FindBox(capture.BoxId);
                box.Slots[capture.BoxSlot.Value] = null;
            }
            _data.DesktopCompanions.RemoveAll(companion => companion.RecordId == recordId);
            for (var slot = 0; slot < _data.DesktopTeamSlots.Count; slot++)
                if (_data.DesktopTeamSlots[slot] == recordId) _data.DesktopTeamSlots[slot] = null;
            _data.Captures.Remove(capture);
            released = CloneCapture(capture);
            Persist();
            return true;
        }
    }

    private bool EnsurePcStorage()
    {
        var changed = false;
        if (_data.PcBoxes.Count == 0)
        {
            _data.PcBoxes.Add(NewBox(1));
            changed = true;
        }
        foreach (var box in _data.PcBoxes)
        {
            box.Name ??= "Caja";
            // Older saves may contain boxes created before the IsSpecial flag
            // was persisted. Recover it from either generated label spelling so
            // automatic captures never count a special box as a regular one.
            if (!box.IsSpecial && (IsSpecialBoxName(box.Name)
                || LegacyNamedSpecialBoxes.Contains(box.Name.Trim())))
            { box.IsSpecial = true; changed = true; }
            if (string.IsNullOrWhiteSpace(box.Id)) { box.Id = Guid.NewGuid().ToString("N"); changed = true; }
            if (box.Slots is null) { box.Slots = Enumerable.Repeat<string?>(null, 30).ToList(); changed = true; }
            if (box.Slots.Count < 30) { box.Slots.AddRange(Enumerable.Repeat<string?>(null, 30 - box.Slots.Count)); changed = true; }
            if (box.Slots.Count > 30) { box.Slots = box.Slots.Take(30).ToList(); changed = true; }
            box.Background = string.IsNullOrWhiteSpace(box.Background) ? "1.gif" : Path.GetFileName(box.Background);
        }
        // Recover older custom-named special boxes when their persisted type flag
        // is absent: the established layout places them before default Caja N
        // boxes, whose old number reflects the count of preceding specials.
        var firstDefaultRegularIndex = _data.PcBoxes.FindIndex(box => TryGetDefaultRegularBoxNumber(box.Name, out _));
        if (firstDefaultRegularIndex > 0
            && TryGetDefaultRegularBoxNumber(_data.PcBoxes[firstDefaultRegularIndex].Name, out var firstRegularNumber)
            && firstRegularNumber == firstDefaultRegularIndex + 1)
        {
            for (var index = 0; index < firstDefaultRegularIndex; index++)
            {
                var box = _data.PcBoxes[index];
                if (box.IsSpecial) continue;
                box.IsSpecial = true;
                changed = true;
            }
        }
        if (!_data.PcBoxes.Any(box => !box.IsSpecial))
        {
            _data.PcBoxes.Add(NewBox(1));
            changed = true;
        }
        var orderedBoxes = _data.PcBoxes.OrderBy(box => box.IsSpecial ? 0 : 1).ToList();
        if (!_data.PcBoxes.SequenceEqual(orderedBoxes))
        {
            _data.PcBoxes = orderedBoxes;
            changed = true;
        }
        if (NormalizeDefaultBoxNames()) changed = true;
        var located = _data.PcBoxes.SelectMany((box, boxIndex) => box.Slots.Select((id, slot) => (id, boxIndex, slot)))
            .Where(entry => entry.id is not null).ToList();
        foreach (var record in _data.Captures)
        {
            var location = located.FirstOrDefault(entry => entry.id == record.RecordId);
            if (location.id is not null)
            {
                if (record.BoxId != _data.PcBoxes[location.boxIndex].Id || record.BoxSlot != location.slot)
                { record.BoxId = _data.PcBoxes[location.boxIndex].Id; record.BoxSlot = location.slot; changed = true; }
                continue;
            }
            PlaceInFirstFreeSlot(record);
            changed = true;
        }
        return changed;
    }

    private bool EnsureDesktopCompanions(bool migrateTeamSlots)
    {
        var changed = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var validRecords = _data.Captures.Where(record => record.BoxId is not null && record.BoxSlot is >= 0 and < 30)
            .Select(record => record.RecordId).ToHashSet(StringComparer.Ordinal);
        for (var index = _data.DesktopCompanions.Count - 1; index >= 0; index--)
        {
            var companion = _data.DesktopCompanions[index];
            if (string.IsNullOrWhiteSpace(companion.RecordId) || !validRecords.Contains(companion.RecordId)
                || !double.IsFinite(companion.X) || !double.IsFinite(companion.Y)
                || !seen.Add(companion.RecordId) || index >= 6)
            {
                _data.DesktopCompanions.RemoveAt(index);
                changed = true;
            }
        }

        var validIds = _data.DesktopCompanions.Select(item => item.RecordId).ToHashSet(StringComparer.Ordinal);
        if (migrateTeamSlots || _data.DesktopTeamSlots.Count == 0)
        {
            _data.DesktopTeamSlots = Enumerable.Repeat<string?>(null, 6).ToList();
            for (var slot = 0; slot < Math.Min(6, _data.DesktopCompanions.Count); slot++)
                _data.DesktopTeamSlots[slot] = _data.DesktopCompanions[slot].RecordId;
            return true;
        }

        if (_data.DesktopTeamSlots.Count != 6)
        {
            var normalized = Enumerable.Repeat<string?>(null, 6).ToList();
            for (var slot = 0; slot < Math.Min(6, _data.DesktopTeamSlots.Count); slot++) normalized[slot] = _data.DesktopTeamSlots[slot];
            _data.DesktopTeamSlots = normalized;
            changed = true;
        }
        var teamSeen = new HashSet<string>(StringComparer.Ordinal);
        for (var slot = 0; slot < 6; slot++)
        {
            var recordId = _data.DesktopTeamSlots[slot];
            if (recordId is null) continue;
            if (!validIds.Contains(recordId) || !teamSeen.Add(recordId))
            { _data.DesktopTeamSlots[slot] = null; changed = true; }
        }
        foreach (var companion in _data.DesktopCompanions)
        {
            if (teamSeen.Contains(companion.RecordId)) continue;
            var freeSlot = _data.DesktopTeamSlots.FindIndex(id => id is null);
            if (freeSlot < 0) break;
            _data.DesktopTeamSlots[freeSlot] = companion.RecordId;
            teamSeen.Add(companion.RecordId);
            changed = true;
        }
        return changed;
    }

    private static bool MigrateLegacySlowbroMega(IEnumerable<CapturedPokemon> captures)
    {
        var changed = false;
        foreach (var capture in captures.Where(record => record.Id.Equals("SLOWBRO_2", StringComparison.OrdinalIgnoreCase)))
        {
            // Older builds exposed Mega Slowbro as a regular regional form. Keep its appearance,
            // record identity, shiny state and PC location, but store it as a permanent Mega form.
            capture.Id = "SLOWBRO";
            capture.SpeciesId = "slowbro";
            capture.Form = string.Empty;
            capture.MegaFormId = "SLOWBRO_2";
            changed = true;
        }
        return changed;
    }

    private void PlaceInFirstFreeSlot(CapturedPokemon record)
    {
        while (true)
        {
            foreach (var box in _data.PcBoxes.Where(box => !box.IsSpecial && !IsSpecialBoxName(box.Name)))
            {
                var slot = box.Slots.FindIndex(value => value is null);
                if (slot < 0) continue;
                box.Slots[slot] = record.RecordId;
                record.BoxId = box.Id; record.BoxSlot = slot;
                return;
            }
            _data.PcBoxes.Add(NewBox(_data.PcBoxes.Count(box => !box.IsSpecial) + 1));
        }
    }

    private PokemonBox FindBox(string boxId) => _data.PcBoxes.FirstOrDefault(box => box.Id == boxId)
        ?? throw new KeyNotFoundException($"No existe la caja {boxId}.");

    private bool NormalizeDefaultBoxNames()
    {
        var changed = false;
        for (var i = 0; i < _data.PcBoxes.Count; i++)
        {
            var box = _data.PcBoxes[i];
            if (!box.IsSpecial && !box.NameIsCustom && IsDefaultBoxName(box.Name))
            {
                var boxNumber = _data.PcBoxes.Take(i + 1).Count(candidate => !candidate.IsSpecial);
                var expectedName = $"Caja {boxNumber}";
                if (box.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) continue;
                box.Name = expectedName;
                changed = true;
            }
        }
        return changed;
    }

    private static bool IsDefaultBoxName(string? name)
    {
        if (name is null) return false;
        if (TryGetDefaultRegularBoxNumber(name, out _)) return true;
        if (name.StartsWith("Especial ", StringComparison.OrdinalIgnoreCase)) return int.TryParse(name.AsSpan(9), out _);
        return false;
    }

    private static bool TryGetDefaultRegularBoxNumber(string? name, out int number)
    {
        number = 0;
        return name is not null && name.StartsWith("Caja ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(5), out number) && number > 0;
    }

    private static bool IsSpecialBoxName(string? name)
    {
        if (name is null) return false;
        foreach (var prefix in new[] { "Especial ", "E ", "E" })
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(name.AsSpan(prefix.Length), out var number) && number > 0) return true;
        }
        return false;
    }

    private static PokemonBox NewBox(int number, bool isSpecial = false) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Name = isSpecial ? $"Especial {number}" : $"Caja {number}",
        IsSpecial = isSpecial, Background = "1.gif",
        Slots = Enumerable.Repeat<string?>(null, 30).ToList()
    };

    private static CapturedPokemon CloneCapture(CapturedPokemon record) => new()
    {
        RecordId = record.RecordId, Id = record.Id, SpeciesId = record.SpeciesId,
        Form = record.Form, IsShiny = record.IsShiny, IsAlpha = record.IsAlpha, CapturedAt = record.CapturedAt,
        BoxId = record.BoxId, BoxSlot = record.BoxSlot, MegaFormId = record.MegaFormId
    };

    private static DesktopCompanionSave CloneDesktopCompanion(DesktopCompanionSave companion) => new()
    {
        RecordId = companion.RecordId, X = companion.X, Y = companion.Y, IsPinned = companion.IsPinned
    };

    private void Persist()
    {
        _data.Version = 7;
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_data, _options));
        File.Move(temporaryPath, _path, overwrite: true);
        try { SaveBackupService.CreateAutomatic(_path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            EncounterDiagnostics.WriteException("save-automatic-backup", exception);
        }
    }

    internal static string ItemId(CaptureBall ball) => ball switch
    {
        CaptureBall.Poke => "poke-ball",
        CaptureBall.Great => "super-ball",
        CaptureBall.Ultra => "ultra-ball",
        CaptureBall.Master => "master-ball",
        _ => throw new ArgumentOutOfRangeException(nameof(ball))
    };

    internal static string ItemId(CaptureBerry berry) => berry switch
    {
        CaptureBerry.Razz => "razz-berry",
        CaptureBerry.SilverRazz => "silver-razz-berry",
        CaptureBerry.GoldenRazz => "golden-razz-berry",
        CaptureBerry.Nanab => "nanab-berry",
        CaptureBerry.SilverNanab => "silver-nanab-berry",
        CaptureBerry.GoldenNanab => "golden-nanab-berry",
        _ => throw new ArgumentOutOfRangeException(nameof(berry))
    };
}

internal readonly record struct CaptureRoll(bool Success, double Chance);
internal sealed record CaptureResolution(bool Success, bool Duplicate, bool Fled, double Chance, IReadOnlyList<RewardItemGrant>? Rewards = null);
internal sealed record CaptureAttempt(CaptureResolution? Result);

internal sealed class EncounterBerrySlot
{
    public CaptureBerry? ActiveBerry { get; private set; }
    public bool Used => ActiveBerry.HasValue;

    public bool TryUse(GameSaveStore save, CaptureBerry berry)
    {
        if (Used || !save.TryConsume(berry)) return false;
        ActiveBerry = berry;
        return true;
    }

    // The effect is pending until a valid Ball throw finishes. A cancelled drag never
    // calls this method, so the pending berry remains available for the next throw.
    public CaptureBerry? ResolveAttempt()
    {
        var resolvedBerry = ActiveBerry;
        ActiveBerry = null;
        return resolvedBerry;
    }

    public void Clear() => ActiveBerry = null;
}

internal static class CaptureMechanics
{
    // PokéGrass has no battle phase, so this maps captureRate to a fair full-health
    // encounter chance while preserving each species' relative difficulty.
    internal static class BerryEffectMultipliers
    {
        public const double RazzCapture = 1.5d;
        public const double SilverRazzCapture = 1.9d;
        public const double GoldenRazzCapture = 2.5d;
        public const double NanabFlee = 0.35d;
        public const double SilverNanabFlee = 0.2d;
        public const double GoldenNanabFlee = 0.1d;
    }

    private static readonly IReadOnlyDictionary<string, double> FleeTierBases =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["common"] = 0.05d,
            ["uncommon"] = 0.08d,
            ["rare"] = 0.12d,
            ["epic"] = 0.16d,
            ["legendary"] = 0.20d
        };

    public static double Chance(PokemonSpeciesMetadata pokemon, CaptureBall ball, CaptureBerry? berry)
    {
        if (ball == CaptureBall.Master) return 1d;
        // Full-health baseline: 35%–85% as captureRate moves from 1 to 255.
        // This preserves per-species ordering without assuming a battle/debuff phase.
        var baseChance = 0.35d + pokemon.CaptureRate / 255d * 0.5d;
        var ballMultiplier = ball switch
        {
            CaptureBall.Poke => 1d,
            CaptureBall.Great => 1.5d,
            CaptureBall.Ultra => 2d,
            _ => 1d
        };
        var berryMultiplier = berry switch
        {
            CaptureBerry.Razz => BerryEffectMultipliers.RazzCapture,
            CaptureBerry.SilverRazz => BerryEffectMultipliers.SilverRazzCapture,
            CaptureBerry.GoldenRazz => BerryEffectMultipliers.GoldenRazzCapture,
            _ => 1d
        };
        // Apply multipliers to the remaining failure chance, avoiding the flat results
        // caused by direct multiplication and an early cap for high-rate species.
        // A golden Razz can make a throw very likely, but only Master Ball is guaranteed.
        var effectiveMultiplier = ballMultiplier * berryMultiplier;
        var chance = 1d - Math.Pow(1d - baseChance, effectiveMultiplier);
        return Math.Clamp(chance, 0d, 0.99d);
    }

    public static CaptureRoll Roll(PokemonSpeciesMetadata pokemon, CaptureBall ball, CaptureBerry? berry,
        bool shiny, Random random)
    {
        var chance = shiny || ball == CaptureBall.Master ? 1d : Chance(pokemon, ball, berry);
        return new CaptureRoll(random.NextDouble() < chance, chance);
    }

    public static double FleeChance(PokemonSpeciesMetadata pokemon, string rarity, CaptureBerry? berry)
    {
        var tierBase = FleeTierBases.TryGetValue(rarity, out var baseChance) ? baseChance : 0.05d;
        // Species variation stays bounded at 0.75×–1.25× of the tier baseline.
        // A lower captureRate makes a species somewhat more likely to flee.
        var speciesFactor = 0.75d + 0.5d * (255 - pokemon.CaptureRate) / 254d;
        var chance = tierBase * speciesFactor;
        chance *= berry switch
        {
            CaptureBerry.Nanab => BerryEffectMultipliers.NanabFlee,
            CaptureBerry.SilverNanab => BerryEffectMultipliers.SilverNanabFlee,
            CaptureBerry.GoldenNanab => BerryEffectMultipliers.GoldenNanabFlee,
            _ => 1d
        };
        return Math.Clamp(chance, 0d, 0.25d);
    }

    public static bool RollFlee(PokemonSpeciesMetadata pokemon, string rarity, CaptureBerry? berry, bool shiny, Random random) =>
        !shiny && random.NextDouble() < FleeChance(pokemon, rarity, berry);
}

internal sealed class FixedCaptureRandom(double value) : Random
{
    public override double NextDouble() => value;
}
