using System.IO;

namespace PokeGrass;

internal static class BattleBackgroundSelectorSelfTest
{
    internal static string Summary { get; private set; } = string.Empty;

    internal static void Run()
    {
        var pikachu = Profile("PIKACHU", "pikachu", ["electric"]);
        var zapdos = Profile("ZAPDOS", "zapdos", ["electric", "flying"]);
        var arceus = Profile("ARCEUS_18", "arceus", ["fairy"]);
        var dialga = Profile("DIALGA", "dialga", ["steel", "dragon"]);
        var palkia = Profile("PALKIA", "palkia", ["water", "dragon"]);
        var megaCharizard = Profile("CHARIZARD_1", "charizard", ["fire", "dragon"], mega: true);
        var megaLucario = Profile("LUCARIO_1", "lucario", ["fighting", "steel"], mega: true);
        var charizard = Profile("CHARIZARD", "charizard", ["fire", "flying"]);
        var moltres = Profile("MOLTRES", "moltres", ["flying", "fire"]);
        var ekans = Profile("EKANS", "ekans", ["poison"]);
        var koffing = Profile("KOFFING", "koffing", ["poison"]);
        var mew = Profile("MEW", "mew", ["psychic"]);
        var celebi = Profile("CELEBI", "celebi", ["psychic", "grass"]);
        var ordinaryPikachu = Profile("PIKACHU", "pikachu", ["electric"]);
        var charmander = Profile("CHARMANDER", "charmander", ["fire"]);

        Assert(Choose(arceus, pikachu, [0d]).BackgroundId == "retro",
            "La tirada retro debe tener prioridad incluso sobre Arceus.");
        Assert(Choose(arceus, pikachu, [.5d]).BackgroundId == "god",
            "Arceus debe seleccionar god por especie base.");
        Assert(Choose(arceus, dialga, [.5d]).BackgroundId == "god",
            "Arceus debe prevalecer sobre el fondo legendary.");
        Assert(Choose(dialga, palkia, [.5d]).BackgroundId == "legendary",
            "Dos especies legendarias deben seleccionar legendary.");
        Assert(Choose(megaCharizard, megaLucario, [.5d]).BackgroundId == "mega",
            "Dos instancias Mega deben seleccionar mega.");
        Assert(Choose(megaCharizard, pikachu, [.5d, .2d]).Category == "common",
            "Una sola instancia Mega no debe activar el fondo mega.");
        Assert(Choose(pikachu, zapdos, [.5d]).BackgroundId == "electric",
            "Un tipo compartido debe seleccionar ese fondo.");
        Assert(Choose(charizard, moltres, [.5d, .1d]).BackgroundId == "fire"
            && Choose(charizard, moltres, [.5d, .9d]).BackgroundId == "flying",
            "Dos tipos compartidos deben poder seleccionar cualquiera de los dos.");
        Assert(Choose(ekans, koffing, [.5d]).BackgroundId == "poison",
            "Poison debe tener soporte de fondo.");
        Assert(Choose(mew, celebi, [.5d]).BackgroundId == "psychic",
            "Los Pokémon míticos no deben activar la categoría legendary.");
        Assert(Choose(ordinaryPikachu, charmander, [.5d, .1d]).BackgroundId == "poke1"
            && Choose(ordinaryPikachu, charmander, [.5d, .7d]).BackgroundId == "poke2"
            && Choose(ordinaryPikachu, charmander, [.5d, .85d]).BackgroundId == "poke3"
            && Choose(ordinaryPikachu, charmander, [.5d, .99d]).BackgroundId == "poke4",
            "Los fondos comunes deben conservar los pesos 55/25/15/5.");

        var alternateType = BattleBackgroundSelector.Select(
            Profile("A", "species-a", ["fire", "flying"]),
            Profile("B", "species-b", ["flying", "fire"]), Sequence(.5d), id => id == "flying");
        Assert(alternateType.BackgroundId == "flying",
            "Si falta un fondo de tipo compartido debe elegirse el otro tipo disponible.");

        var missingSpecial = Choose(arceus, pikachu, [.5d, .3d], id => id.StartsWith("poke", StringComparison.OrdinalIgnoreCase));
        Assert(missingSpecial.Category == "common" && missingSpecial.BackgroundId == "poke1",
            "Si falta un fondo especial se debe usar un fondo común disponible.");

        var redistributed = Choose(ordinaryPikachu, charmander, [.5d, .7d], id => id is "poke2" or "poke3");
        Assert(redistributed.BackgroundId == "poke3",
            "Los pesos comunes deben redistribuirse proporcionalmente entre los archivos disponibles.");
        var noBackground = Choose(ordinaryPikachu, charmander, [.5d, .7d], _ => false);
        Assert(noBackground.BackgroundId is null && noBackground.Category == "none",
            "Sin fondos disponibles, la selección debe permitir continuar sin arena.");

        const int trials = 100_000;
        var random = new Random(0xBACC);
        var retroCount = 0;
        var commonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < trials; i++)
        {
            var selection = BattleBackgroundSelector.Select(ordinaryPikachu, charmander,
                random.NextDouble, _ => true);
            if (selection.BackgroundId == "retro") retroCount++;
            else commonCounts[selection.BackgroundId!] = commonCounts.GetValueOrDefault(selection.BackgroundId!) + 1;
        }

