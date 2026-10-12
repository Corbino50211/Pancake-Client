using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Pancake.Protocol;

/// <summary>Length-prefixed UTF-8 JSON shared by the desktop and game; no buffered text-stream flushes.</summary>
public static class PipeMessages
{
    public const string PipeName = "PancakeClient.Bonelab.v1";
    public const int MaximumBytes = 1024 * 1024;
    public static async Task WriteAsync(Stream stream, object message, CancellationToken token) =>
        await WriteTextAsync(stream, JsonSerializer.Serialize(message), token);
    public static async Task WriteTextAsync(Stream stream, string json, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Companion message exceeds 1 MiB.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header.AsMemory(), token);
        await stream.WriteAsync(bytes.AsMemory(), token);
    }
    public static async Task<JsonElement> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4]; await ReadExactAsync(stream, header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 2 or > MaximumBytes) throw new InvalidDataException("Invalid companion message length.");
        var bytes = new byte[length]; await ReadExactAsync(stream, bytes, token);
        using var json = JsonDocument.Parse(bytes); return json.RootElement.Clone();
    }
    private static async Task ReadExactAsync(Stream stream, byte[] bytes, CancellationToken token)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(offset), token);
            if (count == 0) throw new EndOfStreamException("Companion connection closed.");
            offset += count;
        }
    }
}
