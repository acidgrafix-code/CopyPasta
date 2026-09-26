using System.Text;

namespace CopyPasta.Core.Tests;

public class ClipContentHasherTests
{
    private static readonly byte[] HiUtf16 = [0x68, 0x00, 0x69, 0x00, 0x00, 0x00];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    private static ClipAsset Asset(ClipboardFormat format, byte[] data) => new(format, data);

    private static ClipAsset Text(string value) =>
        new(ClipboardFormat.UnicodeText, Encoding.Unicode.GetBytes(value + '\0'));

    /// <summary>
    /// Pins the serialisation layout. The hash is the database primary key, so a change here
    /// orphans every stored row — if this test fails, the algorithm changed and the change
    /// needs a migration, not a new expected value.
    /// </summary>
    [Fact]
    public void Golden_vector()
    {
        string hash = ClipContentHasher.Compute(
        [
            Asset(ClipboardFormat.UnicodeText, HiUtf16),
            Asset(ClipboardFormat.Png, PngMagic),
        ]);

        Assert.Equal("37730df622616b03a692722cd1ec4ef4be3776d69885df8bf80f69906bf078c1", hash);
    }

    [Fact]
    public void Hashing_is_deterministic()
    {
        Assert.Equal(
            ClipContentHasher.Compute([Text("hello")]),
            ClipContentHasher.Compute([Text("hello")]));
    }

    [Fact]
    public void Different_data_hashes_differently()
    {
        Assert.NotEqual(
            ClipContentHasher.Compute([Text("hello")]),
            ClipContentHasher.Compute([Text("hellp")]));
    }

    [Fact]
    public void The_same_bytes_under_a_different_format_hash_differently()
    {
        Assert.NotEqual(
            ClipContentHasher.Compute([Asset(ClipboardFormat.Png, PngMagic)]),
            ClipContentHasher.Compute([Asset(ClipboardFormat.Tiff, PngMagic)]));
    }

    [Fact]
    public void Asset_order_is_part_of_the_hash()
    {
        ClipAsset text = Text("hello");
        ClipAsset image = Asset(ClipboardFormat.Png, PngMagic);

        Assert.NotEqual(
            ClipContentHasher.Compute([text, image]),
            ClipContentHasher.Compute([image, text]));
    }

    [Fact]
    public void Length_prefixes_stop_field_boundaries_from_colliding()
    {
        // This is the entire reason the serialisation is length-prefixed rather than a plain
        // concatenation: ("AB", "C") and ("A", "BC") would otherwise be indistinguishable,
        // and two unrelated clips would share a primary key.
        string first = ClipContentHasher.Compute(
            [Asset(ClipboardFormat.FromName("AB"), "C"u8.ToArray())]);
        string second = ClipContentHasher.Compute(
            [Asset(ClipboardFormat.FromName("A"), "BC"u8.ToArray())]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Splitting_the_same_bytes_across_assets_differently_hashes_differently()
    {
        ClipboardFormat format = ClipboardFormat.FromName("X");

        Assert.NotEqual(
            ClipContentHasher.Compute([Asset(format, "AB"u8.ToArray())]),
            ClipContentHasher.Compute(
                [Asset(format, "A"u8.ToArray()), Asset(format, "B"u8.ToArray())]));
    }

    [Fact]
    public void Case_differences_in_a_known_format_name_do_not_change_the_hash()
    {
        // FromName canonicalises, so a clip captured as "png" and one captured as "PNG" are
        // the same clip.
        Assert.Equal(
            ClipContentHasher.Compute([Asset(ClipboardFormat.FromName("PNG"), PngMagic)]),
            ClipContentHasher.Compute([Asset(ClipboardFormat.FromName("png"), PngMagic)]));
    }

    [Fact]
    public void An_empty_asset_list_hashes_the_empty_input()
    {
        // SHA-256 of zero bytes. Callers reject empty clips before this point
        // (ClipContent.TryCreate), so this only documents the degenerate case.
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ClipContentHasher.Compute([]));
    }

    [Fact]
    public void Empty_asset_data_is_distinct_from_no_asset_at_all()
    {
        Assert.NotEqual(
            ClipContentHasher.Compute([]),
            ClipContentHasher.Compute([Asset(ClipboardFormat.Png, [])]));
    }

    [Fact]
    public void Large_payloads_hash_without_buffering_the_whole_clip()
    {
        byte[] big = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(big);

        string hash = ClipContentHasher.Compute([Asset(ClipboardFormat.Png, big)]);

        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Hashes_are_lowercase_hex()
    {
        string hash = ClipContentHasher.Compute([Text("hello")]);

        Assert.Equal(64, hash.Length);
        Assert.All(hash, character => Assert.Contains(character, "0123456789abcdef"));
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ClipContentHasher.Compute(null!));
    }
}
