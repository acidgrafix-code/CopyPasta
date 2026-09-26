using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CopyPasta.Core.Menu;
using CopyPasta.Interop;

namespace CopyPasta.App;

/// <summary>
/// Prints the menu twice: as the model, and as the native menu Windows actually ends up with.
/// </summary>
/// <remarks>
/// The Phase 3 acceptance check. <c>MenuBuilderTests</c> already pins the shape, but that is the
/// model; this walks the real HMENU back out with <c>GetMenuItemCount</c> and
/// <c>GetMenuStringW</c>, so the translation — ampersand escaping, submenu nesting, disabled
/// captions, attached bitmaps — is verified rather than assumed.
/// </remarks>
internal static class MenuDiagnostics
{
    private const uint MF_BYPOSITION = 0x00000400;
    private const uint MIIM_STATE = 0x00000001;
    private const uint MIIM_SUBMENU = 0x00000004;
    private const uint MIIM_BITMAP = 0x00000080;
    private const uint MIIM_FTYPE = 0x00000100;
    private const uint MFT_SEPARATOR = 0x00000800;
    private const uint MFS_DISABLED = 0x00000003;

    public static void Dump(ClipMenu menu, TextWriter output)
    {
        output.WriteLine("=== model ===");
        DumpModel(menu, output);

        output.WriteLine();
        output.WriteLine("=== native menu, read back from Win32 ===");

        using PopupMenuRenderer renderer = new();
        IntPtr handle = renderer.BuildMenuHandle(menu);

        if (handle == IntPtr.Zero)
        {
            output.WriteLine("(could not create the menu)");
            return;
        }

        try
        {
            DumpNative(handle, output, depth: 0);
        }
        finally
        {
            PopupMenuRenderer.DestroyMenuHandle(handle);
        }
    }

    private static void DumpModel(ClipMenu menu, TextWriter output)
    {
        foreach (MenuNode node in menu.Nodes)
        {
            switch (node)
            {
                case MenuSeparator:
                    output.WriteLine("  ---");
                    break;

                case MenuLabel label:
                    output.WriteLine($"  [{label.Text}]");
                    break;

                case ClipMenuItem item:
                    output.WriteLine($"  {Describe(item)}");
                    break;

                case ClipFolderMenuItem folder:
                    output.WriteLine($"  > {folder.Title}  ({folder.Items.Count} items)");
                    foreach (ClipMenuItem child in folder.Items)
                    {
                        output.WriteLine($"      {Describe(child)}");
                    }

                    break;

                case SnippetFolderMenuItem snippetFolder:
                    output.WriteLine(
                        $"  > {snippetFolder.Title}  ({snippetFolder.Items.Count} snippets)");
                    foreach (SnippetMenuItem snippet in snippetFolder.Items)
                    {
                        output.WriteLine($"      {snippet.Title}");
                    }

                    break;

                case SnippetMenuItem snippet:
                    output.WriteLine($"  {snippet.Title}");
                    break;

                case MenuCommand command:
                    string state = command.IsEnabled ? string.Empty : "  (disabled)";
                    output.WriteLine($"  * {command.Title}{state}");
                    break;
            }
        }
    }

    private static string Describe(ClipMenuItem item)
    {
        StringBuilder text = new(item.Title);

        if (item.Accelerator is { } accelerator)
        {
            text.Append($"   [accel {accelerator}]");
        }

        if (item.Thumbnail is { } thumbnail)
        {
            text.Append($"   [{thumbnail.Kind} {thumbnail.Data.Length}B]");
        }

        return text.ToString();
    }

    private static void DumpNative(IntPtr menu, TextWriter output, int depth)
    {
        int count = GetMenuItemCount(menu);
        string indent = new(' ', 2 + (depth * 4));

        for (int position = 0; position < count; position++)
        {
            MENUITEMINFO info = new()
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MIIM_STATE | MIIM_SUBMENU | MIIM_BITMAP | MIIM_FTYPE,
            };

            if (!GetMenuItemInfo(menu, (uint)position, fByPosition: true, ref info))
            {
                continue;
            }

            if ((info.fType & MFT_SEPARATOR) != 0)
            {
                output.WriteLine($"{indent}---");
                continue;
            }

            StringBuilder buffer = new(512);
            GetMenuString(menu, (uint)position, buffer, buffer.Capacity, MF_BYPOSITION);

            StringBuilder line = new($"{indent}{buffer}");

            if ((info.fState & MFS_DISABLED) != 0)
            {
                line.Append("   (disabled)");
            }

            if (info.hbmpItem != IntPtr.Zero)
            {
                line.Append("   (bitmap)");
            }

            if (info.hSubMenu != IntPtr.Zero)
            {
                line.Append("   (submenu)");
            }

            output.WriteLine(line);

            if (info.hSubMenu != IntPtr.Zero)
            {
                DumpNative(info.hSubMenu, output, depth + 1);
            }
        }
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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetMenuItemInfoW")]
    private static extern bool GetMenuItemInfo(
        IntPtr hMenu,
        uint item,
        bool fByPosition,
        ref MENUITEMINFO lpmii);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")]
    private static extern int GetMenuString(
        IntPtr hMenu,
        uint uIDItem,
        StringBuilder lpString,
        int cchMax,
        uint flags);
}
