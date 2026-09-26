using System.Text;

namespace CopyPasta.Core.Tests;

public class ClipContentTests
{
    private static ClipAsset Text(string value) =>
        new(ClipboardFormat.UnicodeText, Encoding.Unicode.GetBytes(value + '\0'));

    private static ClipAsset Asset(ClipboardFormat format, params byte[] data) => new(format, data);

    private static ClipContent Create(params ClipAsset[] assets)
    {
        Assert.True(ClipContent.TryCreate(assets, out ClipContent? content));
        return content;
    }

    // ---- Construction -----------------------------------------------------------------

    [Fact]
    public void A_clip_with_no_assets_cannot_be_created()
    {
        Assert.False(ClipContent.TryCreate([], out ClipContent? content));
        Assert.Null(content);
    }

    [Fact]
    public void Formats_mirror_the_assets_in_order()
    {
        ClipContent content = Create(
            Asset(ClipboardFormat.Png, 1),
            Text("hello"));

        AssertFormats(content.Formats, ClipboardFormat.Png, ClipboardFormat.UnicodeText);
        Assert.Equal(ClipboardFormat.Png, content.PrimaryFormat);
    }

    [Fact]
    public void The_hash_matches_the_hasher()
    {
        ClipAsset[] assets = [Text("hello")];
        ClipContent content = Create(assets);

        Assert.Equal(ClipContentHasher.Compute(assets), content.Hash);
    }

    [Fact]
    public void An_asset_must_have_a_named_format()
    {
        Assert.Throws<ArgumentException>(() => new ClipAsset(default, [1]));
    }

    // ---- Ordering against a selection --------------------------------------------------

    [Fact]
    public void Assets_are_reordered_to_match_the_selection()
    {
        // Capture order is not guaranteed to match the selection, and the order matters: it
        // decides the primary format and feeds the hash.
        ClipboardSelection selection = new()
        {
            Formats = [ClipboardFormat.Png, ClipboardFormat.UnicodeText],
        };

        Assert.True(ClipContent.TryCreate(
            selection,
            [Text("hello"), Asset(ClipboardFormat.Png, 1)],
            out ClipContent? content));

        AssertFormats(content.Formats, ClipboardFormat.Png, ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void Assets_the_selection_excludes_are_dropped()
    {
        ClipboardSelection selection = new() { Formats = [ClipboardFormat.UnicodeText] };

        Assert.True(ClipContent.TryCreate(
            selection,
            [Text("hello"), Asset(ClipboardFormat.Dib, 1)],
            out ClipContent? content));

        AssertFormats(content.Formats, ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void A_selection_with_no_matching_assets_produces_no_clip()
    {
        ClipboardSelection selection = new() { Formats = [ClipboardFormat.Png] };

        Assert.False(ClipContent.TryCreate(selection, [Text("hello")], out ClipContent? content));
        Assert.Null(content);
    }

    // ---- Text -------------------------------------------------------------------------

    [Fact]
    public void Text_is_decoded_as_UTF16_with_the_terminator_trimmed()
    {
        ClipContent content = Create(Text("hello"));

        Assert.Equal("hello", content.TextValue);
    }

    [Fact]
    public void Text_decoding_handles_non_ASCII_and_characters_outside_the_BMP()
    {
        const string value = "caf\u00e9 \u65e5\u672c\u8a9e \ud83d\ude00";
        ClipContent content = Create(Text(value));

        Assert.Equal(value, content.TextValue);
    }

    [Fact]
    public void A_clip_with_no_unicode_text_has_no_text_value()
    {
        ClipContent content = Create(Asset(ClipboardFormat.Png, 1));

        Assert.Null(content.TextValue);
    }

    // ---- Blank text --------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public void Whitespace_only_text_is_blank(string value)
    {
        Assert.True(Create(Text(value)).IsBlankText);
    }

    [Fact]
    public void Real_text_is_not_blank()
    {
        Assert.False(Create(Text("hello")).IsBlankText);
    }

    [Fact]
    public void An_image_whose_accompanying_text_is_blank_is_not_blank()
    {
        // Keyed on the primary format, exactly as macOS does: a screenshot is worth keeping
        // even though the text flavour alongside it is empty.
        ClipContent content = Create(Asset(ClipboardFormat.Png, 1), Text("   "));

        Assert.False(content.IsBlankText);
    }

    [Fact]
    public void A_blank_RTF_clip_is_blank_even_though_RTF_itself_is_not_examined()
    {
        ClipContent content = Create(
            Asset(ClipboardFormat.Rtf, Encoding.ASCII.GetBytes(@"{\rtf1 }")),
            Text("  "));

        Assert.True(content.IsBlankText);
    }

    [Fact]
    public void A_file_clip_is_never_blank_text()
    {
        Assert.False(Create(Asset(ClipboardFormat.Hdrop, 1)).IsBlankText);
    }

    // ---- Images ------------------------------------------------------------------------

    [Fact]
    public void Image_data_prefers_the_least_lossy_representation()
    {
        ClipContent content = Create(
            Asset(ClipboardFormat.Dib, 1),
            Asset(ClipboardFormat.Png, 2));

        AssertBytes(new byte[] { 2 }, content.ImageData);
    }

    [Fact]
    public void Image_data_falls_back_through_the_preference_order()
    {
        AssertBytes(new byte[] { 1 }, Create(Asset(ClipboardFormat.DibV5, 1)).ImageData);
        AssertBytes(new byte[] { 2 }, Create(Asset(ClipboardFormat.Dib, 2)).ImageData);
        AssertBytes(new byte[] { 3 }, Create(Asset(ClipboardFormat.Tiff, 3)).ImageData);
    }

    [Fact]
    public void A_text_clip_has_no_image_data()
    {
        Assert.Null(Create(Text("hello")).ImageData);
    }

    // ---- Copied image files ------------------------------------------------------------

    [Fact]
    public void A_copied_image_file_exposes_its_path_for_thumbnailing()
    {
        ClipContent content = Create(
            new ClipAsset(ClipboardFormat.Hdrop, Dropfiles.Wide(@"C:\pics\shot.PNG")));

        Assert.Equal(@"C:\pics\shot.PNG", content.ImageFilePath);
    }

    [Fact]
    public void The_first_image_among_several_copied_files_is_used()
    {
        ClipContent content = Create(new ClipAsset(
            ClipboardFormat.Hdrop,
            Dropfiles.Wide(@"C:\docs\notes.txt", @"C:\pics\a.jpg", @"C:\pics\b.png")));

        Assert.Equal(@"C:\pics\a.jpg", content.ImageFilePath);
    }

    [Fact]
    public void Copied_non_image_files_expose_no_image_path()
    {
        ClipContent content = Create(
            new ClipAsset(ClipboardFormat.Hdrop, Dropfiles.Wide(@"C:\docs\notes.txt")));

        Assert.Null(content.ImageFilePath);
    }

    [Fact]
    public void A_clip_without_file_references_exposes_no_image_path()
    {
        Assert.Null(Create(Text("hello")).ImageFilePath);
    }

    // ---- Lookup ------------------------------------------------------------------------

    [Fact]
    public void DataFor_returns_null_for_a_format_the_clip_does_not_hold()
    {
        Assert.Null(Create(Text("hello")).DataFor(ClipboardFormat.Png));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ClipContent.TryCreate(null!, out _));
        Assert.Throws<ArgumentNullException>(() =>
            ClipContent.TryCreate(ClipboardSelection.None, null!, out _));
        Assert.Throws<ArgumentNullException>(() =>
            ClipContent.TryCreate(null!, [Text("x")], out _));
    }
}
