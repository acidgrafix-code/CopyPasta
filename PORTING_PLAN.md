# Clipy → Windows: Code Review & Porting Plan

Source reviewed: `Clipy-develop/` (v1.3.0, ~6,600 lines of Swift).
This is the **modern rewrite** of Clipy — SwiftUI settings, SQLite (via pointfree's
`sqlite-data`/GRDB) storage, `swift-dependencies` DI, Combine bindings. The older
Realm + RxSwift version survives only as migration code.

---

## 1. What the app actually is

A tray-only (`LSUIElement`) clipboard manager with two halves:

**Clipboard history** — a 500 ms timer polls `NSPasteboard.changeCount`; on change it
snapshots every enabled pasteboard flavor into SQLite as a row + N blob rows, derives a
thumbnail, and runs OCR on images. The tray menu is rebuilt from the DB on every change.
Selecting an item writes the blobs back to the pasteboard and synthesizes ⌘V.

**Snippets** — user-authored text organized into folders, each folder optionally bound to
its own global hotkey, editable in an outline-view window, importable/exportable as XML.

### Module map

| Area | Files | Role |
|---|---|---|
| Lifecycle | `AppDelegate.swift`, `ClipyApp.swift` | wiring, login item, screenshot observer |
| Capture | `Services/ClipService.swift` | poll loop, dedup, exclusions, trimming, periodic wipe |
| Replay | `Services/PasteService.swift` | write to pasteboard, synthesize paste, modifier actions |
| Hotkeys | `Services/HotKeyService.swift` | 5 global hotkeys + 1 per snippet folder |
| UI | `Managers/MenuManager.swift` | tray icon + menu tree construction (413 lines, the densest file) |
| Data | `Database/SQLiteDataSchema.swift`, `Repositories/*` | 8 tables incl. 2 FTS5, repo layer |
| Model | `Models/PasteboardContent.swift`, `PasteboardAvailableType.swift` | flavor filtering + hashing |
| OCR | `OCR/TextRecognizer.swift` | Vision framework, 10k char cap |
| Snippets UI | `Snippets/CPYSnippetsEditorWindowController.swift` | 512 lines, NSOutlineView + XIB |
| Settings | `Settings/*` | 8 SwiftUI panes |
| Migration | `Migration/*`, `Database/Migrations/*` | Realm → SQLite, V1–V4 |

### Design decisions worth preserving verbatim

These are the non-obvious parts where the macOS code encodes hard-won behavior. Port the
*logic*, not just the API calls.

1. **Content-hash identity.** `PasteboardContent.hash` is SHA-256 over length-prefixed
   `(type, data)` pairs, and that hash *is* the primary key (`PasteboardHistory.ID`).
   Dedup, "overwrite same history", and asset-reuse on re-copy all fall out of this for
   free. The length prefix prevents concatenation collisions — keep it.
2. **Flavor filtering** (`PasteboardAvailableType.availableTypes`) is the subtlest code in
   the app. It drops transient/concealed clips, prefers modern over deprecated flavors,
   prefers PNG over TIFF, and refuses to store Universal Clipboard file URLs because the
   sandbox extension will not survive. Each rule has a Windows analogue (§3).
3. **Excluded-app matching** checks the frontmost app's bundle ID *and* whether any
   pasteboard type is prefixed by an excluded ID — because menu-bar apps and extensions
   write to the clipboard without taking focus. Windows needs the same two-pronged check.
4. **Self-paste suppression.** `incrementChangeCount()` pre-bumps the cached counter so the
   app's own write is not re-captured. Direct analogue on Windows: record
   `GetClipboardSequenceNumber()` after writing.
5. **Blank-text rejection** only applies when the *primary* flavor is text — an image whose
   OCR text is empty still gets stored.
6. **Two sort orders** (`createdAt` vs `updateAt`) thread through the menu builder, the
   trimmer, and the settings pane. Trimming by the wrong column silently deletes the wrong
   clips.

### Code-review observations

- **Clean, worth imitating.** Repository interfaces are protocol-first with DI keys, so
  every service is testable. The schema is small and normalized. `withErrorReporting` wraps
  every DB call — no unguarded throws.
- **Polling is a design smell that Windows fixes for free.** The 500 ms timer costs a wakeup
  172,800 times a day. Windows has `WM_CLIPBOARDUPDATE`; go event-driven and this whole
  subsystem becomes cheaper and lower-latency than the original.
