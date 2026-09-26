using CopyPasta.Core.Ocr;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace CopyPasta.Interop;

/// <summary>
/// Reads text out of images using the OCR engine built into Windows.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of macOS Vision, and close to a like-for-like swap: both ship with the OS, run
/// locally, need no model download and no network. The one difference that matters is that Windows
/// OCR depends on an installed language pack — a system with none has no engine at all, which is
/// why availability is checked once up front rather than discovered per image.
/// </para>
/// <para>
/// Engine creation is cached. <c>TryCreateFromUserProfileLanguages</c> is not cheap, and the app
/// recognises one image per captured screenshot.
/// </para>
/// </remarks>
public sealed class WindowsTextRecognizer : IImageTextRecognizer
{
    private readonly OcrEngine? _engine;

    public WindowsTextRecognizer()
    {
        try
        {
            // Follows the user's language list, so a German user gets German recognition.
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _engine = null;
        }
    }

    /// <summary>The language the engine recognises, or null when there is no engine.</summary>
    public string? RecognizerLanguage => _engine?.RecognizerLanguage?.LanguageTag;

    public bool IsAvailable => _engine is not null;

    public async Task<string?> RecognizeAsync(
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        if (_engine is null || imageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using SoftwareBitmap? bitmap = await DecodeAsync(imageBytes, cancellationToken)
                .ConfigureAwait(false);

            if (bitmap is null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            OcrResult result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken)
                .ConfigureAwait(false);

            return result.Text ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // Arbitrary applications put arbitrary bytes on the clipboard; an image the decoder
            // cannot read is not an error worth surfacing.
            return null;
        }
    }

    private static async Task<SoftwareBitmap?> DecodeAsync(
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        using InMemoryRandomAccessStream stream = new();

        using (DataWriter writer = new(stream))
        {
            writer.WriteBytes(imageBytes);
            await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

            // Detach so disposing the writer does not close the stream the decoder still needs.
            writer.DetachStream();
        }

        stream.Seek(0);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken)
            .ConfigureAwait(false);

        return await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Failure modes worth swallowing: a malformed image, a missing codec, or the engine refusing
    /// the bitmap's size or format.
    /// </summary>
    private static bool IsExpected(Exception exception) =>
        exception is ArgumentException
            or System.Runtime.InteropServices.COMException
            or NotSupportedException
            or InvalidOperationException
            or ObjectDisposedException;
}
