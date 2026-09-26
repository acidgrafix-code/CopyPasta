namespace CopyPasta.Core.Clipboard;

/// <summary>
/// One stored clipboard representation: a format and its raw bytes, exactly as the source
/// app offered them. Port of macOS <c>PasteboardContent.Asset</c>.
/// </summary>
/// <remarks>
/// Blobs are never re-encoded. CF_HTML in particular carries byte offsets in its own header,
/// so a "harmless" normalisation would corrupt it.
/// </remarks>
public sealed class ClipAsset : IEquatable<ClipAsset>
{
    public ClipAsset(ClipboardFormat format, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (format.IsUnnamed)
        {
            throw new ArgumentException("Asset format must be named.", nameof(format));
        }

        Format = format;
        Data = data;
    }

    public ClipboardFormat Format { get; }

    public byte[] Data { get; }

    public bool Equals(ClipAsset? other) =>
        other is not null && Format == other.Format && Data.AsSpan().SequenceEqual(other.Data);

    public override bool Equals(object? obj) => Equals(obj as ClipAsset);

    public override int GetHashCode() => HashCode.Combine(Format, Data.Length);

    public override string ToString() => $"{Format} ({Data.Length} bytes)";
}
