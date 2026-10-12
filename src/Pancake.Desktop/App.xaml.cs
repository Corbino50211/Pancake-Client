using System.Windows;
using Pancake.Core;

namespace Pancake.Desktop;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.FirstOrDefault() == "--apply-update")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = UpdateRunner.RunAsync(e.Args);
            return;
        }
        if (e.Args.Contains("--smoke-test"))
        {
            Directory.CreateDirectory("artifacts");
            DispatcherUnhandledException += (_, args) =>
            {
                File.WriteAllText("artifacts/smoke-error.txt", args.Exception.ToString());
                args.Handled = true;
                Shutdown(1);
            };
            try
            {
                var window = new MainWindow();
                window.Show();
                window.Dispatcher.BeginInvoke(new Action(() => { window.CaptureSmokeScreenshots(); window.Close(); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                File.WriteAllText("artifacts/smoke-error.txt", ex.ToString());
                Shutdown(1);
            }
            return;
        }
        instance = new Mutex(true, "Local\\PancakeClient.Desktop", out var first);
        if (!first) { MessageBox.Show("Pancake Client is already running."); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            Directory.CreateDirectory(ClientStorage.Root);
            File.AppendAllText(Path.Combine(ClientStorage.Root, "errors.log"), $"{DateTime.Now:O} {args.Exception}\n");
            MessageBox.Show(args.Exception.Message, "Pancake Client", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
        new MainWindow().Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
