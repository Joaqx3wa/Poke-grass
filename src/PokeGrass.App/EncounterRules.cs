namespace PokeGrass;

internal static class EncounterRules
{
    public static int NextIntervalSeconds(Random random) => random.Next(180, 481);
    public static bool RollShiny(Random random) => random.Next(512) == 0;
    public static bool RollAlpha(Random random) => random.Next(100) < 2;
}
