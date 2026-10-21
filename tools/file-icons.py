#!/usr/bin/env python3
"""Writes src/Passenger.App/Theme/FileIconSet.axaml, the File Icons set's shapes, from the sets the file-icons package
(https://github.com/file-icons) draws with: its own icons (ISC), MFixx (MIT) and GitHub's Octicons (MIT), plus the few
drawn here.

Each SVG is fetched once into artifacts/file-icons/ and kept there; the list below is the only place a shape is named,
so adding one is a line here and `just file-icons`. Which file takes which shape is `Explorer/FileIconSet.cs`.
THIRD_PARTY_NOTICES.md credits the sets.
"""
import os
import re
import sys
import urllib.parse
import urllib.request

# Each set's commit, so every run draws the same shapes: a newer one is a new commit here, its changes seen in review.
SETS = {
    "fi": "https://raw.githubusercontent.com/file-icons/icons/e6e6e6ac8cb1d91867167c228c00a667f4d47101/svg/{}.svg",
    "mf": "https://raw.githubusercontent.com/file-icons/MFixx/aabb5badb3fbf70ea2446794126e4247e184d653/svg/{}.svg",
    "oc": "https://raw.githubusercontent.com/primer/octicons/cc4e12df6ff8292447ba9141eaa2a6f6e1c59a85/icons/{}-16.svg",
}

