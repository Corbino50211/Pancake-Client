using System.IO.Compression;

namespace Pancake.Core;

public static class SafeArchive
{
    public static void Extract(string zipPath, string destination, long maximumBytes = 1024L * 1024 * 1024)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        long total = 0;
        if (zip.Entries.Count > 20000) throw new InvalidDataException("Archive contains too many files.");
        foreach (var entry in zip.Entries)
        {
            total = checked(total + entry.Length);
            if (total > maximumBytes) throw new InvalidDataException("Archive exceeds the extraction size limit.");
            var normalized = entry.FullName.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Any(x => x is ".." || x.EndsWith(' ') || x.EndsWith('.')))
                throw new InvalidDataException("Unsafe path in archive: " + entry.FullName);
            // Reject Unix symlink entries.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Archive symlinks are not supported.");
            var target = Path.GetFullPath(Path.Combine(root, normalized));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archive path escapes its destination.");
            if (normalized.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, false);
        }
    }
}
