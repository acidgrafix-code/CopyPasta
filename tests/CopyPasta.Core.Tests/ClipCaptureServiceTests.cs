using System.Text;
using CopyPasta.Core.Capture;
using Microsoft.Extensions.Time.Testing;

namespace CopyPasta.Core.Tests;

/// <summary>
/// Behaviour of the capture pipeline: the port of macOS <c>ClipService.create()</c> and
/// <c>save(_:)</c>, including the rule order that keeps expensive work off the reject paths.
/// </summary>
public class ClipCaptureServiceTests
{
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeClipStore _store = new();
    private readonly FakeForegroundApplication _foreground = FakeForegroundApplication.Named("notepad");
    private readonly FakeTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

    private ClipCaptureService CreateService(CaptureSettings? settings = null)
    {
        ClipCaptureService service = new(_clipboard, _store, _foreground, _time)
        {
            Settings = settings ?? CaptureSettings.Default,
        };
        return service;
    }

    // ---- The happy path ---------------------------------------------------------------

    [Fact]
    public void A_text_copy_is_stored()
    {
        _clipboard.PutText("hello");
        ClipCaptureService service = CreateService();

        CaptureResult result = service.Capture();

        Assert.Equal(CaptureOutcome.Captured, result.Outcome);
        StoredClip? clip = _store.Single;
        Assert.NotNull(clip);
        Assert.Equal("hello", clip.Title);
        Assert.Equal(1_700_000_000, clip.CreatedAt);
        Assert.Equal(1_700_000_000, clip.UpdatedAt);
        AssertFormats(clip.Formats, ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void The_stored_id_is_the_content_hash_when_duplicates_overwrite()
    {
        _clipboard.PutText("hello");
        ClipCaptureService service = CreateService();

        CaptureResult result = service.Capture();

        Assert.Equal(result.ContentHash, result.ClipId);
    }

    [Fact]
    public void Each_copy_gets_its_own_row_when_duplicates_do_not_overwrite()
    {
        _clipboard.PutText("hello");
        ClipCaptureService service = CreateService(
            CaptureSettings.Default with { OverwritesDuplicates = false });

        CaptureResult result = service.Capture();

        Assert.NotEqual(result.ContentHash, result.ClipId);
        Assert.NotNull(result.ClipId);
    }

    [Fact]
    public void A_clip_with_no_text_gets_an_empty_title()
    {
        _clipboard.PutFormats(ClipboardFormat.Png);
        ClipCaptureService service = CreateService();

        Assert.True(service.Capture().WasCaptured);
        Assert.Equal(string.Empty, _store.Single!.Title);
    }

    [Fact]
    public void A_very_long_text_title_is_capped()
    {
        _clipboard.PutText(new string('x', 50_000));
        ClipCaptureService service = CreateService(
            CaptureSettings.Default with { MaximumTitleLength = 100 });

        Assert.True(service.Capture().WasCaptured);
        Assert.Equal(100, _store.Single!.Title.Length);
    }

    // ---- Sequence-number bookkeeping ---------------------------------------------------

    [Fact]
    public void An_unchanged_clipboard_is_not_captured_twice()
    {
        _clipboard.PutText("hello");
        ClipCaptureService service = CreateService();

        Assert.Equal(CaptureOutcome.Captured, service.Capture().Outcome);
        Assert.Equal(CaptureOutcome.Unchanged, service.Capture().Outcome);
        Assert.Equal(1, _store.Count());
    }

    [Fact]
    public void A_new_clip_after_an_unchanged_one_is_captured()
    {
        ClipCaptureService service = CreateService();

        _clipboard.PutText("first");
        Assert.True(service.Capture().WasCaptured);
        Assert.Equal(CaptureOutcome.Unchanged, service.Capture().Outcome);

        _clipboard.PutText("second");
        Assert.True(service.Capture().WasCaptured);
        Assert.Equal(2, _store.Count());
    }

    [Fact]
    public void Our_own_write_can_be_suppressed_before_it_is_ever_read()
    {
        // The paste path calls IgnoreCurrentClipboardState after writing, so the notification
        // for our own write is dropped. This is the exact equivalent of the macOS
        // incrementChangeCount trick.
        ClipCaptureService service = CreateService();
        _clipboard.PutText("pasted by us");

        service.IgnoreCurrentClipboardState();

        Assert.Equal(CaptureOutcome.Unchanged, service.Capture().Outcome);
        Assert.Equal(0, _store.Count());
        Assert.Equal(0, _clipboard.ReadAttempts);
    }

    [Fact]
    public void Suppression_does_not_block_the_next_real_copy()
    {
        ClipCaptureService service = CreateService();
        _clipboard.PutText("pasted by us");
        service.IgnoreCurrentClipboardState();

        _clipboard.PutText("typed by the user");

        Assert.True(service.Capture().WasCaptured);
    }

    // ---- Clipboard contention ----------------------------------------------------------

    [Fact]
    public void An_unavailable_clipboard_is_reported_without_recording_the_sequence_number()
    {
        // Critical: if contention consumed the sequence number, the clip would be lost for
        // good rather than retried on the next notification.
        _clipboard.PutText("hello");
        _clipboard.IsUnavailable = true;
        ClipCaptureService service = CreateService();

        Assert.Equal(CaptureOutcome.ClipboardUnavailable, service.Capture().Outcome);

        _clipboard.IsUnavailable = false;
        Assert.True(service.Capture().WasCaptured);
    }

    // ---- Rejections --------------------------------------------------------------------

    [Fact]
    public void A_clip_with_nothing_storable_is_rejected()
    {
        _clipboard.PutFormats(ClipboardFormat.Bitmap, ClipboardFormat.Locale);
        ClipCaptureService service = CreateService();

        Assert.Equal(CaptureOutcome.NothingStorable, service.Capture().Outcome);
        Assert.Equal(0, _store.Count());
    }

    [Fact]
    public void Blank_text_is_rejected()
    {
        _clipboard.PutText("   \r\n  ");
        ClipCaptureService service = CreateService();

        Assert.Equal(CaptureOutcome.BlankText, service.Capture().Outcome);
        Assert.Equal(0, _store.Count());
    }

    [Fact]
    public void A_clip_whose_formats_all_fail_to_render_is_rejected()
    {
        _clipboard.PutText("hello");
        _clipboard.FailingFormats.Add(ClipboardFormat.UnicodeText);
        ClipCaptureService service = CreateService();

        Assert.Equal(CaptureOutcome.NoDataProduced, service.Capture().Outcome);
    }

    [Fact]
    public void A_clip_that_partially_renders_keeps_what_it_got()
    {
        _clipboard.PutFormats(ClipboardFormat.Png, ClipboardFormat.UnicodeText);
        _clipboard.FailingFormats.Add(ClipboardFormat.Png);
        ClipCaptureService service = CreateService();

        Assert.True(service.Capture().WasCaptured);
        AssertFormats(_store.Single!.Formats, ClipboardFormat.UnicodeText);
    }

    // ---- Duplicates --------------------------------------------------------------------

    [Fact]
    public void Re_copying_the_same_content_refreshes_the_existing_entry()
    {
        ClipCaptureService service = CreateService();
        _clipboard.PutText("hello");
        Assert.True(service.Capture().WasCaptured);

        _time.Advance(TimeSpan.FromMinutes(5));
        _clipboard.PutText("hello");
        Assert.True(service.Capture().WasCaptured);

        Assert.Equal(1, _store.Count());
    }

    [Fact]
    public void Re_copying_is_ignored_entirely_when_duplicates_are_not_allowed()
    {
        ClipCaptureService service = CreateService(
            CaptureSettings.Default with { AllowsDuplicates = false });

        _clipboard.PutText("hello");
        Assert.True(service.Capture().WasCaptured);

        _clipboard.PutText("hello");
        Assert.Equal(CaptureOutcome.Duplicate, service.Capture().Outcome);
        Assert.Equal(1, _store.Count());
    }

    // ---- Excluded applications ----------------------------------------------------------

    [Fact]
    public void A_clip_from_an_excluded_application_is_rejected()
    {
        _foreground.Current = new ForegroundApplicationInfo("KeePass", @"C:\Apps\KeePass.exe");
        _clipboard.PutText("secret");
        ClipCaptureService service = CreateService(CaptureSettings.Default with
        {
            ExcludedApplications = [new ExcludedApplication("KeePass")],
        });

        Assert.Equal(CaptureOutcome.ExcludedApplication, service.Capture().Outcome);
        Assert.Equal(0, _store.Count());
    }

    [Fact]
    public void An_excluded_application_is_rejected_before_its_blobs_are_ever_read()
    {
        // The whole reason the exclusion check runs inside the format chooser: otherwise a
        // 40 MB screenshot from an excluded app is copied out of shared memory and discarded.
        _foreground.Current = new ForegroundApplicationInfo("KeePass", null);
        _clipboard.PutText("secret");
        ClipCaptureService service = CreateService(CaptureSettings.Default with
        {
            ExcludedApplications = [new ExcludedApplication("KeePass")],
        });

        service.Capture();

        Assert.Empty(_clipboard.LastRequestedFormats);
    }

    [Fact]
    public void A_clip_from_an_allowed_application_is_captured()
    {
        _foreground.Current = new ForegroundApplicationInfo("notepad", @"C:\Windows\notepad.exe");
        _clipboard.PutText("fine");
        ClipCaptureService service = CreateService(CaptureSettings.Default with
        {
            ExcludedApplications = [new ExcludedApplication("KeePass")],
        });

        Assert.True(service.Capture().WasCaptured);
    }

    // ---- Markers survive capture --------------------------------------------------------

    [Fact]
    public void A_sensitive_clip_that_is_stored_stays_flagged()
    {
        _clipboard.Put(
            new ClipboardSnapshot
            {
                Formats = [ClipboardFormat.UnicodeText],
                CanIncludeInClipboardHistory = false,
            },
            new Dictionary<ClipboardFormat, byte[]>
            {
                [ClipboardFormat.UnicodeText] = Encoding.Unicode.GetBytes("secret\0"),
            });

        ClipCaptureService service = CreateService();

        Assert.True(service.Capture().WasCaptured);
        Assert.True(_store.Single!.IsConcealed);
    }

    [Fact]
    public void A_cloud_clip_that_is_stored_stays_flagged()
    {
        _clipboard.Put(
            new ClipboardSnapshot
            {
                Formats = [ClipboardFormat.UnicodeText],
                IsFromCloudClipboard = true,
            },
            new Dictionary<ClipboardFormat, byte[]>
            {
                [ClipboardFormat.UnicodeText] = Encoding.Unicode.GetBytes("from my phone\0"),
            });

        ClipCaptureService service = CreateService();

        Assert.True(service.Capture().WasCaptured);
        Assert.True(_store.Single!.IsFromCloudClipboard);
    }

    // ---- Wiring ------------------------------------------------------------------------

    [Fact]
    public void Only_the_selected_formats_are_read()
    {
        _clipboard.PutFormats(
            ClipboardFormat.Png,
            ClipboardFormat.Dib,
            ClipboardFormat.Bitmap,
            ClipboardFormat.UnicodeText);
        ClipCaptureService service = CreateService();

        service.Capture();

        AssertFormats(
            _clipboard.LastRequestedFormats,
            ClipboardFormat.Png,
            ClipboardFormat.UnicodeText);
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ClipCaptureService(null!, _store, _foreground));
        Assert.Throws<ArgumentNullException>(() =>
            new ClipCaptureService(_clipboard, null!, _foreground));
        Assert.Throws<ArgumentNullException>(() =>
            new ClipCaptureService(_clipboard, _store, null!));
    }
}
