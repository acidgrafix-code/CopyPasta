using System.Text;
using CopyPasta.Core.Capture;
using CopyPasta.Core.Paste;
using Microsoft.Extensions.Time.Testing;

namespace CopyPasta.Core.Tests;

/// <summary>
/// Behaviour of the paste path: the port of macOS <c>PasteService</c>, plus the focus handling
/// Windows requires and macOS does not.
/// </summary>
public class PasteServiceTests
{
    private const string ClipId = "clip-1";

    private readonly FakeClipboardWriter _writer = new();
    private readonly FakeInputSender _input = new();
    private readonly FakeWindowFocus _focus = new();
    private readonly FakeModifierKeys _modifiers = new();
    private readonly FakeCaptureSuppressor _suppressor = new();
    private readonly FakeClipStore _store = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

    private PasteService CreateService(PasteSettings? settings = null) =>
        new(_writer, _input, _focus, _modifiers, _suppressor, _store, _time)
        {
            Settings = settings ?? PasteSettings.Default,
        };

    private static ClipContent Content(params ClipAsset[] assets)
    {
        Assert.True(ClipContent.TryCreate(assets, out ClipContent? content));
        return content;
    }

    private static ClipAsset Text(string value) =>
        new(ClipboardFormat.UnicodeText, Encoding.Unicode.GetBytes(value + '\0'));

    private PasteRequest Request(ClipContent? content = null, bool isConcealed = false)
    {
        content ??= Content(Text("hello"));

        _store.Add(new StoredClip
        {
            Id = ClipId,
            ContentHash = ClipId,
            Title = "hello",
            Formats = content.Formats,
            Assets = content.Assets,
            CreatedAt = 1,
            UpdatedAt = 1,
            IsConcealed = isConcealed,
            IsFromCloudClipboard = false,
        });

        return new PasteRequest
        {
            ClipId = ClipId,
            Content = content,
            IsConcealed = isConcealed,
            Target = new FocusToken(4321),
        };
    }

    // ---- The happy path ---------------------------------------------------------------

    [Fact]
    public void A_clip_is_written_to_the_clipboard_and_pasted()
    {
        PasteResult result = CreateService().Paste(Request());

        Assert.Equal(PasteOutcome.Pasted, result.Outcome);
        Assert.Single(_writer.Writes);
        Assert.Equal(1, _input.SendCount);
    }

    [Fact]
    public void Every_stored_format_is_written_back_in_order()
    {
        ClipContent content = Content(
            new ClipAsset(ClipboardFormat.Html, [1, 2, 3]),
            new ClipAsset(ClipboardFormat.Rtf, [4, 5]),
            Text("hello"));

        CreateService().Paste(Request(content));

        Assert.Equal(content.Assets, _writer.LastWrite!.Assets);
    }

    [Fact]
    public void The_target_window_is_restored_before_the_keystroke_is_sent()
    {
        CreateService().Paste(Request());

        Assert.Equal(new FocusToken(4321), Assert.Single(_focus.RestoreAttempts));
    }

    [Fact]
    public void Pasting_lifts_the_clip_in_the_last_used_ordering()
    {
        CreateService().Paste(Request());

        Assert.Equal(1_700_000_000, _store.Single!.UpdatedAt);
    }

    [Fact]
    public void Our_own_write_is_suppressed_so_pasting_does_not_re_capture_the_clip()
    {
        CreateService().Paste(Request());

        // Once before the write (the notification can arrive before TryWrite returns) and once
        // after, when the clipboard actually holds our content.
        Assert.Equal(2, _suppressor.SuppressCount);
    }

    // ---- Automatic pasting turned off ---------------------------------------------------

    [Fact]
    public void With_automatic_pasting_off_the_clip_is_only_copied()
    {
        PasteResult result = CreateService(
            PasteSettings.Default with { PastesAutomatically = false }).Paste(Request());

        Assert.Equal(PasteOutcome.CopiedOnly, result.Outcome);
        Assert.Single(_writer.Writes);
        Assert.Equal(0, _input.SendCount);
        Assert.Empty(_focus.RestoreAttempts);
    }

    // ---- Failure paths -------------------------------------------------------------------

    [Fact]
    public void An_unavailable_clipboard_changes_nothing()
    {
        _writer.IsUnavailable = true;

        PasteResult result = CreateService().Paste(Request());

        Assert.Equal(PasteOutcome.ClipboardUnavailable, result.Outcome);
        Assert.Equal(0, _input.SendCount);
        Assert.Equal(1, _store.Count());
    }

    [Fact]
    public void No_keystroke_is_sent_when_focus_cannot_be_restored()
    {
        // Sending Ctrl+V without the right window focused would paste into whatever happens to
        // be in front — including our own process. Copy-only is the safe outcome.
        _focus.RestoreSucceeds = false;

        PasteResult result = CreateService().Paste(Request());

        Assert.Equal(PasteOutcome.FocusRestoreFailed, result.Outcome);
        Assert.Single(_writer.Writes);
        Assert.Equal(0, _input.SendCount);
    }

