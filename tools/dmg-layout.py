#!/usr/bin/env python3
"""Writes the .DS_Store of the macOS disk image's top folder (tools/package-mac.sh): Finder opens the image as an icon
window of a fixed size with no toolbar or sidebar, Passenger.app on the left and the Applications link on the right to
drag it onto, as Rider's image and most Mac downloads open.

Finder writes these files itself when told through AppleScript, which needs a window session and the person's leave to
automate Finder, neither of which a build or a CI runner has; so this writes the file Finder would, in its format: a
buddy allocator's blocks holding one B-tree, here a single leaf of records sorted by file name, then by record code.

python3 -I tools/dmg-layout.py OUT
"""
import plistlib
import struct
import sys

APP = "Passenger.app"
WINDOW = (200, 120, 600, 360)  # x, y, width, height on the screen, the title bar included
ICON_SIZE = 128
# Each item's icon centre in the window. The volume's icon and the file system's event log folder are hidden, seen
# only by a person who shows hidden files, and kept below the window rather than between the two that matter.
ITEMS = {
    APP: (160, 165),
    "Applications": (440, 165),
    ".VolumeIcon.icns": (160, 520),
    ".fseventsd": (440, 520),
}

PAGE = 0x1000
# Where the allocator's blocks sit, as offsets after the file's 4-byte prefix, past its own 32-byte header at 0: the
# store's header (block 1), the allocator's bookkeeping (block 0) and the B-tree's one leaf (block 2).
HEADER_AT, HEADER_SIZE = 0x20, 0x20
BOOKKEEPING_AT, BOOKKEEPING_SIZE = 0x800, 0x800
LEAF_AT, LEAF_SIZE = 0x1000, PAGE


def record(name, code, kind, value):
    """One record: its file name as UTF-16, its four-character code and type, then the value in that type's form."""
    data = struct.pack(">I", len(name)) + name.encode("utf-16-be") + code.encode("ascii") + kind.encode("ascii")
    if kind == "blob":
        return data + struct.pack(">I", len(value)) + value
    if kind == "long":
        return data + struct.pack(">I", value)
    if kind == "type":
        return data + value.encode("ascii")
    raise ValueError(kind)


def window_records():
    x, y, width, height = WINDOW
    browser = {
        "ContainerShowSidebar": False,
        "PreviewPaneVisibility": False,
        "ShowPathbar": False,
        "ShowSidebar": False,
        "ShowStatusBar": False,
        "ShowTabView": False,
        "ShowToolbar": False,
        "SidebarWidth": 180,
        "WindowBounds": f"{{{{{x}, {y}}}, {{{width}, {height}}}}}",
    }
    icons = {
        "arrangeBy": "none",
        "backgroundColorBlue": 1.0,
        "backgroundColorGreen": 1.0,
        "backgroundColorRed": 1.0,
        "backgroundType": 0,
        "gridOffsetX": 0.0,
        "gridOffsetY": 0.0,
        "gridSpacing": 100.0,
        "iconSize": float(ICON_SIZE),
        "labelOnBottom": True,
        "scrollPositionX": 0.0,
        "scrollPositionY": 0.0,
        "showIconPreview": False,
        "showItemInfo": False,
        "textSize": 13.0,
        "viewOptionsVersion": 1,
    }
    yield ".", "bwsp", "blob", plistlib.dumps(browser, fmt=plistlib.FMT_BINARY, sort_keys=True)
    yield ".", "icvl", "type", "icnv"
    yield ".", "icvp", "blob", plistlib.dumps(icons, fmt=plistlib.FMT_BINARY, sort_keys=True)
    yield ".", "vSrn", "long", 1
    for name, (left, top) in ITEMS.items():
        # The position, then what Finder writes after it in every one: -1 and 0xffff0000.
        yield name, "Iloc", "blob", struct.pack(">II", left, top) + b"\xff" * 6 + b"\x00" * 2


def address(offset, size):
    """A block's entry in the allocator's table: its offset, with log2 of its size in the low five bits."""
    return offset | (size.bit_length() - 1)


def store():
    # Finder compares names without case, then codes.
    records = sorted(window_records(), key=lambda r: (r[0].lower(), r[1]))
    leaf = struct.pack(">II", 0, len(records)) + b"".join(record(*r) for r in records)
    if len(leaf) > LEAF_SIZE:
        raise ValueError(f"the layout's records ({len(leaf)} bytes) do not fit one {LEAF_SIZE}-byte leaf")
    # The store: its root node (block 2), no levels above the leaf, the record count, one node, the page size.
    header = struct.pack(">IIIII", 2, 0, len(records), 1, PAGE)

    blocks = [address(BOOKKEEPING_AT, BOOKKEEPING_SIZE), address(HEADER_AT, HEADER_SIZE), address(LEAF_AT, LEAF_SIZE)]
    bookkeeping = struct.pack(">II", len(blocks), 0) + struct.pack(">256I", *(blocks + [0] * (256 - len(blocks))))
    bookkeeping += struct.pack(">I", 1) + bytes([4]) + b"DSDB" + struct.pack(">I", 1)
    # The free blocks, one list per size: every buddy of the blocks in use, up to the whole 2^31-byte space.
    used = {HEADER_AT, BOOKKEEPING_AT, LEAF_AT}
    for power in range(32):
        free = [1 << power] if 6 <= power <= 30 and (1 << power) not in used else []
        bookkeeping += struct.pack(">I", len(free)) + b"".join(struct.pack(">I", f) for f in free)

    space = bytearray(LEAF_AT + LEAF_SIZE)
    space[0:4] = b"Bud1"
    space[4:16] = struct.pack(">III", BOOKKEEPING_AT, BOOKKEEPING_SIZE, BOOKKEEPING_AT)
    space[HEADER_AT:HEADER_AT + len(header)] = header
    space[BOOKKEEPING_AT:BOOKKEEPING_AT + len(bookkeeping)] = bookkeeping
    space[LEAF_AT:LEAF_AT + len(leaf)] = leaf
    return struct.pack(">I", 1) + bytes(space)


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: dmg-layout.py OUT (the image's top folder's .DS_Store)")
    with open(sys.argv[1], "wb") as out:
        out.write(store())


main()
