# Third-party notices

CopyPasta incorporates or derives from the following. Each licence below requires its copyright
and permission notice to be included in all copies, so **this file must ship with the
application** — the installer places it next to the executable.

---

## Clipy

CopyPasta is a Windows port of Clipy, a macOS clipboard manager. The capture, paste, menu and
snippet logic is derived from its source; the translations in `Resources/Strings` are taken from
its string catalogues.

- Project: https://github.com/Clipy/Clipy
- Licence: MIT

```
The MIT License (MIT)

Copyright (c) 2015-2018 Clipy Project

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

**Note on icons.** Clipy's own icons are copyrighted by their respective authors and are *not*
covered by the MIT grant above. None of them are used in CopyPasta, whose icons are original work.

**Note on naming.** Clipy's README asks that derived works not use the names "Clipy" or
"ClipMenu" as their product name. CopyPasta does not.

---

## ClipMenu

Clipy derives from ClipMenu, and its notice travels with the code.

- Project: https://github.com/naotaka/ClipMenu
- Licence: MIT

```
The MIT License (MIT)

Copyright (c) 2008-2014 Naotaka Morimoto

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

Icons are copyrighted by their respective authors.
```

---

## Microsoft.Data.Sqlite

Used for the clip and snippet database.

- Project: https://github.com/dotnet/efcore
- Copyright: © Microsoft Corporation
- Licence: MIT — https://github.com/dotnet/efcore/blob/main/LICENSE.txt

## System.Drawing.Common

Used for menu thumbnails and image decoding.

- Project: https://github.com/dotnet/winforms
- Copyright: © Microsoft Corporation
- Licence: MIT — https://github.com/dotnet/winforms/blob/main/LICENSE.TXT

## .NET Runtime and the Windows SDK projections

- Project: https://github.com/dotnet/runtime
- Copyright: © Microsoft Corporation
- Licence: MIT — https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

## SQLite

The database engine itself, bundled through Microsoft.Data.Sqlite.

SQLite is in the **public domain** — https://www.sqlite.org/copyright.html — and imposes no
attribution requirement. Listed here for completeness.

---

*Last reviewed: 2026-09-25. Re-check when dependencies change.*