# Resource key (FileIcon.KEY), the set and the icon's name in it.
ICONS = [
    # Octicons: the plain kinds, as file-icons draws them.
    ("File", "oc", "file"),
    ("Code", "oc", "file-code"),
    ("Binary", "oc", "file-binary"),
    ("Archive", "oc", "file-zip"),
    ("Book", "oc", "book"),
    ("Checklist", "oc", "checklist"),
    ("Markdown", "oc", "markdown"),
    ("Key", "oc", "key"),
    ("Lock", "oc", "lock"),
    ("Git", "oc", "git-branch"),
    ("GitHub", "oc", "mark-github"),
    ("Gears", "oc", "gear"),
    ("Audio", "oc", "unmute"),
    ("Law", "oc", "law"),
    ("History", "oc", "history"),
    ("Stack", "oc", "stack"),
    ("Database", "oc", "database"),
    # MFixx: the languages file-icons takes from Mfizz.
    ("CSharp", "mf", "csharp"),
    ("C", "mf", "c"),
    ("Cpp", "mf", "c++"),
    ("Java", "mf", "java"),
    ("JavaScript", "mf", "javascript"),
    ("Python", "mf", "python"),
    ("Ruby", "mf", "ruby"),
    ("Html", "mf", "html"),
    ("Css", "mf", "css"),
    ("Scala", "mf", "scala"),
    ("Elixir", "mf", "elixir"),
    ("Erlang", "mf", "erlang"),
    ("Clojure", "mf", "clojure"),
    ("Perl", "mf", "perl"),
    ("ObjectiveC", "mf", "objc"),
    ("Angular", "mf", "angular"),
    # file-icons' own.
    ("CSharpScript", "fi", "C#-Script"),
    ("TypeScript", "fi", "TypeScript"),
    ("Tsx", "fi", "TSX"),
    ("Jsx", "fi", "JSX"),
    ("Json", "fi", "JSON-1"),
    ("Yaml", "fi", "YAML"),
    ("Toml", "fi", "TOML"),
    ("Docker", "fi", "Docker"),
    ("DotEnv", "fi", "dotenv"),
    ("EditorConfig", "fi", "EditorConfig"),
    ("ESLint", "fi", "ESLint"),
    ("Prettier", "fi", "Prettier"),
    ("Npm", "fi", "NPM"),
    ("NuGet", "fi", "NuGet"),
    ("PowerShell", "fi", "PowerShell"),
    ("Terminal", "fi", "Terminal"),
    ("Config", "fi", "Config"),
    ("ConfigJs", "fi", "Config-JS"),
    ("Diff", "fi", "Diff"),
    ("Patch", "fi", "Patch"),
    ("Font", "fi", "Font-Outline"),
    ("Video", "fi", "Video"),
    ("Image", "fi", "Image"),
    ("Go", "fi", "Go"),
    ("Kotlin", "fi", "Kotlin"),
    ("Lua", "fi", "Lua"),
    ("Php", "fi", "PHP"),
    ("R", "fi", "R"),
    ("Svelte", "fi", "Svelte"),
    ("Vue", "fi", "Vue"),
    ("Webpack", "fi", "Webpack"),
    ("Yarn", "fi", "Yarn"),
    ("Vite", "fi", "Vite"),
    ("GraphQL", "fi", "GraphQL"),
    ("Babel", "fi", "Babel"),
    ("TestJs", "fi", "Test-JS"),
    ("TestTypeScript", "fi", "Test-TypeScript"),
    ("Jupyter", "fi", "Jupyter"),
    ("CMake", "fi", "Cmake"),
    ("Gradle", "fi", "Gradle"),
    ("Kubernetes", "fi", "Kubernetes"),
    ("Terraform", "fi", "Terraform"),
    ("SQLite", "fi", "SQLite"),
    ("LaTeX", "fi", "LaTeX"),
    ("Pug", "fi", "Pug"),
    ("Tailwind", "fi", "Tailwind"),
    ("Jest", "fi", "Jest"),
    ("Storybook", "fi", "Storybook"),
    ("DevContainer", "fi", "DevContainer"),
    ("VSCode", "fi", "VSCode"),
    ("Robots", "fi", "Robots"),
    ("Mdx", "fi", "MDX"),
    ("Mermaid", "fi", "Mermaid"),
    ("Zig", "fi", "Zig"),
    ("Nix", "fi", "Nix"),
    ("Julia", "fi", "Julia"),
    ("Elm", "fi", "Elm"),
    ("Crystal", "fi", "Crystal"),
    ("WebAssembly", "fi", "WebAssembly"),
    ("Assembly", "fi", "Assembly-Generic"),
    ("Fortran", "fi", "Fortran"),
    ("Arduino", "fi", "Arduino"),
    ("Godot", "fi", "Godot"),
    ("WiX", "fi", "WiX"),
    ("AsciiDoc", "fi", "AsciiDoc"),
    ("ReStructuredText", "fi", "reStructuredText"),
    ("PreCommit", "fi", "Precommit"),
    ("Husky", "fi", "Husky"),
    ("Renovate", "fi", "Renovate"),
    ("Dependabot", "fi", "Dependabot"),
    ("Browserslist", "fi", "Browserslist"),
    ("Pnpm", "fi", "PNPM"),
    ("Deno", "fi", "Deno"),
    ("Vagrant", "fi", "Vagrant"),
    ("Ansible", "fi", "Ansible"),
    ("Lisp", "fi", "Lisp"),
    ("OCaml", "fi", "OCaml"),
    ("Racket", "fi", "Racket"),
    ("Codecov", "fi", "Codecov"),
    ("Rollup", "fi", "Rollup"),
    ("Stylus", "fi", "Stylus"),
    ("PostCss", "fi", "PostCSS"),
    ("Stylelint", "fi", "Stylelint"),
    ("Bazel", "fi", "Bazel"),
    ("NextJs", "fi", "NextJS"),
    ("Nuxt", "fi", "Nuxt"),
    ("Electron", "fi", "Electron"),
    ("Firebase", "fi", "Firebase"),
    ("Netlify", "fi", "Netlify"),
    ("GitLab", "fi", "GitLab"),
    ("Cucumber", "fi", "Cucumber"),
    ("Antlr", "fi", "ANTLR"),
    ("Haxe", "fi", "Haxe"),
    ("Solidity", "fi", "Solidity"),
    ("Prisma", "fi", "Prisma"),
    ("Matlab", "fi", "MATLAB"),
    ("Twig", "fi", "Twig"),
    ("Jinja", "fi", "Jinja"),
    ("Ejs", "fi", "EJS"),
    ("Moustache", "fi", "Moustache"),
    ("OpenApi", "fi", "OpenAPI"),
    ("Swagger", "fi", "Swagger"),
    ("Kusto", "fi", "Kusto"),
    ("LinqPad", "fi", "LINQPad"),
]

