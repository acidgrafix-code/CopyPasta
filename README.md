# CopyPasta

A clipboard history manager for Windows: everything you copy stays available from a tray menu and
a global hotkey, with reusable snippets alongside it.

It is a port of [Clipy](https://clipy-app.com/), the macOS clipboard manager, which is itself
derived from ClipMenu. Both are MIT licensed. See [Attribution](#attribution).

**[User Guide](GUIDE.md)** — what everything does and how to use it.

## What it does

- **Clipboard history** — text, rich text, images, files and colour codes, with thumbnails and
  colour swatches in the menu.
- **Snippets** — reusable text, organised into folders, with an editor and import/export.
- **Global hotkeys** — `Ctrl+Alt+V` for the full menu, `Ctrl+Alt+H` for history, `Ctrl+Alt+B` for
  snippets.
- **Modifier actions** — hold Shift while choosing an item to paste it as plain text.
- **Text in images** — screenshots are run through Windows OCR so their text is searchable.
- **Seven languages** — English, German, Italian, Japanese, Brazilian Portuguese, Ukrainian and
  Simplified Chinese.

## Installing

Download `CopyPasta-win-Setup.exe` from [Releases](../../releases) and run it. It installs per-user
to `%LOCALAPPDATA%\CopyPasta` and needs no administrator prompt.

The installer is **not code-signed yet**, so Windows will show "Windows protected your PC" on first
run. Choose *More info → Run anyway*.

Your data lives in `%APPDATA%\CopyPasta\` and is not removed when you uninstall.

## Building

Needs the .NET 9 SDK on Windows.

```powershell
dotnet test CopyPasta.sln
pwsh build-installer.ps1
```

`build-installer.ps1` runs the tests, publishes a self-contained x64 build and packages it with
Velopack into `artifacts/releases`. Pass `-SigningCertificate` and `-SigningPassword` to produce a
signed build.

### Project layout

| Project | What is in it |
|---|---|
| `CopyPasta.Core` | Clipboard, capture, paste, menu, snippet, hotkey and OCR logic. No P/Invoke, so it is all testable without a desktop. |
| `CopyPasta.Data` | SQLite storage — history, snippets and the full-text index. |
| `CopyPasta.Interop` | The Win32 layer: clipboard listener, tray icon, popup menus, `SendInput`, hotkey registration, Windows OCR. |
| `CopyPasta.App` | The tray application, settings and snippet windows. |

The split is deliberate: the interesting behaviour lives in `Core` and is covered by tests that
need no window, no clipboard and no network.

### Translations

The string tables in `src/CopyPasta.App/Resources/Strings` are generated from Clipy's Apple string
catalogues by `tools/ExtractStrings.ps1`. Re-running it needs a local copy of the Clipy source,
which is not committed here — point the script at one with `-SourceDirectory`.

The generated tables are committed, so a normal build never needs it.

## Known gaps

- **No search UI.** The full-text index works and is tested, but nothing surfaces it yet.
- **No menu tooltips.** Win32 menus cannot show per-item tooltips, so long clips are truncated in
  the title with no hover preview.
- **Per-folder snippet hotkeys** need the folder's id written into `settings.json` by hand.
- **Unsigned installer.** See above.

## Attribution

CopyPasta is an independent Windows implementation, not a fork — the macOS app is Swift and this is
C#. What it takes from upstream is the design, the behaviour and the translations.

- [Clipy](https://github.com/Clipy/Clipy) — © 2015-2026 Clipy Project, MIT licensed.
- [ClipMenu](https://github.com/naotaka/ClipMenu) — © 2010 naotaka, MIT licensed, which Clipy is
  derived from.

**Clipy's icons are copyrighted separately from its MIT code grant and are not used here.**
CopyPasta's artwork is its own.

Full notices for these and for the other third-party components are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licence

MIT — see [LICENSE](LICENSE).
