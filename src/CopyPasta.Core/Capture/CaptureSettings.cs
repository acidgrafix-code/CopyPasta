namespace CopyPasta.Core.Capture;

using CopyPasta.Core.Clipboard;

/// <summary>
/// The settings that govern capture. Port of the <c>@Shared</c> values read by the macOS
/// <c>ClipService</c>.
/// </summary>
public sealed record CaptureSettings
{
    public static CaptureSettings Default { get; } = new();

    /// <summary>Which content categories to store, and the sensitive/cloud opt-outs.</summary>
    public ClipboardFilterOptions Filter { get; init; } = ClipboardFilterOptions.Default;

    /// <summary>Applications whose clips are never captured.</summary>
    public IReadOnlyList<ExcludedApplication> ExcludedApplications { get; init; } = [];

    /// <summary>
    /// When false, copying content that is already in the history is ignored entirely.
    /// Port of <c>allowsDuplicateHistory</c> (macOS default: true).
    /// </summary>
    public bool AllowsDuplicates { get; init; } = true;

    /// <summary>
    /// When true, re-copying the same content updates the existing entry instead of adding a
    /// second one — implemented by using the content hash as the primary key. Port of
    /// <c>overwritesDuplicateHistory</c> (macOS default: true).
    /// </summary>
    public bool OverwritesDuplicates { get; init; } = true;

    /// <summary>
    /// Characters of text kept for the menu title. Matches the macOS 10,000 cap, which exists
    /// so a copied multi-megabyte log does not bloat every row.
    /// </summary>
    public int MaximumTitleLength { get; init; } = 10_000;
}
