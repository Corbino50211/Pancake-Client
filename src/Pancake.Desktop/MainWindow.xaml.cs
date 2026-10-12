using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Pancake.Core;

namespace Pancake.Desktop;

public partial class MainWindow : Window
{
    private ClientSettings settings = new();
    private readonly ObsClient obs = new();
    private readonly CompanionBridge bridge = new();
    private readonly ThunderstoreCatalog catalog = new();
    private readonly CancellationTokenSource closingToken = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private List<ModEntry> mods = new();
    private List<CatalogMod> catalogMods = new();
    private List<string> scenes = new();
    private string rawLog = "";
    private BitmapSource? lastFrame;
    private Window? captureWindow;
    private Image? captureImage;
    private bool ready, busy, polling, closing, canClose;
    private static string SettingsFile => Path.Combine(ClientStorage.Root, "settings.json");
    private static string ProfilesFolder => Path.Combine(ClientStorage.Root, "profiles");

    public MainWindow()
    {
        InitializeComponent();
        try { settings = ClientStorage.Read(SettingsFile, () => new ClientSettings()); }
        catch (Exception ex)
        {
            if (File.Exists(SettingsFile)) File.Copy(SettingsFile, SettingsFile + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            StatusText.Text = "Settings could not be read; the original was preserved. " + ex.Message;
        }
        GamePath.Text = settings.GameDirectory;
        ContentPath.Text = settings.ContentDirectory;
        ScreenshotPath.Text = settings.ScreenshotDirectory;
        ObsAddress.Text = settings.ObsUrl;
        MicSource.Text = settings.MicrophoneSource;
        AllowStreaming.IsChecked = settings.AllowInGameStreaming;
        CameraMode.SelectedIndex = Array.IndexOf(new[] { "First person", "Right hand", "Stationary", "Follow" }, settings.CameraMode);
        if (CameraMode.SelectedIndex < 0) CameraMode.SelectedIndex = 0;
        FovSlider.Value = Math.Clamp(settings.CameraFov, 30, 110);
        bridge.ConnectionChanged += connected => Ui(() =>
        {
            BridgeBadge.Text = connected ? "Companion connected" : "Companion offline";
            if (!connected)
            {
                CameraStatus.Text = "Companion disconnected. Waiting for BONELAB.";
                lastFrame = null;
                if (captureImage != null) captureImage.Source = null;
            }
            else _ = SendObsStatusSafeAsync("Desktop connected");
        });
        bridge.Error += error => Ui(() => StatusText.Text = "Companion: " + error);
        bridge.Message += message => Ui(() => HandleCompanionMessage(message));
        obs.ConnectionChanged += status => Ui(() => { ObsStatus.Text = status; ObsHomeStatus.Text = obs.Connected ? "Connected" : "Offline"; });
        timer.Tick += async (_, _) => await PollAsync();
        ready = true;
        ShowPage(0);
    }
    internal void CaptureSmokeScreenshots()
    {
        Directory.CreateDirectory("artifacts/screenshots");
        foreach (var index in new[] { 0, 2, 6, 8 })
        {
            Navigation.SelectedIndex = index;
            UpdateLayout();
            var body = (FrameworkElement)Content;
            var bitmap = new RenderTargetBitmap((int)body.ActualWidth, (int)body.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(body);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create($"artifacts/screenshots/page-{index}.png"); png.Save(file);
        }
    }
    private void Ui(Action action)
    {
        if (!closing && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(action);
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        bridge.Start();
        timer.Start();
        await RunAsync(() => { RefreshMods(); RefreshProfiles(); return Task.CompletedTask; });
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (canClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        timer.Stop();
        closingToken.Cancel();
        captureWindow?.Close();
        await bridge.DisposeAsync();
        await obs.DisposeAsync();
        catalog.Dispose();
        canClose = true;
        Close();
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (busy || closing) return;
        busy = true;
        Pages.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { if (!closing) StatusText.Text = "Operation timed out or was cancelled."; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { busy = false; if (!closing) Pages.IsEnabled = true; }
    }
    private void Navigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready) ShowPage(Navigation.SelectedIndex);
    }
    private void ShowPage(int index)
    {
        if (Pages == null) return;
        for (var i = 0; i < Pages.Children.Count; i++) Pages.Children[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Persist() => ClientStorage.Write(SettingsFile, settings);
    private void RefreshMods()
    {
        mods = new ModLibrary(settings).Scan();
        FilterMods();
        ModCount.Text = mods.Count.ToString();
        EnabledCount.Text = mods.Count(x => x.Enabled).ToString();
        ProfileBadge.Text = settings.ActiveProfile;
        var found = GameInstallation.FindExecutable(settings.GameDirectory) != null;
        GameStatus.Text = found ? "BONELAB is configured. Your current mod setup is ready to launch." : "Choose the BONELAB game folder in Settings.";
        LoaderStatus.Text = "MelonLoader: " + (Directory.Exists(Path.Combine(settings.GameDirectory, "MelonLoader")) ? "found" : "not found") + "  ·  BoneLib: " + (File.Exists(Path.Combine(settings.GameDirectory, "Mods", "BoneLib.dll")) ? "found" : "not found");
    }
    private void FilterMods() => ModsGrid.ItemsSource = mods.Where(x => x.Name.Contains(ModSearch.Text, StringComparison.OrdinalIgnoreCase) || x.Kind.Contains(ModSearch.Text, StringComparison.OrdinalIgnoreCase)).ToList();
    private void ModSearch_TextChanged(object sender, TextChangedEventArgs e) { if (ready) FilterMods(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(() => { RefreshMods(); StatusText.Text = "Mod inventory refreshed."; return Task.CompletedTask; });
    private async void ToggleMod_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (ModsGrid.SelectedItem is not ModEntry mod) throw new InvalidOperationException("Select a mod first.");
        new ModLibrary(settings).SetEnabled(mod, !mod.Enabled);
        settings.ActiveProfile = "Custom setup";
        Persist(); RefreshMods();
        StatusText.Text = mod.Name + (mod.Enabled ? " disabled. Its files are preserved." : " enabled.");
        return Task.CompletedTask;
    });
    private async void OpenMod_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (ModsGrid.SelectedItem is ModEntry mod) OpenFolder(Directory.Exists(mod.Path) ? mod.Path : Path.GetDirectoryName(mod.Path)!);
        return Task.CompletedTask;
    });
    private async void ImportContent_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Content mod ZIP|*.zip" };
        if (dialog.ShowDialog() != true) return;
        StatusText.Text = "Checking and importing content archive…";
        var destination = await Task.Run(() => ContentInstaller.Install(dialog.FileName, settings.ContentDirectory));
        RefreshMods(); StatusText.Text = "Imported " + Path.GetFileName(destination) + ". Confirm the download supports Windows PCVR.";
    });
    private static void OpenUrl(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https") throw new InvalidOperationException("Only HTTPS links can be opened.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    private static void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Folder not found: " + folder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false });
    }
    private async void Launch_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var exe = GameInstallation.FindExecutable(settings.GameDirectory)
            ?? throw new FileNotFoundException("Choose your BONELAB installation in Settings.");
        if (ModLibrary.GameRunning) throw new InvalidOperationException("BONELAB is already running.");
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = settings.GameDirectory, UseShellExecute = true });
        StatusText.Text = "BONELAB launch requested.";
        return Task.CompletedTask;
    });
    private void Setup_Click(object sender, RoutedEventArgs e) => Navigation.SelectedIndex = 8;
    private void Guide_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/Corbino50211/Pancake-Client/blob/main/docs/SETUP.md");
    private void Modio_Click(object sender, RoutedEventArgs e) => OpenUrl("https://mod.io/g/bonelab");
    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the BONELAB game folder" };
        if (dialog.ShowDialog() == true) GamePath.Text = dialog.FolderName;
    }
    private void BrowseContent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select BONELAB's content Mods folder" };
        if (dialog.ShowDialog() == true) ContentPath.Text = dialog.FolderName;
    }
    private async void Detect_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        var libraries = new List<string>();
        if (!string.IsNullOrEmpty(steam))
        {
            libraries.Add(steam);
            var config = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(config))
                foreach (Match match in Regex.Matches(File.ReadAllText(config), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        }
        var found = libraries.Select(x => Path.Combine(x, "steamapps", "common", "BONELAB")).FirstOrDefault(x => GameInstallation.FindExecutable(x) != null);
        if (found == null) throw new DirectoryNotFoundException("Steam BONELAB was not found. Use Browse game folder for a custom or Meta PC installation.");
        GamePath.Text = found; StatusText.Text = "Installation detected. Press Save settings.";
        return Task.CompletedTask;
    });
    private async void SaveSettings_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var game = Path.GetFullPath(GamePath.Text.Trim());
        if (GameInstallation.FindExecutable(game) == null) throw new IOException("The game folder must contain BONELAB_Steam_Windows64.exe or BONELAB.exe.");
        var content = Path.GetFullPath(ContentPath.Text.Trim());
        var code = Path.GetFullPath(Path.Combine(game, "Mods"));
        if (content.TrimEnd('\\').Equals(code.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("Content mods belong in the separate LocalLow Mods folder, not the code Mods folder in the game installation.");
        settings.GameDirectory = game;
        settings.ContentDirectory = content;
        settings.ScreenshotDirectory = Path.GetFullPath(ScreenshotPath.Text.Trim());
        Persist(); RefreshMods(); StatusText.Text = "Settings saved.";
        return Task.CompletedTask;
    });
    private void RefreshProfiles()
    {
        Directory.CreateDirectory(ProfilesFolder);
        var profiles = new List<ModProfile>();
        foreach (var file in Directory.GetFiles(ProfilesFolder, "*.json"))
        {
            try { var profile = ClientStorage.Read<ModProfile?>(file, () => null); if (profile != null) profiles.Add(profile); }
            catch (Exception ex) when (ex is JsonException or IOException) { StatusText.Text = "Skipped unreadable profile: " + Path.GetFileName(file); }
        }
        ProfilesList.ItemsSource = profiles.OrderBy(x => x.Name).ToList();
    }
    private async void SaveProfile_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var name = ProfileName.Text.Trim();
        if (name.Length is < 1 or > 80) throw new InvalidOperationException("Enter a profile name between 1 and 80 characters.");
        var profile = new ModLibrary(settings).Capture(name);
        ClientStorage.Write(Path.Combine(ProfilesFolder, Guid.NewGuid() + ".json"), profile);
        settings.ActiveProfile = name; Persist(); RefreshProfiles(); RefreshMods(); StatusText.Text = "Profile saved.";
        return Task.CompletedTask;
    });
    private async void ApplyProfile_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (ProfilesList.SelectedItem is not ModProfile profile) throw new InvalidOperationException("Select a saved profile.");
        var library = new ModLibrary(settings);
        ClientStorage.Write(Path.Combine(ClientStorage.Root, "profile-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"), library.Capture("Before " + profile.Name));
        var missing = library.Apply(profile);
        settings.ActiveProfile = profile.Name; Persist(); RefreshMods(); StatusText.Text = $"Profile applied. {missing} referenced mods are not installed.";
        return Task.CompletedTask;
    });
    private async void ExportProfile_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var profile = ProfilesList.SelectedItem as ModProfile ?? new ModLibrary(settings).Capture(settings.ActiveProfile);
        var dialog = new SaveFileDialog { Filter = "Pancake profile|*.json", FileName = "Pancake-profile.json" };
        if (dialog.ShowDialog() == true) ClientStorage.Write(dialog.FileName, profile with { GameDirectory = "", ContentDirectory = "" });
        return Task.CompletedTask;
    });
    private async void ImportProfile_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Pancake profile|*.json" };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        if (new FileInfo(dialog.FileName).Length > 2 * 1024 * 1024) throw new InvalidDataException("Profile is too large.");
        var profile = ClientStorage.Read<ModProfile?>(dialog.FileName, () => null);
        if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || profile.Mods == null || profile.Mods.Count > 10000 || profile.Mods.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id)) || profile.Mods.Select(x => x.Id).Distinct().Count() != profile.Mods.Count)
            throw new InvalidDataException("Invalid profile file.");
        ClientStorage.Write(Path.Combine(ProfilesFolder, Guid.NewGuid() + ".json"), profile);
        RefreshProfiles(); StatusText.Text = "Profile imported. Select it and click Apply to change your setup.";
        return Task.CompletedTask;
    });
    private async void Catalog_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        StatusText.Text = "Loading the Thunderstore BONELAB catalog…";
        catalogMods = await catalog.LoadAsync(closingToken.Token);
        FilterCatalog(); StatusText.Text = $"Loaded {catalogMods.Count} packages. Check each mod's supported BONELAB patch before installing.";
    });
    private void FilterCatalog() => CatalogGrid.ItemsSource = catalogMods.Where(x => (x.Name + " " + x.Author + " " + x.Description).Contains(CatalogSearch.Text, StringComparison.OrdinalIgnoreCase)).ToList();
    private void CatalogSearch_TextChanged(object sender, TextChangedEventArgs e) { if (ready) FilterCatalog(); }
    private void CatalogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CatalogGrid.SelectedItem is CatalogMod mod) CatalogDescription.Text = mod.Description + "\nDependencies: " + (mod.Dependencies.Length > 0 ? mod.Dependencies : "None listed");
    }
    private async void Download_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (CatalogGrid.SelectedItem is not CatalogMod mod) throw new InvalidOperationException("Select a mod first.");
        var dialog = new SaveFileDialog { Filter = "ZIP archive|*.zip", FileName = mod.Author + "-" + mod.Name + "-" + mod.Version + ".zip", OverwritePrompt = false };
        if (dialog.ShowDialog() != true) return;
        if (File.Exists(dialog.FileName)) throw new IOException("Choose a new filename; existing downloads are preserved.");
        await catalog.DownloadAsync(mod, dialog.FileName, new Progress<string>(text => StatusText.Text = text), closingToken.Token);
        StatusText.Text = "ZIP downloaded. Import content ZIPs from Installed Mods; install code mods using the author's instructions.";
    });
    private void ModPage_Click(object sender, RoutedEventArgs e) { if (CatalogGrid.SelectedItem is CatalogMod mod) OpenUrl(mod.PageUrl); }
    private async void Backup_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        ModLibrary.RequireGameClosed();
        var folder = new OpenFolderDialog { Title = "Select the BONELAB Saves folder to back up" };
        if (folder.ShowDialog() != true) return;
        var save = new SaveFileDialog { Filter = "Save backup ZIP|*.zip", FileName = "BONELAB-saves-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
        if (save.ShowDialog() != true) return;
        if (Path.GetFullPath(save.FileName).StartsWith(Path.GetFullPath(folder.FolderName).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Save the backup outside the folder being backed up.");
        if (File.Exists(save.FileName)) throw new IOException("Choose a new backup filename.");
        StatusText.Text = "Creating save backup…";
        await Task.Run(() => ZipFile.CreateFromDirectory(folder.FolderName, save.FileName));
        StatusText.Text = "Save backup created: " + save.FileName;
    });
    private async void InstallCompanion_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        ModLibrary.RequireGameClosed();
        if (GameInstallation.FindExecutable(settings.GameDirectory) == null) throw new IOException("Configure your game folder first.");
        var dialog = new OpenFileDialog { Filter = "Pancake companion|Pancake.Companion.dll" };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        var folder = Path.Combine(settings.GameDirectory, "Mods");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "Pancake.Companion.dll");
        if (File.Exists(target))
        {
            var backup = Path.Combine(ClientStorage.Root, "companion-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".dll");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(target, backup);
        }
        File.Copy(dialog.FileName, target, true);
        RefreshMods(); StatusText.Text = "Companion installed. Launch BONELAB with compatible MelonLoader and BoneLib.";
        return Task.CompletedTask;
    });
    private void SaveObsPreferences()
    {
        settings.ObsUrl = ObsAddress.Text.Trim();
        settings.MicrophoneSource = MicSource.Text.Trim();
        settings.AllowInGameStreaming = AllowStreaming.IsChecked == true;
        Persist();
    }
    private void ObsPreferences_Changed(object sender, RoutedEventArgs e) { if (ready) SaveObsPreferences(); }
    private async void ConnectObs_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        SaveObsPreferences();
        ObsStatus.Text = "Connecting…";
        await obs.ConnectAsync(settings.ObsUrl, ObsPassword.Password);
        await RefreshScenesAsync();
        await RefreshObsStatusAsync();
        StatusText.Text = "OBS connected. Recording controls are available in the desktop app and Pancake BoneMenu.";
    });
    private async void DisconnectObs_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await obs.DisconnectAsync();
        ObsStatus.Text = "OBS disconnected"; ObsHomeStatus.Text = "Offline";
        await SendObsStatusSafeAsync("OBS disconnected");
    });
    private async Task RefreshScenesAsync()
    {
        var result = await obs.RequestAsync("GetSceneList");
        scenes = result.GetProperty("scenes").EnumerateArray().Select(x => x.GetProperty("sceneName").GetString()!).ToList();
        ObsScenes.ItemsSource = scenes;
        ObsScenes.SelectedItem = result.GetProperty("currentProgramSceneName").GetString();
    }
    private async void ObsScenes_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshScenesAsync);
    private async void SwitchScene_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (ObsScenes.SelectedItem is not string scene) throw new InvalidOperationException("Choose an OBS scene.");
        await obs.RequestAsync("SetCurrentProgramScene", new { sceneName = scene });
        StatusText.Text = "OBS scene: " + scene;
    });
    private async Task ObsActionAsync(string request, object? data = null)
    {
        SaveObsPreferences();
        await obs.RequestAsync(request, data);
        await RefreshObsStatusAsync();
        StatusText.Text = "OBS: " + request + " completed.";
    }
    private async void StartRecording_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ObsActionAsync("StartRecord"));
    private async void StopRecording_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ObsActionAsync("StopRecord"));
    private async void StartStreaming_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Start broadcasting using the current OBS streaming destination and scene?", "Go live", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await RunAsync(() => ObsActionAsync("StartStream"));
    }
    private async void StopStreaming_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ObsActionAsync("StopStream"));
    private async void Mute_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ObsActionAsync("SetInputMute", new { inputName = MicSource.Text.Trim(), inputMuted = true }));
    private async void Unmute_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ObsActionAsync("SetInputMute", new { inputName = MicSource.Text.Trim(), inputMuted = false }));
    private async Task RefreshObsStatusAsync()
    {
        var recording = await obs.RequestAsync("GetRecordStatus");
        var streaming = await obs.RequestAsync("GetStreamStatus");
        var isRecording = recording.GetProperty("outputActive").GetBoolean();
        var paused = recording.GetProperty("outputPaused").GetBoolean();
        var isStreaming = streaming.GetProperty("outputActive").GetBoolean();
        var time = recording.GetProperty("outputTimecode").GetString();
        var status = (isRecording ? (paused ? "Paused " : "Recording ") + time : "Recording stopped") + " · " + (isStreaming ? "LIVE" : "Stream offline");
        ObsStatus.Text = status;
        ObsHomeStatus.Text = isRecording ? "Recording" : isStreaming ? "Live" : "Connected";
        await SendObsStatusSafeAsync(status);
    }
    private async Task SendObsStatusSafeAsync(string status)
    {
        try { if (bridge.Connected) await bridge.SendAsync(new { type = "obsStatus", text = status, connected = obs.Connected, scenes }); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
    }
    private async Task PollAsync()
    {
        if (polling || closing || busy) return;
        polling = true;
        try
        {
            if (Navigation.SelectedIndex == 7) await RefreshLogAsync();
            if (obs.Connected) await RefreshObsStatusAsync();
        }
        catch (Exception ex) { if (!closing) StatusText.Text = ex.Message; }
        finally { polling = false; }
    }
    private async Task RefreshLogAsync()
    {
        if (!Directory.Exists(settings.GameDirectory)) return;
        var path = Path.Combine(settings.GameDirectory, "MelonLoader", "Latest.log");
        if (!File.Exists(path)) { rawLog = "No MelonLoader/Latest.log yet. Start BONELAB once with MelonLoader installed."; FilterLog(); return; }
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        var truncated = file.Length > 256 * 1024;
        if (truncated) file.Seek(-256 * 1024, SeekOrigin.End);
        using var reader = new StreamReader(file, Encoding.UTF8);
        if (truncated) await reader.ReadLineAsync(closingToken.Token);
        rawLog = await reader.ReadToEndAsync(closingToken.Token);
        FilterLog();
    }
    private void FilterLog()
    {
        LogText.Text = string.Join(Environment.NewLine, rawLog.Split('\n').Where(x => x.Contains(LogFilter.Text, StringComparison.OrdinalIgnoreCase)).TakeLast(1500));
    }
    private void LogFilter_TextChanged(object sender, TextChangedEventArgs e) { if (ready) FilterLog(); }
    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Text log|*.txt", FileName = "Pancake-BONELAB-log.txt" };
        if (dialog.ShowDialog() == true) File.WriteAllText(dialog.FileName, LogText.Text);
    }
    private async void OpenLogs_Click(object sender, RoutedEventArgs e) => await RunAsync(() => { OpenFolder(Path.Combine(settings.GameDirectory, "MelonLoader")); return Task.CompletedTask; });
    private void Fov_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (FovLabel != null) FovLabel.Text = $"Field of view · {e.NewValue:0}°"; }
    private async void CameraStart_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        settings.CameraMode = ((ComboBoxItem)CameraMode.SelectedItem).Content.ToString()!;
        settings.CameraFov = FovSlider.Value; Persist();
        await bridge.SendAsync(new { type = "camera", enabled = true, mode = settings.CameraMode, fov = settings.CameraFov });
        CameraStatus.Text = "Camera requested. Waiting for the first frame…";
        OpenCaptureWindow();
    });
    private async void CameraStop_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await bridge.SendAsync(new { type = "camera", enabled = false });
        CameraStatus.Text = "Camera stopped."; lastFrame = null;
        if (captureImage != null) captureImage.Source = null;
    });
    private void OpenCaptureWindow()
    {
        if (captureWindow != null) { captureWindow.Activate(); return; }
        captureImage = new Image { Source = lastFrame, Stretch = System.Windows.Media.Stretch.Uniform };
        captureWindow = new Window { Title = "Pancake Camera", Width = 980, Height = 590, MinWidth = 320, MinHeight = 180, Background = System.Windows.Media.Brushes.Black, Content = captureImage };
        captureWindow.Closed += (_, _) => { captureWindow = null; captureImage = null; };
        captureWindow.Show();
        StatusText.Text = "In OBS add Window Capture → Pancake Camera. The window's content contains only the camera feed.";
    }
    private void CaptureWindow_Click(object sender, RoutedEventArgs e) => OpenCaptureWindow();
    private void SaveScreenshot()
    {
        if (lastFrame == null) throw new InvalidOperationException("Start the camera and wait for a frame before taking a screenshot.");
        Directory.CreateDirectory(settings.ScreenshotDirectory);
        var path = Path.Combine(settings.ScreenshotDirectory, "Pancake-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(lastFrame));
        using var file = File.Create(path); encoder.Save(file);
        StatusText.Text = "Screenshot saved: " + path;
    }
    private async void Screenshot_Click(object sender, RoutedEventArgs e) => await RunAsync(() => { SaveScreenshot(); return Task.CompletedTask; });
    private async void HandleCompanionMessage(JsonElement message)
    {
        if (closing) return;
        try
        {
            var type = message.GetProperty("type").GetString();
            if (type == "frame")
            {
                var bytes = Convert.FromBase64String(message.GetProperty("jpeg").GetString()!);
                using var memory = new MemoryStream(bytes);
                var frame = new BitmapImage(); frame.BeginInit(); frame.CacheOption = BitmapCacheOption.OnLoad; frame.DecodePixelWidth = 640; frame.StreamSource = memory; frame.EndInit(); frame.Freeze();
                lastFrame = frame;
                if (captureImage != null) captureImage.Source = frame;
                CameraStatus.Text = "Camera feed connected · 640 × 360 · experimental preview";
                return;
            }
            if (type == "cameraStopped") { lastFrame = null; if (captureImage != null) captureImage.Source = null; CameraStatus.Text = "Camera stopped."; return; }
            if (type == "status") { CameraStatus.Text = message.GetProperty("text").GetString(); return; }
            if (type == "screenshot") { SaveScreenshot(); await SendObsStatusSafeAsync("Screenshot saved"); return; }
            if (type != "obs") return;
            if (busy) { await SendObsStatusSafeAsync("Desktop is busy. Try again shortly."); return; }
            var action = message.GetProperty("action").GetString();
            if ((action is "StartStream" or "StopStream") && !settings.AllowInGameStreaming) throw new InvalidOperationException("Enable in-game streaming controls in Pancake Client first.");
            switch (action)
            {
                case "StartRecord": case "StopRecord": case "StartStream": case "StopStream":
                    await obs.RequestAsync(action); break;
                case "Mute": case "Unmute":
                    await obs.RequestAsync("SetInputMute", new { inputName = MicSource.Text.Trim(), inputMuted = action == "Mute" }); break;
                case "Scene":
                    var scene = message.GetProperty("scene").GetString();
                    if (scene == null || !scenes.Contains(scene)) throw new InvalidOperationException("Refresh the OBS scene list in Pancake Client.");
                    await obs.RequestAsync("SetCurrentProgramScene", new { sceneName = scene }); break;
                default: throw new InvalidOperationException("Unsupported companion command.");
            }
            await RefreshObsStatusAsync();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; await SendObsStatusSafeAsync(ex.Message); }
    }
}
