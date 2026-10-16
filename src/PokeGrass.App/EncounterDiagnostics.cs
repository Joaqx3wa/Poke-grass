using System.IO;
using System.Text;

namespace PokeGrass;

internal static class EncounterDiagnostics
{
    private static readonly object Gate = new();

    internal static string LogPath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("POKEGRASS_LOG_DIRECTORY");
            var directory = string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokéGrass", "logs")
                : Path.GetFullPath(overridePath);
            return Path.Combine(directory, "encounters.log");
        }
    }

    internal static void Write(string stage, string message)
        => Append($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [{stage}] {message}");

    internal static void WriteException(string stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var details = new StringBuilder()
            .AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [{stage}] EXCEPTION")
            .AppendLine($"Type: {exception.GetType().FullName}")
            .AppendLine($"Message: {exception.Message}")
            .AppendLine("Stack trace:")
            .AppendLine(exception.ToString())
            .ToString();
        Append(details);
    }

    private static void Append(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, text + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            System.Diagnostics.Trace.WriteLine($"PokéGrass encounter logging failed: {logException}");
        }
    }
}
