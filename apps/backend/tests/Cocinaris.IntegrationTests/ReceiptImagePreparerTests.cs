using System.Text;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Inventory.Application;
using SkiaSharp;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0046 Phase 3: what leaves the host is a new, smaller image without metadata.</summary>
public sealed class ReceiptImagePreparerTests
{
    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        return encoded.ToArray();
    }

    [Fact]
    public void PreparingAPhotoDropsMetadataAndShrinksIt()
    {
        var original = Jpeg(3200, 800);
        // An application segment right after the start of the JPEG, as a phone stores location.
        var secret = Encoding.ASCII.GetBytes("GPSSECRET-LOCATION");
        var length = secret.Length + 2;
        byte[] segment = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. secret];
        byte[] withMetadata = [.. original[..2], .. segment, .. original[2..]];

        var prepared = new SkiaReceiptImagePreparer().Prepare(withMetadata);

        Assert.NotNull(prepared);
        Assert.DoesNotContain("GPSSECRET", Encoding.ASCII.GetString(prepared));
        using var decoded = SKBitmap.Decode(prepared);
        Assert.Equal(PantryImportService.MaxPreparedImageSide, decoded.Width);
        Assert.Equal(400, decoded.Height);
    }

    [Fact]
    public void WhatIsNotAnImageIsNotPrepared() =>
        Assert.Null(new SkiaReceiptImagePreparer().Prepare(Encoding.ASCII.GetBytes("<svg onload=alert(1)>")));
}
