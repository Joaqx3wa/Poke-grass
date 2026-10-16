using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace PokeGrass;

internal sealed record SaveBackupInfo(string DirectoryPath, string SavePath, DateTime CreatedUtc, string Kind,
    string AppVersion, int SaveFormatVersion)
{
    public string DisplayName => $"{CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {Kind switch { "manual" => "Manual", "migration" => "Previo a migración", _ => "Automática" }} · formato {SaveFormatVersion}";
}

internal static class SaveBackupService
{
    private const int AutomaticRetention = 10;
    private static readonly TimeSpan AutomaticMinimumInterval = TimeSpan.FromMinutes(5);
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private sealed record BackupMetadata(int Version, string Kind, DateTime CreatedUtc, string AppVersion,
        int SaveFormatVersion, string SaveSha256, string SaveFile);

    public static string GetBackupDirectory(string savePath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(savePath))!, "Backups");

    public static SaveBackupInfo CreateManual(string savePath) => Create(savePath, "manual", force: true)
        ?? throw new InvalidOperationException("No se pudo crear la copia de seguridad.");

    public static SaveBackupInfo? CreateAutomatic(string savePath, bool force = false)
    {
        lock (Gate)
        {
            var latestAutomatic = ListValid(savePath).Where(item => item.Kind == "automatic")
                .OrderByDescending(item => item.CreatedUtc).FirstOrDefault();
            if (!force && latestAutomatic is not null && DateTime.UtcNow - latestAutomatic.CreatedUtc < AutomaticMinimumInterval)
                return null;
            var created = CreateLocked(savePath, "automatic");
            RotateAutomatic(savePath);
            return created;
        }
    }

    public static SaveBackupInfo CreateMigrationBackup(string savePath)
    {
        lock (Gate) return CreateLocked(savePath, "migration");
    }

    public static IReadOnlyList<SaveBackupInfo> ListValid(string savePath)
    {
        var root = GetBackupDirectory(savePath);
        if (!Directory.Exists(root)) return [];
        var result = new List<SaveBackupInfo>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            try
            {
                var metadataPath = Path.Combine(directory, "backup.json");
                var copiedSavePath = Path.Combine(directory, "save.json");
                if (!File.Exists(metadataPath) || !File.Exists(copiedSavePath)) continue;
                var metadata = JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(metadataPath), JsonOptions);
                if (metadata is null || metadata.Version != 1 || metadata.SaveFile != "save.json") continue;
                var bytes = File.ReadAllBytes(copiedSavePath);
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(metadata.SaveSha256), SHA256.HashData(bytes))) continue;
                using var document = JsonDocument.Parse(bytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                result.Add(new SaveBackupInfo(directory, copiedSavePath, metadata.CreatedUtc, metadata.Kind,
                    metadata.AppVersion, metadata.SaveFormatVersion));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException)
            {
                // Invalid or incomplete snapshots are ignored and preserved for diagnosis.
            }
        }
        return result.OrderByDescending(item => item.CreatedUtc).ToArray();
    }

    public static void Restore(string savePath, SaveBackupInfo backup)
    {
        lock (Gate)
        {
            var verified = ListValid(savePath).FirstOrDefault(item =>
                string.Equals(item.DirectoryPath, backup.DirectoryPath, StringComparison.OrdinalIgnoreCase));
            if (verified is null) throw new InvalidDataException("La copia seleccionada no existe o no pasó la verificación de integridad.");

            var target = Path.GetFullPath(savePath);
            var targetDirectory = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(targetDirectory);
            if (File.Exists(target))
            {
                var corruptOrPrevious = Path.Combine(targetDirectory,
                    $"save.before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                File.Copy(target, corruptOrPrevious, overwrite: false);
                try
                {
                    using var stream = File.OpenRead(corruptOrPrevious);
                    using var document = JsonDocument.Parse(stream);
                    CreateLocked(target, "manual");
                }
                catch (JsonException)
                {
                    // Preserve the unreadable original above; never try to normalize or replace it before confirmation.
                }
            }

            var temporary = target + $".restore-{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(verified.SavePath, temporary, overwrite: false);
                using (var stream = File.OpenRead(temporary))
                using (var document = JsonDocument.Parse(stream))
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("La copia no contiene una partida válida.");
                if (File.Exists(target)) File.Replace(temporary, target, null, ignoreMetadataErrors: true);
                else File.Move(temporary, target);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static SaveBackupInfo? Create(string savePath, string kind, bool force)
    {
        lock (Gate) return CreateLocked(savePath, kind);
    }

    private static SaveBackupInfo CreateLocked(string savePath, string kind)
    {
        var source = Path.GetFullPath(savePath);
        if (!File.Exists(source)) throw new FileNotFoundException("No se encontró la partida para copiar.", source);
        var bytes = File.ReadAllBytes(source);
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("La partida no es un objeto JSON válido.");
        var format = document.RootElement.TryGetProperty("Version", out var version) && version.TryGetInt32(out var parsedVersion)
            ? parsedVersion : 0;
        var created = DateTime.UtcNow;
        var root = GetBackupDirectory(source);
        Directory.CreateDirectory(root);
        var name = $"{kind}-{created:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var staging = Path.Combine(root, $".pending-{Guid.NewGuid():N}");
        var destination = Path.Combine(root, name);
        try
        {
            Directory.CreateDirectory(staging);
            var stagedSave = Path.Combine(staging, "save.json");
            using (var file = new FileStream(stagedSave, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            var metadata = new BackupMetadata(1, kind, created, typeof(GameSaveStore).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                format, Convert.ToHexString(SHA256.HashData(bytes)), "save.json");
            var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions);
            using (var file = new FileStream(Path.Combine(staging, "backup.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                file.Write(metadataBytes);
                file.Flush(flushToDisk: true);
            }
            Directory.Move(staging, destination);
            return new SaveBackupInfo(destination, Path.Combine(destination, "save.json"), created, kind,
                metadata.AppVersion, format);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void RotateAutomatic(string savePath)
    {
        var automatic = ListValid(savePath).Where(item => item.Kind == "automatic")
            .OrderByDescending(item => item.CreatedUtc).ToArray();
        foreach (var expired in automatic.Skip(AutomaticRetention))
        {
            try { Directory.Delete(expired.DirectoryPath, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
