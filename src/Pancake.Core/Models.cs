using System.Text.Json;

namespace Pancake.Core;

public sealed class ClientSettings
{
    public string GameDirectory { get; set; } = "";
    public string ContentDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Stress Level Zero", "BONELAB", "Mods");
    public string ObsUrl { get; set; } = "ws://127.0.0.1:4455";
    public string MicrophoneSource { get; set; } = "Mic/Aux";
    public bool AllowInGameStreaming { get; set; }
    public string ActiveProfile { get; set; } = "Current setup";
    public string CameraMode { get; set; } = "First person";
    public double CameraFov { get; set; } = 70;
    public string ScreenshotDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pancake Client");
}

public static class ClientStorage
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PancakeClient");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static T Read<T>(string path, Func<T> fallback)
    {
        if (!File.Exists(path)) return fallback();
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty settings file: " + path);
    }
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, path, true);
    }
}

public sealed record ModEntry(string Id, string Name, string Kind, string Path, bool Enabled)
{
    public string State => Enabled ? "Enabled" : "Disabled";
}
public sealed record ProfileEntry(string Id, bool Enabled);
public sealed record ModProfile(string Name, string GameDirectory, string ContentDirectory, List<ProfileEntry> Mods);
