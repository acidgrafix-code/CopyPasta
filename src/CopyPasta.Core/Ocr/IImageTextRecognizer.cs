namespace CopyPasta.Core.Ocr;

/// <summary>
/// Reads text out of a copied image, so screenshots become searchable.
/// </summary>
/// <remarks>
/// Port of the macOS <c>TextRecognizer</c>, which uses Vision. The Windows implementation uses the
/// built-in <c>Windows.Media.Ocr</c> engine, so like Vision it needs no model download and no
/// network — but unlike Vision it depends on an OCR language pack being installed, which is why
/// <see cref="IsAvailable"/> exists.
/// </remarks>
public interface IImageTextRecognizer
{
    /// <summary>
    /// False when the system has no usable OCR engine. Recognition is then skipped entirely rather
    /// than failing once per captured image.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The text found in an image, empty when there is none, or null when recognition failed.
    /// </summary>
    /// <remarks>
    /// Empty and null mean different things on purpose: empty is a completed scan that found
    /// nothing, and gets recorded so the clip is never re-scanned. Null is a failure worth
    /// retrying.
    /// </remarks>
    Task<string?> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}

/// <summary>A recogniser for systems with no OCR engine. Always unavailable.</summary>
public sealed class UnavailableTextRecognizer : IImageTextRecognizer
{
    public bool IsAvailable => false;

    public Task<string?> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
