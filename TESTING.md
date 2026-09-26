# Testing CopyPasta 1.0.0

The installer is at **`artifacts/releases/CopyPasta-win-Setup.exe`**.

It is already installed on this machine and running. To rebuild it:

```powershell
pwsh build-installer.ps1
```

---

## What to expect

| | |
|---|---|
| **Installs to** | `%LOCALAPPDATA%\CopyPasta` — per-user, **no admin prompt** |
| **Your data** | `%APPDATA%\CopyPasta\` — `clips.db`, `settings.json`, `copypasta.log` |
| **Shortcuts** | Start Menu and Desktop |
| **Uninstall** | Settings → Apps, or `%LOCALAPPDATA%\CopyPasta\Update.exe --uninstall` |
| **Runtime** | Self-contained; no .NET install needed |

Released at https://github.com/acidgrafix-code/CopyPasta/releases/tag/v1.0.0, which is also the
feed the app updates itself from.

**It is unsigned**, so downloading it elsewhere will show "Windows protected your PC". Click *More
info → Run anyway*. That disappears once a code-signing certificate is added — no code change
needed.

---

## Default shortcuts

| Keys | Does |
|---|---|
| **Ctrl+Alt+V** | Full menu — history, snippets, commands |
| **Ctrl+Alt+H** | History only |
| **Ctrl+Alt+B** | Snippets only |
| Click the tray icon | Same as Ctrl+Alt+V |

Hold a modifier while choosing a history item to change what happens — **Shift** pastes as plain
text by default. The other two modifier actions are off until you enable them in Settings →
Shortcuts.

---

## A 10-minute pass

### The basics
1. Copy some text in any app. Press **Ctrl+Alt+V** — it should be at the top of the menu.
2. Pick it. It should paste into wherever you were typing.
3. Copy an image (right-click → Copy in a browser). It appears as **(Image)** with a thumbnail.
4. Copy a file in Explorer. It appears as **(Files)**.
5. Copy `#3366ff` as text. A colour swatch appears beside it.

### Things most likely to be wrong
6. **Focus restore.** Paste into several different apps — Notepad, a browser address bar, a
   terminal. This is the most failure-prone part of the design; if a paste ever lands in the wrong
   place or nowhere, that is the bug worth reporting.
7. **Rich text.** Copy formatted text from Word or a browser, paste it back — formatting should
   survive. Then hold **Shift** while choosing it; that one should paste unformatted.
8. **Multiple files.** Copy several files at once, paste into another Explorer window.
9. **High DPI.** If you have a second monitor at a different scaling, open the menu on both. The
   tray icon and the thumbnails should both stay sharp.

### Snippets
10. Tray menu → **Edit Snippets**. Add a folder, add a snippet, type a body.
11. Drag a snippet to reorder it, then drag one between folders.
12. **Export…**, then **Import…** the file back — you should get a duplicate set, unchanged.
13. Close the editor and press **Ctrl+Alt+B**. Your snippet should be there; choosing it pastes the
    body.

### Search and OCR
14. Screenshot something with text in it (Win+Shift+S, then paste, or enable screenshot watching in
    Settings → Type).
15. Give it a few seconds, then check `%APPDATA%\CopyPasta\copypasta.log` for a
    "recognised N characters" line.
16. *There is no search box yet* — the index works and is tested, but nothing surfaces it. See
    Known gaps.

### Settings
17. Tray menu → **Settings…**. Every change applies immediately; there is no OK button.
18. Try **Menu → Items shown directly in the menu**. Set it to 10 and reopen the menu — the first
    ten entries should now be inline rather than in folders.
19. Try **General → Tray icon → Dark**. The glyph should swap immediately.
20. Set a shortcut in **Shortcuts**. If you pick one another app already owns, the log will say
    so rather than failing silently.

### Updates
21. Tray menu → **Check for Updates…**, then look in the log for an `updates:` line. On 1.0.0 it
    should say `1.0.0 is current` — that means it reached the GitHub release feed and compared
    versions.
22. Settings → **Updates** shows the same thing with a *Check Now* button and the last check time.
23. A newer release is downloaded in the background and applied the next time you quit and reopen
    CopyPasta. It will not restart itself while you are using it.

### Lifecycle
24. Quit from the tray menu. Relaunch from the Start Menu — your history should still be there.
25. Turn on **General → Start CopyPasta when I sign in**, then check Task Manager → Startup.

---

## Known gaps in this build

These are known and deliberate, not things to report:

- **No search UI.** The full-text index works and is tested; nothing exposes it yet.
- **No menu tooltips.** Win32 menus cannot show per-item tooltips, so long clips are truncated in
  the title with no hover preview. Needs a custom popup to fix.
- **Per-folder snippet hotkeys** need the folder's id hand-written into `settings.json`; the
  editor has no recorder for them yet.
- **Unsigned.** See above.
- **No German/Japanese/etc. Settings window.** Only the tray menu and the clear-history dialog are
  translated; the Settings and snippet windows are English in every language.

---

## Uninstalling

Settings → Apps → CopyPasta, or `%LOCALAPPDATA%\CopyPasta\Update.exe --uninstall`.

**Uninstalling does not remove your data.** `%APPDATA%\CopyPasta\` — history, snippets, settings —
survives, so reinstalling picks up where you left off. Delete that folder by hand if you want a
genuinely clean slate.

## If an icon looks wrong

The icon is embedded in `CopyPasta.exe` and the shortcuts point at it, so a wrong-looking icon is
almost always a shell cache rather than a bad build. Windows keeps **two separate caches**, and
they are cleared differently.

**Explorer** (desktop, folders, taskbar) caches shortcut icons by path and does not always notice
that a newly installed target supplies one. The installer nudges it on your behalf, but if you ever
see a stale icon:

```powershell
ie4uinit.exe -show
```

**The Start menu** keeps its own cache that `ie4uinit` does not touch, so a tile pinned while the
Explorer cache was stale can stay blank afterwards. Restart the Start menu, then unpin and re-pin:

```powershell
Stop-Process -Name StartMenuExperienceHost -Force
```

It relaunches by itself within a second or two.

Signing out and back in clears both.

## If something goes wrong

`%APPDATA%\CopyPasta\copypasta.log` records startup, every capture, every paste outcome, hotkey
registration and any crash. It is the first place to look, and the most useful thing to attach to
a bug report.

To start completely fresh, quit the app and delete `%APPDATA%\CopyPasta\`. It will be recreated
with defaults.
