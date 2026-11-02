"""Generates the app icon (.ico) and the MSIX logo PNGs. Requires Pillow: pip install pillow"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
S = 1024


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def icon(size=S, plate=True):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    if plate:
        grad = Image.new("RGBA", (S, S))
        top, bottom = (79, 140, 255), (139, 92, 246)
        gd = ImageDraw.Draw(grad)
        for y in range(S):
            gd.line([(0, y), (S, y)], fill=lerp(top, bottom, y / S) + (255,))
        mask = Image.new("L", (S, S), 0)
        ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=220, fill=255)
        img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)
    # Magnifier
    cx, cy, r, w = 440, 470, 210, 70
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=white, width=w)
    d.line([(cx + 150, cy + 150), (cx + 330, cy + 330)], fill=white, width=w + 20)
    d.ellipse([cx + 330 - 45, cy + 330 - 45, cx + 330 + 45, cy + 330 + 45], fill=white)
    # Sparkle (four-point star)
    sx, sy, a, b = 760, 250, 120, 30
    d.polygon([(sx, sy - a), (sx + b, sy - b), (sx + a, sy), (sx + b, sy + b),
               (sx, sy + a), (sx - b, sy + b), (sx - a, sy), (sx - b, sy - b)], fill=white)
    return img.resize((size, size), Image.LANCZOS)


def tile(width, height, scale=0.6):
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    side = int(min(width, height) * scale)
    canvas.paste(icon(side), ((width - side) // 2, (height - side) // 2))
    return canvas


def main():
    app_assets = ROOT / "src" / "CopilotKnappen" / "Assets"
    pkg_assets = ROOT / "packaging" / "Assets"
    app_assets.mkdir(parents=True, exist_ok=True)
    pkg_assets.mkdir(parents=True, exist_ok=True)

    icon(256).save(app_assets / "app.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
    icon(44).save(pkg_assets / "Square44x44Logo.png")
    icon(50).save(pkg_assets / "StoreLogo.png")
    tile(150, 150, 0.66).save(pkg_assets / "Square150x150Logo.png")
    tile(310, 150, 0.66).save(pkg_assets / "Wide310x150Logo.png")
    tile(620, 300, 0.66).save(pkg_assets / "SplashScreen.png")
    print("Assets written to", app_assets, "and", pkg_assets)


if __name__ == "__main__":
    main()
