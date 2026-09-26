# Icon brief

**Project:** [CopyPasta] — a clipboard manager for Windows
**Deliverables:** 3 Windows `.ico` files + editable source
**Platform:** Windows 10 and 11, light and dark themes, 100%–200% display scaling

---

## What the app is

A small utility that keeps a history of everything you copy, so you can paste something from
earlier instead of losing it. It has no main window. It lives permanently in the **notification
area** — the cluster of small icons at the right-hand end of the Windows taskbar, near the clock —
and you click it, or press a keyboard shortcut, to get a menu of recent clips.

That matters for the design: **the icon people actually see, all day, is 16 pixels wide.** The
large artwork only appears in the installer, Alt+Tab and the Programs list.

---

## Deliverables

**Three `.ico` files in total — not one per size.**

`.ico` is a container format: a single file holds every size inside it, and Windows picks the right
one at runtime. So `app.ico` is *one file* containing ten frames, and each tray file is *one file*
containing six.

If your workflow exports a PNG per size, those are an intermediate step — assemble them into the
`.ico` and deliver that. (Keep them in `source/` if you like; no need to.)

| # | File | Contains | Required |
|---|---|---|---|
| 1 | `app.ico` | 10 frames: 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 | Yes |
| 2 | `tray-light.ico` | 6 frames: 16, 20, 24, 32, 40, 48 | Yes |
| 3 | `tray-dark.ico` | 6 frames: 16, 20, 24, 32, 40, 48 | Yes |
| 4 | Editable source (SVG preferred; AI or Figma fine) | — | Yes |
| 5 | `preview.png` — contact sheet, see [Review](#review-sheet) | — | Yes |
| 6 | `menu-folder.ico`, `menu-snippet.ico` | 4 frames each: 16, 20, 24, 32 | Optional |

Filenames are used verbatim by the build. Please match them exactly, lowercase.

---

## 1. `app.ico` — the application mark

Appears in: the installer, Add/Remove Programs, Alt+Tab, window title bars, the taskbar when a
window is open, and file properties.

| | |
|---|---|
| **Format** | Windows `.ico`, multi-resolution (one file, many frames) |
| **Frames** | 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 px — square |
| **Colour** | Full colour, 32-bit BGRA, straight (non-premultiplied) alpha |
| **Background** | Fully transparent |
| **Compression** | 256 frame PNG-compressed; 128 and below as uncompressed BMP frames |

### Why so many sizes

Windows chooses a frame based on display scaling and context. It does **not** interpolate between
frames — it picks the nearest and rescales, which looks soft.

| Display scaling | Frame used for small icons |
|---|---|
| 100% | 16 |
| 125% | 20 |
| 150% | 24 |
| 175% | 28 → falls back to 32 |
| 200% | 32 |

Draw at 256 and work down, but **hand-tune the 16 and 20 frames**. An automatic downscale of a
detailed mark turns to mush at that size. It is entirely normal — and expected — for the small
frames to be a visibly simplified version of the large one: fewer elements, heavier weights,
snapped to the pixel grid.

---

## 2 & 3. `tray-light.ico` and `tray-dark.ico` — the notification-area glyph

This is the important one. It is what the user sees permanently.

| | |
|---|---|
| **Format** | Windows `.ico`, multi-resolution |
| **Frames** | 16, 20, 24, 32, 40, 48 px — square |
| **Colour** | 32-bit BGRA, straight alpha |
| **Background** | Fully transparent |

### Two files, one shape

Both files contain **the same silhouette in different ink**:

| File | Ink colour | Used when the taskbar is |
|---|---|---|
| `tray-light.ico` | `#FFFFFF` (near-white) | **Dark** — the Windows 11 default |
| `tray-dark.ico` | `#1A1A1A` (near-black) | **Light** |

If you are used to macOS: macOS ships one black glyph and the system inverts it automatically for
dark menu bars (*template images*). **Windows has no equivalent.** It draws exactly the image it is
given, so light and dark are two separate files, and the app has a setting to choose between them.

Please use `#FFFFFF` and `#1A1A1A` rather than pure white and pure black — the small amount of
tolerance helps against high-contrast themes.

### Design constraints

These are harder than they look, so they are worth reading before starting:

- **It must read at 16×16.** With a 1px margin that is a 14×14 live area — 196 pixels for the
  entire design. One silhouette, one idea.
- **Monochrome plus alpha only.** No colour, no gradients, no two-tone detail. Shape carries all
  the meaning. A colourful glyph looks out of place beside the system's own icons.
- **Prefer solid filled shapes to outlines.** A 1px stroke disappears against a translucent taskbar
  with a busy wallpaper showing through. If you do stroke: minimum **2px at 32×32**, and convert to
  a filled shape for the 16 and 20 frames.
- **Leave a transparent margin**, roughly 6% per side — 1px at 16, 2px at 32, 3px at 48. Windows
  packs notification icons tightly; a full-bleed glyph touches its neighbours.
- **Snap to the pixel grid** at 16, 20 and 24. Half-pixel edges read as blur at these sizes.
- **No text, no letterforms, no numerals.** Nothing legible survives 16px.
- **Anti-alias, but sparingly.** Heavy anti-aliasing on a thin shape reads as grey mush.

### Avoid

- Detail that only exists in the 48px frame
- Drop shadows, bevels, inner glows
- Thin diagonal lines (they alias badly at small sizes)
- Anything that becomes a featureless blob when squinted at from 60cm

---

## 4. Source files

Please include the editable master — SVG preferred, AI or a Figma export also fine.

Ideally two artboards:

| Artboard | Canvas | Purpose |
|---|---|---|
| `icon-app` | 256×256 | The full mark |
| `icon-tray` | 32×32, drawn on a 16-unit grid | The glyph, so it scales cleanly to 16 |

Drawing the tray glyph on a 16-unit grid at 32×32 means every edge lands on a whole pixel when it
halves to 16.

---

## Review sheet

Alongside the `.ico` files, please include a single `preview.png` showing:

1. Each tray frame at **actual size** (16, 20, 24, 32) on a `#202020` background — the Windows 11
   dark taskbar
2. The same frames on a `#F3F3F3` background — the light taskbar
3. The `app.ico` 256, 48 and 32 frames on a neutral mid-grey

This lets the icons be reviewed without installing anything, and it is the quickest way to catch a
glyph that looks fine in the editor and vanishes at actual size.

---

## Producing the `.ico`

Any of these produce a conformant multi-resolution file:

**ImageMagick** (fastest, if the artwork needs no per-size tuning):

```bash
magick icon-256.png -define icon:auto-resize=256,128,96,64,48,40,32,24,20,16 app.ico
magick tray-light-48.png -define icon:auto-resize=48,40,32,24,20,16 tray-light.ico
```

**Per-size tuning** (recommended for the tray glyph — export each size by hand, then assemble):

```bash
magick tray-light-16.png tray-light-20.png tray-light-24.png \
       tray-light-32.png tray-light-40.png tray-light-48.png tray-light.ico
```

**GUI options:** [RealWorld Icon Editor](https://www.rw-designer.com/icon-editor) (free, Windows),
Axialis IconWorkshop, or GIMP — open the PNGs as layers and export as `.ico`.

> **Please do not use an online PNG-to-ICO converter for the final files.** Most emit a single
> 256px frame, which Windows then rescales down to 16 — producing exactly the softness that the
> multi-resolution format exists to prevent.

---

## Self-check before delivery

- [ ] Exactly three `.ico` files — not one per size
- [ ] Filenames exactly `app.ico`, `tray-light.ico`, `tray-dark.ico` (lowercase)
- [ ] `app.ico` contains all 10 frames; each tray file contains all 6
- [ ] Every frame is square and fully transparent outside the glyph
- [ ] Tray glyph is legible at 16px on both `#202020` and `#F3F3F3`
- [ ] No frame has a white or coloured rectangular background
- [ ] Light and dark tray glyphs are the *same shape*, differing only in ink
- [ ] `preview.png` included
- [ ] Editable source included

A quick way to check the frames on Windows: right-click the `.ico` → Properties, or open it in
IrfanView, which lists every frame in the file.

---

## Originality

Please make this original work.

This project is a Windows port of an open-source macOS app. Its **code** is MIT licensed and reused
with attribution, but its **icons are copyrighted separately by their authors** and are explicitly
not covered by that licence. So: please do not trace, adapt or take direct inspiration from that
app's icons, or from any other existing clipboard manager's icons.

A clipboard, a stack of cards, a layered document — the obvious metaphors are all fair game. Just
draw them fresh.

---

## Delivery

Send the files as they are (or a zip):

```
app.ico
tray-light.ico
tray-dark.ico
preview.png
source/icon-app.svg
source/icon-tray.svg
```

Happy to look at rough directions at 16px before you commit to finishing a set — that size is where
concepts usually succeed or fail, and it is cheaper to find out early.
