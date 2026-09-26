# CopyPasta — Windows port

> `CopyPasta` is a placeholder name, chosen so nothing ships under the Clipy/ClipMenu
> product names (see §2 of [PORTING_PLAN.md](../PORTING_PLAN.md)). It is one rename away
> from whatever you settle on.

**Status: Phases 1 to 7 complete — it runs.** `CopyPasta.App` is a working, localised tray
clipboard manager with snippets, search and a settings window: it captures clips, pops its menu
from the tray or a global hotkey, pastes what you pick, reads text out of copied images so they can
be searched, watches the screenshots folder, edits snippets with drag-and-drop and XML
import/export that interchanges with macOS Clipy, and every setting is editable and applies
immediately. Remaining: packaging, installer and updates (Phase 8).

42 of the 45 settings macOS stores are ported; the other three are listed with their reasons in the
Beta pane and in `SettingsCoverage.cs`, alongside the two settings this port adds. Interface
language follows Windows across de, en, it, ja, pt-BR, uk and zh-Hans.

Default hotkeys: **Ctrl+Alt+V** full menu, **Ctrl+Alt+H** history only, **Ctrl+Alt+B** snippets
(Phase 5).

```bash
dotnet run --project src/CopyPasta.App
```

## What is ported

| Swift source | C# port |
|---|---|
| `PasteboardAvailableType.availableTypes(from:…)` | `ClipboardFormatFilter.Select` |
| `PasteboardContent` (hashing, text, images) | `ClipContent`, `ClipContentHasher` |
| `NSPasteboard.PasteboardType` | `ClipboardFormat` (+ `ClipboardFormatRegistry` for Win32 ids) |
| `PasteboardAvailableType` | `ClipContentType` |
| `fileURL` image handling | `HdropReader` + `ClipContent.ImageFilePath` |
| `ClipService.create()` / `save(_:)` | `ClipCaptureService` |
| `ClipService.isExcludedApplication` | `ExcludedApplicationMatcher` |
| `ClipService` 500 ms poll loop | `ClipboardMonitor` (`WM_CLIPBOARDUPDATE`) |
| `incrementChangeCount()` | `ClipCaptureService.IgnoreCurrentClipboardState()` |
| `PasteboardHistoryRepository` | `SqliteClipStore` |
| `PasteService` | `PasteService` + `Win32ClipboardWriter` |
| `CGEvent` paste synthesis | `InputSender` (`SendInput`) |
| `Accessibility` permission flow | *deleted — Windows needs no permission* |
| *(no counterpart)* | `WindowFocus` — capture and restore the paste target |
| `SQLiteDataSchema.swift` | `ClipDatabase` (schema v4) |
| `TextRecognizer` (Vision) | `WindowsTextRecognizer` (`Windows.Media.Ocr`) + `OcrQueue` |
| `ScreenShotObserver` (Spotlight) | `ScreenshotWatcher` (`FileSystemWatcher`) |
| `PasteboardHistorySearch` FTS5 | `clip_search` FTS5 + `SqliteClipStore.Search` |
| `MenuManager.addHistoryItems` | `MenuBuilder` (model) + `PopupMenuRenderer` (Win32) |
| `MonospacedDigitFormatter`, `trimmedMenuTitle`, `typedTitle` | `ClipTitleFormatter` |
| `NSStatusItem` | `TrayIcon` + `TrayIconGlyph` |
| `NSImage+Resize`, `colorCodeImage` | `ThumbnailRenderer` |
| `AppDelegate` | `TrayApplication` |
| `UserDefaults` / `AppStorageValues` | `AppSettings` (JSON under `%APPDATA%`) |
| `HotKeyService`, Magnet `KeyCombo` | `HotkeyService`, `KeyCombination`, `Win32HotkeyRegistrar` |
| `SnippetRepository` | `SqliteSnippetStore` (schema v3) |
| `SettingsPane` and its eight SwiftUI panes | `SettingsWindow` (WPF, eight tabs) |
| `KeyHolder.RecordView` | `HotkeyRecorder` |
| `SMAppService.mainApp` | `LaunchAtLogin` (HKCU Run key) |
| `NSAlert` with a suppression button | `ConfirmClearHistoryWindow` |
| `Localizable.xcstrings`, `Settings.xcstrings` | `AppLocalization` + `tools/ExtractStrings.ps1` |
| `CPYSnippetsEditorWindowController` | `SnippetEditorWindow` + `SnippetEditorViewModel` (WPF) |
| AEXML snippet import/export | `SnippetXml` |
| `addSnippetItems`, `popUpSnippetFolder` | `MenuBuilder.BuildSnippetSection` / `BuildSnippetFolderMenu` |
| *(no counterpart)* | `DibConverter` — makes clipboard DIBs decodable |