- **`MenuManager.addHistoryItems` is hard to follow.** The interleaved `i`, `listNumber`,
  `subMenuCount`, `subMenuIndex` counters implement "first N inline, rest in folders of M",
  and it indexes back into the menu by position (`menu.item(at: subMenuIndex)`).
  Reimplement this as an explicit chunking pass that builds a tree, then render the tree.
  Do not transliterate it.
- **Unbounded blob growth.** Every flavor of every clip is stored — a copied 4K screenshot
  can land as PNG + TIFF + PDF. Trimming is by row count only, never by bytes. Add a
  byte-budget setting on Windows; it is a real quality-of-life improvement.
- **Thumbnail asset logic has a latent bug** worth not copying:
  `PasteboardHistoryRepository.thumbnailAsset` assigns `asset` for an image, then
  *unconditionally reassigns* if a color code also parses, so the color swatch always wins
  even when the user has color previews disabled and image previews enabled. Decide the
  kind from settings, or store both kinds.
- **Settings keys are legacy-named.** `AppStorageValues.swift` maps clean Swift names onto
  old `UserDefaults` keys (`inputPasteCommand`, `numberOfItemsPlaceInline`). On Windows you
  have no legacy to honor — use the clean names and skip the indirection.

---

## 2. Legal / naming constraints — read before naming anything

MIT licensed, so porting is fine, but the README states two conditions and the icons carry
a separate reservation:

- **Do not ship it as "Clipy" or "ClipMenu."** The README explicitly asks derived works not
  to use those product names. Pick your own name; keep "a Windows port of Clipy" as
  attribution prose, not as the product name.
- **Do not reuse the icon PNGs.** "Icons are copyrighted by their respective authors" —
  that covers `Assets.xcassets` (app icon, tray icons, snippet-editor toolbar icons). You
  need new artwork. The MIT grant covers the code, not the art.
- **Keep the MIT notice** for both `LICENSE` and `LICENSE_CLIPMENU` (Clipy itself derives
  from ClipMenu, so that second notice travels with the code).
- Drop the Sparkle DSA/EdDSA keys and `SUFeedURL` — those are the maintainer's signing
  identity and update channel, not yours.
- `GoogleService-Info.plist` is absent (gitignored). Firebase Analytics/Crashlytics is
  optional; recommend shipping with no telemetry at all, or opt-in only.

**Translations are the one asset you should reuse.** `Localizable.xcstrings` and
`Settings.xcstrings` carry en/ja/de/it/pt-BR/uk/zh-Hans. Those are MIT code resources —
extract them to `.resx`/JSON and you start localized in 7 languages.

---

## 3. Platform mapping

The central question for every subsystem: does Windows have an equivalent, and is it better
or worse than what macOS gave us?

| Concern | macOS (current) | Windows | Verdict |
|---|---|---|---|
| Change detection | 500 ms `changeCount` poll | `AddClipboardFormatListener` → `WM_CLIPBOARDUPDATE`; `GetClipboardSequenceNumber()` as the counter | **Better** — event-driven, no poll |
| Read/write | `NSPasteboard` | `OpenClipboard`/`GetClipboardData`; **must retry with backoff** — another process can hold the clipboard and `OpenClipboard` will fail | **Worse** — needs retry + timeout logic |
| Synthesized paste | `CGEvent` ⌘V, **requires Accessibility permission** | `SendInput` Ctrl+V, no permission needed | **Better** — an entire permission flow, alert, and settings deep-link all delete |
| Restoring focus | menu closes, focus returns naturally | must capture `GetForegroundWindow()` *before* showing the menu and `SetForegroundWindow()` before `SendInput` | **Worse** — new code, and the #1 source of "paste went nowhere" bugs |
| Global hotkeys | Carbon via Magnet | `RegisterHotKey` (simple, detects conflicts) or WH_KEYBOARD_LL hook (flexible, needs care) | Even — start with `RegisterHotKey` |
| Tray + menu | `NSStatusItem` + `NSMenu` | `Shell_NotifyIcon` + `TrackPopupMenuEx`; supports submenus, bitmaps, accelerator chars | Even |
| Modifier-at-click | `NSEvent.modifierFlags` | `GetAsyncKeyState` at click time | Even |
| OCR | Vision | `Windows.Media.Ocr` (built in, free) or Windows AI text recognition on Copilot+ | Even |
| Screenshot watch | `NSMetadataQuery` | `FileSystemWatcher` on `%USERPROFILE%\Pictures\Screenshots` | Even |
| Launch at login | `SMAppService` | HKCU `...\Run` key, or `StartupTask` if MSIX-packaged | Even |
| Updates | Sparkle | **Velopack** (recommended), or WinSparkle to keep appcast XML | Even |
| Settings storage | `UserDefaults` | JSON under `%APPDATA%\<AppName>\settings.json` | Even |
| Sensitive-clip opt-out | `org.nspasteboard.ConcealedType` | `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`, `ClipboardViewerIgnore` | **Better** — password managers already set these |
| Cross-device clips | Universal Clipboard | Cloud Clipboard (`CanUploadToCloudClipboard`) | Even |

