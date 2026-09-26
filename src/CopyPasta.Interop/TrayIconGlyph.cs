using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CopyPasta.Interop;

/// <summary>Whether the tray glyph is drawn for a light or dark taskbar.</summary>
/// <remarks>
/// Port of the macOS <c>StatusType</c> black / white / hidden choice. macOS gets automatic
/// inversion free through template images; Windows draws exactly the icon it is handed, so the
/// colour is a setting and the glyph ships twice.
/// </remarks>
public enum TrayGlyphStyle
{
    /// <summary>Dark glyph, for a light taskbar.</summary>
    Dark,

    /// <summary>Light glyph, for a dark taskbar — the Windows 11 default.</summary>
    Light,
}

/// <summary>
/// Supplies the notification-area icon.
/// </summary>
/// <remarks>
/// <para>
/// Loads <c>tray-light.ico</c> or <c>tray-dark.ico</c> from the application directory, picking the
/// frame that matches the current DPI. Falls back to drawing a placeholder if the files are
/// missing, so a development build without artwork still runs.
/// </para>
/// <para>
/// <c>LoadImage</c> is used rather than <see cref="Icon"/> because it selects the best-matching
/// frame from a multi-resolution <c>.ico</c> for the size asked for. Constructing an
/// <see cref="Icon"/> and calling <see cref="Icon.ToBitmap"/> would rescale a single frame instead,
/// which is the softness the multi-resolution format exists to avoid.
/// </para>
/// </remarks>
public static class TrayIconGlyph
{
    private const int SM_CXSMICON = 49;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTCOLOR = 0x00000000;

    /// <summary>Where the icons live, relative to the executable.</summary>
    public static string IconDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// Creates an icon handle for the current DPI. The caller owns it;
    /// <see cref="TrayIcon.Show"/> takes ownership.
    /// </summary>
    public static IntPtr Create(TrayGlyphStyle style, int size = 0)
    {
        // 0 asks the system for the small-icon metric, which follows the DPI.
        int pixels = size > 0 ? size : GetSystemMetrics(SM_CXSMICON);
        pixels = Math.Clamp(pixels, 16, 64);

        IntPtr fromFile = TryLoadFromFile(style, pixels);
        return fromFile != IntPtr.Zero ? fromFile : DrawPlaceholder(style, pixels);
    }

    /// <summary>True when the shipped artwork is present.</summary>
    public static bool HasArtwork(TrayGlyphStyle style) => File.Exists(PathFor(style));

    private static string PathFor(TrayGlyphStyle style) => Path.Combine(
        IconDirectory,
        style == TrayGlyphStyle.Light ? "tray-light.ico" : "tray-dark.ico");

    private static IntPtr TryLoadFromFile(TrayGlyphStyle style, int pixels)
    {
        string path = PathFor(style);

        if (!File.Exists(path))
        {
            return IntPtr.Zero;
        }

        // cx/cy select the closest frame in the file rather than rescaling one.
        return LoadImage(
            IntPtr.Zero,
            path,
            IMAGE_ICON,
            pixels,
            pixels,
            LR_LOADFROMFILE | LR_DEFAULTCOLOR);
    }

    /// <summary>
    /// A drawn stand-in for builds without artwork.
    /// </summary>
    /// <remarks>
    /// Kept so the app is runnable from a fresh clone before the icons are in place. It is not a
    /// design — see ICON_BRIEF.md for the real specification.
    /// </remarks>
    private static IntPtr DrawPlaceholder(TrayGlyphStyle style, int pixels)
    {
        using Bitmap bitmap = Draw(style, pixels);
        return bitmap.GetHicon();
    }

    private static Bitmap Draw(TrayGlyphStyle style, int pixels)
    {
        Bitmap bitmap = new(pixels, pixels);
        Color ink = style == TrayGlyphStyle.Light ? Color.White : Color.FromArgb(26, 26, 26);

        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        float unit = pixels / 16f;
        float penWidth = Math.Max(1f, unit * 1.4f);

        RectangleF body = new(unit * 3.2f, unit * 3.0f, unit * 9.6f, unit * 11.2f);

        using Pen pen = new(ink, penWidth) { LineJoin = LineJoin.Round };
        using GraphicsPath path = RoundedRectangle(body, unit * 1.8f);
        graphics.DrawPath(pen, path);

        RectangleF clip = new(unit * 5.8f, unit * 1.6f, unit * 4.4f, unit * 2.8f);

        using GraphicsPath clipPath = RoundedRectangle(clip, unit * 1.0f);
        using SolidBrush brush = new(ink);
        graphics.FillPath(brush, clipPath);

        return bitmap;
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        GraphicsPath path = new();

        if (diameter <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        RectangleF arc = new(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180f, 90f);

        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270f, 90f);

        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0f, 90f);

        arc.X = bounds.X;
        path.AddArc(arc, 90f, 90f);

        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern IntPtr LoadImage(
        IntPtr hInst,
        string name,
        uint type,
        int cx,
        int cy,
        uint fuLoad);
}