## Layout

```
src/CopyPasta.App/           net9.0-windows — the tray application itself.
src/CopyPasta.Core/          net9.0 — capture, paste and menu rules. No P/Invoke, no I/O.
src/CopyPasta.Data/          net9.0 — SQLite schema and storage.
src/CopyPasta.Interop/       net9.0-windows — all Win32 lives here and nowhere else.
tests/CopyPasta.Core.Tests/  xUnit, fakes only
tests/CopyPasta.Data.Tests/  xUnit against a real in-memory SQLite database
tools/FormatDump/            prints the live clipboard's formats
tools/CaptureProbe/          runs the real pipeline; the Phase 1 and 2 acceptance harness
```

`CopyPasta.Core` targets plain `net9.0` on purpose. The capture rules are the part most likely
to harbour subtle bugs, and keeping them free of Win32 means they can be tested exhaustively
without a clipboard, a message loop, or even Windows. The Win32 side reaches Core through
`IClipboardSource`, `IForegroundApplication` and `IClipStore`.

## Build and test

```bash
dotnet test
```

## Try the pipeline

```bash
dotnet run --project tools/CaptureProbe
```

Copy things and watch what is stored and what is rejected. `--dump` prints the stored history,
`--exclude <name>` excludes an app, `--text-only` narrows the categories, `--max <n>` trims,
`--verbose` also shows the notifications that were collapsed as unchanged.

Two acceptance checks, both automated:

```bash
dotnet run --project tools/CaptureProbe -- --db clips.db --roundtrip
dotnet run --project tools/CaptureProbe -- --paste-test --hold-shift
```

`--roundtrip` replays every stored clip through the real clipboard, reads it straight back and
compares content hashes — a match proves the replay is byte-identical rather than merely
plausible. `--paste-test` creates a real Win32 edit control and drives the whole paste path into
it, then reads back what arrived.

For text recognition, search and the storage cap:

```bash
dotnet run --project src/CopyPasta.App -- --ocr-test invoice.png --search invoice --storage
```

Captures an image through the real pipeline, waits for recognition, then searches for the result
and reports the stored size. Writes `%APPDATA%\CopyPasta\ocr-report.txt`.

To see the settings panes without clicking through them:

```bash
dotnet run --project src/CopyPasta.App -- --render-settings out/
```

Writes one PNG per pane. `PrintWindow` returns a blank client area for WPF content, which composes
through DirectX rather than drawing into the window DC, so rendering the visual tree from inside
the app is the only way to check the layout from a script. `--settings` opens the window normally,
and doubles as the way back in if the tray icon is hidden and no hotkeys are bound.

For snippets:

```bash
dotnet run --project src/CopyPasta.App -- --import-snippets from-mac.xml --export-snippets ours.xml --dump-snippets
```

Runs import and export through the real storage path — the same one the editor uses — and writes
`%APPDATA%\CopyPasta\snippets-report.txt` with every folder, snippet and its content, with line
breaks escaped so a lost carriage return is visible.

For the menu:

```bash
dotnet run --project src/CopyPasta.App -- --dump-menu
```

Writes `%APPDATA%\CopyPasta\menu-dump.txt` showing the menu twice: the model, and the native
HMENU read back out of Win32 with `GetMenuItemCount` / `GetMenuStringW`. `TrackPopupMenuEx` blocks
until a human dismisses it, so without this the translation from model to native menu — ampersand
escaping, submenu nesting, disabled captions, attached bitmaps — would be checkable only by eye.

```bash
dotnet run --project tools/FormatDump -- --watch
```

Prints the raw format list for whatever is on the clipboard — the instrument for turning the
provisional expectations in `CaptureScenarioTests` into verified ones.

## What the Windows platform forced, and what it gave back

### Cheaper than macOS

- **No polling.** `WM_CLIPBOARDUPDATE` replaces the 500 ms `changeCount` timer outright.
  Measured behaviour: one PowerShell copy fires *three* notifications, and the
  sequence-number guard collapses them to a single capture.
- **Exact self-paste suppression.** macOS pre-increments a cached counter and hopes.
  `GetClipboardSequenceNumber()` is the real value, so `IgnoreCurrentClipboardState()` is exact.
