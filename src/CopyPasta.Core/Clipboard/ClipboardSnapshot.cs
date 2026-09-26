namespace CopyPasta.Core.Clipboard;

/// <summary>
/// What the clipboard is currently offering: the format list plus the marker values that
/// cannot be expressed by format presence alone.
/// </summary>
/// <remarks>
/// This is the filter's input, produced by the interop layer from
/// <c>EnumClipboardFormats</c>. Presence-only markers (e.g.
/// <see cref="ClipboardFormat.ExcludeFromMonitorProcessing"/>) stay in
/// <see cref="Formats"/>; markers whose DWORD payload carries the meaning are resolved into
/// the nullable properties below, where <c>null</c> means "the app said nothing".
/// </remarks>
public sealed record ClipboardSnapshot
{
    /// <summary>
    /// Offered formats, in the order the clipboard reports them — which is the offering
    /// app's own priority order, most descriptive first. Order is significant: several
    /// rules below key off the primary (first) format.
    /// </summary>
    public IReadOnlyList<ClipboardFormat> Formats { get; init; } = [];

    /// <summary>
    /// Value of the <c>CanIncludeInClipboardHistory</c> DWORD; <c>false</c> marks the clip
    /// as sensitive (password managers set this).
    /// </summary>
    public bool? CanIncludeInClipboardHistory { get; init; }

    /// <summary>Value of the <c>CanUploadToCloudClipboard</c> DWORD.</summary>
    public bool? CanUploadToCloudClipboard { get; init; }

    /// <summary>True when the clip arrived from another device via Cloud Clipboard.</summary>
    public bool IsFromCloudClipboard { get; init; }

    public static ClipboardSnapshot FromFormats(params ClipboardFormat[] formats) =>
        new() { Formats = formats };
}

/// <summary>User settings that affect what gets captured.</summary>
public sealed record ClipboardFilterOptions
{
    /// <summary>Content categories the user has left enabled. Default: all.</summary>
    public IReadOnlySet<ClipContentType> EnabledTypes { get; init; } =
        ClipContentTypes.All.ToHashSet();

    /// <summary>
    /// Skip clips marked as sensitive. Port of <c>ignoresConcealedPasteboardTypes</c>.
    /// </summary>
    public bool IgnoresConcealedContent { get; init; }

    /// <summary>
    /// Skip clips that arrived from another device. Port of
    /// <c>ignoresUniversalClipboard</c>.
    /// </summary>
    public bool IgnoresCloudClipboard { get; init; }

    public static ClipboardFilterOptions Default { get; } = new();

    public static ClipboardFilterOptions WithEnabledTypes(params ClipContentType[] types) =>
        new() { EnabledTypes = types.ToHashSet() };
}

/// <summary>The filter's verdict: which formats to store, and how to re-mark them on replay.</summary>
/// <remarks>
/// macOS appends the <c>concealed</c> / <c>universalClipboard</c> marker types to the stored
/// type list so that replaying a clip re-marks it. This port keeps the markers out of band
/// instead, because the Windows markers carry a DWORD payload rather than being
/// presence-only: mixing them into the stored blob list would mean persisting pseudo-assets
/// that are really flags. The replay layer re-applies them from these properties.
/// </remarks>
public sealed record ClipboardSelection
{
    public static readonly ClipboardSelection None = new();

    /// <summary>
    /// Formats to persist, in priority order. Empty means "do not capture this clip".
    /// </summary>
    public IReadOnlyList<ClipboardFormat> Formats { get; init; } = [];

    /// <summary>Re-apply <c>CanIncludeInClipboardHistory = 0</c> when replaying.</summary>
    public bool IsConcealed { get; init; }

    /// <summary>The clip originated on another device.</summary>
    public bool IsFromCloudClipboard { get; init; }

    public bool IsEmpty => Formats.Count == 0;
}
