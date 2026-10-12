using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Pancake.Protocol;

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
                using var connection = new NamedPipeClientStream(".", PipeMessages.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe = connection;
                await connection.ConnectAsync(1500, stop.Token);
                await WriteAsync(connection, "{\"type\":\"hello\",\"protocol\":1,\"version\":\"0.3.0\"}");
                Connected = true;
                var read = PipeMessages.ReadAsync(connection, stop.Token);
                while (connection.IsConnected && !stop.IsCancellationRequested)
                {
                    if (read.IsCompleted)
                    {
                        var message = await read;
                        if (incoming.Count < 64) incoming.Enqueue(message);
                        read = PipeMessages.ReadAsync(connection, stop.Token);
                    }
                    for (var i = 0; i < 8 && outgoing.TryDequeue(out var message); i++) await WriteAsync(connection, message);
                    var frame = Interlocked.Exchange(ref newestFrame, null);
                    if (frame != null) await WriteAsync(connection, frame);
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
    private async Task WriteAsync(Stream stream, string json)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await PipeMessages.WriteTextAsync(stream, json, timeout.Token);
    }
    public void Dispose() { stop.Cancel(); pipe?.Dispose(); }
}
