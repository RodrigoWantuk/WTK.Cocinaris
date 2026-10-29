using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocinaris.Modules.Ai.Images;

/// <summary>
/// OpenRouter Images API adapter (<c>POST /images</c>). Sends one prompt with a size tier and an
/// aspect ratio and returns the decoded bytes and the reported cost. It never retries. OpenRouter
/// bills image generation all-or-nothing, so a failed call costs nothing.
/// </summary>
public sealed class OpenRouterImageProvider(HttpClient httpClient, OpenRouterOptions options, string model) : IImageGenerationProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Largest response body read, so a hostile or broken answer cannot exhaust memory.</summary>
    private const int MaxResponseBytes = 12 * 1024 * 1024;

    /// <inheritdoc />
    public string Name => "openrouter";

    /// <inheritdoc />
    public async Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        int Elapsed() => (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl), "images"))
        {
            Content = new StringContent(JsonSerializer.Serialize(new OpenRouterImageRequest(model, request.Prompt, request.Resolution, request.AspectRatio, 1), SerializerOptions), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Headers.TryAddWithoutValidation("X-Title", "Cocinaris");

        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var kind = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || status >= 500
                    ? ImageGenerationFailureKind.Transient
                    : response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity ? ImageGenerationFailureKind.Refused : ImageGenerationFailureKind.Invalid;
                return new ImageGenerationResult(null, null, null, model, Elapsed(), kind, $"http_{status}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var bounded = new MemoryStream();
            var buffer = new byte[81_920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (bounded.Length + read > MaxResponseBytes)
                {
                    return new ImageGenerationResult(null, null, null, model, Elapsed(), ImageGenerationFailureKind.Invalid, "response_too_large");
                }

                bounded.Write(buffer, 0, read);
            }

            var body = JsonSerializer.Deserialize<OpenRouterImageResponse>(bounded.ToArray(), SerializerOptions);
            var image = body?.Data?.FirstOrDefault();
            if (image?.B64Json is not { Length: > 0 } encoded)
            {
                return new ImageGenerationResult(null, null, body?.Usage?.Cost, model, Elapsed(), ImageGenerationFailureKind.Invalid, "no_image");
            }

            var bytes = new byte[encoded.Length * 3 / 4 + 3];
            return Convert.TryFromBase64String(encoded, bytes, out var length)
                ? new ImageGenerationResult(bytes[..length], image.MediaType, body?.Usage?.Cost, model, Elapsed(), ImageGenerationFailureKind.None)
                : new ImageGenerationResult(null, null, body?.Usage?.Cost, model, Elapsed(), ImageGenerationFailureKind.Invalid, "bad_base64");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ImageGenerationResult(null, null, null, model, Elapsed(), ImageGenerationFailureKind.Transient, "timeout");
        }
        catch (HttpRequestException)
        {
            return new ImageGenerationResult(null, null, null, model, Elapsed(), ImageGenerationFailureKind.Transient, "network");
        }
        catch (JsonException)
        {
            return new ImageGenerationResult(null, null, null, model, Elapsed(), ImageGenerationFailureKind.Invalid, "bad_json");
        }
    }

    private sealed record OpenRouterImageRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("resolution")] string Resolution,
        [property: JsonPropertyName("aspect_ratio")] string AspectRatio,
        [property: JsonPropertyName("n")] int N);

    private sealed record OpenRouterImageResponse(
        [property: JsonPropertyName("data")] List<OpenRouterImageData>? Data,
        [property: JsonPropertyName("usage")] OpenRouterImageUsage? Usage);

    private sealed record OpenRouterImageData(
        [property: JsonPropertyName("b64_json")] string? B64Json,
        [property: JsonPropertyName("media_type")] string? MediaType);

    private sealed record OpenRouterImageUsage([property: JsonPropertyName("cost")] decimal? Cost);
}

/// <summary>
/// Test-only provider (Testing environment): returns a valid blank PNG of the requested 16:9 size,
/// costing nothing, so the thumbnail job can be exercised without a provider.
/// </summary>
public sealed class FakeImageProvider(int width, int height) : IImageGenerationProvider
{
    /// <inheritdoc />
    public string Name => "fake";

    /// <summary>Number of calls made, for tests.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new ImageGenerationResult(BlankPng(width, height), "image/png", 0m, "fake-image", 1, ImageGenerationFailureKind.None));
    }

    /// <summary>Builds a minimal valid grayscale PNG.</summary>
    public static byte[] BlankPng(int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; // bit depth
        header[9] = 0; // grayscale
        WriteChunk(output, "IHDR", header);
        using var raw = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(raw, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[width + 1];
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
            }
        }

        WriteChunk(output, "IDAT", raw.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32([.. typeBytes, .. data]);
        Span<byte> crcBytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    // PNG chunk checksum (ISO 3309 CRC-32); small enough to keep the test fake dependency-free.
    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
