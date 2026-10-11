using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pancake.Core;

/// <summary>OBS WebSocket 5.x, RPC 1. One reader; serialized writes; bounded request lifetime.</summary>
public sealed class ObsClient : IAsyncDisposable
{
    private ClientWebSocket? socket;
    private CancellationTokenSource? lifetime;
    private Task? readerTask;
    private readonly SemaphoreSlim sendGate = new(1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    public bool Connected => socket?.State == WebSocketState.Open && Identified;
    public bool Identified { get; private set; }
    public event Action<string>? ConnectionChanged;
    public event Action<string, JsonElement>? EventReceived;

    public static string Authentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }
    public async Task ConnectAsync(string address, string password)
    {
        await DisconnectAsync();
        var uri = new Uri(address);
        if (uri.Scheme is not ("ws" or "wss")) throw new ArgumentException("Use a ws:// or wss:// OBS address.");
        // First release intentionally supports OBS on this PC only.
        if (!uri.IsLoopback) throw new ArgumentException("Use localhost or 127.0.0.1 for OBS on this PC.");
        var client = new ClientWebSocket();
        socket = client;
        lifetime = new CancellationTokenSource();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await client.ConnectAsync(uri, timeout.Token);
            var hello = await ReceiveAsync(client, timeout.Token);
            if (hello.GetProperty("op").GetInt32() != 0) throw new InvalidDataException("Expected OBS WebSocket 5 Hello.");
            var identify = new Dictionary<string, object> { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
            if (hello.GetProperty("d").TryGetProperty("authentication", out var auth))
                identify["authentication"] = Authentication(password, auth.GetProperty("salt").GetString()!, auth.GetProperty("challenge").GetString()!);
            await SendAsync(client, new { op = 1, d = identify }, timeout.Token);
            var response = await ReceiveAsync(client, timeout.Token);
            if (response.GetProperty("op").GetInt32() != 2) throw new InvalidDataException("OBS did not identify the client.");
            Identified = true;
            readerTask = ReadLoopAsync(client, lifetime.Token);
            ConnectionChanged?.Invoke("Connected to OBS");
        }
        catch
        {
            await DisconnectAsync();
            throw;
        }
    }
    public async Task<JsonElement> RequestAsync(string type, object? data = null)
    {
        var client = socket;
        if (!Connected || client == null) throw new InvalidOperationException("Connect to OBS first.");
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime!.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await SendAsync(client, new { op = 6, d = new { requestType = type, requestId = id, requestData = data ?? new { } } }, timeout.Token);
            return await completion.Task.WaitAsync(timeout.Token);
        }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task ReadLoopAsync(ClientWebSocket client, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var message = await ReceiveAsync(client, token);
                var op = message.GetProperty("op").GetInt32();
                var data = message.GetProperty("d");
                if (op == 7 && pending.TryRemove(data.GetProperty("requestId").GetString()!, out var completion))
                {
                    var status = data.GetProperty("requestStatus");
                    if (status.GetProperty("result").GetBoolean())
                        completion.TrySetResult(data.TryGetProperty("responseData", out var result) ? result.Clone() : JsonSerializer.SerializeToElement(new { }));
                    else completion.TrySetException(new InvalidOperationException("OBS: " + (status.TryGetProperty("comment", out var comment) ? comment.GetString() : "Request failed")));
                }
                else if (op == 5) EventReceived?.Invoke(data.GetProperty("eventType").GetString()!, data.GetProperty("eventData").Clone());
            }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or JsonException or InvalidDataException)
        { if (!token.IsCancellationRequested) ConnectionChanged?.Invoke("OBS disconnected: " + ex.Message); }
        finally
        {
            Identified = false;
            foreach (var item in pending) item.Value.TrySetException(new IOException("OBS connection closed."));
            pending.Clear();
        }
    }
    private async Task SendAsync(ClientWebSocket client, object message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await sendGate.WaitAsync(token);
        try { await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token); }
        finally { sendGate.Release(); }
    }
    private static async Task<JsonElement> ReceiveAsync(ClientWebSocket client, CancellationToken token)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close) throw new IOException("OBS closed the connection. Check its WebSocket password and server settings.");
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Unexpected OBS message type.");
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("OBS message exceeded the size limit.");
        } while (!result.EndOfMessage);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
    public async Task DisconnectAsync()
    {
        Identified = false;
        lifetime?.Cancel();
        socket?.Abort();
        if (readerTask != null) await readerTask;
        socket?.Dispose();
        lifetime?.Dispose();
        socket = null;
        lifetime = null;
        readerTask = null;
    }
    public async ValueTask DisposeAsync() { await DisconnectAsync(); sendGate.Dispose(); }
}
