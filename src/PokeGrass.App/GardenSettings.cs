using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PokeGrass;

internal sealed record GardenSettings(int X = 24, int Y = 24, int Scale = 3, bool IsLocked = false)
{
    public static IReadOnlyList<int> AllowedScales { get; } = Array.AsReadOnly([1, 2, 3, 4]);

    private static string SettingsPath => Environment.GetEnvironmentVariable("POKEGRASS_SETTINGS_PATH") is { Length: > 0 } overridePath
        ? Path.GetFullPath(overridePath)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "garden-settings.json");

    public static GardenSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new GardenSettings();

            var loaded = JsonSerializer.Deserialize<GardenSettings>(File.ReadAllText(SettingsPath));
            return loaded is null
                ? new GardenSettings()
                : loaded with { Scale = NormalizeScale(loaded.Scale) };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"PokéGrass settings could not be loaded: {exception.Message}");
            return new GardenSettings();
        }
    }

    public static int NormalizeScale(int scale) =>
        AllowedScales.Contains(scale) ? scale : 3;

    public static void Save(GardenSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);

            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"PokéGrass settings could not be saved: {exception.Message}");
        }
    }
}
