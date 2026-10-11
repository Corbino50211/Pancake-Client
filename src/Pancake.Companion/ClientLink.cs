using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Pancake.Companion;

/// <summary>All game API calls remain on the Unity thread. This worker only exchanges JSON.</summary>
internal sealed class ClientLink : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentQueue<string> outgoing = new();
    private readonly ConcurrentQueue<JsonElement> incoming = new();
    private NamedPipeClientStream? pipe;
    private string? newestFrame;
    public volatile bool Connected;
    public ClientLink() => _ = Task.Run(ConnectLoopAsync);
    public void Send(object value)
    {
        if (Connected && outgoing.Count < 64) outgoing.Enqueue(JsonSerializer.Serialize(value));
    }
    public void Frame(string base64)
    {
        if (Connected) Interlocked.Exchange(ref newestFrame, JsonSerializer.Serialize(new { type = "frame", jpeg = base64 }));
    }
    public bool TryReceive(out JsonElement message) => incoming.TryDequeue(out message);
    private async Task ConnectLoopAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var connection = new NamedPipeClientStream(".", "PancakeClient.Bonelab.v1", PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe = connection;
                await connection.ConnectAsync(1500, stop.Token);
                using var reader = new StreamReader(connection, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(connection, new UTF8Encoding(false), 8192, true) { AutoFlush = true };
                await writer.WriteLineAsync("{\"type\":\"hello\",\"protocol\":1,\"version\":\"0.1.0\"}");
                Connected = true;
                var read = reader.ReadLineAsync();
                while (connection.IsConnected && !stop.IsCancellationRequested)
                {
                    if (read.IsCompleted)
                    {
                        var line = await read;
                        if (line == null) break;
                        if (line.Length > 65536) throw new InvalidDataException("Desktop message too large.");
                        using var json = JsonDocument.Parse(line);
                        if (incoming.Count < 64) incoming.Enqueue(json.RootElement.Clone());
                        read = reader.ReadLineAsync();
                    }
                    for (var i = 0; i < 8 && outgoing.TryDequeue(out var message); i++) await writer.WriteLineAsync(message);
                    var frame = Interlocked.Exchange(ref newestFrame, null);
                    if (frame != null) await writer.WriteLineAsync(frame);
                    await Task.Delay(20, stop.Token);
                }
            }
            catch (Exception) { /* Reconnect quietly; the desktop app may be closed. */ }
            finally
            {
                Connected = false;
                pipe = null;
                Interlocked.Exchange(ref newestFrame, null);
                while (outgoing.TryDequeue(out _)) { }
            }
            try { await Task.Delay(1500, stop.Token); } catch (OperationCanceledException) { break; }
        }
    }
    public void Dispose() { stop.Cancel(); pipe?.Dispose(); }
}
