using System.Buffers.Binary;

namespace Cocinaris.Modules.Ai.Images;

/// <summary>What the bytes of an image actually are, read from its own header.</summary>
/// <param name="MediaType"><c>image/png</c>, <c>image/jpeg</c>, or <c>image/webp</c>.</param>
/// <param name="Extension">File extension without a dot.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public sealed record InspectedImage(string MediaType, string Extension, int Width, int Height);

/// <summary>
/// Identifies PNG, JPEG, and WebP images by file signature and reads their dimensions from the
/// header, without decoding pixels. Provider output is hostile input: the reported media type is
/// ignored, and anything else (SVG, HTML, truncated data) is rejected.
/// </summary>
public static class ImageInspector
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Returns the image's type and size, or null when the bytes are not a supported image.</summary>
    public static InspectedImage? Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(PngSignature) && bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return Sized("image/png", "png", BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4)));
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return Jpeg(bytes);
        }

        if (bytes.Length >= 30 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return WebP(bytes);
        }

        return null;
    }

    private static InspectedImage? Sized(string mediaType, string extension, int width, int height) =>
        width is > 0 and <= 16_384 && height is > 0 and <= 16_384 ? new InspectedImage(mediaType, extension, width, height) : null;

    // Walks JPEG segments to the first start-of-frame marker, which holds the height and width.
    private static InspectedImage? Jpeg(ReadOnlySpan<byte> bytes)
    {
        var offset = 2;
        while (offset + 9 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                return null;
            }

            var marker = bytes[offset + 1];
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 2, 2));
            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isStartOfFrame)
            {
                return Sized("image/jpeg", "jpg", BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 7, 2)), BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2)));
            }

            if (length < 2)
            {
                return null;
            }

            offset += 2 + length;
        }

        return null;
    }

    private static InspectedImage? WebP(ReadOnlySpan<byte> bytes)
    {
        var chunk = bytes.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            // 24-bit little-endian canvas size minus one.
            var width = 1 + (bytes[24] | bytes[25] << 8 | bytes[26] << 16);
            var height = 1 + (bytes[27] | bytes[28] << 8 | bytes[29] << 16);
            return Sized("image/webp", "webp", width, height);
        }

        if (chunk.SequenceEqual("VP8 "u8) && bytes[23] == 0x9D && bytes[24] == 0x01 && bytes[25] == 0x2A)
        {
            return Sized("image/webp", "webp", BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(26, 2)) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28, 2)) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8) && bytes[20] == 0x2F)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(21, 4));
            return Sized("image/webp", "webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        return null;
    }
}
