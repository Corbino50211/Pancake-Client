using System.IO.Pipes;
using System.Text.Json;
using Pancake.Protocol;

namespace Pancake.Core;

/// <summary>Local, current-user-only IPC. No network port, game file or OBS password exchange.</summary>
public sealed class CompanionBridge : IAsyncDisposable
{
    public const string PipeName = PipeMessages.PipeName;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim writeGate = new(1);
    private NamedPipeServerStream? pipe;
    private Task? loop;
    public bool Connected { get; private set; }
    public event Action<bool>? ConnectionChanged;
    public event Action<JsonElement>? Message;
    public event Action<string>? Error;
    public void Start() => loop ??= Task.Run(RunAsync);
    private async Task RunAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var connection = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 65536, 65536);
                pipe = connection;
                await connection.WaitForConnectionAsync(stop.Token);
                using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                helloTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var hello = await PipeMessages.ReadAsync(connection, helloTimeout.Token);
                if (hello.GetProperty("type").GetString() != "hello" || hello.GetProperty("protocol").GetInt32() != 1)
                    throw new InvalidDataException("Unsupported companion protocol.");
                Connected = true;
                ConnectionChanged?.Invoke(true);
                while (!stop.IsCancellationRequested)
                {
                    var message = await PipeMessages.ReadAsync(connection, stop.Token);
                    Message?.Invoke(message);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (EndOfStreamException) { }
            catch (Exception ex) { if (!stop.IsCancellationRequested) Error?.Invoke(ex.Message); }
            finally
            {
                Connected = false;
                pipe = null;
                ConnectionChanged?.Invoke(false);
            }
            if (!stop.IsCancellationRequested) await Task.Delay(500, stop.Token).ContinueWith(_ => { });
        }
    }
    public async Task SendAsync(object message)
    {
        if (!Connected) throw new InvalidOperationException("The BONELAB companion is not connected.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await writeGate.WaitAsync(timeout.Token);
        try
        {
            var target = pipe ?? throw new IOException("Companion disconnected.");
            await PipeMessages.WriteAsync(target, message, timeout.Token);
        }
        finally { writeGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        pipe?.Dispose();
        if (loop != null) await loop.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Dispose();
        // Writers may still be unwinding cancellation. SemaphoreSlim owns no native handle here.
    }
}
