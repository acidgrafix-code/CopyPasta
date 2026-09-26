using System.Drawing;
using System.Runtime.InteropServices;
using CopyPasta.Core.Menu;

namespace CopyPasta.Interop;

/// <summary>What the user chose from a popup menu.</summary>
public abstract record MenuSelection
{
    /// <summary>Dismissed without choosing.</summary>
    public sealed record None : MenuSelection;

    /// <summary>A history entry.</summary>
    public sealed record Clip(string ClipId) : MenuSelection;

    /// <summary>A snippet.</summary>
    public sealed record SnippetChoice(Guid SnippetId) : MenuSelection;

    /// <summary>One of the commands at the bottom.</summary>
    public sealed record Command(MenuCommandKind Kind) : MenuSelection;
}

/// <summary>
/// Renders a <see cref="ClipMenu"/> as a native popup menu and returns what the user chose.
/// </summary>
/// <remarks>
/// <para>
/// A mechanical walk of the model tree: everything about <em>shape</em> was decided by
/// <see cref="MenuBuilder"/>, so this file only translates nodes into Win32 calls. That split is
/// what makes the layout rules testable without a display.
/// </para>
/// <para>
/// Known parity gap: Win32 menus have no per-item tooltips. macOS sets
/// <c>NSMenuItem.toolTip</c> and gets a hover preview for free; there is no HMENU equivalent
/// short of owner-drawing the menu and hosting a tooltip window. The model still carries
/// <see cref="ClipMenuItem.ToolTip"/> so a future custom renderer can use it, but nothing
/// displays it today.
/// </para>
/// </remarks>
public sealed class PopupMenuRenderer : IDisposable
{
    private const uint MF_STRING = 0x00000000;
    private const uint MF_POPUP = 0x00000010;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_DISABLED = 0x00000002;

    private const uint MIIM_BITMAP = 0x00000080;
    private const uint MIIM_STATE = 0x00000001;

    private const uint MFS_DISABLED = 0x00000003;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;

    /// <summary>Command ids start above zero, because zero means "dismissed".</summary>
    private const uint FirstCommandId = 1;

    private readonly List<IntPtr> _bitmaps = [];
    private readonly MenuIconCache _icons = new();
    private readonly Dictionary<uint, MenuSelection> _selections = [];
    private uint _nextId = FirstCommandId;
    private bool _disposed;

    /// <summary>
    /// Shows the menu at the cursor and blocks until the user chooses or dismisses.
    /// </summary>
    /// <param name="menu">The menu shape to render.</param>
    /// <param name="ownerWindow">
    /// A window to own the menu. It must be brought to the foreground first, or Windows dismisses
    /// the menu on the next click outside it.
    /// </param>
    public MenuSelection Show(ClipMenu menu, IntPtr ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Reset();

        IntPtr handle = CreatePopupMenu();
        if (handle == IntPtr.Zero)
        {
            return new MenuSelection.None();
        }

        try
        {
            AppendNodes(handle, menu.Nodes);

            GetCursorPos(out POINT cursor);

            // The owner window has to be foreground or the menu closes immediately. This is the
            // tray-menu equivalent of the focus dance in the paste path.
            SetForegroundWindow(ownerWindow);

            uint chosen = (uint)TrackPopupMenuEx(
                handle,
                TPM_LEFTALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                cursor.X,
                cursor.Y,
                ownerWindow,
                IntPtr.Zero);

            // Documented quirk: without this the owner can be left thinking the menu is still up.
            PostMessage(ownerWindow, 0x0000, IntPtr.Zero, IntPtr.Zero);

            return chosen != 0 && _selections.TryGetValue(chosen, out MenuSelection? selection)
                ? selection
                : new MenuSelection.None();
        }
        finally
        {
            DestroyMenu(handle);
            ReleaseBitmaps();
        }
    }

    /// <summary>
    /// Builds the native menu without showing it, for diagnostics. The caller must pass the handle
    /// to <see cref="DestroyMenuHandle"/>.
    /// </summary>
    /// <remarks>
    /// Exists so the rendering can be verified automatically. <c>TrackPopupMenuEx</c> blocks until a
    /// human dismisses it, which would otherwise leave the whole translation from model to HMENU —
    /// text escaping, submenu nesting, disabled captions, attached bitmaps — checkable only by eye.
    /// </remarks>
    internal IntPtr BuildMenuHandle(ClipMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        Reset();

        IntPtr handle = CreatePopupMenu();
        if (handle != IntPtr.Zero)
        {
            AppendNodes(handle, menu.Nodes);
        }

        return handle;
    }

