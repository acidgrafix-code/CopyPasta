namespace CopyPasta.Core.Capture;

using CopyPasta.Core.Clipboard;

/// <summary>
/// Chooses which formats to fetch, given what the clipboard is offering.
/// </summary>
/// <remarks>
/// Invoked by <see cref="IClipboardSource.TryRead"/> <em>while the clipboard is open</em>, so
/// that the decision and the blob reads happen inside a single open/close. Returning an empty
/// list means "read nothing" — which is how the pipeline avoids pulling a 40 MB bitmap off the
/// clipboard only to discard it.
/// </remarks>
public delegate IReadOnlyList<ClipboardFormat> ClipboardFormatChooser(ClipboardSnapshot snapshot);

/// <summary>Read access to the system clipboard.</summary>
/// <remarks>
/// The Win32 implementation lives in <c>CopyPasta.Interop</c>. This interface exists so the
/// capture pipeline can be tested without a clipboard, and so the awkward parts of the Win32
/// contract (one open per read, retry on contention) stay on one side of a line.
/// </remarks>
public interface IClipboardSource
{
    /// <summary>
    /// The clipboard's change counter — the Windows analogue of macOS
    /// <c>NSPasteboard.changeCount</c>. Cheap to call and safe to poll.
    /// </summary>
    uint GetSequenceNumber();

    /// <summary>
    /// Opens the clipboard, hands the offered formats to <paramref name="chooser"/>, reads the
    /// formats it asks for, and closes again.
    /// </summary>
    /// <returns>
    /// The read, or <c>null</c> if the clipboard could not be opened — which happens routinely
    /// when another process holds it, and is not an error worth surfacing to the user.
    /// </returns>
    ClipboardRead? TryRead(ClipboardFormatChooser chooser);
}

/// <summary>The outcome of one successful clipboard read.</summary>
public sealed record ClipboardRead
{
    /// <summary>What the clipboard was offering.</summary>
    public required ClipboardSnapshot Snapshot { get; init; }

    /// <summary>
    /// The blobs actually fetched. A format the chooser asked for can legitimately be missing
    /// here: delayed rendering means the owning app may fail to produce it on demand.
    /// </summary>
    public required IReadOnlyList<ClipAsset> Assets { get; init; }

    /// <summary>The sequence number observed during the read.</summary>
    public required uint SequenceNumber { get; init; }
}

/// <summary>Identifies the application that currently has focus.</summary>
/// <remarks>
/// Port of the macOS <c>NSWorkspace.shared.frontmostApplication</c> lookup used for the
/// excluded-applications check.
/// </remarks>
public interface IForegroundApplication
{
    /// <summary>
    /// The focused application's identity, or <c>null</c> when it cannot be determined.
    /// </summary>
    ForegroundApplicationInfo? GetCurrent();
}

/// <param name="ProcessName">Executable name without extension, e.g. <c>notepad</c>.</param>
/// <param name="ExecutablePath">Full path when available; may be null for protected processes.</param>
public sealed record ForegroundApplicationInfo(string ProcessName, string? ExecutablePath);
