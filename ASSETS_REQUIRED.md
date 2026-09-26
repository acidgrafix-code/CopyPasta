# What you need to produce before Phase 8

Phase 8 is packaging: installer, auto-updates, high-DPI checks, release build. Four things have to
come from you rather than from code. Two of them block the phase; two only block a *public*
release.

| # | Item | Blocks | Effort |
|---|---|---|---|
| 1 | **Product name** | Everything below | A decision |
| 2 | **Icons** (3 `.ico` files) | Phase 8 | A designer, or an afternoon |
| 3 | **Publisher name** | Installer metadata | A decision |
| 4 | **Code-signing certificate** | A *public* release, not a local build | Money and identity checks |
| 5 | **Update feed hosting** | Auto-updates only | Free if GitHub Releases |

Everything else — translations, toolbar icons, menu glyphs — is optional and listed at the end.

---

## 1. Product name — do this first

This is the real blocker. It is not just a label: it goes into the icon design, the executable
metadata, the installer, the registry, and the folder your users' data lives in.

**It cannot be "Clipy" or "ClipMenu".** Clipy's README asks derived works not to use those product
names (see §2 of [PORTING_PLAN.md](PORTING_PLAN.md)). `CopyPasta` is my placeholder and is one
find-and-replace from being whatever you choose.

Where the name currently appears in code:

| Where | Today | Consequence of changing later |
|---|---|---|
| `AssemblyName` | `CopyPasta` → `CopyPasta.exe` | Executable filename |
| Tray tooltip | `CopyPasta` | Cosmetic |
| Run-key value name | `CopyPasta` | A rename orphans the old auto-start entry |
| `%APPDATA%\CopyPasta\` | settings, log, database | **A rename orphans existing user data** |
| Single-instance mutex | `Local\CopyPasta.SingleInstance` | Two copies could run during an upgrade |
| Window titles, About pane | `CopyPasta` | Cosmetic |

The two marked in bold are why this is worth settling before rather than after the first release.

Pick something that is not already a Windows clipboard manager — Ditto, ClipClip, CopyQ, ArsClip
and Clipboard Master are all taken. Check the name is free on GitHub and as a domain if that
matters to you.

**Also decide:** a publisher name for the executable's metadata and the installer ("Published by
…"). Your own name is fine.

---

## 2. Icons

### What to deliver

Three `.ico` files, into **`src/CopyPasta.App/Resources/Icons/`**:

| File | Purpose |
|---|---|
| `app.ico` | The executable, window title bars, Alt+Tab, taskbar, installer, Add/Remove Programs |
| `tray-light.ico` | Notification-area glyph for a **dark** taskbar — the Windows 11 default |
| `tray-dark.ico` | Notification-area glyph for a **light** taskbar |

Keep the editable masters (SVG/AI/Figma) in **`/assets/`** at the repository root. That folder is
not compiled; it exists so the source of the artwork does not get lost.

Both folders already exist with a README pointing back here. `app.ico` is wired into the build
conditionally — drop the file in and the next build uses it, with nothing else to change.

### Why three files and not one

macOS gets light/dark inversion free through *template images*: it ships one black glyph and the
system inverts it for dark menu bars. **Windows has no equivalent.** The notification area hands
you an `HICON` and draws exactly what you give it. So the two tray variants are two real files, and
there is a setting (General → Tray icon) to pick between them.

### `app.ico` specification

| | |
|---|---|
| **Format** | Windows `.ico`, multi-resolution |
| **Sizes** | 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 |
| **Colour depth** | 32-bit BGRA, straight alpha |
| **Compression** | Store the 256 frame PNG-compressed; keep 48 and below as uncompressed BMP frames |
| **Background** | Transparent |

The odd sizes are not padding. Windows picks a frame by DPI: 100% → 16, 125% → 20, 150% → 24,
200% → 32. Omitting them forces the shell to rescale and the result looks soft.

Draw at 256 and work down, but **hand-tune 16 and 20** — an automatic downscale of a detailed mark
turns to mush at that size. It is normal for the small frames to be a simplified version of the
large one.

### Tray glyph specification

| | |
|---|---|
| **Format** | Windows `.ico`, multi-resolution |
| **Sizes** | 16, 20, 24, 32, 40, 48 |
| **Colour depth** | 32-bit BGRA, straight alpha |
| **`tray-light.ico`** | Near-white: `#FFFFFF`, alpha 100% |
| **`tray-dark.ico`** | Near-black: `#1A1A1A`, alpha 100% |
| **Background** | Fully transparent |

This is the one most likely to be got wrong, so the constraints matter more than the artwork:

- **It has to read at 16×16.** That is roughly nine usable pixels of interior after margins. One
  silhouette, one idea. No text, no gradients, no two-tone detail, no outline-plus-fill.
- **Monochrome plus alpha, not colour.** Shape carries the meaning; the two files differ only in
  ink colour. A colourful tray icon looks wrong next to the system's own.
- **Prefer filled shapes to strokes at small sizes.** A 1px stroke disappears against a busy
  wallpaper showing through a translucent taskbar. If you must stroke, keep it ≥ 2px at 32×32 and
  convert to a filled shape for the 16 and 20 frames.
