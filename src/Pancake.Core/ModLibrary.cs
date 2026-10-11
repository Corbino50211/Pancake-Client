using System.Diagnostics;
using System.Text.Json;

namespace Pancake.Core;

public sealed class ModLibrary
{
    private readonly ClientSettings settings;
    public ModLibrary(ClientSettings settings) => this.settings = settings;
    public static bool GameRunning
    {
        get
        {
            var processes = Process.GetProcessesByName("BONELAB");
            try { return processes.Length > 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }
    public static void RequireGameClosed()
    {
        if (GameRunning) throw new InvalidOperationException("Close BONELAB before changing mods or profiles.");
    }
    public List<ModEntry> Scan()
    {
        var result = new List<ModEntry>();
        if (Directory.Exists(settings.GameDirectory))
        {
            ScanCode(Path.Combine(settings.GameDirectory, "Mods"), "Code mod", "code", result);
            ScanCode(Path.Combine(settings.GameDirectory, "Plugins"), "Plugin", "plugin", result);
        }
        if (Directory.Exists(settings.ContentDirectory))
            foreach (var path in Directory.EnumerateDirectories(settings.ContentDirectory))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                var name = Path.GetFileName(path);
                if (name.StartsWith('.')) continue;
                result.Add(new("content/" + name, ContentTitle(path, name), "Content", path, true));
            }
        var disabled = ContentDisabledDirectory;
        if (Directory.Exists(disabled))
            foreach (var path in Directory.EnumerateDirectories(disabled))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                var name = Path.GetFileName(path);
                result.Add(new("content/" + name, ContentTitle(path, name), "Content", path, false));
            }
        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    private string ContentDisabledDirectory => settings.ContentDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".pancake-disabled";
    private static string ContentTitle(string path, string fallback)
    {
        try
        {
            var manifest = Path.Combine(path, "pallet.json");
            if (!File.Exists(manifest)) return fallback;
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            if (json.RootElement.TryGetProperty("title", out var title)) return title.GetString() ?? fallback;
        }
        catch (JsonException) { }
        catch (IOException) { }
        return fallback;
    }
    private static void ScanCode(string directory, string kind, string prefix, List<ModEntry> target)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            var name = Path.GetFileName(path);
            var enabled = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            if (!enabled && !name.EndsWith(".dll.pancake-disabled", StringComparison.OrdinalIgnoreCase)) continue;
            var clean = enabled ? name : name[..^".pancake-disabled".Length];
            target.Add(new(prefix + "/" + clean, clean, kind, path, enabled));
        }
    }
    public void SetEnabled(ModEntry mod, bool enabled)
    {
        RequireGameClosed();
        // Resolve against a new inventory: never accept an arbitrary path from a profile.
        var current = Scan().SingleOrDefault(x => x.Id == mod.Id) ?? throw new FileNotFoundException("Mod is no longer installed.");
        if (current.Enabled == enabled) return;
        if (current.Kind == "Content")
        {
            var root = enabled ? settings.ContentDirectory : ContentDisabledDirectory;
            Directory.CreateDirectory(root);
            var destination = Path.Combine(root, Path.GetFileName(current.Path));
            if (Directory.Exists(destination)) throw new IOException("A folder with that name already exists in the destination.");
            Directory.Move(current.Path, destination);
        }
        else
        {
            var destination = enabled ? current.Path[..^".pancake-disabled".Length] : current.Path + ".pancake-disabled";
            File.Move(current.Path, destination); // No overwrite: conflicts must be resolved explicitly.
        }
    }
    public ModProfile Capture(string name) => new(name, settings.GameDirectory, settings.ContentDirectory, Scan().Select(x => new ProfileEntry(x.Id, x.Enabled)).ToList());
    public int Apply(ModProfile profile)
    {
        RequireGameClosed();
        var installed = Scan().ToDictionary(x => x.Id);
        var changes = new List<ModEntry>();
        try
        {
            foreach (var entry in profile.Mods)
                if (installed.TryGetValue(entry.Id, out var mod) && mod.Enabled != entry.Enabled)
                {
                    SetEnabled(mod, entry.Enabled);
                    changes.Add(mod);
                }
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var mod in changes.AsEnumerable().Reverse())
                try { SetEnabled(mod, mod.Enabled); } catch (Exception ex) { rollbackErrors.Add(ex); }
            if (rollbackErrors.Count > 0) throw new AggregateException("Profile failed and some files could not be restored. Inspect Installed Mods.", new[] { failure }.Concat(rollbackErrors));
            throw;
        }
        return profile.Mods.Count(x => !installed.ContainsKey(x.Id));
    }
}
