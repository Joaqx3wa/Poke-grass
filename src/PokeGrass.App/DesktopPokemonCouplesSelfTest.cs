using System.IO;

namespace PokeGrass;

internal static class DesktopPokemonCouplesSelfTest
{
    internal static string Summary { get; private set; } = "Couples compatibility: not run";

    internal static void Run()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "assets");
        var catalog = PokemonCatalog.Load(Path.Combine(assets, "data", "pokemon-catalog.json"));
        var metadata = PokemonSpeciesMetadataCatalog.Load(
            Path.Combine(assets, "data", "pokemon-species-metadata.json"), catalog);

        Assert(CouplesCompatibilityPolicy.AreCompatible("hypno", "male", "HYPNO", "female"),
            "A base form and female encounter variant with fallback assets should match by species and stored sex.");
        Assert(CouplesCompatibilityPolicy.AreCompatible("gengar", "female", "GENGAR", "male"),
            "Mega and non-Mega species identity should remain compatible.");
        Assert(CouplesCompatibilityPolicy.AreCompatible("pikachu", "male", "pikachu", "female"),
            "Cosmetic variants should use their species identity and stored sex.");
        var specialPairs = new (string MaleId, string FemaleId)[]
        {
            ("NIDORANmA", "NIDORANfE"),
            ("NIDORINO", "NIDORINA"),
            ("NIDOKING", "NIDOQUEEN"),
            ("TAUROS", "MILTANK"),
            ("VOLBEAT", "ILLUMISE"),
            ("LATIOS", "LATIAS"),
            ("MOTHIM", "WORMADAM"),
            ("RUFFLET", "VULLABY"),
            ("BRAVIARY", "MANDIBUZZ"),
            ("IMPIDIMP", "HATENNA"),
            ("MORGREM", "HATTREM"),
            ("GRIMMSNARL", "HATTERENE")
        };
        foreach (var (maleId, femaleId) in specialPairs)
        {
            var male = metadata.Get(maleId);
            var female = metadata.Get(femaleId);
            var maleSex = PokemonSex.Get(male);
            var femaleSex = PokemonSex.Get(female);
            Assert(maleSex == "male" && femaleSex == "female"
                && CouplesCompatibilityPolicy.AreCompatible(male.SpeciesId, maleSex, female.SpeciesId, femaleSex)
                && CouplesCompatibilityPolicy.AreCompatible(female.SpeciesId, femaleSex, male.SpeciesId, maleSex),
                $"The special pair {maleId}/{femaleId} must have curated opposite sexes and match in either order.");
            Assert(!CouplesCompatibilityPolicy.AreCompatible(male.SpeciesId, maleSex, female.SpeciesId, maleSex)
                && !CouplesCompatibilityPolicy.AreCompatible(male.SpeciesId, femaleSex, female.SpeciesId, femaleSex),
                $"The special pair {maleId}/{femaleId} must still reject same-sex pairs.");
        }
        Assert(CouplesCompatibilityPolicy.AreCompatible("tauros", "male", "miltank", "female")
            && CouplesCompatibilityPolicy.AreCompatible("volbeat", "male", "illumise", "female")
            && CouplesCompatibilityPolicy.AreCompatible("nidoking", "male", "nidoqueen", "female"),
            "Tauros/Miltank, Volbeat/Illumise and Nidoking/Nidoqueen must be Couples compatible.");
        Assert(!CouplesCompatibilityPolicy.AreCompatible("hypno", "male", "hypno", "male"),
            "Same-sex Pokémon must not match.");
        Assert(!CouplesCompatibilityPolicy.AreCompatible("hypno", "female", "hypno", "female"),
            "Two female Pokémon must not match.");
        Assert(!CouplesCompatibilityPolicy.AreCompatible("hypno", "male", "slowpoke", "female"),
            "Different species must not match.");
        Assert(!CouplesCompatibilityPolicy.AreCompatible("ditto", "genderless", "ditto", "female"),
            "Genderless Pokémon must not match.");
        Assert(!CouplesCompatibilityPolicy.AreCompatible("", "male", "", "female"),
            "Missing species identity must not match.");
        Summary = "Couples: same-species and 12 special cross-species pairs compatible only for opposite binary sexes; Mega/base and cosmetic variants preserved.";
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
