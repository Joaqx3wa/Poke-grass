using System.IO;

namespace PokeGrass;

// Windows releases this cross-process file lock even after a crash.
internal static class SingleInstanceGate
{
    internal static FileStream? TryAcquire(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            return new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
        {
            return null;
        }
    }
}
