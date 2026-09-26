# Icons

Drop the finished `.ico` files here. The build picks them up automatically — nothing else to
configure.

| File | Used for | Required |
|---|---|---|
| `app.ico` | The executable, window title bars, Alt+Tab, the installer, Add/Remove Programs | Yes |
| `tray-light.ico` | Notification-area glyph for a **dark** taskbar (the Windows 11 default) | Yes |
| `tray-dark.ico` | Notification-area glyph for a **light** taskbar | Yes |
| `menu-folder.ico` | Folder glyph beside menu items | Optional |
| `menu-snippet.ico` | Text glyph beside snippet menu items | Optional |

Full specifications, sizes and design constraints: [ASSETS_REQUIRED.md](../../../../ASSETS_REQUIRED.md)

Until these exist, the tray glyph is drawn at runtime by `TrayIconGlyph` and the executable uses
the default .NET icon.

Keep editable masters (SVG, AI, Figma exports) in `/assets/` at the repository root, not here —
this folder is for build inputs only.
