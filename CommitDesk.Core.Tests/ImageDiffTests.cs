using System.Buffers.Binary;
using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

public class ImageDiffTests
{
    /// <summary>The start of a PNG: signature and IHDR with the given size (enough for the header reader).</summary>
    static byte[] Png(int width, int height, byte fill = 0)
    {
        var b = new byte[64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), height);
        b[40] = fill;   // makes otherwise-identical test files differ
        return b;
    }

    [Fact]
    public void ReadsPngGifBmpIcoWebpJpegHeaders()
    {
        Assert.Equal(("PNG", 487, 300), ImageInfo.Read(Png(487, 300)));

        var gif = new byte[16]; "GIF89a"u8.CopyTo(gif); BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(6), 32); BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(8), 24);
        Assert.Equal(("GIF", 32, 24), ImageInfo.Read(gif));

        var bmp = new byte[40]; bmp[0] = (byte)'B'; bmp[1] = (byte)'M'; BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 640); BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -480);
        Assert.Equal(("BMP", 640, 480), ImageInfo.Read(bmp));

        // Two entries, 16 and 256 (stored as 0): the largest is reported
        var ico = new byte[6 + 32]; ico[2] = 1; ico[4] = 2; ico[6] = 16; ico[7] = 16; ico[22] = 0; ico[23] = 0;
        Assert.Equal(("ICO", 256, 256), ImageInfo.Read(ico));

        var webp = new byte[32]; "RIFF"u8.CopyTo(webp); "WEBP"u8.CopyTo(webp.AsSpan(8)); "VP8X"u8.CopyTo(webp.AsSpan(12));
        webp[24] = 99; webp[27] = 49;   // stored as size - 1
        Assert.Equal(("WebP", 100, 50), ImageInfo.Read(webp));

        // SOI, an APP0 segment to skip, then SOF0 with height 120 and width 160
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x78, 0x00, 0xA0, 0x03, 0, 0, 0 };
        Assert.Equal(("JPEG", 160, 120), ImageInfo.Read(jpeg));

        Assert.Equal(("Image", null, null), ImageInfo.Read("not an image"u8));
    }

    /// <summary>An .ico holding the given entries (each already encoded) at the given sizes.</summary>
    static byte[] Ico(params (int Size, byte[] Data)[] entries)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)entries.Length);
        int offset = 6 + 16 * entries.Length;
        foreach (var (size, data) in entries)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32); w.Write((uint)data.Length); w.Write((uint)offset);
            offset += data.Length;
        }
        foreach (var (_, data) in entries) w.Write(data);
        return ms.ToArray();
    }

    [Fact]
    public void IconPreviewUsesTheLargestPngEntry()
    {
        var small = Png(16, 16); var large = Png(256, 256);
        var version = ImageVersion.From(Ico((16, small), (256, large)));
        Assert.Equal(("ICO", 256, 256), (version.Format, version.Width, version.Height));
        Assert.Equal(large, version.DisplayBytes);
    }

    [Fact]
    public void IconPreviewWrapsA32BitBitmapEntry()
    {
        // 2x2 32-bit DIB as stored in an icon: height doubled (image + AND mask), then BGRA pixels and the mask
        var dib = new byte[40 + 2 * 2 * 4 + 8];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
        for (int i = 40; i < 56; i++) dib[i] = 0xAB;

        var bmp = ImageVersion.From(Ico((2, dib))).DisplayBytes;
        Assert.Equal(("BMP", 2, 2), ImageInfo.Read(bmp));                   // a valid BMP with the real height
        Assert.Equal(14 + 40 + 16, bmp.Length);                             // file header + info header + pixels, no mask
        Assert.Equal(0xAB, bmp[14 + 40]);
    }

    [Fact]
    public void NonIconDisplaysAsIs()
    {
        var png = Png(10, 10);
        Assert.Same(png, ImageVersion.From(png).DisplayBytes);
    }

    [Fact]
    public void CaptionShowsFormatSizeAndBytes()
    {
        Assert.Equal("PNG · 487 × 300 · 64 bytes", ImageVersion.From(Png(487, 300)).Caption);
        Assert.Equal("Image · 2 KB", ImageVersion.From(new byte[2048]).Caption);
    }

    [Theory]
    [InlineData("assets/logo.png", true)]
    [InlineData("icon.ICO", true)]
    [InlineData("photo.jpeg", true)]
    [InlineData("vector.svg", false)]
    [InlineData("readme.md", false)]
    public void IsImage(string path, bool image) => Assert.Equal(image, ImageDiff.IsImage(path));

    [Fact]
    public async Task WorkingAndCommitImageDiffs()
    {
        using var t = await TempRepo.CreateAsync();
        // Binary in git's eyes, like a real image
        File.WriteAllBytes(Path.Combine(t.Dir, "logo.png"), Png(16, 16));
        File.WriteAllBytes(Path.Combine(t.Dir, "old.png"), Png(8, 8));
        await t.CommitAllAsync("add images");

        // Working tree: logo.png modified, old.png deleted, new.png added
        File.WriteAllBytes(Path.Combine(t.Dir, "logo.png"), Png(32, 32, fill: 1));
        File.Delete(Path.Combine(t.Dir, "old.png"));
        File.WriteAllBytes(Path.Combine(t.Dir, "new.png"), Png(64, 48));
        var status = await t.Repo.GetStatusAsync();

        var modified = await t.Repo.GetWorkingImageDiffAsync(status.Files.Single(f => f.Path == "logo.png"), status.IsUnborn);
        Assert.Equal((16, 16), (modified.Old!.Width, modified.Old.Height));
        Assert.Equal((32, 32), (modified.New!.Width, modified.New.Height));

        var deleted = await t.Repo.GetWorkingImageDiffAsync(status.Files.Single(f => f.Path == "old.png"), status.IsUnborn);
        Assert.NotNull(deleted.Old);
        Assert.Null(deleted.New);

        var added = await t.Repo.GetWorkingImageDiffAsync(status.Files.Single(f => f.Path == "new.png"), status.IsUnborn);
        Assert.Null(added.Old);
        Assert.Equal((64, 48), (added.New!.Width, added.New.Height));

        // History: commit those changes and read the same pairs back from the commit
        await t.CommitAllAsync("change images");
        var commit = (await t.Repo.GetLogAsync(0, 1))[0];
        var files = await t.Repo.GetCommitFilesAsync(commit);
        var inCommit = await t.Repo.GetCommitImageDiffAsync(commit, files.Single(f => f.Path == "logo.png"));
        Assert.Equal(16, inCommit.Old!.Width);
        Assert.Equal(32, inCommit.New!.Width);
        Assert.Null((await t.Repo.GetCommitImageDiffAsync(commit, files.Single(f => f.Path == "old.png"))).New);
        Assert.Null((await t.Repo.GetCommitImageDiffAsync(commit, files.Single(f => f.Path == "new.png"))).Old);

        // The first commit has no parent: everything in it is "added"
        var first = (await t.Repo.GetLogAsync(1, 1))[0];
        var root = await t.Repo.GetCommitImageDiffAsync(first, (await t.Repo.GetCommitFilesAsync(first)).Single(f => f.Path == "logo.png"));
        Assert.Null(root.Old);
        Assert.Equal(16, root.New!.Width);
    }
}