    [Fact]
    public void An_invalid_target_window_downgrades_to_copy_only()
    {
        PasteRequest request = Request() with { Target = default };

        PasteResult result = CreateService().Paste(request);

        Assert.Equal(PasteOutcome.CopiedOnly, result.Outcome);
        Assert.Equal(0, _input.SendCount);
    }

    [Fact]
    public void Rejected_input_is_reported_but_the_clip_still_reached_the_clipboard()
    {
        // What an elevated target application looks like from here.
        _input.Succeeds = false;

        PasteResult result = CreateService().Paste(Request());

        Assert.Equal(PasteOutcome.InputRejected, result.Outcome);
        Assert.True(result.ReachedClipboard);
    }

    // ---- Plain-text modifier --------------------------------------------------------------

    [Fact]
    public void Holding_the_plain_text_modifier_writes_only_text()
    {
        ClipContent content = Content(
            new ClipAsset(ClipboardFormat.Html, [1, 2, 3]),
            Text("hello"));
        _modifiers.Held.Add(ModifierKey.Shift);

        PasteResult result = CreateService().Paste(Request(content));

        Assert.True(result.PastedAsPlainText);
        ClipAsset written = Assert.Single(_writer.LastWrite!.Assets);
        Assert.Equal(ClipboardFormat.UnicodeText, written.Format);
        Assert.Equal(PasteOutcome.Pasted, result.Outcome);
    }

    [Fact]
    public void The_plain_text_modifier_does_nothing_when_the_feature_is_disabled()
    {
        ClipContent content = Content(new ClipAsset(ClipboardFormat.Html, [1]), Text("hello"));
        _modifiers.Held.Add(ModifierKey.Shift);

        PasteResult result = CreateService(PasteSettings.Default with
        {
            PastePlainText = ModifierAction.Disabled(ModifierKey.Shift),
        }).Paste(Request(content));

        Assert.False(result.PastedAsPlainText);
        Assert.Equal(2, _writer.LastWrite!.Assets.Count);
    }

    [Fact]
    public void Holding_a_different_modifier_does_not_trigger_plain_text()
    {
        ClipContent content = Content(new ClipAsset(ClipboardFormat.Html, [1]), Text("hello"));
        _modifiers.Held.Add(ModifierKey.Control);

        PasteResult result = CreateService().Paste(Request(content));

        Assert.False(result.PastedAsPlainText);
    }

    [Fact]
    public void Plain_text_over_a_clip_with_no_text_falls_back_to_the_full_content()
    {
        // macOS writes an empty string here, silently clearing the clipboard when the user holds
        // the modifier over an image. Falling back is the less surprising failure.
        ClipContent content = Content(new ClipAsset(ClipboardFormat.Png, [1, 2, 3]));
        _modifiers.Held.Add(ModifierKey.Shift);

        PasteResult result = CreateService().Paste(Request(content));

        Assert.False(result.PastedAsPlainText);
        Assert.Equal(
            ClipboardFormat.Png,
            Assert.Single(_writer.LastWrite!.Assets).Format);
    }

    // ---- Delete modifiers -----------------------------------------------------------------

    [Fact]
    public void Holding_the_delete_modifier_removes_the_clip_without_touching_the_clipboard()
    {
        _modifiers.Held.Add(ModifierKey.Control);

        PasteResult result = CreateService(PasteSettings.Default with
        {
            DeleteWithoutPasting = ModifierAction.Enabled(ModifierKey.Control),
        }).Paste(Request());

        Assert.Equal(PasteOutcome.DeletedOnly, result.Outcome);
        Assert.True(result.ClipDeleted);
        Assert.Empty(_writer.Writes);
        Assert.Equal(0, _input.SendCount);
        Assert.Equal(0, _store.Count());
    }

    [Fact]
    public void Holding_the_paste_and_delete_modifier_pastes_then_removes_the_clip()
    {
        _modifiers.Held.Add(ModifierKey.Alt);

        PasteResult result = CreateService(PasteSettings.Default with
        {
            PasteAndDelete = ModifierAction.Enabled(ModifierKey.Alt),
        }).Paste(Request());

        Assert.Equal(PasteOutcome.Pasted, result.Outcome);
        Assert.True(result.ClipDeleted);
        Assert.Single(_writer.Writes);
        Assert.Equal(1, _input.SendCount);
        Assert.Equal(0, _store.Count());
    }

    [Fact]
    public void Paste_and_delete_wins_when_both_delete_modifiers_are_held()
    {
        // Holding both must not be ambiguous: the one that still pastes is the safer reading.
        _modifiers.Held.Add(ModifierKey.Control);
        _modifiers.Held.Add(ModifierKey.Alt);

        PasteResult result = CreateService(PasteSettings.Default with
        {
            DeleteWithoutPasting = ModifierAction.Enabled(ModifierKey.Control),
            PasteAndDelete = ModifierAction.Enabled(ModifierKey.Alt),
        }).Paste(Request());

        Assert.Equal(PasteOutcome.Pasted, result.Outcome);
        Assert.True(result.ClipDeleted);
    }

