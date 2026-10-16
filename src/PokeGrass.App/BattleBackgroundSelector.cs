namespace PokeGrass;

internal sealed record BattleBackgroundProfile(
    string PokemonId,
    string SpeciesId,
    IReadOnlyList<string> Types,
    bool IsMega);

internal sealed record BattleBackgroundSelection(string? BackgroundId, string Category, string Reason);

/// <summary>Selects one available arena background once at the start of each battle.</summary>
internal static class BattleBackgroundSelector
{
    private const double RetroChance = .01d;
    private static readonly IReadOnlyDictionary<string, int> CommonWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["poke1"] = 55, ["poke2"] = 25, ["poke3"] = 15, ["poke4"] = 5
    };

    // Mythical Pokémon are intentionally kept out of this list. Arceus has its own
    // higher-priority category and is identified by SpeciesId, including its forms.
    private static readonly HashSet<string> LegendarySpecies = new(StringComparer.OrdinalIgnoreCase)
    {
        "articuno", "zapdos", "moltres", "mewtwo",
        "raikou", "entei", "suicune", "lugia", "ho-oh",
        "regirock", "regice", "registeel", "latias", "latios", "kyogre", "groudon", "rayquaza",
        "uxie", "mesprit", "azelf", "dialga", "palkia", "heatran", "regigigas", "giratina", "cresselia",
        "cobalion", "terrakion", "virizion", "tornadus", "thundurus", "reshiram", "zekrom", "landorus", "kyurem",
        "xerneas", "yveltal", "zygarde",
        "type-null", "silvally", "tapu-koko", "tapu-lele", "tapu-bulu", "tapu-fini",
        "cosmog", "cosmoem", "solgaleo", "lunala", "necrozma",
        "zacian", "zamazenta", "eternatus", "kubfu", "urshifu", "regieleki", "regidrago",
        "glastrier", "spectrier", "calyrex",
        "wo-chien", "chien-pao", "ting-lu", "chi-yu", "koraidon", "miraidon", "ogerpon",
        "terapagos", "okidogi", "munkidori", "fezandipiti", "enamorus"
    };

    internal static BattleBackgroundSelection Select(BattleBackgroundProfile first,
        BattleBackgroundProfile second, Func<double> nextRandom, Func<string, bool> isAvailable)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(nextRandom);
        ArgumentNullException.ThrowIfNull(isAvailable);

        if (NextUnit(nextRandom) < RetroChance)
            return SelectSpecial("retro", "retro", "La tirada independiente de 1 % seleccionó retro.", isAvailable, nextRandom);

        if (IsSpecies(first, "arceus") || IsSpecies(second, "arceus"))
            return SelectSpecial("god", "god", "Al menos un participante pertenece a la especie Arceus.", isAvailable, nextRandom);

        if (IsLegendary(first) && IsLegendary(second))
            return SelectSpecial("legendary", "legendary", "Ambos participantes son legendarios.", isAvailable, nextRandom);

        if (first.IsMega && second.IsMega)
            return SelectSpecial("mega", "mega", "Ambos participantes están megaevolucionados.", isAvailable, nextRandom);

        var sharedTypes = first.Types
            .Where(type => second.Types.Contains(type, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var availableTypes = sharedTypes.Where(isAvailable).ToArray();
        if (availableTypes.Length > 0)
        {
            var chosenType = availableTypes.Length == 1
                ? availableTypes[0]
                : availableTypes[Math.Min((int)(NextUnit(nextRandom) * availableTypes.Length), availableTypes.Length - 1)];
            return new BattleBackgroundSelection(chosenType, "type",
                $"Tipo compartido disponible: {string.Join('/', availableTypes)}.");
        }

        var noSharedReason = sharedTypes.Length > 0
            ? $"Los tipos compartidos ({string.Join('/', sharedTypes)}) no tienen fondo disponible; se usan fondos comunes."
            : "No comparten tipos; se usan fondos comunes.";
        return SelectCommon(nextRandom, isAvailable, noSharedReason);
    }

    private static BattleBackgroundSelection SelectSpecial(string id, string category, string reason,
        Func<string, bool> isAvailable, Func<double> nextRandom)
    {
        if (isAvailable(id)) return new BattleBackgroundSelection(id, category, reason);
        return SelectCommon(nextRandom, isAvailable,
            $"Falta el fondo especial '{id}'; alternativa común. Motivo original: {reason}");
    }

    private static BattleBackgroundSelection SelectCommon(Func<double> nextRandom,
        Func<string, bool> isAvailable, string reason)
    {
        var available = CommonWeights
            .Where(pair => isAvailable(pair.Key))
            .ToArray();
        if (available.Length == 0)
            return new BattleBackgroundSelection(null, "none", $"No hay fondos comunes disponibles. {reason}");

        var totalWeight = available.Sum(pair => pair.Value);
        var threshold = NextUnit(nextRandom) * totalWeight;
        foreach (var (id, weight) in available)
        {
            threshold -= weight;
            if (threshold < 0d)
                return new BattleBackgroundSelection(id, "common", $"Selección común ponderada ({reason})");
        }

        var last = available[^1].Key;
        return new BattleBackgroundSelection(last, "common", $"Selección común ponderada ({reason})");
    }

    private static bool IsSpecies(BattleBackgroundProfile profile, string speciesId) =>
        profile.SpeciesId.Equals(speciesId, StringComparison.OrdinalIgnoreCase);

    private static bool IsLegendary(BattleBackgroundProfile profile) =>
        LegendarySpecies.Contains(profile.SpeciesId);

    private static double NextUnit(Func<double> nextRandom)
    {
        var value = nextRandom();
        if (double.IsNaN(value) || value <= 0d) return 0d;
        return value >= 1d ? Math.BitDecrement(1d) : value;
    }
}
