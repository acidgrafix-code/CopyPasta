using System.Buffers.Binary;

namespace CopyPasta.Core.Tests;

public class HdropReaderTests
{
    [Fact]
    public void Reads_a_single_wide_path()
    {
        Assert.Equal(
            new[] { @"C:\pics\shot.png" },
            HdropReader.ReadPaths(Dropfiles.Wide(@"C:\pics\shot.png")).ToArray());
    }

    [Fact]
    public void Reads_several_wide_paths_in_order()
    {
        Assert.Equal(
            new[] { @"C:\a.txt", @"C:\b.txt", @"C:\c.txt" },
            HdropReader.ReadPaths(Dropfiles.Wide(@"C:\a.txt", @"C:\b.txt", @"C:\c.txt")).ToArray());
    }

    [Fact]
    public void Reads_ANSI_paths()
    {
        Assert.Equal(
            new[] { @"C:\a.txt", @"C:\b.txt" },
            HdropReader.ReadPaths(Dropfiles.Ansi(@"C:\a.txt", @"C:\b.txt")).ToArray());
    }

    [Fact]
    public void Reads_non_ASCII_wide_paths()
    {
        // Not a verbatim string: the \uXXXX escapes have to be interpreted.
        const string path = "C:\\\u30d5\u30a9\u30eb\u30c0\u30fc\\caf\u00e9.png";

        Assert.Equal(new[] { path }, HdropReader.ReadPaths(Dropfiles.Wide(path)).ToArray());
    }

    [Fact]
    public void Honours_a_list_offset_beyond_the_fixed_header()
    {
        // Real-world blobs sometimes pad between the header and the path list.
        Assert.Equal(
            new[] { @"C:\a.txt" },
            HdropReader.ReadPaths(Dropfiles.WithListOffset(64, @"C:\a.txt")).ToArray());
    }

    // ---- Malformed input: must degrade, never throw ------------------------------------
    // These blobs are written by arbitrary third-party apps, so the parser is the boundary
    // between us and their bugs.

    [Fact]
    public void An_empty_blob_yields_no_paths()
    {
        Assert.Empty(HdropReader.ReadPaths([]));
    }

    [Fact]
    public void A_blob_shorter_than_the_header_yields_no_paths()
    {
        Assert.Empty(HdropReader.ReadPaths(new byte[8]));
    }

    [Fact]
    public void A_header_with_no_path_list_yields_no_paths()
    {
        Assert.Empty(HdropReader.ReadPaths(new byte[20]));
    }

    [Fact]
    public void A_list_offset_inside_the_header_is_rejected()
    {
        Assert.Empty(HdropReader.ReadPaths(Dropfiles.WithListOffset(4, @"C:\a.txt")));
    }

    [Fact]
    public void A_list_offset_past_the_end_of_the_blob_is_rejected()
    {
        byte[] blob = Dropfiles.Wide(@"C:\a.txt");
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(0), (uint)blob.Length + 16);

        Assert.Empty(HdropReader.ReadPaths(blob));
    }

    [Fact]
    public void A_truncated_path_list_still_yields_the_intact_paths()
    {
        byte[] blob = Dropfiles.Wide(@"C:\a.txt", @"C:\b.txt");

        // Lose the terminator and part of the last path, including a dangling odd byte.
        IReadOnlyList<string> paths = HdropReader.ReadPaths(blob.AsSpan(0, blob.Length - 9));

        Assert.Equal(2, paths.Count);
        Assert.Equal(@"C:\a.txt", paths[0]);
        Assert.StartsWith(@"C:\b", paths[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Trailing_content_after_the_double_terminator_is_ignored()
    {
        byte[] blob = Dropfiles.Wide(@"C:\a.txt");
        byte[] padded = new byte[blob.Length + 4];
        blob.CopyTo(padded, 0);
        "junk"u8.CopyTo(padded.AsSpan(blob.Length));

        Assert.Equal(new[] { @"C:\a.txt" }, HdropReader.ReadPaths(padded).ToArray());
    }
}
