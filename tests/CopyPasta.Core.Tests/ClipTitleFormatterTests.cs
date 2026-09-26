using CopyPasta.Core.Menu;

namespace CopyPasta.Core.Tests;

public class ClipTitleFormatterTests
{
    // ---- Trimming ---------------------------------------------------------------------

    [Fact]
    public void Surrounding_whitespace_is_removed()
    {
        Assert.Equal("hello", ClipTitleFormatter.TrimForMenu("   hello  ", 20));
    }

    [Fact]
    public void Only_the_first_line_is_kept()
    {
        // A copied code block or log excerpt would otherwise make one unusable menu item.
        Assert.Equal("first", ClipTitleFormatter.TrimForMenu("first\nsecond\nthird", 20));
        Assert.Equal("first", ClipTitleFormatter.TrimForMenu("first\r\nsecond", 20));
    }

    [Fact]
    public void A_long_title_is_truncated_with_an_ellipsis_inside_the_limit()
    {
        string result = ClipTitleFormatter.TrimForMenu(new string('x', 50), 10);

        Assert.Equal(10, result.Length);
        Assert.Equal("xxxxxxx...", result);
    }

    [Fact]
    public void A_title_exactly_at_the_limit_is_untouched()
    {
        Assert.Equal("1234567890", ClipTitleFormatter.TrimForMenu("1234567890", 10));
    }

    [Fact]
    public void The_limit_is_never_shorter_than_the_ellipsis()
    {
        // Matching macOS, which raises the limit to the ellipsis length rather than producing
        // nonsense from a limit of 1.
        Assert.Equal("...", ClipTitleFormatter.TrimForMenu(new string('x', 50), 1));
        Assert.Equal("...", ClipTitleFormatter.TrimForMenu(new string('x', 50), 0));
    }

    [Fact]
    public void Whitespace_only_text_trims_to_nothing()
    {
        Assert.Equal(string.Empty, ClipTitleFormatter.TrimForMenu("   \r\n  ", 20));
    }

    // ---- Kind prefixes ----------------------------------------------------------------

    [Theory]
    [InlineData("PNG", "(Image)")]
    [InlineData("CF_DIBV5", "(Image)")]
    [InlineData("CF_DIB", "(Image)")]
    [InlineData("TIFF", "(Image)")]
    [InlineData("Portable Document Format", "(PDF)")]
    [InlineData("CF_HDROP", "(Files)")]
    [InlineData("FileNameW", "(Files)")]
    [InlineData("HTML Format", "(HTML)")]
    public void A_kind_prefix_identifies_entries_whose_text_would_not(string format, string prefix)
    {
        string title = ClipTitleFormatter.TypedTitle(
            "thing",
            ClipboardFormat.FromName(format),
            20);

        Assert.Equal($"{prefix} thing", title);
    }

    [Theory]
    [InlineData("CF_UNICODETEXT")]
    [InlineData("Rich Text Format")]
    [InlineData("UniformResourceLocatorW")]
    public void Text_like_formats_get_no_prefix(string format)
    {
        Assert.Equal(
            "thing",
            ClipTitleFormatter.TypedTitle("thing", ClipboardFormat.FromName(format), 20));
    }

    [Fact]
    public void A_clip_with_no_format_gets_no_prefix()
    {
        Assert.Equal("thing", ClipTitleFormatter.TypedTitle("thing", null, 20));
    }

    [Fact]
    public void An_image_with_no_text_shows_just_its_prefix()
    {
        Assert.Equal(
            "(Image)",
            ClipTitleFormatter.TypedTitle(string.Empty, ClipboardFormat.Png, 20));
    }

    [Fact]
    public void The_prefix_does_not_count_against_the_title_limit()
    {
        // Matching macOS: the limit applies to the clip's own text, then the prefix is prepended.
        string title = ClipTitleFormatter.TypedTitle(new string('x', 50), ClipboardFormat.Png, 10);

        Assert.Equal("(Image) xxxxxxx...", title);
    }

    // ---- Numbering ---------------------------------------------------------------------

    [Fact]
    public void A_numbered_title_reads_number_dot_space_title()
    {
        Assert.Equal("7. hello", ClipTitleFormatter.Numbered("hello", 7, showsNumber: true));
    }

    [Fact]
    public void Numbering_can_be_suppressed()
    {
        Assert.Equal("hello", ClipTitleFormatter.Numbered("hello", 7, showsNumber: false));
    }

    [Fact]
    public void A_range_title_reads_first_dash_last()
    {
        Assert.Equal("11 - 20", ClipTitleFormatter.Range(11, 20));
    }

    // ---- Tooltips -----------------------------------------------------------------------

    [Fact]
    public void A_tooltip_shows_what_the_title_had_to_cut()
    {
        // Built from the untrimmed text, including the later lines the title dropped.
        string? tip = ClipTitleFormatter.ToolTip("first\nsecond", showsToolTips: true, 200);

        Assert.Equal("first\nsecond", tip);
    }

    [Fact]
    public void A_long_tooltip_is_capped_without_an_ellipsis()
    {
        string? tip = ClipTitleFormatter.ToolTip(new string('x', 500), showsToolTips: true, 200);

        Assert.Equal(200, tip!.Length);
    }

    [Fact]
    public void Tooltips_can_be_turned_off()
    {
        Assert.Null(ClipTitleFormatter.ToolTip("hello", showsToolTips: false, 200));
    }

    [Fact]
    public void An_empty_clip_has_no_tooltip()
    {
        Assert.Null(ClipTitleFormatter.ToolTip(string.Empty, showsToolTips: true, 200));
    }

    [Fact]
    public void A_non_positive_tooltip_limit_yields_no_tooltip()
    {
        Assert.Null(ClipTitleFormatter.ToolTip("hello", showsToolTips: true, 0));
    }

    // ---- Argument validation --------------------------------------------------------------

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ClipTitleFormatter.TrimForMenu(null!, 20));
        Assert.Throws<ArgumentNullException>(() => ClipTitleFormatter.TypedTitle(null!, null, 20));
        Assert.Throws<ArgumentNullException>(() => ClipTitleFormatter.Numbered(null!, 1, true));
        Assert.Throws<ArgumentNullException>(() => ClipTitleFormatter.ToolTip(null!, true, 20));
    }
}
