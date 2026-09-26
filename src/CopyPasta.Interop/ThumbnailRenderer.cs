using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Menu;

namespace CopyPasta.Interop;

/// <summary>Size limits for menu thumbnails.</summary>
/// <param name="Width">Port of <c>thumbnailWidth</c> (macOS default 100).</param>
/// <param name="Height">Port of <c>thumbnailHeight</c> (macOS default 32).</param>
public sealed record ThumbnailSize(int Width = 100, int Height = 32)
{
    public static ThumbnailSize Default { get; } = new();
}

/// <summary>
/// Produces the small previews shown beside menu items.
/// </summary>
/// <remarks>
/// Ports the macOS <c>NSImage+Resize</c> / <c>NSImage+NSColor</c> helpers and the
/// <c>colorCodeImage</c> path. Two kinds of preview, matching macOS: a scaled copy of a copied
/// image, and a swatch for a copied colour code such as <c>#3366ff</c>.
/// </remarks>
public static class ThumbnailRenderer
{
    /// <summary>
    /// A thumbnail for a clip, or null when it has nothing worth previewing.
    /// </summary>
    /// <remarks>
    /// The image is tried first and the colour swatch second, which is the opposite of the macOS
    /// order — there, the swatch assignment overwrites the image unconditionally, so a clip that is
    /// both never shows its image. Since a clip whose primary format is an image is not a colour
    /// code in practice, preferring the image is the better reading of intent.
    /// </remarks>
    public static ClipThumbnail? Create(ClipContent content, ThumbnailSize? size = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        size ??= ThumbnailSize.Default;

        if (content.TryGetDecodableImage(out byte[]? decodable) &&
            TryScale(decodable, size, out byte[]? scaled))
        {
            return new ClipThumbnail(ClipThumbnailKind.Image, scaled);
        }

        if (content.TextValue is { } text && TryParseColor(text, out Color color))
        {
            return new ClipThumbnail(ClipThumbnailKind.ColorCode, DrawSwatch(color));
        }

        return null;
    }

    /// <summary>
    /// Scales image bytes to fit the given box, preserving aspect ratio and never enlarging.
    /// </summary>
    /// <remarks>
    /// Encoded as PNG rather than kept as raw pixels: a thumbnail is persisted alongside the clip,
    /// and a 100x32 PNG is a few hundred bytes where the equivalent DIB is ten kilobytes.
    /// </remarks>
    private static bool TryScale(byte[] imageData, ThumbnailSize size, out byte[] thumbnail)
    {
        thumbnail = [];

        try
        {
            using MemoryStream input = new(imageData, writable: false);
            using Image source = Image.FromStream(input);

            if (source.Width <= 0 || source.Height <= 0)
            {
                return false;
            }

            double scale = Math.Min(
                (double)size.Width / source.Width,
                (double)size.Height / source.Height);

            // Never upscale: a 4x4 copied swatch should stay small rather than become a blurry box.
            scale = Math.Min(scale, 1.0);

            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using Bitmap scaled = new(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(source, 0, 0, width, height);
            }

            using MemoryStream output = new();
            scaled.Save(output, ImageFormat.Png);
            thumbnail = output.ToArray();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException)
        {
            // Arbitrary apps put arbitrary bytes on the clipboard; an undecodable image is not an
            // error worth surfacing.
            return false;
        }
    }

    private static byte[] DrawSwatch(Color color, int size = 20)
    {
        using Bitmap bitmap = new(size, size, PixelFormat.Format32bppPArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);

            // A hairline border, so a white or near-white swatch is still visible against the menu.
            using Pen pen = new(Color.FromArgb(64, 0, 0, 0));
            graphics.DrawRectangle(pen, 0, 0, size - 1, size - 1);
        }

        using MemoryStream output = new();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>
    /// Parses a CSS-style hex colour. Port of the <c>SwiftHEXColors</c> usage in macOS.
    /// </summary>
    /// <remarks>
    /// Accepts 3, 6 and 8 digit forms with or without a leading <c>#</c>. Deliberately strict
    /// about length: without that, any hex-looking word in copied text would sprout a colour
    /// swatch in the menu.
    /// </remarks>
    internal static bool TryParseColor(string text, out Color color)
    {
        color = Color.Empty;

        string value = text.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length is not (3 or 6 or 8))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        if (value.Length == 3)
        {
            // #abc means #aabbcc.
            value = string.Concat(value.Select(character => new string(character, 2)));
        }

        uint packed = uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        if (value.Length == 8)
        {
            // RRGGBBAA, the CSS ordering, which is not ARGB.
            color = Color.FromArgb(
                (int)(packed & 0xFF),
                (int)((packed >> 24) & 0xFF),
                (int)((packed >> 16) & 0xFF),
                (int)((packed >> 8) & 0xFF));
            return true;
        }

        color = Color.FromArgb(
            255,
            (int)((packed >> 16) & 0xFF),
            (int)((packed >> 8) & 0xFF),
            (int)(packed & 0xFF));
        return true;
    }
}