    [Fact]
    public void Plain_text_and_paste_and_delete_combine()
    {
        ClipContent content = Content(new ClipAsset(ClipboardFormat.Html, [1]), Text("hello"));
        _modifiers.Held.Add(ModifierKey.Shift);
        _modifiers.Held.Add(ModifierKey.Alt);

        PasteResult result = CreateService(PasteSettings.Default with
        {
            PasteAndDelete = ModifierAction.Enabled(ModifierKey.Alt),
        }).Paste(Request(content));

        Assert.True(result.PastedAsPlainText);
        Assert.True(result.ClipDeleted);
        Assert.Equal(ClipboardFormat.UnicodeText, Assert.Single(_writer.LastWrite!.Assets).Format);
    }

    [Fact]
    public void A_failed_write_does_not_delete_the_clip()
    {
        _writer.IsUnavailable = true;
        _modifiers.Held.Add(ModifierKey.Alt);

        PasteResult result = CreateService(PasteSettings.Default with
        {
            PasteAndDelete = ModifierAction.Enabled(ModifierKey.Alt),
        }).Paste(Request());

        Assert.Equal(PasteOutcome.ClipboardUnavailable, result.Outcome);
        Assert.False(result.ClipDeleted);
        Assert.Equal(1, _store.Count());
    }

    [Fact]
    public void A_deleted_clip_is_not_also_touched()
    {
        _modifiers.Held.Add(ModifierKey.Alt);

        CreateService(PasteSettings.Default with
        {
            PasteAndDelete = ModifierAction.Enabled(ModifierKey.Alt),
        }).Paste(Request());

        Assert.Equal(0, _store.Count());
    }

    // ---- Sensitive content ----------------------------------------------------------------

    [Fact]
    public void A_sensitive_clip_is_re_marked_when_replayed()
    {
        // Otherwise replaying a password leaks it into Windows' own clipboard history, which the
        // source application explicitly asked to avoid.
        CreateService().Paste(Request(isConcealed: true));

        Assert.True(_writer.LastWrite!.MarkAsConcealed);
    }

    [Fact]
    public void An_ordinary_clip_is_not_marked_sensitive()
    {
        CreateService().Paste(Request());

        Assert.False(_writer.LastWrite!.MarkAsConcealed);
    }

    [Fact]
    public void A_sensitive_clip_pasted_as_plain_text_stays_marked()
    {
        _modifiers.Held.Add(ModifierKey.Shift);

        CreateService().Paste(Request(isConcealed: true));

        Assert.True(_writer.LastWrite!.MarkAsConcealed);
    }

    // ---- Snippets --------------------------------------------------------------------------

    [Fact]
    public void Snippet_text_is_written_and_pasted()
    {
        PasteResult result = CreateService().PasteText("snippet body", new FocusToken(99));

        Assert.Equal(PasteOutcome.Pasted, result.Outcome);
        ClipAsset written = Assert.Single(_writer.LastWrite!.Assets);
        Assert.Equal(ClipboardFormat.UnicodeText, written.Format);
        Assert.Equal(
            "snippet body",
            Encoding.Unicode.GetString(written.Data).TrimEnd('\0'));
    }

    [Fact]
    public void Snippet_pasting_does_not_touch_the_history()
    {
        CreateService().PasteText("snippet body", new FocusToken(99));

        Assert.Equal(0, _store.Count());
    }

    // ---- Wiring ----------------------------------------------------------------------------

    [Fact]
    public void Capture_target_reads_the_current_focus()
    {
        _focus.Captured = new FocusToken(777);

        Assert.Equal(new FocusToken(777), CreateService().CaptureTarget());
    }

    [Fact]
    public void An_invalid_focus_token_is_recognised()
    {
        Assert.False(default(FocusToken).IsValid);
        Assert.True(new FocusToken(1).IsValid);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PasteService(null!, _input, _focus, _modifiers, _suppressor, _store));
        Assert.Throws<ArgumentNullException>(() =>
            new PasteService(_writer, null!, _focus, _modifiers, _suppressor, _store));
        Assert.Throws<ArgumentNullException>(() =>
            new PasteService(_writer, _input, null!, _modifiers, _suppressor, _store));
        Assert.Throws<ArgumentNullException>(() =>
            new PasteService(_writer, _input, _focus, null!, _suppressor, _store));
        Assert.Throws<ArgumentNullException>(() =>
            new PasteService(_writer, _input, _focus, _modifiers, null!, _store));
        Assert.Throws<ArgumentNullException>(() =>
            new PasteService(_writer, _input, _focus, _modifiers, _suppressor, null!));

        Assert.Throws<ArgumentNullException>(() => CreateService().Paste(null!));
        Assert.Throws<ArgumentNullException>(() =>
            CreateService().PasteText(null!, new FocusToken(1)));
    }
}