- **Better sensitive-content signal.** Password managers already set
  `CanIncludeInClipboardHistory = 0`; no convention has to be evangelised.

### More expensive than macOS

- **Clipboard contention is real.** `OpenClipboard` fails outright when another process holds
  the clipboard. `Win32ClipboardSource` retries 12 times with backoff from 8 ms to 120 ms, and
  gives up quietly. Critically, a failed read does **not** record the sequence number, so the
  clip is retried on the next notification rather than lost.
- **Delayed rendering can fail.** An app may advertise a format and then decline to produce the
  bytes. A missing blob is normal; only a clip where *every* format failed is rejected
  (`NoDataProduced`).
- **`GlobalSize` over-reports.** It returns the allocated block size, which can exceed what the
  app wrote. Since blob bytes feed the content hash, uncompensated slack would make two copies
  of identical text hash differently and defeat duplicate detection entirely. `LogicalLength`
  trims at the terminator for the NUL-terminated text formats; every other format is stored
  exactly as offered.
- **Handle formats are not bytes.** `CF_BITMAP` and friends hand back a GDI handle, so they are
  never read. Windows synthesises `CF_DIB` from them, so nothing is lost.

## Where this port deliberately diverges from macOS

1. **Markers travel out of band.** macOS appends its `concealed` / `universalClipboard` marker
   types to the stored type list. The Windows markers carry a DWORD payload rather than being
   presence-only, so `ClipboardSelection` exposes `IsConcealed` / `IsFromCloudClipboard` as
   properties and the replay layer will re-apply them. Persisting pseudo-assets that are really
   flags would be worse.
2. **Format names are canonicalised.** Win32 compares registered format names
   case-insensitively, but the name's bytes feed the content hash. `ClipboardFormat.FromName`
   normalises the spelling of every known format so `png` and `PNG` are the same clip.
3. **Image preference order is longer.** macOS expresses only "prefer PNG over TIFF". Windows
   needs `PNG > CF_DIBV5 > CF_DIB > TIFF`, because plain `CF_DIB` drops the alpha channel.
4. **Exclusion is checked before the blobs are read.** macOS has already materialised the data
   by the time it checks. Here the check runs inside the format chooser, so an excluded app's
   40 MB screenshot is never copied out of shared memory.
5. **The private-format marker check is guarded.** macOS matches an excluded bundle id against
   any pasteboard type name. Windows format names are short enough that `PNG` would match an
   exclusion for `PNGGleam`, so only app-private names of 6+ characters count as ownership
   markers.
6. **Legacy-format filtering matters more.** Windows *synthesises* `CF_TEXT` and `CF_OEMTEXT`
   from `CF_UNICODETEXT`, so without that rule every text clip would be stored three times over
   — confirmed against a real capture.
7. **`RTFD` is gone**, and **pasteboard item boundaries are gone** (the Windows clipboard is
   flat; multiple files live inside one `CF_HDROP`).
8. **"Last used" ordering is explicit.** macOS refreshes a clip's timestamp as a *side effect* of
   its own clipboard write being re-captured by the monitor. That is fragile: with "allow
   duplicates" off the refresh never happens and the sort quietly stops working, and with
   "overwrite duplicates" off every paste inserts a new row. `IClipStore.Touch` does it directly,
   which is correct under every combination of those settings and skips a write→notify→read→write
   round trip through the OS on every paste.
9. **Plain-text paste falls back instead of clearing.** macOS writes an empty string when the
   user holds the plain-text modifier over an image or file list, silently wiping the clipboard.
   Here the full content is written instead.
10. **Held modifiers are released before the keystroke.** The modifier that triggered the action
    is still physically down when the paste is synthesised, so a naive Ctrl+V arrives as
    Ctrl+Shift+V. `InputSender` releases whatever is held, sends Ctrl+V, then presses the still-held
    keys back down. macOS has the same hazard and does not address it. See the caveat below.
11. **Focus is captured and restored explicitly.** No macOS counterpart at all — see
    `IWindowFocus`. This is the single most failure-prone step in the paste path, and a failed
    restore deliberately downgrades to copy-only rather than sending input somewhere unintended.
12. **The menu is built as data, then rendered.** macOS computes its `NSMenu` in one pass with four
    interleaved counters, indexing submenus back out of the parent by position. Here `MenuBuilder`
    produces a tree and `PopupMenuRenderer` walks it, which is what lets the layout rules be tested
    without a display.
