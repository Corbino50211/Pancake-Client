using System.IO.Compression;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Pancake.Core;
using Pancake.Protocol;

var checks = new List<(string Name, Func<Task> Run)>
{
    ("Update verification, preservation and rollback", Updates),
    ("Game executable detection and running-game guard", GameDetection),
    ("Inventory and reversible toggles", Inventory),
    ("Profile round trip, missing mods and rollback", Profiles),
    ("Archive traversal and extraction limits", Archives),
    ("Content import preserves existing mods", ContentImport),
    ("OBS authentication, fragmented replies and errors", ObsProtocol),
    ("Companion pipe handshake and round trip", BridgeProtocol)
};
var failed = 0;
foreach (var check in checks)
{
    try { await Task.Run(check.Run).WaitAsync(TimeSpan.FromSeconds(30)); Console.WriteLine("PASS " + check.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + check.Name + "\n" + ex); }
}
Console.WriteLine($"{checks.Count - failed}/{checks.Count} checks passed.");
return failed == 0 ? 0 : 1;

static void Require(bool value, string message) { if (!value) throw new Exception(message); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static string Temp()
{
    var path = Path.Combine(Path.GetTempPath(), "PancakeTests-" + Guid.NewGuid());
    Directory.CreateDirectory(path); return path;
}
static ClientSettings Fixture(string root)
{
    var game = Path.Combine(root, "game"); var content = Path.Combine(root, "content", "Mods");
    Directory.CreateDirectory(Path.Combine(game, "Mods")); Directory.CreateDirectory(Path.Combine(game, "Plugins"));
    Directory.CreateDirectory(Path.Combine(content, "ExampleAvatar"));
    File.WriteAllText(Path.Combine(game, "Mods", "Example.dll"), "original code");
    File.WriteAllText(Path.Combine(game, "Plugins", "Loader.dll"), "plugin");
    File.WriteAllText(Path.Combine(content, "ExampleAvatar", "pallet.json"), "{\"title\":\"Example Avatar\"}");
    return new() { GameDirectory = game, ContentDirectory = content };
}
static Task Updates()
{
    var root = Temp();
    try
    {
        var stage = Path.Combine(root, "stage"); var target = Path.Combine(root, "target");
        Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "PancakeClient.exe"), "old");
        File.WriteAllText(Path.Combine(target, "personal.txt"), "keep");
        foreach (var name in new[] { "PancakeClient.exe", "PancakeClient.dll", "PancakeClient.runtimeconfig.json" }) File.WriteAllText(Path.Combine(stage, name), "new");
        File.WriteAllText(Path.Combine(stage, "client-version.json"), "{\"Version\":\"0.1.2.1\"}");
        var zip = Path.Combine(root, "update.zip"); ZipFile.CreateFromDirectory(stage, zip);
        using var input = File.OpenRead(zip);
        var update = new ClientUpdate("0.1.2.1", "https://github.com/Corbino50211/Pancake-Client/releases/download/v0.1.2.1/PancakeClient-win-x64.zip", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)));
        Require(ClientUpdater.IsNewer(update, "0.1.1.1"), "New update missed.");
        Require(!ClientUpdater.IsNewer(update, update.Version), "Same version offered.");
        Throws<InvalidDataException>(() => ClientUpdater.Validate(update with { DownloadUrl = "https://example.com/update.zip" }));
        Throws<InvalidDataException>(() => ClientUpdater.Stage(zip, Path.Combine(root, "bad"), update with { Sha256 = new string('0', 64) }));
        Require(!Directory.Exists(Path.Combine(root, "bad")), "Bad checksum extracted files.");
        var verified = ClientUpdater.Stage(zip, Path.Combine(root, "verified"), update);
        ClientUpdater.Apply(verified, target, Path.Combine(root, "backup"));
        Require(File.ReadAllText(Path.Combine(target, "PancakeClient.exe")) == "new", "Update not installed.");
        Require(File.ReadAllText(Path.Combine(target, "personal.txt")) == "keep", "Unrelated file changed.");
        Require(File.ReadAllText(Path.Combine(root, "backup", "PancakeClient.exe")) == "old", "Backup missing.");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(stage, "a.txt"), "new a"); File.WriteAllText(Path.Combine(stage, "b.txt"), "new b");
            File.WriteAllText(Path.Combine(target, "a.txt"), "old a"); File.WriteAllText(Path.Combine(target, "b.txt"), "old b");
            using var locked = new FileStream(Path.Combine(target, "b.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
            Throws<IOException>(() => ClientUpdater.Apply(stage, target, Path.Combine(root, "rollback")));
            Require(File.ReadAllText(Path.Combine(target, "a.txt")) == "old a", "Failed update did not restore previous files.");
        }
    }
    finally { Directory.Delete(root, true); }
    return Task.CompletedTask;
}
static Task GameDetection()
{
    var root = Temp();
    try
    {
        Require(GameInstallation.FindExecutable(root) == null, "Empty folder accepted.");
        File.WriteAllText(Path.Combine(root, "UnityCrashHandler64.exe"), "");
        Require(GameInstallation.FindExecutable(root) == null, "Unrelated executable accepted.");
        foreach (var name in new[] { "BONELAB.exe", "BONELAB_Steam_Windows64.exe" })
        {
            var path = Path.Combine(root, name); File.WriteAllText(path, "");
            Require(GameInstallation.FindExecutable(root) == path, "Game executable was not recognized: " + name);
            File.Delete(path);
            if (!OperatingSystem.IsWindows()) continue;
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), path);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, "/c ping -n 30 127.0.0.1 > nul") { CreateNoWindow = true })!;
            try { Require(ModLibrary.GameRunning, "Running game was not recognized: " + name); Throws<InvalidOperationException>(ModLibrary.RequireGameClosed); }
            finally { if (!process.HasExited) process.Kill(true); process.WaitForExit(); }
            File.Delete(path);
        }
    }
    finally { Directory.Delete(root, true); }
    return Task.CompletedTask;
}
static Task Inventory()
{
    var root = Temp();
    try
    {
        var settings = Fixture(root); var library = new ModLibrary(settings);
        Require(library.Scan().Count == 3, "Three mod types should be discovered.");
        foreach (var mod in library.Scan())
        {
            library.SetEnabled(mod, false);
            Require(!library.Scan().Single(x => x.Id == mod.Id).Enabled, "Disabled state missing.");
            library.SetEnabled(mod, true);
            Require(library.Scan().Single(x => x.Id == mod.Id).Enabled, "Restore failed.");
        }
        Require(File.ReadAllText(Path.Combine(settings.GameDirectory, "Mods", "Example.dll")) == "original code", "Code bytes changed.");
    }
    finally { Directory.Delete(root, true); }
    return Task.CompletedTask;
}
static Task Profiles()
{
    var root = Temp();
    try
    {
        var settings = Fixture(root); var library = new ModLibrary(settings);
        var all = library.Capture("All");
        library.SetEnabled(library.Scan().First(), false);
        var serialized = Path.Combine(root, "profile.json"); ClientStorage.Write(serialized, all);
        var loaded = ClientStorage.Read(serialized, () => all);
        Require(library.Apply(loaded) == 0 && library.Scan().All(x => x.Enabled), "Profile failed to restore states.");
        var wanted = all with { Mods = new() { new("code/Example.dll", false), new("content/ExampleAvatar", false), new("code/Missing.dll", true) } };
        Directory.CreateDirectory(settings.ContentDirectory + ".pancake-disabled");
        File.WriteAllText(settings.ContentDirectory + ".pancake-disabled/ExampleAvatar", "conflict");
        Throws<IOException>(() => library.Apply(wanted));
        Require(File.Exists(Path.Combine(settings.GameDirectory, "Mods", "Example.dll")), "Earlier profile change was not rolled back.");
        File.Delete(settings.ContentDirectory + ".pancake-disabled/ExampleAvatar");
        Require(library.Apply(wanted) == 1, "Missing mods should be reported.");
        Require(library.Scan().Single(x => x.Kind == "Plugin").Enabled, "Mods absent from a profile must remain unchanged.");
    }
    finally { Directory.Delete(root, true); }
    return Task.CompletedTask;
}
static void Zip(string path, params (string Name, string Text)[] entries)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var entry in entries) { using var writer = new StreamWriter(archive.CreateEntry(entry.Name).Open()); writer.Write(entry.Text); }
}
static Task Archives()
{
    var root = Temp();
    try
    {
        foreach (var bad in new[] { "../escape.txt", "..\\escape.txt", "/absolute.txt", "C:/evil.dll", "folder/file:stream" })
        {
            var zip = Path.Combine(root, Guid.NewGuid() + ".zip"); Zip(zip, (bad, "bad"));
            Throws<InvalidDataException>(() => SafeArchive.Extract(zip, Path.Combine(root, Guid.NewGuid().ToString())));
        }
        var large = Path.Combine(root, "large.zip"); Zip(large, ("large.txt", new string('x', 100)));
        Throws<InvalidDataException>(() => SafeArchive.Extract(large, Path.Combine(root, "limited"), 10));
        var good = Path.Combine(root, "good.zip"); Zip(good, ("folder/file.txt", "ok"));
        SafeArchive.Extract(good, Path.Combine(root, "good"));
        Require(File.ReadAllText(Path.Combine(root, "good/folder/file.txt")) == "ok", "Good archive failed.");
    }
    finally { Directory.Delete(root, true); }
    return Task.CompletedTask;
}
static Task ContentImport()
{
    var root = Temp();
    try
    {
        var settings = Fixture(root);
        var zip = Path.Combine(root, "avatar.zip"); Zip(zip, ("NewAvatar/pallet.json", "{}"), ("NewAvatar/Windows/avatar.bundle", "asset"));
        var path = ContentInstaller.Install(zip, settings.ContentDirectory);
        Require(File.Exists(Path.Combine(path, "pallet.json")), "Content was not installed.");
        Throws<IOException>(() => ContentInstaller.Install(zip, settings.ContentDirectory));
        Require(File.ReadAllText(Path.Combine(path, "Windows/avatar.bundle")) == "asset", "Existing mod changed.");
        var code = Path.Combine(root, "code.zip"); Zip(code, ("Code/pallet.json", "{}"), ("Code/evil.dll", "code"));
        Throws<InvalidDataException>(() => ContentInstaller.Install(code, settings.ContentDirectory));
        Require(!Directory.EnumerateDirectories(Path.GetDirectoryName(settings.ContentDirectory)!, ".pancake-stage-*").Any(), "Staging folders leaked.");
    }
    finally { Directory.Delete(root, true); }
    return Task.CompletedTask;
}
static async Task<JsonElement> Receive(WebSocket socket)
{
    var bytes = new byte[16384]; using var stream = new MemoryStream();
    WebSocketReceiveResult read;
    do { read = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), CancellationToken.None); stream.Write(bytes, 0, read.Count); } while (!read.EndOfMessage);
    using var json = JsonDocument.Parse(stream.ToArray()); return json.RootElement.Clone();
}
static async Task Send(WebSocket socket, object message, bool fragmented = false)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
    if (fragmented)
    {
        var half = bytes.Length / 2;
        await socket.SendAsync(new ArraySegment<byte>(bytes, 0, half), WebSocketMessageType.Text, false, CancellationToken.None);
        await socket.SendAsync(new ArraySegment<byte>(bytes, half, bytes.Length - half), WebSocketMessageType.Text, true, CancellationToken.None);
    }
    else await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
}
static async Task ObsProtocol()
{
    Require(ObsClient.Authentication("password", "salt", "challenge") == "zTM5ki6L2vVvBQiTG9ckH1Lh64AbnCf6XZ226UmnkIA=", "Authentication formula changed.");
    var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
    using var listener = new HttpListener(); listener.Prefixes.Add($"http://localhost:{port}/"); listener.Start();
    var server = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync(); var web = await context.AcceptWebSocketAsync(null); using var socket = web.WebSocket;
        await Send(socket, new { op = 0, d = new { rpcVersion = 1, authentication = new { salt = "salt", challenge = "challenge" } } });
        var identify = await Receive(socket);
        Require(identify.GetProperty("d").GetProperty("authentication").GetString() == ObsClient.Authentication("password", "salt", "challenge"), "Client sent wrong auth.");
        await Send(socket, new { op = 2, d = new { negotiatedRpcVersion = 1 } });
        var one = await Receive(socket); var two = await Receive(socket);
        // Respond out of order to prove correlation, and fragment one JSON response.
        foreach (var request in new[] { two, one })
        {
            var d = request.GetProperty("d"); var kind = d.GetProperty("requestType").GetString();
            await Send(socket, new { op = 7, d = new { requestId = d.GetProperty("requestId").GetString(), requestStatus = new { result = kind != "BadRequest", code = kind == "BadRequest" ? 400 : 100, comment = "Test rejection" }, responseData = new { outputActive = true, request = kind } } }, true);
        }
        var pending = await Receive(socket);
        socket.Abort();
    });
    await using var client = new ObsClient();
    await client.ConnectAsync($"ws://localhost:{port}", "password");
    var good = client.RequestAsync("GetRecordStatus"); var bad = client.RequestAsync("BadRequest");
    Require((await good).GetProperty("outputActive").GetBoolean(), "Successful response missing.");
    try { await bad; throw new Exception("OBS rejection was swallowed."); } catch (InvalidOperationException) { }
    try { await client.RequestAsync("CloseNow"); throw new Exception("Disconnected request succeeded."); } catch (IOException) { }
    await server.WaitAsync(TimeSpan.FromSeconds(10));
    listener.Stop();
}
static async Task BridgeProtocol()
{
    if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP pipe ownership test: Windows only"); return; }
    await using var bridge = new CompanionBridge();
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    bridge.ConnectionChanged += value => { if (value) connected.TrySetResult(); };
    bridge.Message += message => received.TrySetResult(message);
    bridge.Start();
    using var client = new NamedPipeClientStream(".", CompanionBridge.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await client.ConnectAsync(3000);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    Console.WriteLine("  Pipe connected; sending hello");
    await PipeMessages.WriteAsync(client, new { type = "hello", protocol = 1 }, timeout.Token);
    await connected.Task.WaitAsync(timeout.Token);
    Console.WriteLine("  Hello accepted; sending command");
    await PipeMessages.WriteAsync(client, new { type = "obs", action = "StartRecord" }, timeout.Token);
    Require((await received.Task.WaitAsync(timeout.Token)).GetProperty("action").GetString() == "StartRecord", "Bridge lost command.");
    Console.WriteLine("  Command received; returning status");
    var response = PipeMessages.ReadAsync(client, timeout.Token);
    await bridge.SendAsync(new { type = "obsStatus", text = "Recording" });
    Require((await response).GetProperty("text").GetString() == "Recording", "Bridge lost status response.");
    Console.WriteLine("  Round trip complete; closing pipe");
}
