using Cocinaris.Modules.Recipes.Thumbnails;
using SkiaSharp;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// WebP encoder for recipe images (PLAN-0030 follow-up), on SkiaSharp. The provider returns
/// 236–330 KB JPEGs at 1024 × 576; the full WebP is about a quarter of that, and the card size is a
/// fraction more. Encoding runs once per image, when it is generated or first read.
/// </summary>
public sealed class SkiaThumbnailImageEncoder : IThumbnailImageEncoder
{
    /// <summary>Card width; 480 covers a 144 CSS-pixel card on a 3× screen.</summary>
    public const int SmallWidth = 480;

    /// <summary>Card height (16:9 of <see cref="SmallWidth"/>).</summary>
    public const int SmallHeight = 270;

    private const int FullQuality = 80;
    private const int SmallQuality = 75;

    /// <inheritdoc />
    public byte[]? EncodeWebP(byte[] original, RecipeThumbnailSize size)
    {
        using var decoded = SKBitmap.Decode(original);
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
        {
            return null;
        }

        if (size == RecipeThumbnailSize.Full)
        {
            return Encode(decoded, FullQuality);
        }

        using var resized = decoded.Resize(new SKImageInfo(SmallWidth, SmallHeight), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return resized is null ? null : Encode(resized, SmallQuality);
    }

    private static byte[]? Encode(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image?.Encode(SKEncodedImageFormat.Webp, quality);
        return data?.ToArray();
    }
}
