namespace CopyPasta.Core.Tests;

public class ClipboardFormatTests
{
    [Fact]
    public void FromName_is_case_insensitive_like_Win32()
    {
        Assert.Equal(ClipboardFormat.Png, ClipboardFormat.FromName("png"));
        Assert.Equal(ClipboardFormat.Rtf, ClipboardFormat.FromName("RICH TEXT FORMAT"));
    }

    [Fact]
    public void FromName_canonicalises_the_spelling_of_known_formats()
    {
        // This is what keeps the content hash stable: the hash covers the format name's
        // bytes, so a clip captured as "png" must hash identically to one captured as "PNG",
        // or re-copying the same content would create a second database row.
        Assert.Equal("PNG", ClipboardFormat.FromName("png").Name);
        Assert.Equal("CF_UNICODETEXT", ClipboardFormat.FromName("cf_unicodetext").Name);
    }

    [Fact]
    public void FromName_preserves_unknown_names_verbatim()
    {
        ClipboardFormat custom = ClipboardFormat.FromName("Acme.PrivateFormat");

        Assert.Equal("Acme.PrivateFormat", custom.Name);
        Assert.NotEqual(ClipboardFormat.Png, custom);
    }

    [Fact]
    public void Unknown_formats_compare_case_insensitively_too()
    {
        Assert.Equal(ClipboardFormat.FromName("Acme.Thing"), ClipboardFormat.FromName("acme.thing"));
    }

    [Fact]
    public void Default_instance_is_unnamed_and_not_usable()
    {
        Assert.True(default(ClipboardFormat).IsUnnamed);
        Assert.False(ClipboardFormat.Png.IsUnnamed);
    }

    [Fact]
    public void FromName_rejects_an_empty_name()
    {
        Assert.Throws<ArgumentException>(() => ClipboardFormat.FromName(string.Empty));
    }

    [Fact]
    public void FromName_rejects_a_null_name()
    {
        Assert.Throws<ArgumentNullException>(() => ClipboardFormat.FromName(null!));
    }

    [Fact]
    public void Known_formats_have_no_duplicate_names()
    {
        IEnumerable<string> duplicates = ClipboardFormat.Known
            .GroupBy(format => format.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        Assert.Empty(duplicates);
    }
}
