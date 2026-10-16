using System;
using System.IO;
using System.Linq;

namespace PokeGrass;

internal static class SaveBackupSelfTest
{
    public static string Summary { get; private set; } = "Not run";

    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PokéGrass-backup-self-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var savePath = Path.Combine(root, "save.json");
            var save = GameSaveStore.LoadOrCreate(savePath);
            var automatic = SaveBackupService.ListValid(savePath);
            Assert(automatic.Count == 1 && automatic[0].Kind == "automatic", "New valid save receives a verified automatic snapshot.");

            var manual = SaveBackupService.CreateManual(savePath);
            Assert(SaveBackupService.ListValid(savePath).Any(item => item.DirectoryPath == manual.DirectoryPath && item.Kind == "manual"),
                "Manual snapshots are listed separately from automatic snapshots.");

            File.WriteAllText(savePath, "{damaged json");
            SaveBackupService.Restore(savePath, manual);
            _ = GameSaveStore.LoadOrCreate(savePath);
            Assert(Directory.EnumerateFiles(root, "save.before-restore-*.json").Any(), "Restore preserves the prior active file for diagnosis.");

            var copiedSave = Path.Combine(manual.DirectoryPath, "save.json");
            File.AppendAllText(copiedSave, " ");
            Assert(SaveBackupService.ListValid(savePath).All(item => item.DirectoryPath != manual.DirectoryPath),
                "Modified backup data fails integrity validation.");
            Summary = "Automatic/manual snapshots, restore with original preservation, and SHA-256 validation passed.";
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException($"Save backup self-test failed: {message}");
    }
}
