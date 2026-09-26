namespace CopyPasta.Core.Capture;

using CopyPasta.Core.Clipboard;

/// <summary>Persistence for captured clips. Port of the macOS repository protocol.</summary>
public interface IClipStore
{
    /// <summary>True when a clip with this id is already stored.</summary>
    bool Exists(string clipId);

    /// <summary>
    /// Inserts the clip, or updates its timestamp if the id already exists.
    /// </summary>
    /// <remarks>
    /// Upsert rather than insert, because the id is the content hash whenever "overwrite
    /// duplicates" is on: re-copying the same content should move the existing entry to the
    /// top, not add a second one. When the id already exists its assets are identical by
    /// construction, so they are not rewritten.
    /// </remarks>
    void Save(StoredClip clip);

    /// <summary>Number of stored clips.</summary>
    int Count();

    /// <summary>Removes a clip and its blobs. False when the id is unknown.</summary>
    bool Delete(string clipId);

    /// <summary>
    /// Marks a clip as just used, moving it to the top of the "last used" ordering without
    /// touching its creation time or its blobs.
    /// </summary>
    /// <remarks>
    /// This has no macOS counterpart, and replaces something implicit there. macOS refreshes
    /// the timestamp as a side effect of its own clipboard write being re-captured by the
    /// monitor, which is fragile: with "allow duplicates" off the refresh never happens and the
    /// "last used" sort quietly stops working, and with "overwrite duplicates" off every paste
    /// inserts a new row instead. Doing it explicitly is both cheaper and correct under every
    /// combination of those settings.
    /// </remarks>
    bool Touch(string clipId, long updatedAt);
}

/// <summary>A clip as persisted.</summary>
public sealed record StoredClip
{
    /// <summary>
    /// Primary key. Either the content hash (when duplicates overwrite) or a fresh GUID
    /// (when every copy should get its own entry).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The content hash, regardless of what <see cref="Id"/> is.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Menu title text, capped. Empty for clips with no text representation.</summary>
    public required string Title { get; init; }

    /// <summary>Formats held, in priority order.</summary>
    public required IReadOnlyList<ClipboardFormat> Formats { get; init; }

    /// <summary>The stored blobs, in the same order as <see cref="Formats"/>.</summary>
    public required IReadOnlyList<ClipAsset> Assets { get; init; }

    /// <summary>Unix seconds. On an upsert the existing row's value is kept.</summary>
    public required long CreatedAt { get; init; }

    /// <summary>Unix seconds, refreshed on every capture of the same content.</summary>
    public required long UpdatedAt { get; init; }

    /// <summary>Re-apply the sensitive-content marker when replaying this clip.</summary>
    public required bool IsConcealed { get; init; }

    /// <summary>The clip arrived from another device.</summary>
    public required bool IsFromCloudClipboard { get; init; }
}
