using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace CopyPasta.Core.Clipboard;

/// <summary>
/// One captured clip: every stored representation, plus the derived values the rest of the
/// app needs. Port of macOS <c>PasteboardContent</c>.
/// </summary>
/// <remarks>
/// macOS additionally reconstructs <c>NSPasteboardItem</c> boundaries on replay, because a
/// single macOS pasteboard can hold several items each with their own flavours. The Windows
/// clipboard is flat — one blob per format, with multiple files carried inside a single
/// CF_HDROP blob — so that whole concept disappears here.
/// </remarks>
public sealed class ClipContent
{
    private ClipContent(IReadOnlyList<ClipAsset> assets, string hash)
    {
        Assets = assets;
        Hash = hash;
        Formats = assets.Select(asset => asset.Format).ToArray();
    }

    /// <summary>Stored representations, in priority order (most descriptive first).</summary>
    public IReadOnlyList<ClipAsset> Assets { get; }

    /// <summary>Formats of <see cref="Assets"/>, in the same order.</summary>
    public IReadOnlyList<ClipboardFormat> Formats { get; }

    /// <summary>
    /// SHA-256 over the assets; doubles as the database primary key.
    /// See <see cref="ClipContentHasher"/>.
    /// </summary>
    public string Hash { get; }

    /// <summary>The primary (highest-priority) format.</summary>
    public ClipboardFormat PrimaryFormat => Formats[0];

    /// <summary>
    /// Plain text of the clip, or null if it holds none.
    /// </summary>
    /// <remarks>
    /// Decoded from CF_UNICODETEXT as UTF-16LE with the terminating NUL trimmed. CF_TEXT is
    /// intentionally not a fallback: it is always superseded by CF_UNICODETEXT (which Windows
    /// synthesises when an app offers only CF_TEXT), so it is never stored, and decoding it
    /// would require the source app's ANSI codepage.
    /// </remarks>
    public string? TextValue
    {
        get
        {
            byte[]? data = DataFor(ClipboardFormat.UnicodeText);
            if (data is null)
            {
                return null;
            }

            return Encoding.Unicode.GetString(data).TrimEnd('\0');
        }
    }

    /// <summary>
    /// True for a clip that is textual but holds nothing but whitespace — not worth a history
    /// entry.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed on the <em>primary</em> format, exactly as macOS does: an image
    /// whose accompanying text happens to be blank is still worth keeping.
    /// </remarks>
    public bool IsBlankText
    {
        get
        {
            ClipboardFormat primary = PrimaryFormat;
            bool isTextual = primary == ClipboardFormat.UnicodeText ||
                             primary == ClipboardFormat.Text ||
                             primary == ClipboardFormat.OemText ||
                             primary == ClipboardFormat.Rtf;
            if (!isTextual)
            {
                return false;
            }

            string? text = TextValue;
            return text is not null && string.IsNullOrWhiteSpace(text);
        }
    }

    /// <summary>
    /// Stored image bytes for thumbnailing and OCR, preferring the least lossy available
    /// representation, or null if the clip holds no inline image.
    /// </summary>
    public byte[]? ImageData => ImageAsset?.Data;

    /// <summary>
    /// Which format <see cref="ImageData"/> came from, or null when the clip holds no inline image.
    /// </summary>
    /// <remarks>
    /// Needed because the bytes alone are not self-describing: a <c>CF_DIB</c> blob is a bare
    /// <c>BITMAPINFOHEADER</c> with no file header, so a decoder has to be told what it is looking
    /// at. PNG and the rest carry their own signatures.
    /// </remarks>
    public ClipboardFormat? ImageFormat => ImageAsset?.Format;

    private ClipAsset? ImageAsset =>
        AssetFor(ClipboardFormat.Png) ??
        AssetFor(ClipboardFormat.DibV5) ??
        AssetFor(ClipboardFormat.Dib) ??
        AssetFor(ClipboardFormat.Tiff) ??
        AssetFor(ClipboardFormat.Jfif) ??
        AssetFor(ClipboardFormat.Gif);

    /// <summary>
    /// Path of a copied image <em>file</em>, when the clip is a file reference to one.
    /// </summary>
    /// <remarks>
    /// Port of the macOS <c>imageFileURL</c> path. Returns a path rather than bytes so that
    /// this assembly stays free of I/O; the caller decides whether to read it, and handles the
    /// file having since moved.
    /// </remarks>
    public string? ImageFilePath
    {
        get
        {
            byte[]? hdrop = DataFor(ClipboardFormat.Hdrop);
            if (hdrop is null)
            {
                return null;
            }

            return HdropReader.ReadPaths(hdrop).FirstOrDefault(IsImagePath);
        }
    }

    /// <summary>
    /// Builds a clip from its assets, or returns false when there is nothing to store.
    /// Port of the failable macOS initialiser.
    /// </summary>
    public static bool TryCreate(
        IReadOnlyList<ClipAsset> assets,
        [NotNullWhen(true)] out ClipContent? content)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (assets.Count == 0)
        {
            content = null;
            return false;
        }

        content = new ClipContent(assets, ClipContentHasher.Compute(assets));
        return true;
    }

    /// <summary>
    /// Builds a clip from assets in arbitrary order, ordering them to match
    /// <paramref name="selection"/> and dropping any asset the selection excludes.
    /// </summary>
    /// <remarks>
    /// The ordering matters beyond tidiness: <see cref="PrimaryFormat"/> drives
    /// <see cref="IsBlankText"/> and the menu-item title prefix, and the order feeds the hash.
    /// Port of the macOS <c>sorted(by: types)</c> step.
    /// </remarks>
    public static bool TryCreate(
        ClipboardSelection selection,
        IReadOnlyList<ClipAsset> assets,
        [NotNullWhen(true)] out ClipContent? content)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(assets);

        List<ClipAsset> ordered = [];
        foreach (ClipboardFormat format in selection.Formats)
        {
            ordered.AddRange(assets.Where(asset => asset.Format == format));
        }

        return TryCreate(ordered, out content);
    }

    /// <summary>
    /// The clip's image in a form a standard decoder can read, or false when it holds none.
    /// </summary>
    /// <remarks>
    /// Clipboard DIBs are a bare <c>BITMAPINFOHEADER</c> with no file header, so every decoder
    /// rejects them until one is prepended. Both the thumbnail renderer and the text recogniser
    /// need the same treatment, so it lives here rather than in either of them.
    /// </remarks>
    public bool TryGetDecodableImage([NotNullWhen(true)] out byte[]? image)
    {
        image = null;

        if (ImageData is not { Length: > 0 } data)
        {
            return false;
        }

        ClipboardFormat? format = ImageFormat;

        if (format == ClipboardFormat.Dib || format == ClipboardFormat.DibV5)
        {
            if (!DibConverter.TryWrapAsBitmapFile(data, out byte[] wrapped))
            {
                return false;
            }

            image = wrapped;
            return true;
        }

        image = data;
        return true;
    }

    public byte[]? DataFor(ClipboardFormat format) => AssetFor(format)?.Data;

    private ClipAsset? AssetFor(ClipboardFormat format) =>
        Assets.FirstOrDefault(asset => asset.Format == format);

    private static bool IsImagePath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
