using System.IO;
using System.Windows.Media;
using NVorbis;

namespace PokeGrass;

/// <summary>Plays each requested effect or cry on its own player so overlapping game sounds mix naturally.</summary>
internal sealed class EncounterAudio : IDisposable
{
    private static readonly object IndependentPlaybackGate = new();
    private static readonly HashSet<MediaPlayer> IndependentPlayers = [];
    private readonly object _gate = new();
    private readonly HashSet<MediaPlayer> _players = [];
    private readonly HashSet<string> _temporaryFiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public void PlayEffect(string relativePath)
    {
        try
        {
            var volume = GameSoundSettings.EffectiveVolume;
            if (volume <= 0) return;
            var path = Asset(relativePath);
            if (!File.Exists(path)) return;
            StartPlayback(path, volume, temporaryFile: null, $"audio-effect:{relativePath}");
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException($"audio-effect:{relativePath}", exception);
        }
    }

    public static void PlayEffectToCompletion(string relativePath)
    {
        MediaPlayer? player = null;
        try
        {
            var volume = GameSoundSettings.EffectiveVolume;
            if (volume <= 0) return;
            var path = Asset(relativePath);
            if (!File.Exists(path)) return;

            player = new MediaPlayer { Volume = volume };
            lock (IndependentPlaybackGate) IndependentPlayers.Add(player);
            player.MediaEnded += (_, _) => FinishIndependentPlayback(player);
            player.MediaFailed += (_, args) =>
            {
                if (args.ErrorException is { } error)
                    EncounterDiagnostics.WriteException($"audio-effect:{relativePath}", error);
                FinishIndependentPlayback(player);
            };
            player.Open(new Uri(path, UriKind.Absolute));
            player.Play();
        }
        catch (Exception exception)
        {
            if (player is not null) FinishIndependentPlayback(player);
            EncounterDiagnostics.WriteException($"audio-effect:{relativePath}", exception);
        }
    }

    private static void FinishIndependentPlayback(MediaPlayer player)
    {
        lock (IndependentPlaybackGate)
        {
            if (!IndependentPlayers.Remove(player)) return;
        }
        try { player.Stop(); } catch { }
        try { player.Close(); } catch { }
    }

    public void PlayCry(string id)
    {
        string? temporaryFile = null;
        try
        {
            var volume = GameSoundSettings.EffectiveVolume;
            if (volume <= 0) return;
            var path = Asset($"sounds/pokemon/{id}.ogg");
            if (!File.Exists(path))
            {
                var baseId = id.Split('_')[0];
                path = Asset($"sounds/pokemon/{baseId}.ogg");
            }
            if (!File.Exists(path)) return;

            var wav = DecodeToWav(path);
            temporaryFile = Path.Combine(Path.GetTempPath(), $"PokéGrass-audio-{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(temporaryFile, wav);
            StartPlayback(temporaryFile, volume, temporaryFile, $"pokemon-cry:{id}");
        }
        catch (Exception exception)
        {
            if (temporaryFile is not null) TryDelete(temporaryFile);
            EncounterDiagnostics.WriteException($"pokemon-cry:{id}", exception);
        }
    }

    private void StartPlayback(string path, double volume, string? temporaryFile, string diagnosticContext)
    {
        var player = new MediaPlayer { Volume = volume };
        lock (_gate)
        {
            if (_disposed)
            {
                player.Close();
                if (temporaryFile is not null) TryDelete(temporaryFile);
                return;
            }
            _players.Add(player);
            if (temporaryFile is not null) _temporaryFiles.Add(temporaryFile);
        }

        player.MediaEnded += (_, _) => FinishPlayback(player, temporaryFile);
        player.MediaFailed += (_, args) =>
        {
            if (args.ErrorException is { } error)
                EncounterDiagnostics.WriteException(diagnosticContext, error);
            FinishPlayback(player, temporaryFile);
        };
        try
        {
            player.Open(new Uri(path, UriKind.Absolute));
            player.Play();
        }
        catch
        {
            FinishPlayback(player, temporaryFile);
            throw;
        }
    }

    private void FinishPlayback(MediaPlayer player, string? temporaryFile)
    {
        lock (_gate)
        {
            if (!_players.Remove(player)) return;
            if (temporaryFile is not null) _temporaryFiles.Remove(temporaryFile);
        }
        try { player.Stop(); } catch { }
        try { player.Close(); } catch { }
        if (temporaryFile is not null) TryDelete(temporaryFile);
    }

    public void Stop()
    {
        MediaPlayer[] players;
        string[] temporaryFiles;
        lock (_gate)
        {
            players = _players.ToArray();
            temporaryFiles = _temporaryFiles.ToArray();
            _players.Clear();
            _temporaryFiles.Clear();
        }
        foreach (var player in players)
        {
            try { player.Stop(); } catch { }
            try { player.Close(); } catch { }
        }
        foreach (var file in temporaryFiles) TryDelete(file);
    }

    private static string Asset(string relativePath) =>
        Path.Combine(AppContext.BaseDirectory, "assets", relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    internal static byte[] DecodeToWav(string path)
    {
        using var reader = new VorbisReader(path);
        var channels = reader.Channels;
        var sampleRate = reader.SampleRate;
        var samples = new List<float>(sampleRate * channels);
        var buffer = new float[8192];
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            samples.AddRange(buffer.AsSpan(0, read).ToArray());

        var pcmBytes = new byte[samples.Count * 2];
        for (var i = 0; i < samples.Count; i++)
        {
            var value = (short)Math.Clamp((int)Math.Round(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
            pcmBytes[i * 2] = (byte)(value & 0xff);
            pcmBytes[i * 2 + 1] = (byte)((value >> 8) & 0xff);
        }

        using var output = new MemoryStream(44 + pcmBytes.Length);
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8);
        writer.Write(36 + pcmBytes.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcmBytes.Length);
        writer.Write(pcmBytes);
        return output.ToArray();
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop();
    }
}
