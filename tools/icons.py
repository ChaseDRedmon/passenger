#!/usr/bin/env python3
"""Writes src/Passenger.App/Theme/Icons.axaml from Microsoft's Fluent UI System Icons (MIT).

Each icon is fetched once into artifacts/icons/ and kept there; the list below is the only place an
icon is named, so adding one is a line here and `just icons`. THIRD_PARTY_NOTICES.md credits the set.
"""
import os
import re
import sys
import urllib.parse
import urllib.request

BASE = "https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/assets"

# Resource key, the icon's folder in the Fluent repo, and the file's size and style.
ICONS = [
    ("Explorer", "Folder", "20_regular"),
    ("FolderOpen", "Folder Open", "20_regular"),
    ("Structure", "Text Bullet List Tree", "20_regular"),
    ("Branch", "Branch", "20_regular"),
    ("Run", "Play", "20_filled"),
    ("Debug", "Bug", "20_regular"),
    ("Search", "Search", "20_regular"),
    ("Terminal", "Window Console", "20_regular"),
    ("Problems", "Warning", "20_regular"),
    ("Notifications", "Alert", "20_regular"),
    ("Settings", "Settings", "20_regular"),
    ("ChevronDown", "Chevron Down", "16_regular"),
    ("ChevronRight", "Chevron Right", "16_regular"),
    ("Document", "Document", "20_regular"),
    ("DocumentText", "Document Text", "20_regular"),
    ("Code", "Code", "20_regular"),
    ("Close", "Dismiss", "16_regular"),
    ("ArrowUp", "Arrow Up", "16_regular"),
    ("ArrowDown", "Arrow Down", "16_regular"),
    ("MatchCase", "Text Case Title", "16_regular"),
    ("WholeWord", "Text Whole Word", "20_regular"),
    ("Build", "Wrench", "20_regular"),
    ("RunOutline", "Play", "20_regular"),
    ("Stop", "Stop", "20_filled"),
    ("Rerun", "Arrow Clockwise", "20_regular"),
    ("Clear", "Delete", "20_regular"),
    ("Solution", "Grid", "20_regular"),
    ("Project", "Box", "20_regular"),
    ("Package", "Box Multiple", "20_regular"),
    ("Reference", "Link", "20_regular"),
    ("Error", "Error Circle", "20_regular"),
    ("Info", "Info", "20_regular"),
    ("Resume", "Play", "20_filled"),
    ("Pause", "Pause", "20_regular"),
    ("StepOver", "Arrow Step Over", "20_regular"),
    ("StepInto", "Arrow Step In", "20_regular"),
    ("StepOut", "Arrow Step Out", "20_regular"),
    ("RunToCursor", "Arrow Step In Right", "20_regular"),
    ("Breakpoints", "Record", "20_regular"),
    ("MuteBreakpoints", "Circle Off", "20_regular"),
    ("Evaluate", "Calculator", "20_regular"),
    ("Add", "Add", "16_regular"),
    ("Variable", "Braces Variable", "20_regular"),
    ("More", "More Vertical", "16_regular"),
    ("Minimize", "Subtract", "16_regular"),
    ("Tests", "Beaker", "20_regular"),
    ("Commit", "Checkmark", "20_regular"),
    ("Check", "Checkmark", "16_regular"),
    ("Rollback", "Arrow Undo", "20_regular"),
    ("Push", "Arrow Upload", "20_regular"),
    ("Diff", "Split Vertical", "20_regular"),
    ("Profile", "Top Speed", "20_regular"),
    ("Bookmark", "Bookmark", "20_regular"),
]


def fetch(folder, stem, cache):
    name = f"ic_fluent_{folder.lower().replace(' ', '_')}_{stem}.svg"
    path = os.path.join(cache, name)
    if not os.path.exists(path):
        url = f"{BASE}/{urllib.parse.quote(folder)}/SVG/{name}"
        with urllib.request.urlopen(url) as response:
            data = response.read()
        with open(path, "wb") as out:
            out.write(data)
    with open(path, encoding="utf-8") as svg:
        return svg.read()


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    cache = os.path.join(root, "artifacts", "icons")
    os.makedirs(cache, exist_ok=True)
    lines = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        "    <!-- Written by tools/icons.py from Fluent UI System Icons (MIT, Microsoft; see",
        "         THIRD_PARTY_NOTICES.md). F1 is the nonzero fill rule the SVGs are drawn for. -->",
    ]
    for key, folder, stem in ICONS:
        svg = fetch(folder, stem, cache)
        paths = re.findall(r'<path[^>]*\sd="([^"]+)"', svg)
        if len(paths) != 1 or re.search(r"<(rect|circle|line|polygon|g)\b", svg):
            sys.exit(f"icons: {folder} {stem} is not a single path")
        lines.append(f'    <StreamGeometry x:Key="Icon.{key}">F1 {paths[0]}</StreamGeometry>')
    lines.append("</ResourceDictionary>")
    target = os.path.join(root, "src", "Passenger.App", "Theme", "Icons.axaml")
    with open(target, "w", encoding="utf-8") as out:
        out.write("\n".join(lines) + "\n")
    print(f"icons: {len(ICONS)} written to {os.path.relpath(target, root)}")


if __name__ == "__main__":
    main()