        Assert(retroCount is >= 850 and <= 1150,
            $"Retro debe permanecer cerca de 1 % por combate; resultado {retroCount}/{trials}.");
        var commonTotal = trials - retroCount;
        Assert(WithinPercent(commonCounts.GetValueOrDefault("poke1"), commonTotal, 55, 1.0)
            && WithinPercent(commonCounts.GetValueOrDefault("poke2"), commonTotal, 25, 1.0)
            && WithinPercent(commonCounts.GetValueOrDefault("poke3"), commonTotal, 15, 1.0)
            && WithinPercent(commonCounts.GetValueOrDefault("poke4"), commonTotal, 5, .7),
            "La simulación de selección común debe aproximarse a 55/25/15/5.");

        var sharedTypeRandom = new Random(0x71F3);
        var fireCount = 0;
        const int sharedTypeTrials = 10_000;
        for (var i = 0; i < sharedTypeTrials; i++)
            if (BattleBackgroundSelector.Select(charizard, moltres, sharedTypeRandom.NextDouble, _ => true).BackgroundId == "fire")
                fireCount++;
        Assert(fireCount is >= 4500 and <= 5500,
            $"Dos tipos compartidos deben elegirse 50/50; fire obtuvo {fireCount}/{sharedTypeTrials}.");

        var globalRandom = new Random(0x1A2B3C);
        var priorityBattles = new[]
        {
            (arceus, pikachu), (dialga, palkia), (megaCharizard, megaLucario), (ordinaryPikachu, charmander)
        };
        var globalRetroCount = 0;
        for (var i = 0; i < trials; i++)
        {
            var pair = priorityBattles[i % priorityBattles.Length];
            if (BattleBackgroundSelector.Select(pair.Item1, pair.Item2, globalRandom.NextDouble, _ => true).BackgroundId == "retro")
                globalRetroCount++;
        }
        Assert(globalRetroCount is >= 850 and <= 1150,
            $"Retro debe conservar 1 % entre categorías distintas; resultado {globalRetroCount}/{trials}.");

        var availableBackgroundCount = BattleArenaWindow.ValidateBackgroundAssets();
        Assert(availableBackgroundCount == 26,
            $"Se esperaban 26 identificadores de fondo disponibles; se encontraron {availableBackgroundCount}.");
        Summary = $"Battle backgrounds: retro common-pair {retroCount}/{trials}, retro mixed-priority {globalRetroCount}/{trials}; common weights 55/25/15/5; shared types 50/50; loaded assets {availableBackgroundCount}; priority/fallback scenarios passed.";
    }

    private static BattleBackgroundSelection Choose(BattleBackgroundProfile first, BattleBackgroundProfile second,
        double[] rolls, Func<string, bool>? isAvailable = null) =>
        BattleBackgroundSelector.Select(first, second, Sequence(rolls), isAvailable ?? (_ => true));

    private static Func<double> Sequence(params double[] values)
    {
        var index = 0;
        return () => values[Math.Min(index++, values.Length - 1)];
    }

    private static BattleBackgroundProfile Profile(string id, string species, IReadOnlyList<string> types, bool mega = false) =>
        new(id, species, types, mega);

    private static bool WithinPercent(int observed, int total, double expectedPercent, double tolerancePercent) =>
        total > 0 && Math.Abs(observed * 100d / total - expectedPercent) <= tolerancePercent;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
