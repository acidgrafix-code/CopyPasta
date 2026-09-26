namespace CopyPasta.Core.Tests;

/// <summary>
/// End-to-end filter behaviour for the clipboard shapes real applications produce.
/// </summary>
/// <remarks>
/// Split into two groups. The <c>Verified</c> region uses format lists captured from a live
/// Windows 11 clipboard with <c>tools/FormatDump</c>, including the exact order the clipboard
/// reported them. The <c>Provisional</c> region is hand-authored expectation: run FormatDump
/// while each application has something on the clipboard, replace the list, and move the test
/// up. Until then, a failure there may mean the expectation was wrong rather than the filter.
/// </remarks>
public class CaptureScenarioTests
{
    private static ClipboardSelection Select(
        ClipboardSnapshot snapshot,
        ClipboardFilterOptions? options = null) =>
        ClipboardFormatFilter.Select(snapshot, options ?? ClipboardFilterOptions.Default);

    // =========================== Verified captures ===================================
    // Captured on Windows 11 with tools/FormatDump. Format order is as reported.

    [Fact]
    public void Verified_text_copy_keeps_one_text_representation()
    {
        // DataObject / Ole Private Data are OLE bookkeeping that no other app can consume;
        // CF_TEXT and CF_OEMTEXT are Windows' own synthesised duplicates of CF_UNICODETEXT.
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.FromName("DataObject"),
            ClipboardFormat.UnicodeText,
            ClipboardFormat.FromName("Ole Private Data"),
            ClipboardFormat.Locale,
            ClipboardFormat.Text,
            ClipboardFormat.OemText));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void Verified_file_copy_keeps_only_the_file_list()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.FromName("DataObject"),
            ClipboardFormat.Hdrop,
            ClipboardFormat.FileNameW,
            ClipboardFormat.FileName,
            ClipboardFormat.FromName("Ole Private Data")));

        AssertFormats(selection.Formats, ClipboardFormat.Hdrop);
    }

    [Fact]
    public void Verified_image_copy_keeps_the_representation_that_preserves_alpha()
    {
        // Note the order: CF_DIB is offered *before* CF_DIBV5, and CF_BITMAP reports zero
        // bytes because it is a handle. The richest representation has to win regardless of
        // the order the clipboard happens to list them in.
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.FromName("DataObject"),
            ClipboardFormat.FromName("System.Drawing.Bitmap"),
            ClipboardFormat.Bitmap,
            ClipboardFormat.FromName("Ole Private Data"),
            ClipboardFormat.Dib,
            ClipboardFormat.DibV5));

        AssertFormats(selection.Formats, ClipboardFormat.DibV5);
    }

    // ========================== Provisional expectations =============================

    [Fact]
    public void Word_processor_keeps_rich_text_html_and_plain_text_but_no_synthesised_duplicates()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.FromName("Rich Text Format"),
            ClipboardFormat.FromName("HTML Format"),
            ClipboardFormat.UnicodeText,
            ClipboardFormat.Text,
            ClipboardFormat.OemText,
            ClipboardFormat.Locale,
            ClipboardFormat.FromName("Object Descriptor"),
            ClipboardFormat.FromName("Embed Source")));

        AssertFormats(
            selection.Formats,
            ClipboardFormat.Rtf,
            ClipboardFormat.Html,
            ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void Browser_page_selection_keeps_html_and_text()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.UnicodeText,
            ClipboardFormat.Text,
            ClipboardFormat.FromName("HTML Format"),
            ClipboardFormat.FromName("Chromium internal source URL")));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText, ClipboardFormat.Html);
    }

    [Fact]
    public void Browser_image_copy_prefers_PNG_over_every_bitmap_flavour()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.FromName("PNG"),
            ClipboardFormat.DibV5,
            ClipboardFormat.Dib,
            ClipboardFormat.Bitmap,
            ClipboardFormat.FromName("HTML Format")));

        AssertFormats(selection.Formats, ClipboardFormat.Png, ClipboardFormat.Html);
    }

    [Fact]
    public void Image_editor_copy_falls_back_to_DIB_when_nothing_richer_is_offered()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.Bitmap,
            ClipboardFormat.Dib,
            ClipboardFormat.FromName("Paint.Picture")));

        AssertFormats(selection.Formats, ClipboardFormat.Dib);
    }

    [Fact]
    public void Explorer_copy_drops_shell_bookkeeping()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.Hdrop,
            ClipboardFormat.FromName("Shell IDList Array"),
            ClipboardFormat.FromName("Preferred DropEffect"),
            ClipboardFormat.FromName("FileGroupDescriptorW"),
            ClipboardFormat.FileNameW,
            ClipboardFormat.FileName));

        AssertFormats(selection.Formats, ClipboardFormat.Hdrop);
    }

    [Fact]
    public void Browser_address_bar_copy_keeps_the_url_and_the_text()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.UnicodeText,
            ClipboardFormat.Text,
            ClipboardFormat.FromName("UniformResourceLocatorW"),
            ClipboardFormat.FromName("UniformResourceLocator")));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText, ClipboardFormat.UrlW);
    }

    [Fact]
    public void Spreadsheet_copy_keeps_text_html_rich_text_and_the_rendered_image()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.FromName("Biff12"),
            ClipboardFormat.FromName("HTML Format"),
            ClipboardFormat.FromName("Rich Text Format"),
            ClipboardFormat.UnicodeText,
            ClipboardFormat.Text,
            ClipboardFormat.Locale,
            ClipboardFormat.Dib,
            ClipboardFormat.Bitmap));

        AssertFormats(
            selection.Formats,
            ClipboardFormat.Html,
            ClipboardFormat.Rtf,
            ClipboardFormat.UnicodeText,
            ClipboardFormat.Dib);
    }

    [Fact]
    public void Screenshot_tool_copy_is_captured_as_an_image()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.FromName("PNG"),
            ClipboardFormat.Dib,
            ClipboardFormat.Bitmap));

        AssertFormats(selection.Formats, ClipboardFormat.Png);
    }

    // ---- Sensitive content -------------------------------------------------------------

    [Fact]
    public void Password_manager_copy_is_not_captured_when_sensitive_clips_are_ignored()
    {
        ClipboardSnapshot snapshot = new()
        {
            Formats = [ClipboardFormat.UnicodeText, ClipboardFormat.CanIncludeInClipboardHistory],
            CanIncludeInClipboardHistory = false,
            CanUploadToCloudClipboard = false,
        };

        Assert.True(Select(snapshot, new ClipboardFilterOptions { IgnoresConcealedContent = true })
            .IsEmpty);
    }

    [Fact]
    public void Password_manager_copy_that_opts_out_of_all_monitoring_is_never_captured()
    {
        ClipboardSnapshot snapshot = new()
        {
            Formats =
            [
                ClipboardFormat.UnicodeText,
                ClipboardFormat.ExcludeFromMonitorProcessing,
            ],
        };

        // Not a preference: ignored even with every setting at its most permissive.
        Assert.True(Select(snapshot, new ClipboardFilterOptions
        {
            IgnoresConcealedContent = false,
            IgnoresCloudClipboard = false,
        }).IsEmpty);
    }

    // ---- User settings -----------------------------------------------------------------

    [Fact]
    public void A_user_who_only_wants_text_gets_only_text_from_a_rich_clip()
    {
        ClipboardSelection selection = Select(
            ClipboardSnapshot.FromFormats(
                ClipboardFormat.FromName("Rich Text Format"),
                ClipboardFormat.FromName("HTML Format"),
                ClipboardFormat.UnicodeText,
                ClipboardFormat.Dib),
            ClipboardFilterOptions.WithEnabledTypes(ClipContentType.Text));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText);
    }
}
