namespace CopyPasta.Core.Paste;

using CopyPasta.Core.Capture;
using CopyPasta.Core.Clipboard;

/// <summary>What the paste path did.</summary>
public enum PasteOutcome
{
    /// <summary>Placed on the clipboard and the paste shortcut was sent.</summary>
    Pasted,

    /// <summary>
    /// Placed on the clipboard, but not pasted — the user has automatic pasting turned off.
    /// </summary>
    CopiedOnly,

    /// <summary>
    /// Placed on the clipboard, but the target window could not be brought back to the
    /// foreground, so no input was sent. The user can still paste manually.
    /// </summary>
    FocusRestoreFailed,

    /// <summary>
    /// Placed on the clipboard, but the paste shortcut could not be delivered — most often
    /// because the target runs at a higher integrity level than we do.
    /// </summary>
    InputRejected,

    /// <summary>Removed from the history without pasting, per the delete modifier.</summary>
    DeletedOnly,

    /// <summary>The clipboard could not be opened; nothing was changed.</summary>
    ClipboardUnavailable,
}

/// <param name="Outcome">What happened.</param>
/// <param name="PastedAsPlainText">The plain-text modifier was in effect.</param>
/// <param name="ClipDeleted">The clip was removed from the history.</param>
public sealed record PasteResult(
    PasteOutcome Outcome,
    bool PastedAsPlainText = false,
    bool ClipDeleted = false)
{
    /// <summary>True when the content reached the clipboard, whether or not it was pasted.</summary>
    public bool ReachedClipboard =>
        Outcome is PasteOutcome.Pasted
            or PasteOutcome.CopiedOnly
            or PasteOutcome.FocusRestoreFailed
            or PasteOutcome.InputRejected;
}

/// <param name="ClipId">The history entry being pasted.</param>
/// <param name="Content">Its stored representations.</param>
/// <param name="IsConcealed">Whether it was marked sensitive when captured.</param>
/// <param name="Target">
/// The window that had focus before our menu opened. Must be captured <em>before</em> the menu
/// is shown; by the time the user picks an item, we are the foreground process.
/// </param>
public sealed record PasteRequest
{
    public required string ClipId { get; init; }

    public required ClipContent Content { get; init; }

    public bool IsConcealed { get; init; }

    public FocusToken Target { get; init; }
}

/// <summary>
/// Puts a clip back on the clipboard and pastes it. Port of macOS <c>PasteService</c>.
/// </summary>
public sealed class PasteService
{
    private readonly IClipboardWriter _writer;
    private readonly IInputSender _input;
    private readonly IWindowFocus _focus;
    private readonly IModifierKeys _modifiers;
    private readonly ICaptureSuppressor _captureSuppressor;
    private readonly IClipStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    public PasteService(
        IClipboardWriter writer,
        IInputSender input,
        IWindowFocus focus,
        IModifierKeys modifiers,
        ICaptureSuppressor captureSuppressor,
        IClipStore store,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(modifiers);
        ArgumentNullException.ThrowIfNull(captureSuppressor);
        ArgumentNullException.ThrowIfNull(store);

        _writer = writer;
        _input = input;
        _focus = focus;
        _modifiers = modifiers;
        _captureSuppressor = captureSuppressor;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public PasteSettings Settings { get; set; } = PasteSettings.Default;

    /// <summary>Captures the window to restore later. Call before showing any menu.</summary>
    public FocusToken CaptureTarget() => _focus.Capture();

    /// <summary>Pastes a history entry, honouring whichever modifiers are held.</summary>
    public PasteResult Paste(PasteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            // Read once: the user may release a key while this runs, and the three decisions
            // have to agree with each other.
            bool plainText = IsHeld(Settings.PastePlainText);
            bool deleteOnly = IsHeld(Settings.DeleteWithoutPasting);
            bool pasteAndDelete = IsHeld(Settings.PasteAndDelete);

            // Delete without pasting wins outright, and nothing touches the clipboard.
            // Checked before pasteAndDelete so that holding both is not ambiguous.
            if (deleteOnly && !pasteAndDelete)
            {
                bool deleted = _store.Delete(request.ClipId);
                return new PasteResult(PasteOutcome.DeletedOnly, ClipDeleted: deleted);
            }

            ClipboardWriteRequest write = BuildWrite(request, plainText, out bool wrotePlainText);

            PasteResult result = PlaceAndPaste(write, wrotePlainText, request.Target);
            if (!result.ReachedClipboard)
            {
                return result;
            }

            if (pasteAndDelete)
            {
                bool deleted = _store.Delete(request.ClipId);
                return result with { ClipDeleted = deleted };
            }

            // Lift the clip to the top of the "last used" ordering. See IClipStore.Touch for
            // why this is explicit here rather than a side effect of re-capture.
            _store.Touch(request.ClipId, _timeProvider.GetUtcNow().ToUnixTimeSeconds());
            return result;
        }
    }

    /// <summary>Pastes snippet text. Port of the snippet path in the macOS app delegate.</summary>
    public PasteResult PasteText(string text, FocusToken target)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (_gate)
        {
            return PlaceAndPaste(ClipboardWriteRequest.PlainText(text), pastedAsPlainText: true, target);
        }
    }

    private ClipboardWriteRequest BuildWrite(
        PasteRequest request,
        bool plainText,
        out bool wrotePlainText)
    {
        if (plainText)
        {
            string? text = request.Content.TextValue;
            if (!string.IsNullOrEmpty(text))
            {
                wrotePlainText = true;
                return ClipboardWriteRequest.PlainText(text, request.IsConcealed);
            }

            // macOS writes an empty string here, which silently clears the clipboard when the
            // user holds the plain-text modifier over an image or a file list. Falling back to
            // the full content is the less surprising failure.
        }

        wrotePlainText = false;
        return new ClipboardWriteRequest(request.Content.Assets, request.IsConcealed);
    }

    private PasteResult PlaceAndPaste(
        ClipboardWriteRequest write,
        bool pastedAsPlainText,
        FocusToken target)
    {
        // Suppress before writing, not after: the notification for our own write can arrive
        // before TryWrite returns.
        _captureSuppressor.IgnoreCurrentClipboardState();

        if (!_writer.TryWrite(write))
        {
            return new PasteResult(PasteOutcome.ClipboardUnavailable);
        }

        // Written content differs from what the suppression snapshot recorded, so record it
        // again now that the clipboard holds our data.
        _captureSuppressor.IgnoreCurrentClipboardState();

        if (!Settings.PastesAutomatically)
        {
            return new PasteResult(PasteOutcome.CopiedOnly, pastedAsPlainText);
        }

        if (!target.IsValid)
        {
            return new PasteResult(PasteOutcome.CopiedOnly, pastedAsPlainText);
        }

        if (!_focus.TryRestore(target))
        {
            return new PasteResult(PasteOutcome.FocusRestoreFailed, pastedAsPlainText);
        }

        return _input.TrySendPasteShortcut()
            ? new PasteResult(PasteOutcome.Pasted, pastedAsPlainText)
            : new PasteResult(PasteOutcome.InputRejected, pastedAsPlainText);
    }

    private bool IsHeld(ModifierAction action) =>
        action.IsEnabled && _modifiers.IsPressed(action.Modifier);
}
