# Third-party notices

What Passenger uses that others made, and under what terms.

## Packages

| Package | Licence | Source |
|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Skia, Avalonia.HarfBuzz, Avalonia.Headless | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Fonts.Inter (the Inter typeface) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| SkiaSharp (with Skia, BSD-3-Clause) | MIT | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp (with HarfBuzz, Old MIT) | MIT | https://github.com/mono/SkiaSharp |
| Microsoft.CodeAnalysis.CSharp (Roslyn) | MIT | https://github.com/dotnet/roslyn |

## JetBrains Mono

`src/Passenger.Editor/Fonts/` holds JetBrains Mono 2.304 (Regular, Bold, Italic, Bold Italic), the
editor's font, from https://github.com/JetBrains/JetBrainsMono under the SIL Open Font License 1.1,
whose full text is beside the fonts (`OFL.txt`). The fonts are unmodified.

## Fluent UI System Icons

`src/Passenger.App/Theme/Icons.axaml` holds path data from Microsoft's Fluent UI System Icons
(https://github.com/microsoft/fluentui-system-icons), written by `tools/icons.py`, which lists every
icon used.

```
MIT License

Copyright (c) 2020 Microsoft Corporation

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
