using System.Net.Http;
using System.Text.Json;

namespace Pancake.Core;

public sealed record CatalogMod(string Name, string Author, string Version, string Description, string PageUrl, string DownloadUrl, string Dependencies, long Downloads);

public sealed class ThunderstoreCatalog : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(3) };
    public ThunderstoreCatalog() => http.DefaultRequestHeaders.UserAgent.ParseAdd("PancakeClient/0.1.0");
    public async Task<List<CatalogMod>> LoadAsync(CancellationToken token)
    {
        using var response = await http.GetAsync("https://thunderstore.io/c/bonelab/api/v1/package/", HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var mods = new List<CatalogMod>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.GetProperty("is_deprecated").GetBoolean()) continue;
            var versions = item.GetProperty("versions");
            if (versions.GetArrayLength() == 0) continue;
            var version = versions.EnumerateArray().FirstOrDefault(x => !x.TryGetProperty("is_active", out var active) || active.GetBoolean());
            if (version.ValueKind == JsonValueKind.Undefined) continue;
            mods.Add(new(item.GetProperty("name").GetString()!, item.GetProperty("owner").GetString()!, version.GetProperty("version_number").GetString()!,
                version.GetProperty("description").GetString() ?? "", item.GetProperty("package_url").GetString()!, version.GetProperty("download_url").GetString()!,
                string.Join(", ", version.GetProperty("dependencies").EnumerateArray().Select(x => x.GetString())), version.GetProperty("downloads").GetInt64()));
        }
        return mods.OrderByDescending(x => x.Downloads).ToList();
    }
    public async Task DownloadAsync(CatalogMod mod, string destination, IProgress<string> progress, CancellationToken token)
    {
        var uri = new Uri(mod.DownloadUrl);
        if (uri.Scheme != "https" || !(uri.Host == "thunderstore.io" || uri.Host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unexpected download source.");
        var temporary = destination + ".partial";
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(token);
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > 2L * 1024 * 1024 * 1024) throw new IOException("Download exceeds the 2 GiB limit.");
                    await target.WriteAsync(buffer.AsMemory(0, count), token);
                    progress.Report($"Downloading {mod.Name} · {total / 1048576d:F1} MiB");
                }
            }
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() => http.Dispose();
}
