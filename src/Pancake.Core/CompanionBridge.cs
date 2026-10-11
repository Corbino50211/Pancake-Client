using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Pancake.Core;

/// <summary>Local, current-user-only IPC. No network port, game file or OBS password exchange.</summary>
public sealed class CompanionBridge : IAsyncDisposable
{
    public const string PipeName = "PancakeClient.Bonelab.v1";
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim writeGate = new(1);
    private NamedPipeServerStream? pipe;
    private StreamWriter? writer;
    private Task? loop;
    public bool Connected { get; private set; }
    public event Action<bool>? ConnectionChanged;
    public event Action<JsonElement>? Message;
    public event Action<string>? Error;
    public void Start() => loop ??= RunAsync();
    private async Task RunAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var connection = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe = connection;
                await connection.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(connection, Encoding.UTF8, false, 8192, true);
                using var output = new StreamWriter(connection, new UTF8Encoding(false), 8192, true) { AutoFlush = true };
                writer = output;
                // Require a versioned hello before exposing controls.
                using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                helloTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var helloLine = await ReadBoundedLineAsync(reader, helloTimeout.Token);
                using var hello = JsonDocument.Parse(helloLine ?? "{}");
                if (hello.RootElement.GetProperty("type").GetString() != "hello" || hello.RootElement.GetProperty("protocol").GetInt32() != 1)
                    throw new InvalidDataException("Unsupported companion protocol.");
                Connected = true;
                ConnectionChanged?.Invoke(true);
                while (!stop.IsCancellationRequested)
                {
                    var line = await ReadBoundedLineAsync(reader, stop.Token);
                    if (line == null) break;
                    using var json = JsonDocument.Parse(line);
                    Message?.Invoke(json.RootElement.Clone());
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) { if (!stop.IsCancellationRequested) Error?.Invoke(ex.Message); }
            finally
            {
                Connected = false;
                writer = null;
                pipe = null;
                ConnectionChanged?.Invoke(false);
            }
            if (!stop.IsCancellationRequested) await Task.Delay(500, stop.Token).ContinueWith(_ => { });
        }
    }
    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken token)
    {
        // StreamReader.ReadLineAsync has no length limit. Keep IPC memory bounded.
        var line = new StringBuilder();
        var chars = new char[4096];
        // A byte/message-framed protocol would be faster, but Peek risks blocking. Read one char
        // with cancellation; StreamReader still buffers underlying pipe I/O.
        while (true)
        {
            var count = await reader.ReadAsync(chars.AsMemory(0, 1), token);
            if (count == 0) return line.Length == 0 ? null : line.ToString();
            if (chars[0] == '\n') return line.ToString().TrimEnd('\r');
            line.Append(chars[0]);
            if (line.Length > 1024 * 1024) throw new InvalidDataException("Companion message exceeded 1 MiB.");
        }
    }
    public async Task SendAsync(object message)
    {
        if (!Connected) throw new InvalidOperationException("The BONELAB companion is not connected.");
        await writeGate.WaitAsync(stop.Token);
        try
        {
            var target = writer ?? throw new IOException("Companion disconnected.");
            await target.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), stop.Token);
        }
        finally { writeGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        pipe?.Dispose();
        if (loop != null) await loop;
        stop.Dispose();
        writeGate.Dispose();
    }
}
