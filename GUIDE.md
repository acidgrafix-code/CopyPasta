# CopyPasta — User Guide

**Version 1.0.0**

---

## What it is

Windows remembers exactly one thing you copied. CopyPasta remembers all of them.

It sits in your notification area and quietly records everything that goes onto the clipboard —
text, formatted text, images, files, colour codes. Press a hotkey, pick something from the list,
and it goes straight into whatever you were typing in. Nothing leaves your machine.

Alongside the history it keeps **snippets**: text you reuse often — signatures, addresses,
boilerplate replies, code fragments — organised into folders and always two keystrokes away.

CopyPasta is a Windows port of [Clipy](https://clipy-app.com/), a long-running macOS clipboard
manager.

---

## Installing

Download **`CopyPasta-win-Setup.exe`** from the
[releases page](https://github.com/acidgrafix-code/CopyPasta/releases) and run it.

It installs just for you, into `%LOCALAPPDATA%\CopyPasta`, and never asks for administrator
rights. No .NET runtime needed — everything is included.

> **"Windows protected your PC"**
> This build is not code-signed yet, so Windows will warn you the first time. Click **More info**,
> then **Run anyway**. It only happens once.

Once it is running you will see the CopyPasta icon in your notification area, near the clock. If
you cannot see it, click the **^** arrow — Windows hides new tray icons by default. Drag it out of
that overflow to keep it visible.

---

## The first two minutes

1. Copy some text from anywhere — a browser, a document, an email.
2. Press **`Ctrl` + `Alt` + `V`**.
3. Your text is at the top of the menu. Click it.

It pastes into wherever your cursor was. That is the whole idea; everything below is refinement.

---

## The menus

There are three, and they all do the same kind of thing at different scopes.

| Shortcut | Opens |
|---|---|
| **`Ctrl` `Alt` `V`** | Everything — history, snippets and commands |
| **`Ctrl` `Alt` `H`** | History only |
| **`Ctrl` `Alt` `B`** | Snippets only |
| **Click the tray icon** | Same as `Ctrl` `Alt` `V` |

### Reading the menu

Recent items are listed directly. Once the list gets past a set length, older entries are tucked
into numbered folders — `11 - 20`, `21 - 30` and so on — so the menu never runs off the screen.

Items are labelled by what they are:

- Plain text shows the first part of the text itself.
- **(Image)** entries show a small preview beside them.
- **(Files)** entries are files or folders you copied in Explorer.
- Colour codes such as `#3366ff` get a swatch of that colour. `#abc`, `#3366ff` and `#3366ffcc`
  all work — three, six or eight hex digits, with or without the `#`.

### Choosing by number

The first ten items can be picked by pressing their number instead of clicking, if you turn that
on in **Settings → Menu → Let the first ten items be chosen by number**. With the menu open, press
`1` for the first item, `2` for the second, and so on.

---

## Paste modifiers

Hold a key **while choosing** a history item to change what happens to it.

| Hold | Does | On by default |
|---|---|---|
| **`Shift`** | Pastes as plain text, stripping fonts, colours and links | Yes |
| **`Ctrl`** | Deletes the item without pasting it | No |
| **`Alt`** | Pastes it, then removes it from the history | No |

`Shift` is the useful one day to day: copy something out of a web page or Word, hold `Shift` while
picking it, and it arrives as clean unformatted text instead of dragging the original styling with
it.

Turn the other two on in **Settings → Shortcuts → Paste modifiers**, where you can also change
which key does what.

> If you hold both the delete key and the paste-and-delete key at once, the item is pasted and then
> deleted — the more specific action wins rather than the item silently vanishing.

---

## Snippets

Snippets are text you keep on hand permanently. Unlike history, they never expire and are never
cleared.

### Creating them

Open the tray menu, then **Edit Snippets…**

- **New Folder** creates a category — *Signatures*, *Addresses*, *Code*, whatever suits.
- **New Snippet** adds an entry inside the selected folder. Give it a **Title** (what you will see
  in the menu) and a **Content** (what actually gets pasted).
- **Drag** entries to reorder them, or drag a snippet from one folder into another.
- **Enable/Disable** hides a folder or snippet from the menu without deleting it — handy for
  seasonal things.
- **Delete** removes the selected item.

Everything saves as you type. There is no Save button and no way to lose work by closing the
window.

### Using them

Press **`Ctrl` + `Alt` + `B`**, or open the full menu and look under **Snippet**. Folders appear as
submenus; choosing a snippet pastes its content.

### Moving them between machines

**Export…** writes all your folders and snippets to an XML file. **Import…** reads one back,
adding to what is already there rather than replacing it. The format is compatible with the macOS
Clipy app, so snippets travel between the two.

---

## Screenshots and text in images

Two related features, both under **Settings → Type**.

**Save screenshots to the history as they are taken** watches your `Pictures\Screenshots` folder
and adds new screenshots to the history automatically, so a `Win` + `PrtScn` capture is waiting in
the menu without you copying anything.

**Read text in images so they can be searched** runs images through the OCR engine built into
Windows and stores whatever text it finds. This makes the words inside a screenshot searchable
rather than the image being an opaque blob.

The pane tells you which OCR language is in use, or says so plainly if no language pack is
installed on your machine.

> **Note:** the search index is built and working, but there is no search box in this version yet.
> The text is being captured for when there is one.

---

## Keeping things out of the history

A clipboard manager sees everything you copy, which is not always what you want.

### By kind of content

**Settings → Type** lists each kind — Plain text, Rich text (RTF), HTML, PDF, Files, URLs, Images.
Untick anything you would rather not keep. Turning off Images, for instance, keeps the database
small if you mostly copy text.

### Passwords and sensitive content

**Skip content marked as sensitive (passwords)** respects the flag that password managers and some
banking sites set on the clipboard to say *do not record this*. Worth turning on if you use a
password manager.

**Skip content copied on another device** ignores clipboard content arriving from a phone or
another PC through Windows' cloud clipboard.

### Per application

**Settings → Exclude → Add App…** and pick an executable. Nothing copied while that app is in the
foreground will be saved. Use it for password managers, banking apps, or anything handling
information you would rather not have sitting in a list.

### Duplicates

By default, copying something you already have moves the existing entry back to the top instead of
creating a second copy. Both behaviours are adjustable under **Settings → Type → Duplicates**.

---

## Managing how much is kept

**Settings → General → History limit** sets how many items are kept. The default is 30.

**Settings → Type → Storage budget** sets a size cap in megabytes — 256 MB by default. Once the
stored history exceeds it, the oldest entries are dropped. This matters because a count alone tells
you nothing about size: one clip might be a line of text and the next a full-screen screenshot.
Set it to `0` for no limit.

### Clearing it

- **Clear History** in the tray menu empties it now. You will be asked to confirm, unless you turn
  that off.
- **Clear history when CopyPasta quits** empties it every time you close the app.
- **Clear history periodically** empties it on a schedule — every 5 minutes through to every
  7 days.

**Snippets are never affected by any of these.**

---

## Changing the shortcuts

**Settings → Shortcuts.** Click a shortcut box and press the combination you want. `Esc` cancels,
`Backspace` clears it.

Five actions can be bound: the three menus, plus **Edit snippets** and **Clear history** — the last
two are unbound by default.

If you choose a combination another program has already claimed, CopyPasta tells you rather than
failing silently.

> **Why `Ctrl` `Alt` and not `Ctrl` `Shift`?** A global hotkey intercepts the keys before any
> application sees them. `Ctrl` `Shift` `V` is Windows' own shortcut for "paste without
> formatting", and taking it over would break that everywhere. `Ctrl` `Alt` is the conventional
> space for third-party global hotkeys.

---

## Appearance

**Settings → General → Tray icon** offers **Light** (for a dark taskbar), **Dark** (for a light
taskbar), or **Hidden**.

Windows has no equivalent of the template images macOS uses, so the icon cannot invert itself to
match your theme — pick the one that suits your taskbar.

> Hiding the tray icon means the hotkeys are your only way in. If you hide it and then clear your
> shortcuts, launch `CopyPasta.exe --settings` to get back.

**Settings → Menu** controls the rest: how many items appear directly, how many go in each folder,
how long titles can be before they are truncated, and the size of image previews.

---

## Updates

**Settings → Updates.**

CopyPasta checks the GitHub releases page once a day by default, shortly after it starts. You can
change that to hourly, weekly or monthly, or turn automatic checking off entirely and use
**Check Now** — or **Check for Updates…** in the tray menu — when you feel like it.

When a new version is found it downloads in the background and is applied **the next time you quit
and reopen CopyPasta**. It will not restart itself while you are in the middle of something.

---

## Your data

Everything lives in **`%APPDATA%\CopyPasta\`**. Paste that into Explorer's address bar to get
there.

| File | What it is |
|---|---|
| `clips.db` | Your history and snippets |
| `settings.json` | Your preferences |
| `copypasta.log` | A record of what the app has been doing |

Nothing is sent anywhere. There is no account, no sync and no telemetry.

**Uninstalling does not delete this folder**, so reinstalling picks up exactly where you left off.
To start genuinely fresh, quit CopyPasta and delete the folder — it is recreated with defaults.

To back up or move to another machine, copy `clips.db` and `settings.json` across while CopyPasta
is closed.

---

## If something goes wrong

**A paste went to the wrong place.** CopyPasta restores focus to whatever window you were in before
the menu opened. Some applications — elevated ones especially — resist this. Windows does not allow
a normal program to send keystrokes to a program running as administrator; that is a security
boundary rather than a bug.

**A hotkey does nothing.** Another program has claimed it. Check `copypasta.log` for a line about
registration, and pick a different combination.

**A copied image did not appear.** Check that Images is ticked in **Settings → Type**, and that
your storage budget has not been reached.

**The desktop or Start icon looks wrong.** Windows caches shortcut icons and does not always notice
a newly installed one. Run `ie4uinit.exe -show` in PowerShell for Explorer's cache. For a Start
menu tile, unpin and re-pin it. Signing out and back in clears both.

**Nothing is being recorded.** Confirm the app is running — look for the tray icon — and check it
is not in your own exclusion list.

`copypasta.log` records startup, every capture, every paste outcome, hotkey registration and any
crash. It is the first place to look and the most useful thing to attach to a bug report.

---

## Not in this version yet

Known and deliberate, so you are not hunting for them:

- **No search box.** The full-text index works and your clips — including text read out of images
  — are indexed. Nothing surfaces it yet.
- **No tooltips on menu items.** Windows menus cannot show them, so a long clip is truncated in the
  title with no way to hover for the rest. The setting exists but has no effect.
- **Per-folder snippet hotkeys** cannot be set from the editor; they need the folder's id written
  into `settings.json` by hand.
- **The Settings and snippet windows are English only.** The tray menu is translated into all seven
  languages; those two windows are not.
- **The installer is unsigned.** See [Installing](#installing).

---

## Credits

CopyPasta is a Windows port of [Clipy](https://github.com/Clipy/Clipy) (© 2015-2026 Clipy Project),
which is itself derived from [ClipMenu](https://github.com/naotaka/ClipMenu) (© 2010 naotaka). Both
are MIT licensed, and CopyPasta's translations come from Clipy's string catalogues.

Clipy's icons are copyrighted separately from its code and are not used here — CopyPasta's artwork
is its own.

CopyPasta is © 2026 ACIDgrafix, LLC, and is MIT licensed.