13. **A partial final folder numbered from zero ends on the right entry.** macOS clamps the folder's
    upper bound to the *number* of clips rather than the last clip's *number*, so 25 clips numbered
    from zero produce a folder titled "20 - 25" for entries 20–24. This reports "20 - 24".
14. **The two preview settings are independent.** macOS decides the thumbnail kind at capture time
    and lets a colour swatch overwrite an image unconditionally, so "show images" and "show colour
    previews" are not really separate. Here both are stored and the choice happens at display time.
15. **Numeric accelerators become mnemonics.** `NSMenuItem.keyEquivalent` is a separate field; a
    Win32 menu has no equivalent, so the digit is marked in the title text with an ampersand. Since
    titles already read "3. …" this is invisible — and it is why clipboard text has to be escaped.
16. **Hotkey conflicts are reported.** macOS calls `HotKey.register()` and discards the result, so a
    combination another application already owns silently does nothing and the user cannot tell why.
    `RegisterHotKey` distinguishes ERROR_HOTKEY_ALREADY_REGISTERED from other failures, and each
    outcome is logged per action.
17. **Hotkey defaults are not transliterated.** macOS defaults to ⌘⇧V / ⌘⌃V / ⌘⇧B. The direct
    reading of the first is Ctrl+Shift+V — a bad default here, because a global hotkey intercepts
    the combination before any application sees it and Ctrl+Shift+V is Windows' own "paste without
    formatting". The defaults use Ctrl+Alt, the conventional space for third-party global hotkeys.
18. **A bare key cannot be bound.** Win32 will register one globally, which would swallow that key
    for every application on the machine; `KeyCombination` requires at least one modifier.
19. **Carriage returns are entitised on export.** XML parsers are *required* to normalise a literal
    CRLF to LF, so writing it raw silently converts every snippet to Unix line endings. macOS writes
    it raw and loses CRs on its own round trip; `&#13;` reads back correctly in any conformant
    parser, AEXML included, so this is strictly safer and still compatible.
20. **The export declares the encoding it is actually written in.** `XmlWriter` takes the declared
    encoding from the writer it is handed, and a plain `StringWriter` reports UTF-16 — so the
    document announced `encoding="utf-16"` while being saved as UTF-8 bytes, and anything non-ASCII
    failed to import on the Mac. `SnippetXml` writes through a UTF-8-reporting writer.
21. **Snippets and clips share a database but nothing else.** Clips are captured and continuously
    trimmed; snippets are authored and permanent. Separate tables, separate stores, no cascade
    between them.
22. **The image content type is spelled `Image`, not `TIFF`.** macOS names that settings category
    after the pasteboard flavour it happens to use. Settings are not interchanged between the ports
    — macOS keeps them in a defaults plist, this keeps them in JSON — so there was nothing to gain
    from a name that leaves anyone hand-editing the file wondering why disabling `Image` did nothing.
23. **Settings names drop the legacy indirection.** `AppStorageValues.swift` maps clean Swift names
    onto old `UserDefaults` keys (`inputPasteCommand`, `numberOfItemsPlaceInline`). With no legacy
    to honour, the JSON uses the clean names directly.
24. **Screenshots are found by watching a folder, not by querying an index.** macOS runs an
    `NSMetadataQuery` over Spotlight. Windows has no equivalent index to query, but it does have a
    conventional destination, so `ScreenshotWatcher` watches
    `%USERPROFILE%\Pictures\Screenshots` — where Win+PrtScn and the Snipping Tool both save.
    It also has to wait for the writer to let go of the file, which Spotlight handles for macOS.
25. **Text recognition can be switched off.** macOS always runs Vision. Windows OCR depends on an
    installed language pack — a machine without one has no engine at all — and it costs real CPU on
    a full-screen image, so availability is reported and the feature can be disabled.
26. **Empty and failed recognition are recorded differently.** An empty result means a completed
    scan that found nothing and is stored, so the clip is never re-scanned. A failure is left
    unrecorded so a restart retries it. macOS stores `""` for both.
27. **The history has a byte budget, not just a row count.** macOS trims by row count alone, so
    thirty 4K screenshots are thirty rows and several hundred megabytes. The newest clip is always
    kept, however large — deleting what the user just copied would be worse than briefly exceeding
    the budget.
28. **The search index is maintained by triggers.** Trimming, clearing, and the paste-and-delete
    modifier all remove clips by different routes; a trigger cannot be forgotten by a new one.