    internal static void DestroyMenuHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            DestroyMenu(handle);
        }
    }

    private void AppendNodes(IntPtr menu, IReadOnlyList<MenuNode> nodes)
    {
        foreach (MenuNode node in nodes)
        {
            switch (node)
            {
                case MenuSeparator:
                    AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
                    break;

                case MenuLabel label:
                    // A caption: present but unselectable, matching the disabled NSMenuItem macOS
                    // uses for its section headings.
                    AppendMenu(
                        menu,
                        MF_STRING | MF_GRAYED | MF_DISABLED,
                        IntPtr.Zero,
                        Escape(label.Text));
                    break;

                case ClipMenuItem item:
                    AppendClipItem(menu, item);
                    break;

                case ClipFolderMenuItem folder:
                    AppendFolder(menu, folder);
                    break;

                case SnippetMenuItem snippet:
                    AppendSnippetItem(menu, snippet);
                    break;

                case SnippetFolderMenuItem snippetFolder:
                    AppendSnippetFolder(menu, snippetFolder);
                    break;

                case MenuCommand command:
                    AppendCommand(menu, command);
                    break;
            }
        }
    }

    private void AppendFolder(IntPtr menu, ClipFolderMenuItem folder)
    {
        IntPtr submenu = CreatePopupMenu();
        if (submenu == IntPtr.Zero)
        {
            return;
        }

        foreach (ClipMenuItem item in folder.Items)
        {
            AppendClipItem(submenu, item);
        }

        // The submenu handle becomes owned by the parent, so destroying the parent frees it.
        AppendMenu(menu, MF_STRING | MF_POPUP, submenu, Escape(folder.Title));
        AttachIcon(menu, folder.Icon, byPosition: true);
    }

    private void AppendSnippetFolder(IntPtr menu, SnippetFolderMenuItem folder)
    {
        IntPtr submenu = CreatePopupMenu();
        if (submenu == IntPtr.Zero)
        {
            return;
        }

        foreach (SnippetMenuItem snippet in folder.Items)
        {
            AppendSnippetItem(submenu, snippet);
        }

        // An empty folder would otherwise render as a submenu arrow that leads nowhere.
        if (folder.Items.Count == 0)
        {
            AppendMenu(submenu, MF_STRING | MF_GRAYED, IntPtr.Zero, "(empty)");
        }

        AppendMenu(menu, MF_STRING | MF_POPUP, submenu, Escape(folder.Title));
        AttachIcon(menu, folder.Icon, byPosition: true);
    }

    private void AppendSnippetItem(IntPtr menu, SnippetMenuItem snippet)
    {
        uint id = Register(new MenuSelection.SnippetChoice(snippet.SnippetId));
        AppendMenu(menu, MF_STRING, (IntPtr)id, Escape(snippet.Title));
        AttachIcon(menu, snippet.Icon, byPosition: false, id);
    }

    /// <summary>
    /// Puts a cached glyph on the item just appended.
    /// </summary>
    /// <remarks>
    /// Submenu rows have no command id, so they are addressed by position — which is why the last
    /// appended item is targeted rather than looked up.
    /// </remarks>
    private void AttachIcon(IntPtr menu, MenuIcon icon, bool byPosition, uint id = 0)
    {
        if (icon == MenuIcon.None)
        {
            return;
        }

        IntPtr bitmap = _icons.GetBitmap(icon);
        if (bitmap == IntPtr.Zero)
        {
            return;
        }

        MENUITEMINFO info = new()
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MIIM_BITMAP,
            hbmpItem = bitmap,
        };

        uint target = byPosition ? (uint)(GetMenuItemCount(menu) - 1) : id;
        SetMenuItemInfo(menu, target, byPosition, ref info);
    }

    private void AppendClipItem(IntPtr menu, ClipMenuItem item)
    {
        uint id = Register(new MenuSelection.Clip(item.ClipId));

        AppendMenu(menu, MF_STRING, (IntPtr)id, BuildItemText(item));

        if (item.Thumbnail is { } thumbnail)
        {
            AttachBitmap(menu, id, thumbnail);
        }
    }

    private void AppendCommand(IntPtr menu, MenuCommand command)
    {
        uint id = Register(new MenuSelection.Command(command.Kind));

        uint flags = MF_STRING;
        if (!command.IsEnabled)
        {
            flags |= MF_GRAYED;
        }

        string text = Escape(command.Title);
        if (command.Accelerator is { Length: > 0 } accelerator)
        {
            text = $"{text}\t{Escape(accelerator)}";
        }

        AppendMenu(menu, flags, (IntPtr)id, text);
    }

    /// <summary>
    /// The item's display text, with the numeric accelerator expressed as a mnemonic.
    /// </summary>
    /// <remarks>
    /// macOS sets <c>keyEquivalent</c>, which is a separate field. Win32 menus have no such field:
    /// the selectable character is whatever follows an ampersand in the text. When the title
    /// already begins with the accelerator digit — the usual case, since titles read "3. …" —
    /// marking that digit in place gives the same behaviour with no visible change. Otherwise the
    /// digit is shown right-aligned after a tab, where Windows conventionally puts shortcuts.
    /// </remarks>
    private static string BuildItemText(ClipMenuItem item)
    {
        string text = Escape(item.Title);

        if (item.Accelerator is not { Length: 1 } accelerator)
        {
            return text;
        }

        if (item.Title.StartsWith(accelerator, StringComparison.Ordinal))
        {
            return $"&{text}";
        }

        return $"{text}\t&{accelerator}";
    }

    /// <summary>
    /// Doubles ampersands so they render literally.
    /// </summary>
    /// <remarks>
    /// Not cosmetic: an unescaped ampersand in clipboard text would silently become a mnemonic and
    /// swallow the following character. Copying "R&amp;D notes" must not produce "RD notes".
    /// </remarks>
    private static string Escape(string text) => text.Replace("&", "&&", StringComparison.Ordinal);

    private void AttachBitmap(IntPtr menu, uint id, ClipThumbnail thumbnail)
    {
        IntPtr bitmap = CreateBitmapHandle(thumbnail.Data);
        if (bitmap == IntPtr.Zero)
        {
            return;
        }

        _bitmaps.Add(bitmap);

        MENUITEMINFO info = new()
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MIIM_BITMAP,
            hbmpItem = bitmap,
        };

        SetMenuItemInfo(menu, id, fByPosition: false, ref info);
    }

    /// <summary>
    /// Decodes thumbnail bytes into an HBITMAP the menu can display.
    /// </summary>
    /// <remarks>
    /// Menu bitmaps must be pre-multiplied 32-bit DIBs for alpha to render correctly, which is what
    /// drawing into a 32bppPArgb bitmap produces. Copying through that intermediate also
    /// normalises whatever the thumbnail was encoded as.
    /// </remarks>
    private static IntPtr CreateBitmapHandle(byte[] data)
    {
        try
        {
            using MemoryStream stream = new(data, writable: false);
            using Image decoded = Image.FromStream(stream);
            using Bitmap premultiplied = new(
                decoded.Width,
                decoded.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            using (Graphics graphics = Graphics.FromImage(premultiplied))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(decoded, 0, 0, decoded.Width, decoded.Height);
            }

            return premultiplied.GetHbitmap(Color.Transparent);
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException)
        {
            // An undecodable thumbnail is not worth failing the whole menu over.
            return IntPtr.Zero;
        }
    }

    private uint Register(MenuSelection selection)
    {
        uint id = _nextId++;
        _selections[id] = selection;
        return id;
    }

    private void Reset()
    {
        _selections.Clear();
        _nextId = FirstCommandId;
        ReleaseBitmaps();
    }

    private void ReleaseBitmaps()
    {
        foreach (IntPtr bitmap in _bitmaps)
        {
            DeleteObject(bitmap);
        }

        _bitmaps.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseBitmaps();
        _icons.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MENUITEMINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetMenuItemInfoW")]
    private static extern bool SetMenuItemInfo(
        IntPtr hMenu,
        uint item,
        bool fByPosition,
        ref MENUITEMINFO lpmii);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
