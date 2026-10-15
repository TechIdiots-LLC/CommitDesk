using System.Buffers.Binary;

namespace CommitDesk.Core.Git;

/// <summary>One side of an image change: the file's bytes, and its format and pixel size when the header says.</summary>
public sealed record ImageVersion(byte[] Bytes, string Format, int? Width, int? Height)
{
    public static ImageVersion From(byte[] bytes)
    {
        var (format, width, height) = ImageInfo.Read(bytes);
        return new ImageVersion(bytes, format, width, height);
    }

    /// <summary>
    /// The bytes to display. Image decoders show an .ico's first image (often 16 px), blurrily stretched, so for icons
    /// this is the largest image inside the file instead.
    /// </summary>
    public byte[] DisplayBytes => Format == "ICO" ? ImageInfo.LargestIconImage(Bytes) ?? Bytes : Bytes;

    /// <summary>"PNG · 487 × 487 · 247 KB".</summary>
    public string Caption
    {
        get
        {
            var size = Bytes.Length switch
            {
                < 1024 => $"{Bytes.Length} bytes",
                < 1024 * 1024 => $"{Bytes.Length / 1024.0:0.#} KB",
                _ => $"{Bytes.Length / (1024.0 * 1024):0.##} MB",
            };
            return Width is { } w && Height is { } h ? $"{Format} · {w} × {h} · {size}" : $"{Format} · {size}";
        }
    }
}

/// <summary>An image file's previous and current versions; one side is null when the file was added or deleted.</summary>
public sealed record ImageDiff(ImageVersion? Old, ImageVersion? New)
{
    /// <summary>Larger files are not previewed.</summary>
    public const long MaxPreviewBytes = 50L * 1024 * 1024;

    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tif", ".tiff",
    };

    /// <summary>Whether the path is an image shown as a picture rather than a text diff.</summary>
    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path));
}

/// <summary>Reads an image's format and pixel size from its header, without decoding it.</summary>
public static class ImageInfo
{
    /// <summary>
    /// The largest image in an .ico, as a standalone PNG or BMP file, or null when it can't be extracted (then the
    /// whole file is displayed). Entries are PNG (kept as is) or a 32-bit DIB, which gets a BMP file header; the DIB's
    /// height counts the AND mask too, so it is halved, and the mask itself is ignored as 32-bit pixels carry alpha.
    /// </summary>
    public static byte[]? LargestIconImage(ReadOnlySpan<byte> ico)
    {
        try
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(ico[4..]);
            int bestSize = -1, bestOffset = 0, bestLength = 0;
            for (int i = 0; i < count; i++)
            {
                var entry = ico[(6 + 16 * i)..];
                int size = entry[0] == 0 ? 256 : entry[0];
                int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
                if (size > bestSize && offset > 0 && length > 0 && (long)offset + length <= ico.Length)
                    (bestSize, bestOffset, bestLength) = (size, offset, length);
            }
            if (bestSize < 0) return null;

            var image = ico.Slice(bestOffset, bestLength);
            if (image.Length >= 8 && image[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
                return image.ToArray();

            // BITMAPINFOHEADER: only 32 bpp is handled; paletted icons fall back to the whole file
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(image);
            if (headerSize < 40 || BinaryPrimitives.ReadUInt16LittleEndian(image[14..]) != 32) return null;
            int height = BinaryPrimitives.ReadInt32LittleEndian(image[8..]);
            int width = BinaryPrimitives.ReadInt32LittleEndian(image[4..]);
            int pixelBytes = width * Math.Abs(height / 2) * 4;
            if (headerSize + pixelBytes > image.Length) return null;

            var bmp = new byte[14 + headerSize + pixelBytes];
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 14 + headerSize);
            image[..(headerSize + pixelBytes)].CopyTo(bmp.AsSpan(14));
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14 + 8), height / 2);
            return bmp;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;   // malformed directory
        }
    }

    public static (string Format, int? Width, int? Height) Read(ReadOnlySpan<byte> b)
    {
        try
        {
            // PNG: signature, then the IHDR chunk's width and height (big-endian)
            if (b.Length >= 24 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
                return ("PNG", BinaryPrimitives.ReadInt32BigEndian(b[16..]), BinaryPrimitives.ReadInt32BigEndian(b[20..]));

            // GIF87a / GIF89a: logical screen size (little-endian)
            if (b.Length >= 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
                return ("GIF", BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));

            // BMP: BITMAPINFOHEADER width/height (height is negative for top-down bitmaps)
            if (b.Length >= 26 && b[0] == 'B' && b[1] == 'M')
                return ("BMP", BinaryPrimitives.ReadInt32LittleEndian(b[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b[22..])));

            // ICO: the largest image in the directory (a stored 0 means 256)
            if (b.Length >= 6 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0)
            {
                int count = BinaryPrimitives.ReadUInt16LittleEndian(b[4..]);
                int best = 0, bestH = 0;
                for (int i = 0; i < count && 6 + 16 * i + 2 <= b.Length; i++)
                {
                    int w = b[6 + 16 * i] == 0 ? 256 : b[6 + 16 * i];
                    int h = b[7 + 16 * i] == 0 ? 256 : b[7 + 16 * i];
                    if (w > best) { best = w; bestH = h; }
                }
                return best > 0 ? ("ICO", best, bestH) : ("ICO", null, null);
            }

            // WebP: RIFF....WEBP with a VP8 (lossy), VP8L (lossless) or VP8X (extended) chunk
            if (b.Length >= 30 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8))
            {
                var chunk = b[12..16];
                if (chunk.SequenceEqual("VP8X"u8))
                    return ("WebP", 1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
                if (chunk.SequenceEqual("VP8L"u8))
                {
                    uint bits = BinaryPrimitives.ReadUInt32LittleEndian(b[21..]);
                    return ("WebP", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
                }
                if (chunk.SequenceEqual("VP8 "u8))
                    return ("WebP", BinaryPrimitives.ReadUInt16LittleEndian(b[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(b[28..]) & 0x3FFF);
                return ("WebP", null, null);
            }

            // JPEG: walk the markers to the first start-of-frame (SOF0-SOF15, except DHT/JPG/DAC)
            if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    byte marker = b[i + 1];
                    if (marker == 0xFF) { i++; continue; }
                    int length = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
                    if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                        return ("JPEG", BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]));
                    i += 2 + length;
                }
                return ("JPEG", null, null);
            }

            // TIFF (II* / MM*): the size is in the IFD; the format alone is enough for the caption
            if (b.Length >= 4 && ((b[0] == 'I' && b[1] == 'I' && b[2] == 42) || (b[0] == 'M' && b[1] == 'M' && b[3] == 42)))
                return ("TIFF", null, null);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Truncated header: fall through
        }
        return ("Image", null, null);
    }
}