## Known gaps

- **Search has no user interface.** The index, the query sanitising and the store API all work and
  are covered by tests, but nothing in the app surfaces them — the tray menu has no search box.
  A filter field over the history menu is the obvious next step, and it needs a custom popup
  rather than a native `HMENU`, which is the same constraint that blocks per-item tooltips.
- **Recognition language follows the user's Windows language list.** A machine with only an English
  pack will not read Japanese text in an image, and the app cannot install packs on the user's
  behalf. The Type pane reports which language is in use.
- **Per-folder snippet hotkeys still need the folder's id.** They work — put
  `"SnippetFolderHotkeys": { "<folder-guid>": "Ctrl+Alt+1" }` in `settings.json` — but neither the
  snippet editor nor the Shortcuts pane offers a recorder for them, so you need the id out of the
  database. `HotkeyRecorder` is built and used by the five built-in shortcuts; wiring it into the
  snippet editor is the remaining piece.
- **The windows have not been driven by hand.** Every pane is rendered and checked, and the view
  models and settings round-trip are covered by tests, but no one has clicked through the settings
  window or the snippet editor — drag-and-drop, the recorder capturing a real keypress, the delete
  confirmation. Those need a human.
- **Some translated strings still read as macOS.** The catalogues carry entries naming Clipy and
  pointing at System Settings; the menu captions are fine, but anything referencing the product
  name or an Apple-specific path needs rewording before release. The English quit caption is
  already overridden for exactly this reason.
- **The editor runs on the message-loop thread.** It is shown non-modally on purpose: a modal loop
  would stop the clipboard monitor from seeing anything while the editor was open. Worth keeping in
  mind if the editor ever grows a blocking dialog of its own.
- **Win32 menus have no per-item tooltips.** macOS sets `NSMenuItem.toolTip` and gets a hover
  preview for free. There is no HMENU equivalent short of owner-drawing the menu and hosting a
  tooltip window, so nothing displays one today. The model still carries `ClipMenuItem.ToolTip`,
  computed and tested, so a future custom renderer can use it. This is a real parity gap: with
  titles trimmed to ~20 characters, the tooltip is where the rest of the text lived.
- **The tray icon is a drawn placeholder.** `TrayIconGlyph` renders it at runtime rather than
  shipping an asset, because Clipy's icons are copyrighted separately from its MIT code grant. It
  needs real artwork before any release.
- **The tray icon was not visually confirmed.** `Shell_NotifyIcon(NIM_ADD)` returning success is
  good evidence the shell accepted it, but screenshotting the notification area did not work in
  this environment — the capture kept landing on the wrong surface. Worth thirty seconds of
  looking at the tray with the app running.
- **The modifier neutralisation is reasoned, not verified.** `--paste-test --hold-shift` passes,
  but its own control experiment shows that a classic Win32 edit control accepts Ctrl+Shift+V
  anyway — so that test does not prove the neutralisation is necessary. It rests on the fact that
  browsers, editors and terminals *do* bind Ctrl+Shift+V (usually to "paste without formatting").
  Worth confirming against one of those before trusting it.
- **`SendInput` cannot reach a higher-integrity process.** Pasting into an elevated application
  from a non-elevated CopyPasta silently does nothing, and `SendInput` still reports success, so
  `InputRejected` will not fire for it. This is UIPI and it is the practical Windows analogue of
  the macOS Accessibility requirement — not something the permission-free `SendInput` path avoids.
  Untested here; it needs an elevated target.
- `ClipboardFormat.FromCloudClipboard` is marked **UNVERIFIED** in code. The two
  `CanInclude…`/`CanUpload…` markers are documented by Microsoft; the marker for an *incoming*
  cloud clip is not. Needs a real cross-device paste to confirm. The filter degrades safely to
  "treat as local" if the name is wrong.
- The sensitive-clip and excluded-application paths are covered by unit tests but have not been
  exercised against a real password manager. Worth doing with `CaptureProbe --ignore-secrets`.
- `CaptureScenarioTests` has three verified captures (text, files, image) and seven still
  hand-authored — Word, Chrome, Explorer, Excel and the rest.
- No byte-budget trimming yet (`TotalAssetBytes` exists; the policy does not). Phase 7.
- `ClipboardMonitor` must be created on a thread with a message loop, and used only from that
  thread. Phase 3 needs to respect this when the tray UI arrives.
