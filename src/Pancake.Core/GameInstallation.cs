namespace Pancake.Core;

public static class GameInstallation
{
    private static readonly string[] ExecutableNames = { "BONELAB_Steam_Windows64.exe", "BONELAB.exe" };
    public static string? FindExecutable(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        return ExecutableNames.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
    }
    public static bool IsRunning()
    {
        foreach (var name in ExecutableNames)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name));
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }
}
