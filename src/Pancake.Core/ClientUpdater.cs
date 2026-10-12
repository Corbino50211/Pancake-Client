using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pancake.Core;

public sealed record ClientUpdate(string Version, string DownloadUrl, string Sha256);

public static class ClientUpdater
{
    public const string Repository = "Corbino50211/Pancake-Client";
    public const string ManifestUrl = "https://github.com/" + Repository + "/releases/latest/download/update.json";
    public static string InstalledVersion(string directory)
    {
        var file = Path.Combine(directory, "client-version.json");
        return File.Exists(file) ? ClientStorage.Read(file, () => new ClientUpdate("0.0.0", "", "")).Version : "0.0.0";
    }
    public static bool IsNewer(ClientUpdate update, string installed)
    {
        Validate(update);
        return Version.Parse(update.Version) > Version.Parse(installed);
    }
    public static void Validate(ClientUpdate update)
    {
        if (!Version.TryParse(update.Version, out _) || !Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.Host != "github.com" || url.Port != 443 || url.UserInfo.Length != 0
            || !url.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.Ordinal)
            || !url.AbsolutePath.EndsWith("/PancakeClient-win-x64.zip", StringComparison.Ordinal)
            || update.Sha256.Length != 64 || update.Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid Pancake Client update manifest.");
    }
    private static HttpClient Http()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PancakeClient-Updater/1.0");
        return http;
    }
    public static async Task<ClientUpdate?> CheckAsync(CancellationToken token = default)
    {
        using var http = Http();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync(ManifestUrl, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var update = JsonSerializer.Deserialize<ClientUpdate>(await response.Content.ReadAsStringAsync(timeout.Token), ClientStorage.Json)
            ?? throw new InvalidDataException("Empty update manifest.");
        Validate(update); return update;
    }
    public static async Task<string> DownloadAsync(ClientUpdate update, string updateRoot, CancellationToken token = default)
    {
        Validate(update);
        var work = Path.Combine(updateRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        var zip = Path.Combine(work, "download.zip");
        using var http = Http();
        using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 512L * 1024 * 1024) throw new InvalidDataException("Update download is too large.");
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = File.Create(zip))
        {
            var buffer = new byte[65536]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                total += read;
                if (total > 512L * 1024 * 1024) throw new InvalidDataException("Update download is too large.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
        }
        return await Task.Run(() => Stage(zip, Path.Combine(work, "app"), update), token);
    }
    public static string Stage(string zip, string directory, ClientUpdate update)
    {
        Validate(update);
        using (var input = File.OpenRead(zip))
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update checksum failed. The installed app was not changed.");
        SafeArchive.Extract(zip, directory);
        foreach (var name in new[] { "PancakeClient.exe", "PancakeClient.dll", "PancakeClient.runtimeconfig.json", "client-version.json" })
            if (!File.Exists(Path.Combine(directory, name))) throw new InvalidDataException("Update is missing " + name);
        if (InstalledVersion(directory) != update.Version) throw new InvalidDataException("Update package version does not match its manifest.");
        return directory;
    }
    public static void Apply(string stage, string target, string backup)
    {
        stage = Path.GetFullPath(stage); target = Path.GetFullPath(target);
        if (stage.Equals(target, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(target, "PancakeClient.exe")))
            throw new IOException("The update target is not an installed Pancake Client folder.");
        var changed = new List<(string Path, string? Backup)>();
        try
        {
            foreach (var source in Directory.GetFiles(stage, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(stage, source); var destination = Path.Combine(target, relative);
                for (var folder = Path.GetDirectoryName(destination); folder != null; folder = Path.GetDirectoryName(folder))
                {
                    if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("Update target contains a linked directory.");
                    if (folder.Equals(target, StringComparison.OrdinalIgnoreCase)) break;
                }
                if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("Update target contains a linked file.");
                string? saved = null;
                if (File.Exists(destination))
                {
                    saved = Path.Combine(backup, relative); Directory.CreateDirectory(Path.GetDirectoryName(saved)!); File.Copy(destination, saved, false);
                }
                changed.Add((destination, saved));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true);
            }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception>();
            foreach (var entry in changed.AsEnumerable().Reverse())
                try { if (entry.Backup == null) File.Delete(entry.Path); else File.Copy(entry.Backup, entry.Path, true); }
                catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 0) throw new AggregateException("Update failed; some files could not be restored. Backup: " + backup, new[] { failure }.Concat(errors));
            throw;
        }
    }
}
