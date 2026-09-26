using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CopyPasta.Core.Menu;

namespace CopyPasta.Interop;

/// <summary>
/// Supplies the small glyphs drawn beside menu entries, as menu-ready bitmaps.
/// </summary>
/// <remarks>
/// <para>
/// Port of the macOS <c>folderIcon</c> / <c>snippetIcon</c> cache. Cached for the same reason: a
/// menu can hold thirty folder rows, and each one would otherwise decode the same file and leak a
/// GDI object.
/// </para>
/// <para>
/// Win32 menus take an <c>HBITMAP</c>, not an <c>HICON</c>, and want it pre-multiplied 32-bit for
/// the alpha to composite correctly — so the icon is loaded at the menu's own check-mark size and
/// drawn into a <c>Format32bppPArgb</c> bitmap.
/// </para>
/// </remarks>
public sealed class MenuIconCache : IDisposable
{
    private const int SM_CXMENUCHECK = 71;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    private readonly Dictionary<MenuIcon, IntPtr> _bitmaps = [];
    private readonly string _directory;
    private bool _disposed;

    public MenuIconCache(string? directory = null) =>
        _directory = directory ?? AppContext.BaseDirectory;

    /// <summary>
    /// The bitmap for a glyph, or <see cref="IntPtr.Zero"/> when the artwork is missing.
    /// </summary>
    /// <remarks>
    /// The cache owns the handle; callers must not delete it. Zero is a normal answer — a build
    /// without artwork simply shows no glyphs rather than failing to build a menu.
    /// </remarks>
    public IntPtr GetBitmap(MenuIcon icon)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (icon == MenuIcon.None)
        {
            return IntPtr.Zero;
        }

        if (_bitmaps.TryGetValue(icon, out IntPtr cached))
        {
            return cached;
        }

        IntPtr bitmap = Load(icon);
        _bitmaps[icon] = bitmap;
        return bitmap;
    }

    private IntPtr Load(MenuIcon icon)
    {
        string path = Path.Combine(
            _directory,
            icon == MenuIcon.Folder ? "menu-folder.ico" : "menu-snippet.ico");

        if (!File.Exists(path))
        {
            return IntPtr.Zero;
        }

        // The menu check-mark metric is what Windows sizes menu bitmaps to, and it follows DPI.
        int size = Math.Clamp(GetSystemMetrics(SM_CXMENUCHECK), 16, 32);

        IntPtr iconHandle = LoadImage(IntPtr.Zero, path, IMAGE_ICON, size, size, LR_LOADFROMFILE);
        if (iconHandle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            using Icon loaded = Icon.FromHandle(iconHandle);
            using Bitmap source = loaded.ToBitmap();
            using Bitmap premultiplied = new(size, size, PixelFormat.Format32bppPArgb);

            using (Graphics graphics = Graphics.FromImage(premultiplied))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(source, 0, 0, size, size);
            }

            return premultiplied.GetHbitmap(Color.Transparent);
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException)
        {
            return IntPtr.Zero;
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IntPtr bitmap in _bitmaps.Values)
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }
        }

        _bitmaps.Clear();
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
