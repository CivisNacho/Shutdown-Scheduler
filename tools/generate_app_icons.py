"""Render every Assets/*.png and AppIcon.ico from the vector master Assets/AppIcon.svg.

Needs Google Chrome (headless, as the SVG rasterizer) and Pillow.
"""
import io, os, subprocess, tempfile
from pathlib import Path
from PIL import Image

ASSETS = Path(__file__).resolve().parent.parent / "Assets"
SVG = ASSETS / "AppIcon.svg"
CHROME = next(p for p in (r"C:\Program Files\Google\Chrome\Application\chrome.exe",
                          r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe") if os.path.exists(p))
PANEL = (24, 20, 40, 255)


def render(w, h):
    with tempfile.TemporaryDirectory() as tmp:
        html, out = Path(tmp, "i.html"), Path(tmp, "o.png")
        html.write_text(f'<body style="margin:0;background:transparent"><img src="{SVG.as_uri()}" '
                        f'style="width:{w}px;height:{h}px;display:block">')
        subprocess.run([CHROME, "--headless=new", f"--user-data-dir={tmp}/p", "--disable-gpu",
                        "--hide-scrollbars", "--default-background-color=00000000",
                        f"--window-size={w},{h}", f"--screenshot={out}", html.as_uri()],
                       check=True, capture_output=True)
        return Image.open(io.BytesIO(out.read_bytes())).convert("RGBA")


def on_panel(w, h, icon):
    img = Image.new("RGBA", (w, h), PANEL)
    img.alpha_composite(master.resize((icon, icon), Image.LANCZOS), ((w - icon) // 2, (h - icon) // 2))
    return img


master = render(1024, 1024)
master.save(ASSETS / "IconPreview.png")
for name, size in [("Square44x44Logo.scale-200.png", 88), ("Square150x150Logo.scale-200.png", 300),
                   ("Square44x44Logo.targetsize-24_altform-unplated.png", 24),
                   ("StoreLogo.png", 50), ("LockScreenLogo.scale-200.png", 48)]:
    master.resize((size, size), Image.LANCZOS).save(ASSETS / name)
on_panel(620, 300, 216).save(ASSETS / "Wide310x150Logo.scale-200.png")
on_panel(1240, 600, 300).save(ASSETS / "SplashScreen.scale-200.png")
render(256, 256).save(ASSETS / "AppIcon.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
