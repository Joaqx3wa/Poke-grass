using System.IO;
using System.Text.Json;

namespace PokeGrass;

internal static class DesktopVisualScaleService
{
    private sealed record Preference(double Scale);
    private static readonly object Gate = new();
    internal static IReadOnlyList<double> AllowedScales { get; } = [1d, 1.5d, 2d];
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "desktop-visual-scale.json");
    private static double _current = Load();

    internal static event Action<double>? Changed;
    internal static double Current { get { lock (Gate) return _current; } }

    internal static void Set(double scale)
    {
        scale = Normalize(scale);
        lock (Gate)
        {
            if (Math.Abs(_current - scale) < .001d) return;
            _current = scale;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Preference(scale), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception exception)
            {
                EncounterDiagnostics.WriteException("desktop-visual-scale-save", exception);
            }
        }
        Changed?.Invoke(scale);
    }

    private static double Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return 1d;
            return Normalize(JsonSerializer.Deserialize<Preference>(File.ReadAllText(SettingsPath))?.Scale ?? 1d);
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException("desktop-visual-scale-load", exception);
            return 1d;
        }
    }

    private static double Normalize(double value) => AllowedScales
        .FirstOrDefault(scale => Math.Abs(scale - value) < .001d, 1d);
}
