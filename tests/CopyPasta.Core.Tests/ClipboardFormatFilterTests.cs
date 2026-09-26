namespace CopyPasta.Core.Tests;

/// <summary>
/// Rule-by-rule coverage of <see cref="ClipboardFormatFilter"/>, the port of macOS
/// <c>PasteboardAvailableType.availableTypes</c>. Each region maps to a numbered rule in the
/// implementation.
/// </summary>
public class ClipboardFormatFilterTests
{
    private static ClipboardSelection Select(
        ClipboardSnapshot snapshot,
        ClipboardFilterOptions? options = null) =>
        ClipboardFormatFilter.Select(snapshot, options ?? ClipboardFilterOptions.Default);

    // ---- Baseline --------------------------------------------------------------------

    [Fact]
    public void An_empty_clipboard_yields_nothing()
    {
        Assert.True(Select(ClipboardSnapshot.FromFormats()).IsEmpty);
    }

    [Fact]
    public void A_plain_text_clip_is_stored()
    {
        ClipboardSelection selection = Select(
            ClipboardSnapshot.FromFormats(ClipboardFormat.UnicodeText));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText);
        Assert.False(selection.IsConcealed);
        Assert.False(selection.IsFromCloudClipboard);
    }

    [Fact]
    public void Format_order_is_preserved_because_the_first_format_is_the_primary_one()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.Png,
            ClipboardFormat.UnicodeText));

        AssertFormats(selection.Formats, ClipboardFormat.Png, ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void Duplicate_and_unnamed_formats_are_discarded()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.UnicodeText,
            default,
            ClipboardFormat.FromName("cf_unicodetext")));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void App_private_formats_are_ignored()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.FromName("Acme.InternalOnly"),
            ClipboardFormat.UnicodeText));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void A_clip_of_only_unstorable_formats_yields_nothing()
    {
        Assert.True(Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.Bitmap,
            ClipboardFormat.Locale,
            ClipboardFormat.FromName("Acme.InternalOnly"))).IsEmpty);
    }

    // ---- Rule 1: monitoring opt-out ---------------------------------------------------

    [Theory]
    [InlineData("ExcludeClipboardContentFromMonitorProcessing")]
    [InlineData("Clipboard Viewer Ignore")]
    public void A_clip_marked_do_not_record_is_never_stored(string marker)
    {
        // Rejected outright, regardless of settings: this is not a user preference.
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.UnicodeText,
            ClipboardFormat.FromName(marker)));

        Assert.True(selection.IsEmpty);
    }

    // ---- Rule 2: sensitive content ----------------------------------------------------

    [Fact]
    public void A_sensitive_clip_is_skipped_when_the_user_asked_to_ignore_them()
    {
        ClipboardSelection selection = Select(
            new ClipboardSnapshot
            {
                Formats = [ClipboardFormat.UnicodeText],
                CanIncludeInClipboardHistory = false,
            },
            new ClipboardFilterOptions { IgnoresConcealedContent = true });

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void A_sensitive_clip_is_stored_but_stays_flagged_when_the_user_allows_them()
    {
        // The flag has to survive capture so that replaying the clip re-marks it as
        // sensitive rather than silently leaking it into Windows' own clipboard history.
        ClipboardSelection selection = Select(
            new ClipboardSnapshot
            {
                Formats = [ClipboardFormat.UnicodeText],
                CanIncludeInClipboardHistory = false,
            },
            new ClipboardFilterOptions { IgnoresConcealedContent = false });

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText);
        Assert.True(selection.IsConcealed);
    }

    [Fact]
    public void Saying_nothing_about_history_inclusion_does_not_mean_sensitive()
    {
        ClipboardSelection selection = Select(
            new ClipboardSnapshot
            {
                Formats = [ClipboardFormat.UnicodeText],
                CanIncludeInClipboardHistory = null,
            },
            new ClipboardFilterOptions { IgnoresConcealedContent = true });

        Assert.False(selection.IsEmpty);
        Assert.False(selection.IsConcealed);
    }

    // ---- Rules 3 and 4: cross-device clips --------------------------------------------

    [Fact]
    public void A_cloud_clip_is_skipped_when_the_user_asked_to_ignore_them()
    {
        ClipboardSelection selection = Select(
            new ClipboardSnapshot
            {
                Formats = [ClipboardFormat.UnicodeText],
                IsFromCloudClipboard = true,
            },
            new ClipboardFilterOptions { IgnoresCloudClipboard = true });

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void A_cloud_clip_whose_primary_representation_is_a_file_is_never_stored()
    {
        // The path points at transfer storage that may not exist on this machine later, so a
        // stored entry would be a dead reference with nothing else to fall back on.
        ClipboardSelection selection = Select(new ClipboardSnapshot
        {
            Formats = [ClipboardFormat.Hdrop, ClipboardFormat.UnicodeText],
            IsFromCloudClipboard = true,
        });

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void A_cloud_clip_keeps_its_other_representations_and_drops_only_the_file_reference()
    {
        ClipboardSelection selection = Select(new ClipboardSnapshot
        {
            Formats = [ClipboardFormat.Png, ClipboardFormat.Hdrop],
            IsFromCloudClipboard = true,
        });

        AssertFormats(selection.Formats, ClipboardFormat.Png);
        Assert.True(selection.IsFromCloudClipboard);
    }

    [Fact]
    public void A_local_clip_of_files_is_stored_normally()
    {
        ClipboardSelection selection = Select(
            ClipboardSnapshot.FromFormats(ClipboardFormat.Hdrop));

        AssertFormats(selection.Formats, ClipboardFormat.Hdrop);
    }

    // ---- Rule 5.2: disabled categories ------------------------------------------------

    [Fact]
    public void Disabling_a_category_drops_its_formats_and_keeps_the_rest()
    {
        ClipboardSelection selection = Select(
            ClipboardSnapshot.FromFormats(ClipboardFormat.Png, ClipboardFormat.UnicodeText),
            ClipboardFilterOptions.WithEnabledTypes(ClipContentType.Text));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void Disabling_every_category_yields_nothing()
    {
        ClipboardSelection selection = Select(
            ClipboardSnapshot.FromFormats(ClipboardFormat.Png, ClipboardFormat.UnicodeText),
            ClipboardFilterOptions.WithEnabledTypes());

        Assert.True(selection.IsEmpty);
    }

    // ---- Rule 5.3: richer representation wins -----------------------------------------

    [Fact]
    public void PNG_supersedes_every_other_image_representation()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.Png,
            ClipboardFormat.DibV5,
            ClipboardFormat.Dib,
            ClipboardFormat.Tiff));

        AssertFormats(selection.Formats, ClipboardFormat.Png);
    }

    [Fact]
    public void DIBV5_supersedes_plain_DIB_because_plain_DIB_loses_alpha()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.DibV5,
            ClipboardFormat.Dib));

        AssertFormats(selection.Formats, ClipboardFormat.DibV5);
    }

    [Fact]
    public void A_lone_DIB_is_still_stored()
    {
        ClipboardSelection selection = Select(
            ClipboardSnapshot.FromFormats(ClipboardFormat.Dib));

        AssertFormats(selection.Formats, ClipboardFormat.Dib);
    }

    [Fact]
    public void CF_BITMAP_is_never_stored_because_it_is_a_handle_not_a_blob()
    {
        Assert.True(Select(ClipboardSnapshot.FromFormats(ClipboardFormat.Bitmap)).IsEmpty);
    }

    [Fact]
    public void CF_LOCALE_is_never_stored_on_its_own_account()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.UnicodeText,
            ClipboardFormat.Locale));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText);
    }

    // ---- Rule 5.4: legacy formats -----------------------------------------------------

    [Fact]
    public void Synthesised_ANSI_text_is_dropped_when_Unicode_text_is_present()
    {
        // Windows synthesises CF_TEXT and CF_OEMTEXT from CF_UNICODETEXT, so without this
        // rule every text clip would be stored three times over.
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.UnicodeText,
            ClipboardFormat.Text,
            ClipboardFormat.OemText));

        AssertFormats(selection.Formats, ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void ANSI_text_alone_is_still_stored()
    {
        ClipboardSelection selection = Select(
            ClipboardSnapshot.FromFormats(ClipboardFormat.Text));

        AssertFormats(selection.Formats, ClipboardFormat.Text);
    }

    [Fact]
    public void Legacy_single_file_references_are_dropped_when_CF_HDROP_is_present()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.Hdrop,
            ClipboardFormat.FileNameW,
            ClipboardFormat.FileName));

        AssertFormats(selection.Formats, ClipboardFormat.Hdrop);
    }

    [Fact]
    public void The_ANSI_URL_flavour_is_dropped_when_the_wide_one_is_present()
    {
        ClipboardSelection selection = Select(ClipboardSnapshot.FromFormats(
            ClipboardFormat.UrlW,
            ClipboardFormat.Url));

        AssertFormats(selection.Formats, ClipboardFormat.UrlW);
    }

    // ---- Category mapping -------------------------------------------------------------

    [Theory]
    [InlineData("CF_UNICODETEXT", ClipContentType.Text)]
    [InlineData("CF_TEXT", ClipContentType.Text)]
    [InlineData("Rich Text Format", ClipContentType.Rtf)]
    [InlineData("HTML Format", ClipContentType.Html)]
    [InlineData("Portable Document Format", ClipContentType.Pdf)]
    [InlineData("CF_HDROP", ClipContentType.Files)]
    [InlineData("UniformResourceLocatorW", ClipContentType.Url)]
    [InlineData("PNG", ClipContentType.Image)]
    [InlineData("CF_DIBV5", ClipContentType.Image)]
    public void Formats_map_to_the_expected_settings_category(string name, ClipContentType expected)
    {
        Assert.Equal(expected, ClipboardFormatFilter.MapToContentType(ClipboardFormat.FromName(name)));
    }

    [Theory]
    [InlineData("CF_BITMAP")]
    [InlineData("CF_LOCALE")]
    [InlineData("CanIncludeInClipboardHistory")]
    [InlineData("Acme.InternalOnly")]
    public void Unstorable_formats_map_to_no_category(string name)
    {
        Assert.Null(ClipboardFormatFilter.MapToContentType(ClipboardFormat.FromName(name)));
    }

    [Fact]
    public void Every_content_category_is_reachable_from_some_known_format()
    {
        // Guards against adding a settings toggle that can never match anything.
        IEnumerable<ClipContentType> reachable = ClipboardFormat.Known
            .Select(ClipboardFormatFilter.MapToContentType)
            .Where(type => type is not null)
            .Select(type => type!.Value)
            .Distinct();

        Assert.Empty(ClipContentTypes.All.Except(reachable));
    }

    // ---- Argument validation ----------------------------------------------------------

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ClipboardFormatFilter.Select(null!, ClipboardFilterOptions.Default));
        Assert.Throws<ArgumentNullException>(() =>
            ClipboardFormatFilter.Select(ClipboardSnapshot.FromFormats(), null!));
    }
}