### Clipboard format mapping

| `PasteboardAvailableType` | Windows format |
|---|---|
| `string` | `CF_UNICODETEXT` |
| `rtf` | registered `"Rich Text Format"` |
| `rtfd` | **no equivalent** — drop, or degrade to RTF |
| `html` | registered `"HTML Format"` (CF_HTML — note its byte-offset header) |
| `pdf` | no standard flavor; handle via `CF_HDROP` when a PDF file is copied |
| `filenames` | `CF_HDROP` |
| `url` | `"UniformResourceLocatorW"` (+ `"text/x-moz-url"` for browsers) |
| `tiff`/`png` | `CF_DIBV5` / `CF_DIB`, plus registered `"PNG"` — **prefer PNG** (mirrors the existing `isCovered(by:)` rule and avoids DIB alpha loss) |

**New Windows-only concern:** Windows 10+ has built-in clipboard history (Win+V). Mark your
own writes so you do not feed it duplicates, and consider a setting to suppress the built-in
history while yours is running.

---

## 4. Recommended stack

**C# / .NET 9 + WPF.** Rationale:

- Best-in-class Win32 interop for the parts that are unavoidably Win32 (clipboard formats,
  `RegisterHotKey`, `TrackPopupMenuEx`, `SendInput`, `Shell_NotifyIcon`).
- WinRT OCR reachable via CsWinRT with no extra dependency.
- `Microsoft.Data.Sqlite` with FTS5 — the existing schema ports nearly verbatim.
- Self-contained single-file publish; Velopack for updates.
- WPF `TreeView` with drag/drop maps cleanly onto the `NSOutlineView` snippet editor.

Alternatives considered: **C++/Win32** — smallest and fastest, but 3–4× the development
time and you hand-roll OCR interop. **Rust + Tauri** — weak native tray/menu story, which is
the entire UI here. **Electron** — 100 MB+ resident for a tray utility that must stay
invisible; wrong tool for this job.

Use WinUI 3 instead of WPF only if you specifically want Fluent/Mica styling and accept
heavier packaging.

### Project layout

```
src/
  App/              tray icon, menu builder, lifecycle, settings window
  Core/             ClipService, PasteService, HotKeyService (platform-agnostic logic)
  Interop/          Win32 P/Invoke: clipboard, hotkeys, SendInput, tray, menus
  Data/             SQLite schema, migrations, repositories
  Ocr/              WinRT OCR wrapper
  Snippets/         editor window, XML import/export
tests/
```

Keep `Core/` free of P/Invoke behind interfaces (`IClipboard`, `IHotKeys`, `IInputSender`) —
that is what makes the capture/dedup rules testable without a message loop, and it mirrors
the DI discipline the Swift code already has.

---

## 5. Phased plan

Each phase ends at something runnable. Estimates assume one developer familiar with .NET but
new to this codebase.

### Phase 0 — Foundations (2–3 days)
Solution scaffold, tray icon that appears and quits cleanly, settings JSON load/save,
logging. Hidden message-only window to receive `WM_CLIPBOARDUPDATE`.
**Done when:** the app sits in the tray, right-click → Quit works, settings round-trip.

### Phase 1 — Capture pipeline (1 week)
`WM_CLIPBOARDUPDATE` → enumerate formats → filter (port `availableTypes` rule-for-rule) →
build content with SHA-256 length-prefixed hash → SQLite. Clipboard open/read with retry and
backoff. Sequence-number self-paste suppression.
**Done when:** copying in Notepad, Word, Explorer, and a browser produces correct rows;
copying from a password manager produces none.
**Unit-test this phase hard** — it is where the subtle bugs live, and it needs no UI.