- **Leave a 1px transparent margin** on all sides at 16×16, more proportionally at larger sizes.
  Windows packs notification icons tightly and a full-bleed glyph collides with its neighbours.
- **Check both variants against both taskbars.** Users run light taskbars, and some run
  high-contrast themes where neither variant is ideal — near-white and near-black rather than pure
  white and pure black gives you a little tolerance.
- **Test at 100% and 200%**, not just in the editor. The 16 frame is what most people will see.

The current runtime-drawn placeholder (`TrayIconGlyph.cs`) is a rounded clipboard outline with a
clip at the top. It is a placeholder, not a design direction — ignore it if you have a better idea.

### Tools

Any of these produce a conformant multi-resolution `.ico`:

- **ImageMagick** — `magick icon-256.png -define icon:auto-resize=256,128,96,64,48,40,32,24,20,16 app.ico`
- **Inkscape** → export PNGs per size → assemble with ImageMagick or [RealWorld Icon Editor](https://www.rw-designer.com/icon-editor) (free)
- **Figma** with an ICO export plugin
- **GIMP** — open the PNGs as layers, export as `.ico`, tick "compressed" for the 256 layer only

Avoid online converters for the final file: most emit a single 256 frame, which Windows then
rescales to 16 with exactly the softness the multi-resolution format exists to prevent.

---

## 3. Code-signing certificate

**Not needed to build or run locally. Needed for anyone else to install it comfortably.**

Unsigned, Windows SmartScreen shows "Windows protected your PC" on first run, with the publisher
listed as "Unknown". Most people stop there.

| Option | Cost | SmartScreen behaviour |
|---|---|---|
| Unsigned | — | Warning on every download until enough people run it |
| **OV** (organisation validated) | ~$200–400/year | Warning until reputation accrues, then clears |
| **EV** (extended validation) | ~$300–600/year | Trusted immediately; requires a hardware token |
| Azure Trusted Signing | ~$10/month | OV-equivalent; needs a verifiable business identity |

Certificates now require the private key on an HSM or hardware token, so CI signing means either a
cloud signing service or a self-hosted runner with the token attached. Worth knowing before you
plan a release pipeline.

If you go unsigned, say so on the download page and tell people what they will see. That converts
far better than letting them discover it.

---

## 4. Update feed hosting

Velopack (the plan's choice, §3) polls a URL for a version manifest. Options:

- **GitHub Releases** — free, Velopack supports it directly, nothing to host
- **Any static host** — S3, Azure Blob, a plain web server; Velopack publishes a folder you upload
- **No updates at all** — ship the installer and let people re-download

Decide before Phase 8 because the feed URL is compiled into the app.

---

## Optional — not blocking anything

### Menu glyphs

`showsIconsInMenu` is the one macOS setting I omitted purely for lack of artwork. Restoring it
needs two more icons in the same folder:

| File | Sizes | Purpose |
|---|---|---|
| `menu-folder.ico` | 16, 20, 24, 32 | Beside folder entries in the tray menu |
| `menu-snippet.ico` | 16, 20, 24, 32 | Beside snippet entries |

Same monochrome constraints as the tray glyph, but these sit on the menu background, so mid-grey
(`#606060`) reads better than black in both light and dark menus.

### Snippet editor toolbar

The editor currently uses text buttons — "New Folder", "New Snippet", "Delete", "Import…",
"Export…". That is legible and needs nothing. macOS uses icons for these; if you want to match, six
16×16 glyphs would do it. Cosmetic only.

### Installer artwork

Velopack can show a splash during install. Optional; a 256×256 PNG of the app mark is plenty.

### Translation rewording

The translations came over from Clipy's string catalogues and mostly apply unchanged. Nine keys do
not. Five mention the product name and need one word swapped in each of the six languages
(de, it, ja, pt-BR, uk, zh-Hans):

- `Quit Clipy`
- `Clear history when Clipy quits`
- `Launch Clipy on system startup?`
- `Donation message`
- `historyClearIntervalDescription`

Four are dead — they describe macOS flows this port does not have (Accessibility permission,
telemetry) and should simply be deleted rather than translated:

- `Allow Clipy in System Settings > Privacy & Security > Accessibility.`
- `Open System Settings`
- `Please allow Accessibility`
- `Changes take effect the next time Clipy launches. Usage logs…`

English already overrides `Quit Clipy` → `Quit` so no menu currently shows the wrong name. The
other five only surface once those strings are used in the UI, so this can follow the release
rather than block it.

---

## The short version

To start Phase 8 I need, in `src/CopyPasta.App/Resources/Icons/`:

```
app.ico          16,20,24,32,40,48,64,96,128,256   full colour, transparent
tray-light.ico   16,20,24,32,40,48                 near-white silhouette
tray-dark.ico    16,20,24,32,40,48                 near-black silhouette
```

…and two decisions: **the product name** and **the publisher name**.

The certificate and the update feed can follow — I can build and test the installer without them,
and wire them in when you have them.
