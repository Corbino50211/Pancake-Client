using System.Diagnostics;
using System.Windows;
using Pancake.Core;

namespace Pancake.Desktop;

internal static class UpdateRunner
{
    internal static void Start(string stage)
    {
        var start = new ProcessStartInfo(Path.Combine(stage, "PancakeClient.exe")) { UseShellExecute = false, WorkingDirectory = stage };
        foreach (var value in new[] { "--apply-update", Environment.ProcessId.ToString(), AppContext.BaseDirectory }) start.ArgumentList.Add(value);
        _ = Process.Start(start) ?? throw new IOException("The update helper could not start.");
    }
    internal static async Task RunAsync(string[] args)
    {
        var target = args.Length == 3 ? args[2] : "";
        try
        {
            if (args.Length != 3 || !int.TryParse(args[1], out var pid)) throw new IOException("Invalid update request.");
            try
            {
                using var parent = Process.GetProcessById(pid);
                await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            }
            catch (ArgumentException) { }
            using var mutex = new Mutex(true, "Local\\PancakeClient.Desktop", out var first);
            if (!first) throw new IOException("Close the other Pancake Client window before updating.");
            try
            {
                ModLibrary.RequireGameClosed();
                var backup = Path.Combine(ClientStorage.Root, "updates", "backups", Guid.NewGuid().ToString("N"));
                ClientUpdater.Apply(AppContext.BaseDirectory, target, backup);
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(ClientStorage.Root);
            File.AppendAllText(Path.Combine(ClientStorage.Root, "update-errors.log"), $"{DateTime.Now:O} {ex}\n");
            MessageBox.Show("Update could not finish. " + ex.Message, "Pancake Client update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            var exe = Path.Combine(target, "PancakeClient.exe");
            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = target, UseShellExecute = true });
            Application.Current.Shutdown();
        }
    }
}
