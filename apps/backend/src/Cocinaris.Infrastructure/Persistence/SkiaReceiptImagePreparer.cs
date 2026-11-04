using Cocinaris.Modules.Inventory.Application;
using SkiaSharp;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// Re-encodes an uploaded receipt photo as a new upright JPEG (PLAN-0046). Only the pixels survive,
/// so metadata such as location never reaches the model. The upload is hostile input; a decoding
/// failure is reported as null. Nothing is written to disk.
/// </summary>
public sealed class SkiaReceiptImagePreparer : IReceiptImagePreparer
{
    private const int JpegQuality = 80;

    /// <inheritdoc />
    public byte[]? Prepare(byte[] image)
    {
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        // Phones store the rotation in metadata, which is about to be dropped.
        var quarterTurns = codec.EncodedOrigin switch
        {
            SKEncodedOrigin.BottomRight => 2,
            SKEncodedOrigin.RightTop => 1,
            SKEncodedOrigin.LeftBottom => 3,
            _ => 0
        };
        var scale = Math.Min(1f, (float)PantryImportService.MaxPreparedImageSide / Math.Max(decoded.Width, decoded.Height));
        var width = Math.Max(1, (int)(decoded.Width * scale));
        var height = Math.Max(1, (int)(decoded.Height * scale));
        var (outputWidth, outputHeight) = quarterTurns % 2 == 1 ? (height, width) : (width, height);
        using var surface = SKSurface.Create(new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Opaque));
        if (surface is null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Translate(outputWidth / 2f, outputHeight / 2f);
        canvas.RotateDegrees(90 * quarterTurns);
        using var picture = SKImage.FromBitmap(decoded);
        canvas.DrawImage(picture, SKRect.Create(-width / 2f, -height / 2f, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return encoded?.ToArray();
    }
}
