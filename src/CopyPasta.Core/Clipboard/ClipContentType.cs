namespace CopyPasta.Core.Clipboard;

/// <summary>
/// The user-facing content categories that can be individually enabled or disabled in
/// settings. Port of macOS <c>PasteboardAvailableType</c>.
/// </summary>
/// <remarks>
/// macOS additionally has <c>RTFD</c> (rich text with embedded attachments). Windows has no
/// equivalent clipboard flavour, so it is deliberately absent — see §5 "deferred" in
/// PORTING_PLAN.md.
/// </remarks>
public enum ClipContentType
{
    Text,
    Rtf,
    Html,
    Pdf,
    Files,
    Url,
    Image,
}

public static class ClipContentTypes
{
    public static readonly IReadOnlyList<ClipContentType> All =
    [
        ClipContentType.Text,
        ClipContentType.Rtf,
        ClipContentType.Html,
        ClipContentType.Pdf,
        ClipContentType.Files,
        ClipContentType.Url,
        ClipContentType.Image,
    ];

    /// <summary>
    /// The stable string used in the settings file.
    /// </summary>
    /// <remarks>
    /// Mostly the macOS <c>PasteboardAvailableType</c> raw values, with one deliberate exception:
    /// macOS spells the image category <c>TIFF</c>, after the flavour its pasteboard happens to
    /// use. Settings are not interchanged between the ports — macOS keeps them in a defaults plist
    /// and this keeps them in JSON — so there is nothing to gain from a name that would leave
    /// anyone hand-editing the file wondering why disabling <c>Image</c> did nothing.
    /// </remarks>
    public static string ToSettingsKey(this ClipContentType type) => type switch
    {
        ClipContentType.Text => "String",
        ClipContentType.Rtf => "RTF",
        ClipContentType.Html => "HTML",
        ClipContentType.Pdf => "PDF",
        ClipContentType.Files => "Filenames",
        ClipContentType.Url => "URL",
        ClipContentType.Image => "Image",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
