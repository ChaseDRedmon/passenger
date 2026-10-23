#!/usr/bin/env python3
# The site's themes scene (just site-shots themes): one capture of the same window per look, stacked as a fanned deck,
# Passenger's own look in front and whole, every other one behind it showing its title bar, with the look's name as its
# solution's, and its left edge. shots.sh takes the captures (CAPTURES/<look>-<theme>.png, each the same size) and renders
# what this writes, an SVG with each capture inside it, with sips, as tools/dmg-background.svg is drawn.
#   python3 -I themes.py THEME CAPTURES OUT.svg
import base64
import struct
import sys

# Front to back: Passenger's own, then the order Settings lists them in (Themes.cs' Look).
LOOKS = ["passenger", "harbour", "sorbet", "nocturne", "saffron", "bonbon", "ledger", "slate", "orbit", "nightjar", "moss",
         "phosphor"]
# How far each window behind sits up and to the left of the one before it, in the captures' pixels (scale 2): the strip
# shows the title bar down past its words (they end 54 pixels down) and the stripe's edge beside the window in front.
STEP_X, STEP_Y = 40, 60
# Room round the deck for the windows' shadows.
MARGIN = 48
# macOS's window corners at scale 2.
RADIUS = 20


def size(png):
    with open(png, "rb") as f:
        head = f.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        sys.exit(f"themes.py: {png} is not a PNG")
    return struct.unpack(">II", head[16:24])


def main():
    if len(sys.argv) != 4 or sys.argv[1] not in ("dark", "light"):
        sys.exit("themes.py: THEME (dark or light) CAPTURES OUT.svg")
    theme, captures, out = sys.argv[1:]
    pngs = [f"{captures}/{look}-{theme}.png" for look in LOOKS]
    width, height = size(pngs[0])
    for png in pngs[1:]:
        if size(png) != (width, height):
            sys.exit(f"themes.py: {png} is {size(png)}, not {(width, height)} as the front one is")
    behind = len(LOOKS) - 1
    canvas_w = 2 * MARGIN + behind * STEP_X + width
    canvas_h = 2 * MARGIN + behind * STEP_Y + height
    # A window's edge, and the shadow it casts on the one behind it: soft and wide in the dark, faint in the light.
    edge = "#FFFFFF" if theme == "dark" else "#000000"
    edge_opacity = 0.16 if theme == "dark" else 0.14
    shadow_opacity = 0.11 if theme == "dark" else 0.035
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{canvas_w}" height="{canvas_h}" '
             f'viewBox="0 0 {canvas_w} {canvas_h}">',
             f'<defs><clipPath id="window"><rect width="{width}" height="{height}" rx="{RADIUS}"/></clipPath></defs>']
    # Back to front, so each window covers all but the strip of the ones behind it.
    for depth in range(behind, -1, -1):
        x = MARGIN + (behind - depth) * STEP_X
        y = MARGIN + (behind - depth) * STEP_Y
        with open(pngs[depth], "rb") as f:
            data = base64.b64encode(f.read()).decode("ascii")
        parts.append(f'<g transform="translate({x} {y})">')
        # A shadow of rounded rectangles each a little wider and fainter, which reads as a blur where sips draws none.
        for spread in (28, 20, 13, 7, 3):
            parts.append(f'<rect x="{-spread}" y="{-spread + spread // 3}" width="{width + 2 * spread}" '
                         f'height="{height + 2 * spread}" rx="{RADIUS + spread}" fill="#000000" '
                         f'fill-opacity="{shadow_opacity}"/>')
        parts.append(f'<g clip-path="url(#window)"><image width="{width}" height="{height}" '
                     f'href="data:image/png;base64,{data}"/></g>')
        parts.append(f'<rect x="0.5" y="0.5" width="{width - 1}" height="{height - 1}" rx="{RADIUS}" fill="none" '
                     f'stroke="{edge}" stroke-opacity="{edge_opacity}"/>')
        parts.append("</g>")
    parts.append("</svg>")
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(parts) + "\n")


main()
