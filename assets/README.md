# Source artwork

Editable masters live here: SVG, AI, Figma exports, whatever the icons were drawn in. Nothing in
this folder is compiled or shipped.

Exported `.ico` files go to [`src/CopyPasta.App/Resources/Icons/`](../src/CopyPasta.App/Resources/Icons/),
which is where the build looks for them.

Suggested layout:

```
assets/
  icon-app.svg          the application mark, drawn at 256
  icon-tray.svg         the tray glyph, drawn at 32 on a 16-unit grid
  exports/              intermediate PNGs, if your ICO tool needs them
```

Specifications: [ASSETS_REQUIRED.md](../ASSETS_REQUIRED.md)