### Phase 2 — Replay + paste (3–4 days)
Write blobs back to all stored formats. Capture foreground window before the menu opens;
restore it and `SendInput` Ctrl+V. Plain-text paste, delete-on-modifier, and
paste-and-delete via `GetAsyncKeyState`.
**Done when:** a round-trip of RTF, an image, and a multi-file selection all paste
byte-identical into their native apps.

### Phase 3 — Tray menu (1 week)
Menu tree from the DB: history section, inline/folder chunking, numeric marks, numeric
accelerators, tooltips, thumbnails as owner-draw bitmaps, color swatches.
**Rewrite the chunking as a tree-building pass, not a positional-index transliteration.**
**Done when:** the menu matches macOS Clipy's structure at inline limits of 0, 10, and 30.

### Phase 4 — Hotkeys (2–3 days)
`RegisterHotKey` for main/history/snippet/edit-snippets/clear-history. Conflict detection and
a clear error when a combo is already taken system-wide. Per-folder snippet hotkeys.
**Done when:** every hotkey pops its menu at the cursor, and taken combos are reported rather
than silently dropped.

### Phase 5 — Snippets (1 week)
Folders/snippets CRUD, enable toggles, reorder by drag, move between folders. XML
import/export using the **exact** `folders/folder/{title,snippets/snippet/{title,content}}`
shape so snippet files interchange with macOS Clipy — a genuine cross-platform win.
**Done when:** a file exported from macOS Clipy imports losslessly, and vice versa.

### Phase 6 — Settings UI (1 week)
Eight panes mirroring the originals: General, Menu, Clipboard Type, Excluded Applications,
Shortcuts, Updates, Beta, About/Donate. Excluded apps by process/executable path rather than
bundle ID; keep the "marker format" prefix check from §1.3.
Extract the 7 `.xcstrings` locales into resources here.
**Done when:** every setting in `AppStorageValues.swift` has a Windows counterpart or a
documented reason for omission.

### Phase 7 — OCR, screenshots, trimming (3–4 days)
WinRT OCR on a background queue with the 10k cap. `FileSystemWatcher` on the Screenshots
folder. Row-count trimming *plus* the new byte-budget cap. Periodic + on-quit wipe.
**Done when:** OCR text is searchable via FTS5 and the DB stops growing at the cap.

### Phase 8 — Polish and ship (1 week)
Launch at login, Velopack updates, installer, high-DPI and multi-monitor menu placement,
crash handling. Icon set (new artwork — see §2).
**Done when:** a clean Windows 11 VM installs, runs, updates, and uninstalls cleanly.

**Total: roughly 6–8 weeks** for full parity.

### Deliberately deferred / dropped

- Realm→SQLite migration (`Migration/*`, `Database/Migrations/*`) — no legacy Windows
  install exists. Skip entirely; that is ~500 lines you do not write.
- `rtfd` flavor — no Windows equivalent.
- Firebase — recommend omitting.
- Sparkle DSA keys, `SUFeedURL`, code-signing xcconfigs.

### Risk register

| Risk | Mitigation |
|---|---|
| Clipboard contention (`OpenClipboard` fails) | Retry with backoff + timeout; never block the UI thread. Non-negotiable — it *will* happen in practice |
| Focus restoration fails, paste goes nowhere | Capture foreground HWND before menu display; `AllowSetForegroundWindow`; fall back to copy-only with a toast |
| CF_HTML byte-offset header written wrong | Round-trip tests against Word, Chrome, and Outlook specifically |
| Owner-draw menu bitmaps at mixed DPI | Test at 100/150/200% with two monitors early, not in Phase 8 |
| Windows' own clipboard history interferes | Mark own writes; document the interaction |

---

## 6. First concrete step

Before writing UI code, port `PasteboardAvailableType.availableTypes` and
`PasteboardContent`'s hashing to C# with unit tests, driven by captured real-world format
lists (Word, Chrome, Explorer, a password manager, Paint). Those two functions are the app's
actual intelligence — everything else is plumbing around them. Getting them right first
means Phases 1–2 have a correctness oracle instead of manual poking.
