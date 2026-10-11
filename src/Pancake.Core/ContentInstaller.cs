namespace Pancake.Core;

/// <summary>Imports content-only ZIPs. Code mod installation remains manual until dependency resolution is implemented.</summary>
public static class ContentInstaller
{
    public static string Install(string archive, string contentDirectory)
    {
        ModLibrary.RequireGameClosed();
        if (string.IsNullOrWhiteSpace(contentDirectory)) throw new ArgumentException("Set the content Mods folder first.");
        // Stage next to Mods so a successful install is a same-volume atomic directory rename.
        var parent = Path.GetDirectoryName(Path.GetFullPath(contentDirectory).TrimEnd(Path.DirectorySeparatorChar))!;
        var stage = Path.Combine(parent, ".pancake-stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            SafeArchive.Extract(archive, stage);
            var pallets = Directory.GetFiles(stage, "pallet.json", SearchOption.AllDirectories);
            if (pallets.Length != 1) throw new InvalidDataException("Import one content mod at a time. This ZIP must contain exactly one pallet.json. Code mods use the manual installation instructions.");
            var source = Path.GetDirectoryName(pallets[0])!;
            if (Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Any(x => Path.GetExtension(x).Equals(".dll", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(x).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("This archive contains executable code. Use the mod author's code-mod installation instructions.");
            var name = source == stage ? Path.GetFileNameWithoutExtension(archive) : Path.GetFileName(source);
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.')) throw new InvalidDataException("Invalid mod folder name.");
            Directory.CreateDirectory(contentDirectory);
            var destination = Path.Combine(contentDirectory, name);
            if (Directory.Exists(destination) || Directory.Exists(contentDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".pancake-disabled/" + name))
                throw new IOException("This mod folder already exists. Version replacement is not enabled in this first release.");
            ModLibrary.RequireGameClosed();
            Directory.Move(source, destination);
            return destination;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
