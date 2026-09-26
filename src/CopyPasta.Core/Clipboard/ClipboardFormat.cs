namespace CopyPasta.Core.Clipboard;

/// <summary>
/// A Windows clipboard format, identified by name.
/// </summary>
/// <remarks>
/// Ported from the macOS <c>NSPasteboard.PasteboardType</c> usage in Clipy.
///
/// Formats are keyed by <em>name</em> rather than by the numeric id returned by
/// <c>RegisterClipboardFormat</c>, for two reasons:
/// <list type="number">
/// <item>Registered format ids are only stable within a boot session, so they must never
/// be persisted — but the format identifier is part of the stored clip and feeds the
/// content hash.</item>
/// <item>Keying on names keeps this assembly free of Win32, so the capture rules are
/// testable without a clipboard.</item>
/// </list>
///
/// Win32 treats registered format names case-insensitively, so equality here is
/// <see cref="StringComparison.OrdinalIgnoreCase"/>. Because the name's bytes feed the
/// content hash (and therefore the database primary key), <see cref="FromName"/>
/// canonicalises the spelling of every known format: a clip offering "png" and a clip
/// offering "PNG" must hash identically, or re-copying the same content would create a
/// duplicate row.
/// </remarks>
public readonly struct ClipboardFormat : IEquatable<ClipboardFormat>
{
    // ---- Standard formats (CF_* constants in Win32) ----------------------------------

    /// <summary>CF_UNICODETEXT. UTF-16LE, NUL-terminated.</summary>
    public static readonly ClipboardFormat UnicodeText = new("CF_UNICODETEXT");

    /// <summary>CF_TEXT. ANSI codepage. Auto-synthesised by Windows from CF_UNICODETEXT.</summary>
    public static readonly ClipboardFormat Text = new("CF_TEXT");

    /// <summary>CF_OEMTEXT. OEM codepage. Auto-synthesised by Windows from CF_UNICODETEXT.</summary>
    public static readonly ClipboardFormat OemText = new("CF_OEMTEXT");

    /// <summary>CF_LOCALE. Codepage hint that accompanies text; never storable on its own.</summary>
    public static readonly ClipboardFormat Locale = new("CF_LOCALE");

    /// <summary>CF_HDROP. A DROPFILES blob holding one or more file paths.</summary>
    public static readonly ClipboardFormat Hdrop = new("CF_HDROP");

    /// <summary>CF_DIB. Device-independent bitmap, no alpha channel.</summary>
    public static readonly ClipboardFormat Dib = new("CF_DIB");

    /// <summary>CF_DIBV5. Device-independent bitmap with an alpha channel.</summary>
    public static readonly ClipboardFormat DibV5 = new("CF_DIBV5");

    /// <summary>
    /// CF_BITMAP. An HBITMAP <em>handle</em>, not a byte blob — it cannot be persisted.
    /// Windows auto-synthesises CF_DIB from it, so it is never selected for storage.
    /// </summary>
    public static readonly ClipboardFormat Bitmap = new("CF_BITMAP");

    // ---- Registered formats ----------------------------------------------------------

    public static readonly ClipboardFormat Rtf = new("Rich Text Format");

    /// <summary>
    /// CF_HTML. Note its textual header carries byte offsets into the payload, so the blob
    /// must be stored and replayed verbatim — never re-encoded.
    /// </summary>
    public static readonly ClipboardFormat Html = new("HTML Format");

    public static readonly ClipboardFormat Png = new("PNG");
    public static readonly ClipboardFormat Tiff = new("TIFF");
    public static readonly ClipboardFormat Jfif = new("JFIF");
    public static readonly ClipboardFormat Gif = new("GIF");
    public static readonly ClipboardFormat Pdf = new("Portable Document Format");
    public static readonly ClipboardFormat UrlW = new("UniformResourceLocatorW");
    public static readonly ClipboardFormat Url = new("UniformResourceLocator");

    /// <summary>Legacy single-file reference, superseded by CF_HDROP.</summary>
    public static readonly ClipboardFormat FileNameW = new("FileNameW");

    /// <summary>Legacy single-file reference, superseded by CF_HDROP.</summary>
    public static readonly ClipboardFormat FileName = new("FileName");

    // ---- Markers ---------------------------------------------------------------------
    // Windows' equivalents of the nspasteboard.org conventions the macOS app honours.

    /// <summary>
    /// Presence asks every clipboard monitor not to record this clip at all.
    /// Maps to macOS <c>org.nspasteboard.TransientType</c>.
    /// </summary>
    public static readonly ClipboardFormat ExcludeFromMonitorProcessing =
        new("ExcludeClipboardContentFromMonitorProcessing");

    /// <summary>
    /// Legacy convention predating the format above; honoured the same way.
    /// </summary>
    public static readonly ClipboardFormat ClipboardViewerIgnore = new("Clipboard Viewer Ignore");

    /// <summary>
    /// DWORD payload. 0 means "do not put this in clipboard history" — the closest
    /// equivalent of macOS <c>org.nspasteboard.ConcealedType</c>, and what password
    /// managers already set.
    /// </summary>
    public static readonly ClipboardFormat CanIncludeInClipboardHistory =
        new("CanIncludeInClipboardHistory");

    /// <summary>DWORD payload. 0 means "do not sync this to other devices".</summary>
    public static readonly ClipboardFormat CanUploadToCloudClipboard =
        new("CanUploadToCloudClipboard");

    /// <summary>
    /// Presence indicates the clip arrived from Cloud Clipboard on another device — the
    /// analogue of macOS <c>com.apple.is-remote-clipboard</c>.
    /// </summary>
    /// <remarks>
    /// UNVERIFIED: unlike the two formats above, this marker's name is not clearly
    /// documented by Microsoft. Confirm it empirically with <c>tools/FormatDump</c> against
    /// a real cross-device paste before relying on it; the filter degrades safely to
    /// "treat as local" if the name is wrong.
    /// </remarks>
    public static readonly ClipboardFormat FromCloudClipboard = new("FromCloudClipboard");

    private static readonly Dictionary<string, ClipboardFormat> CanonicalByName;

    /// <summary>Every format this build knows by name.</summary>
    public static readonly IReadOnlyList<ClipboardFormat> Known;

    static ClipboardFormat()
    {
        Known =
        [
            UnicodeText, Text, OemText, Locale, Hdrop, Dib, DibV5, Bitmap,
            Rtf, Html, Png, Tiff, Jfif, Gif, Pdf, UrlW, Url, FileNameW, FileName,
            ExcludeFromMonitorProcessing, ClipboardViewerIgnore,
            CanIncludeInClipboardHistory, CanUploadToCloudClipboard, FromCloudClipboard,
        ];

        CanonicalByName = new Dictionary<string, ClipboardFormat>(StringComparer.OrdinalIgnoreCase);
        foreach (ClipboardFormat format in Known)
        {
            CanonicalByName[format.Name] = format;
        }
    }

    private ClipboardFormat(string name) => Name = name;

    /// <summary>The format's name, in canonical spelling when the format is known.</summary>
    public string Name { get; }

    /// <summary>True for a format this build has no name for (default-constructed).</summary>
    public bool IsUnnamed => string.IsNullOrEmpty(Name);

    /// <summary>
    /// Returns the format for <paramref name="name"/>, canonicalising the spelling when the
    /// format is known so that hashes stay stable across differently-cased captures.
    /// </summary>
    public static ClipboardFormat FromName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return CanonicalByName.TryGetValue(name, out ClipboardFormat known)
            ? known
            : new ClipboardFormat(name);
    }

    public bool Equals(ClipboardFormat other) =>
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is ClipboardFormat other && Equals(other);

    public override int GetHashCode() =>
        Name is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    public override string ToString() => Name ?? string.Empty;

    public static bool operator ==(ClipboardFormat left, ClipboardFormat right) => left.Equals(right);

    public static bool operator !=(ClipboardFormat left, ClipboardFormat right) => !left.Equals(right);
}