# Drawn here for the languages none of the sets has (in a 16 by 16 box, nonzero): F# as two chevrons opening right,
# Visual Basic as a V over a bar, Rust as a cog's ring, Swift as a swallow's wedge, Dart as an arrow head.
OWN = {
    "FSharp": "M7 1.5 L1 8 L7 14.5 L7 11 L4.2 8 L7 5 Z M8.5 4.5 L5.3 8 L8.5 11.5 Z M9.5 1.5 L9.5 5 L12.3 8 L9.5 11 L9.5 14.5 L15.5 8 Z",
    "VisualBasic": "M1.5 2 L4 2 L8 10.5 L12 2 L14.5 2 L9.2 13 L6.8 13 Z M3 14 L13 14 L13 15.5 L3 15.5 Z",
    "Rust": "M8 0.5 L9.4 2 L11.3 1.4 L11.9 3.3 L13.9 3.6 L13.6 5.6 L15.2 6.8 L14 8.4 L15.2 10 L13.6 11.2 L13.9 13.2 L11.9 13.5 L11.3 15.4 L9.4 14.8 L8 16 L6.6 14.8 L4.7 15.4 L4.1 13.5 L2.1 13.2 L2.4 11.2 L0.8 10 L2 8.4 L0.8 6.8 L2.4 5.6 L2.1 3.6 L4.1 3.3 L4.7 1.4 L6.6 2 Z M5 5 L5 12 L6.6 12 L6.6 9.4 L8.2 9.4 L9.6 12 L11.4 12 L9.8 9.1 C10.6 8.8 11.1 8.1 11.1 7.2 C11.1 5.9 10.2 5 8.8 5 Z M6.6 6.4 L8.7 6.4 C9.2 6.4 9.5 6.8 9.5 7.2 C9.5 7.7 9.2 8 8.7 8 L6.6 8 Z",
    "Swift": "M1 4 C4 7 7 9.5 10 11 C7.5 12 4.5 11.5 1.5 9.5 C4 13 8 15 11.5 14 C13 13.6 14 14 15 15 C15 13.5 14.5 12.5 14 12 C15.5 9 14 5 10 1.5 C11.5 4 12 6.5 11 8.5 C8 6.5 5 5 1 4 Z",
    "Dart": "M4 1 L10.5 1 L15 5.5 L15 12 L12 15 L5.5 15 L1 10.5 L1 4 Z M4 3.5 L3.5 10 L6 12.5 L12.5 12.5 Z",
}

NUMBER = re.compile(r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
ARITY = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}


def path_data(d, places=2):
    """An SVG path as Avalonia's path markup: every number apart and rounded to PLACES, an arc's flags read as the one
    digit each is, since SVG lets them run together ("a.25.25 0 0 0-.25.25") where Avalonia's parser would read a
    longer number."""
    out = []
    i = 0
    command = None
    count = 0
    while i < len(d):
        c = d[i]
        if c in " ,\t\r\n":
            i += 1
            continue
        if c.upper() in ARITY:
            command = c
            count = 0
            out.append(c)
            i += 1
            continue
        if command is None:
            sys.exit(f"file-icons: a number before any command in {d[:40]}")
        arity = ARITY[command.upper()]
        if arity == 0:
            sys.exit(f"file-icons: a number after Z in {d[:40]}")
        if command.upper() == "A" and count % 7 in (3, 4):
            out.append(c)
            i += 1
        else:
            m = NUMBER.match(d, i)
            if not m:
                sys.exit(f"file-icons: can't read {d[i:i + 20]!r}")
            out.append(f"{round(float(m.group(0)), places):g}")
            i = m.end()
        count += 1
    return " ".join(out)


def attribute(element, name):
    m = re.search(rf'\s{name}="([^"]*)"', element)
    return m.group(1) if m else None


def number(element, name, default="0"):
    return float(attribute(element, name) or default)


def figures(svg):
    """Every shape of an SVG as path markup, and its fill rule; anything moved by a transform, or shapes of two fill
    rules, ends the script, since one StreamGeometry holds one rule and no transform. Numbers keep what a 16-pixel
    icon can show: two places in Octicons' 16 box, one in the fonts' 512."""
    if re.search(r"\stransform=", svg) or re.search(r"<(use|clipPath|mask|image|text)\b", svg):
        sys.exit("file-icons: a transform or reference this script doesn't draw")
    box = re.search(r'\sviewBox="[-\d.]+ [-\d.]+ ([\d.]+)', svg) or re.search(r'\swidth="([\d.]+)', svg)
    places = 2 if box and float(box.group(1)) <= 64 else 1
    parts = []
    rules = set()
    for element in re.findall(r"<(?:path|polygon|polyline|rect|circle|ellipse)\b[^>]*>", svg):
        if re.search(r'\sfill="none"', element):
            continue
        rules.add(attribute(element, "fill-rule") or attribute(element, "clip-rule") or "nonzero")
        tag = re.match(r"<(\w+)", element).group(1)
        if tag == "path":
            parts.append(path_data(attribute(element, "d"), places))
        elif tag in ("polygon", "polyline"):
            points = NUMBER.findall(attribute(element, "points"))
            pairs = [f"{round(float(points[k]), places):g} {round(float(points[k + 1]), places):g}" for k in range(0, len(points), 2)]
            parts.append("M " + " L ".join(pairs) + " Z")
        elif tag == "rect":
            x, y, w, h = number(element, "x"), number(element, "y"), number(element, "width"), number(element, "height")
            if attribute(element, "rx") or attribute(element, "ry"):
                sys.exit("file-icons: a rounded rect")
            parts.append(f"M {x:g} {y:g} H {x + w:g} V {y + h:g} H {x:g} Z")
        else:
            cx, cy = number(element, "cx"), number(element, "cy")
            rx = number(element, "r") if tag == "circle" else number(element, "rx")
            ry = number(element, "r") if tag == "circle" else number(element, "ry")
            parts.append(f"M {cx - rx:g} {cy:g} A {rx:g} {ry:g} 0 1 0 {cx + rx:g} {cy:g} A {rx:g} {ry:g} 0 1 0 {cx - rx:g} {cy:g} Z")
    if not parts or len(rules) != 1:
        sys.exit(f"file-icons: {len(parts)} shapes in {len(rules)} fill rules")
    return " ".join(parts), "F0" if rules.pop() == "evenodd" else "F1"


def fetch(source, name, cache):
    path = os.path.join(cache, source, re.sub(r"[^\w.+#-]", "_", name) + ".svg")
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with urllib.request.urlopen(SETS[source].format(urllib.parse.quote(name))) as response:
            data = response.read()
        with open(path, "wb") as out:
            out.write(data)
    with open(path, encoding="utf-8") as svg:
        return svg.read()


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    cache = os.path.join(root, "artifacts", "file-icons")
    lines = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        "    <!-- Written by tools/file-icons.py from file-icons' icons (ISC, John Gardner), MFixx (MIT, Fizzed, Inc. and",
        "         John Gardner) and Octicons (MIT, GitHub Inc.), see THIRD_PARTY_NOTICES.md, and the few drawn there.",
        "         F1 is the nonzero fill rule, F0 the even-odd one. -->",
    ]
    keys = set()
    for key, source, name in ICONS:
        try:
            data, rule = figures(fetch(source, name, cache))
        except SystemExit as stop:
            sys.exit(f"{stop} ({source} {name})")
        keys.add(key)
        lines.append(f'    <StreamGeometry x:Key="FileIcon.{key}">{rule} {data}</StreamGeometry>')
    for key, data in OWN.items():
        if key in keys:
            sys.exit(f"file-icons: {key} twice")
        lines.append(f'    <StreamGeometry x:Key="FileIcon.{key}">F1 {data}</StreamGeometry>')
    lines.append("</ResourceDictionary>")
    target = os.path.join(root, "src", "Passenger.App", "Theme", "FileIconSet.axaml")
    with open(target, "w", encoding="utf-8", newline="\n") as out:
        out.write("\n".join(lines) + "\n")
    print(f"file-icons: {len(ICONS) + len(OWN)} written to {os.path.relpath(target, root)}")


if __name__ == "__main__":
    main()
