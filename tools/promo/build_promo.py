"""Genera el material promocional de Microsoft Store para un idioma.

Produce, en promo/<idioma>/:
  screenshots/01..06 (1920x1080), trailer-thumbnail.png, logos/ y trailer<IDIOMA>.mp4.

Los textos de la interfaz que aparecen en las capturas salen de los diccionarios de
ShutdownScheduler.Core/Services/LocalizationService*.cs, y los textos de marketing de
tools/promo/copy/<idioma>.json. La música es la misma para todos (promo/musica/trailer.wav).

Uso desde la raíz del repo:
    python tools/promo/build_promo.py                 # los 11 idiomas nuevos
    python tools/promo/build_promo.py --langs en --out <carpeta>   # comprobar la plantilla
Requiere Google Chrome (o Microsoft Edge), ffmpeg y Pillow.
"""

import argparse
import json
import math
import re
import shutil
import subprocess
import tempfile
from html import escape
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SERVICES = ROOT / "ShutdownScheduler.Core" / "Services"
ICON_URI = (ROOT / "Assets" / "AppIcon.svg").as_uri()
MUSIC = ROOT / "promo" / "musica" / "trailer.wav"
BROWSERS = [
    Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe"),
    Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
]

NEW_LANGS = ["ja", "hi", "zh", "zh-Hant", "ko", "fr", "de", "pl", "uk", "ar", "pt", "it"]

# Idioma -> (archivo del diccionario, marcador donde empieza)
DICTIONARIES = {
    "en": ("LocalizationService.cs", "English = new()"),
    "es": ("LocalizationService.cs", "Spanish = new(English)"),
    "ja": ("LocalizationService.ja.cs", "Japanese = new()"),
    "hi": ("LocalizationService.hi.cs", "Hindi = new()"),
    "zh": ("LocalizationService.zh.cs", "Chinese = new()"),
    "ko": ("LocalizationService.ko.cs", "Korean = new()"),
    "fr": ("LocalizationService.fr.cs", "French = new()"),
    "de": ("LocalizationService.de.cs", "German = new()"),
    "pl": ("LocalizationService.pl.cs", "Polish = new()"),
    "uk": ("LocalizationService.uk.cs", "Ukrainian = new()"),
    "ar": ("LocalizationService.ar.cs", "Arabic = new()"),
    "pt": ("LocalizationService.pt.cs", "Portuguese = new()"),
    "it": ("LocalizationService.it.cs", "Italian = new()"),
}

ENTRY = re.compile(r'\["(\w+)"\]\s*=\s*"((?:[^"\\]|\\.)*)"')
SHOTS = ["01-schedule", "02-clock", "03-conditions", "04-condition-types", "05-widget", "06-light-theme"]
SCENE_FADE = 0.6
# Duración de cada escena del tráiler (la última se ajusta para que suene con la música completa).
SCENE_SECONDS = [3.8, 3.8, 4.2, 4.2, 6.2, 4.2, 5.8]

# Iconos de trazo (Fluent-like) usados en los paneles.
ICONS = {
    "calendar": '<rect x="3" y="4" width="18" height="17" rx="3"/><path d="M3 9h18M8 2v4M16 2v4"/>',
    "filter": '<path d="M3 4h18l-7 9v6l-4-2v-4z"/>',
    "box": '<rect x="3" y="4" width="18" height="16" rx="3"/><path d="M3 9h18"/>',
    "laptop": '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M2 20h20"/>',
    "timer": '<circle cx="12" cy="13" r="8"/><path d="M12 9v4l3 2M10 2h4"/>',
    "trash": '<path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13"/>',
    "refresh": '<path d="M20 12a8 8 0 1 1-2.3-5.7M20 4v5h-5"/>',
    "up": '<path d="M2.5 7.5 6 4l3.5 3.5"/>',
    "down": '<path d="M2.5 4.5 6 8l3.5-3.5"/>',
    "power": '<path d="M12 3v8M6.3 6.6a8 8 0 1 0 11.4 0"/>',
    "pip": '<rect x="3" y="5" width="18" height="14" rx="2"/><rect x="12" y="11" width="7" height="5" rx="1"/>',
    "clock": '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
}

BASE_CSS = """
*{box-sizing:border-box}
html,body{margin:0;overflow:hidden}
body{
  --accent:#ece6da;--accent-ink:#17141f;--text:#f3efe7;--muted:#9b978f;--win:#0d0e10;
  --surface:#131517;--panel:#1c1f23;--field:#25282e;--line:#2d3138;--pink:#ff4d84;
  color:var(--text);
  font-family:"Segoe UI Variable Display","Segoe UI",system-ui,"Yu Gothic UI","Meiryo","Microsoft YaHei UI","Malgun Gothic","Nirmala UI","Leelawadee UI",sans-serif;
  -webkit-font-smoothing:antialiased;
}
body.light{
  --accent:#1d1c1a;--accent-ink:#f7f4ee;--text:#1d1c1a;--muted:#7d786e;--win:#ffffff;
  --surface:#fbfaf7;--panel:#ffffff;--field:#f3f1ec;--line:#e4dfd5;
}
.stage{position:absolute;left:0;top:0;overflow:hidden;background:radial-gradient(120% 90% at 30% 35%,#2a2622 0%,#1b1918 55%,#0f1012 100%)}
body.light .stage{background:radial-gradient(120% 90% at 30% 35%,#fbfaf6 0%,#f1ede4 100%)}
.abs{position:absolute}
.copy{position:absolute}
.eyebrow{display:flex;align-items:center;gap:12px;font-size:16px;letter-spacing:.08em;text-transform:uppercase;color:#c9c3b8;font-weight:600;margin-bottom:26px}
body.light .eyebrow{color:#6d685e}
.eyebrow i{width:10px;height:10px;border-radius:50%;background:var(--pink);display:inline-block;flex:none}
h1{margin:0;font-weight:600;line-height:1.12;letter-spacing:-.01em;word-break:auto-phrase}
.copy p{margin:24px 0 0;font-size:27px;line-height:1.45;color:#a9a399}
body.light .copy p{color:#6d685e}
.win{position:absolute;background:var(--win);border-radius:22px;padding:14px;box-shadow:0 40px 90px rgba(0,0,0,.45)}
.hdr{height:132px;background:var(--surface);border-radius:16px;display:flex;align-items:center;gap:22px;padding:0 24px;margin-bottom:12px}
.hdr img{width:64px;height:64px;border-radius:16px;flex:none}
.htxt{display:flex;flex-direction:column;min-width:0;flex:1}
.htxt small{font-size:13px;color:var(--muted)}
.htxt big{font-size:46px;font-weight:500;line-height:1.1;font-variant-numeric:tabular-nums}
.htxt span{font-size:15px;color:var(--muted);margin-top:2px}
.htxt em{font-style:normal;font-size:14px;color:var(--muted);margin-top:3px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.btn{height:42px;padding:0 22px;border-radius:10px;font:600 16px inherit;border:1px solid var(--line);background:var(--field);color:var(--text);display:flex;align-items:center;white-space:nowrap;font-family:inherit}
.btn.acc{background:var(--accent);color:var(--accent-ink);border-color:transparent}
.btn.dis{opacity:.45}
.cols{display:flex;gap:12px;height:548px}
.panel{flex:1;min-width:0;background:var(--panel);border-radius:16px;padding:18px;display:flex;flex-direction:column;gap:14px}
.ph{display:flex;align-items:center;gap:12px}
.pico{width:40px;height:40px;border-radius:10px;background:var(--field);display:grid;place-items:center;color:var(--muted);flex:none}
.pt{min-width:0;flex:1}
.pt b{display:block;font-size:20px;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.pt small{display:block;font-size:13px;color:var(--muted);margin-top:2px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.add{height:36px;padding:0 14px;border-radius:9px;background:var(--field);border:1px solid var(--line);color:var(--text);font:600 14px inherit;font-family:inherit;white-space:nowrap}
.fl{font-size:13px;color:var(--muted);margin-bottom:-6px}
.sel,.num{height:44px;background:var(--field);border-radius:8px;border:1px solid var(--line);display:flex;align-items:center;justify-content:space-between;padding:0 14px;font-size:16px;white-space:nowrap;overflow:hidden}
.num{background:var(--panel)}
.sel span,.num span{overflow:hidden;text-overflow:ellipsis}
.chk{display:flex;align-items:center;gap:12px;font-size:16px}
.box{width:18px;height:18px;border:1.5px solid var(--muted);border-radius:4px;flex:none}
.clock{position:relative;align-self:center;background:var(--field);border-radius:14px;flex:none}
.clock svg{position:absolute;left:0;top:0}
.tc{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;text-align:center}
.tval{font-size:38px;font-weight:600;line-height:1;font-variant-numeric:tabular-nums}
.tval span{font-size:16px;color:var(--muted);margin-inline-start:6px;font-weight:500}
.tc small{font-size:14px;color:var(--muted);margin-top:6px}
.chips,.days{display:flex;gap:8px;justify-content:center;flex-wrap:nowrap}
.chip,.day{height:36px;padding:0 12px;border-radius:18px;font-size:14px;display:flex;align-items:center;white-space:nowrap;border:1px solid var(--line)}
.chip{border-color:transparent;background:var(--field)}
.day{border-color:transparent}
.day.on{background:var(--accent);color:var(--accent-ink)}
.card{background:var(--field);border-radius:12px;padding:14px;display:flex;flex-direction:column;gap:10px}
.ct{display:flex;align-items:center;gap:10px;font-size:17px;font-weight:600}
.ct .tr{margin-inline-start:auto;color:var(--muted);display:flex}
.tog{display:flex;align-items:center;justify-content:space-between;font-size:16px;padding:9px 0;white-space:nowrap}
.switch{width:46px;height:24px;border-radius:12px;background:var(--line);position:relative;flex:none}
.switch::after{content:"";position:absolute;left:4px;top:4px;width:16px;height:16px;border-radius:50%;background:var(--muted)}
.switch.on{background:var(--accent)}
.switch.on::after{left:26px;background:var(--accent-ink)}
.dlg{position:absolute;width:444px;background:var(--panel);border-radius:18px;padding:22px;border:1px solid var(--line);box-shadow:0 30px 70px rgba(0,0,0,.5)}
.dlg h2{margin:0;font-size:20px;font-weight:600}
.dlg .sub{font-size:13px;color:var(--muted);margin-top:4px}
.opt{display:flex;gap:12px;align-items:flex-start;padding:12px;border-radius:10px;background:var(--field);margin-top:8px}
.opt b{display:block;font-size:15px;font-weight:600}
.opt small{display:block;font-size:12.5px;color:var(--muted);margin-top:2px}
.opt .pico{width:34px;height:34px}
.dlg .foot{display:flex;justify-content:flex-end;margin-top:16px}
.float{position:absolute;width:718px;height:184px;border-radius:26px;background:var(--panel);border:1px solid var(--line);box-shadow:0 30px 70px rgba(0,0,0,.55);display:flex;align-items:center;gap:26px;padding:0 34px}
.float img{width:96px;height:96px;border-radius:22px;flex:none}
.float .ft small{display:block;font-size:24px;color:var(--muted)}
.float .ft big{display:block;font-size:64px;font-weight:500;line-height:1.05;font-variant-numeric:tabular-nums}
.float .fb{margin-inline-start:auto;display:flex;gap:12px}
.float .fb span{width:48px;height:48px;border-radius:50%;background:var(--field);display:grid;place-items:center;color:var(--muted)}
.card-shot{position:absolute;background:var(--panel);border-radius:22px;padding:22px;border:1px solid var(--line);box-shadow:0 40px 90px rgba(0,0,0,.45);display:flex;flex-direction:column;gap:14px}
.brand{position:absolute;left:0;top:0;width:100%;height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;text-align:center}
"""


def esc(text):
    return escape(text, quote=True)


TRADITIONAL_FIXES = {"空闲": "閒置"}  # OpenCC convierte «空闲» en «空間»


def to_traditional(text):
    """Chino simplificado -> tradicional (Taiwán). Requiere el paquete opencc-python-reimplemented."""
    import opencc

    for simplified, traditional in TRADITIONAL_FIXES.items():
        text = text.replace(simplified, traditional)
    return opencc.OpenCC("s2twp").convert(text)


def convert_tree(value):
    if isinstance(value, str):
        return to_traditional(value)
    if isinstance(value, list):
        return [convert_tree(v) for v in value]
    if isinstance(value, dict):
        return {k: convert_tree(v) for k, v in value.items()}
    return value


def load_strings(code):
    if code == "zh-Hant":
        return {k: to_traditional(v) for k, v in load_strings("zh").items()}
    filename, marker = DICTIONARIES[code]
    text = (SERVICES / filename).read_text(encoding="utf-8")
    start = text.index(marker)
    end = text.index("\n    };", start)
    return dict(ENTRY.findall(text[start:end]))


def load_copy(code):
    if code == "zh-Hant":
        return convert_tree(load_copy("zh"))
    return json.loads((HERE / "copy" / f"{code}.json").read_text(encoding="utf-8"))


def ico(name, size=18):
    return (
        f'<svg width="{size}" height="{size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
        f'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">{ICONS[name]}</svg>'
    )


def place(x, y, w, rtl, extra=""):
    """Posición absoluta; en árabe el diseño se refleja horizontalmente."""
    left = 1920 - x - w if rtl else x
    side = "right" if rtl else "left"
    return f"{side}:{left}px;top:{y}px;width:{w}px;{extra}"


def clock_svg(size):
    r = size / 2
    c = size / 2
    rim = r * 0.9
    parts = []
    for i in range(60):
        a = math.radians(i * 6)
        inner = r * (0.78 if i % 5 == 0 else 0.84)
        width = 2 if i % 5 == 0 else 1
        parts.append(
            f'<line x1="{c + rim * math.sin(a):.1f}" y1="{c - rim * math.cos(a):.1f}" '
            f'x2="{c + inner * math.sin(a):.1f}" y2="{c - inner * math.cos(a):.1f}" '
            f'stroke="#5a5f68" stroke-width="{width}"/>'
        )

    def point(deg, radius):
        a = math.radians(deg)
        return c + radius * math.sin(a), c - radius * math.cos(a)

    arc_r = r * 0.66
    x0, y0 = point(195, arc_r)
    x1, y1 = point(244, arc_r)
    kx, ky = point(244, arc_r)
    parts.append(
        f'<path d="M{x0:.1f} {y0:.1f} A{arc_r:.1f} {arc_r:.1f} 0 0 1 {x1:.1f} {y1:.1f}" '
        f'fill="none" stroke="#ece6da" stroke-width="7" stroke-linecap="round"/>'
    )
    parts.append(f'<circle cx="{kx:.1f}" cy="{ky:.1f}" r="8" fill="#ece6da"/>')
    return f'<svg width="{size}" height="{size}" viewBox="0 0 {size} {size}">{"".join(parts)}</svg>'


def clock_block(cfg, size):
    if cfg.get("clock_12h"):
        value = '8:05<span>PM</span>'
    else:
        value = "20:05"
    in_text = cfg["in_fmt"].format(h=1, m=35)
    return (
        f'<div class="clock" style="width:{size}px;height:{size}px">{clock_svg(size)}'
        f'<div class="tc"><div class="tval">{value}</div><small>{esc(in_text)}</small></div></div>'
    )


def field(label, value):
    return f'<div class="fl">{esc(label)}</div><div class="sel"><span>{esc(value)}</span>{ico("down", 12)}</div>'


def schedule_panel(S, cfg, big):
    today = esc(S["Today"])
    tomorrow = esc(S["Tomorrow"])
    wd = cfg["weekdays"]
    chips = "".join(f'<span class="chip">{esc(c)}</span>' for c in cfg["chips"])
    days = (
        f'<span class="day on">{today}</span><span class="day">{tomorrow}</span>'
        f'<span class="day">{esc(wd[0])} 23</span><span class="day">{esc(wd[1])} 24</span>'
        f'<span class="day">{ico("calendar", 16)}</span>'
    )
    body = (
        field(S["Action"], S["Action_Shutdown"])
        + f'<div class="chk"><span class="box"></span>{esc(S["ForceAdmin"])}</div>'
        + clock_block(cfg, 220 if big else 196)
        + f'<div class="chips">{chips}</div><div class="days">{days}</div>'
    )
    return panel(ico("calendar"), S["Schedule"], S["ScheduleHint"], body)


def conditions_panel(S, cfg, add_label=True):
    add = f'<button class="add">+ {esc(S["AddShort"])}</button>' if add_label else ""
    card1 = (
        f'<div class="card"><div class="ct"><span>{ico("laptop", 16)}</span>{esc(S["CpuBelow"])}'
        f'<span class="tr">{ico("trash")}</span></div>'
        f'<div class="fl">{esc(S["CpuShort"])}</div><div class="num"><span>20</span>'
        f'<span>{ico("up", 12)}&nbsp;&nbsp;{ico("down", 12)}</span></div></div>'
    )
    app = f'{cfg["example_app"]} (VideoRender.exe)'
    card2 = (
        f'<div class="card"><div class="ct"><span>{ico("timer", 16)}</span>{esc(S["AppIdleBelow"])}'
        f'<span class="tr">{ico("trash")}</span></div>'
        f'<div class="fl">{esc(S["AppIdleApp"])}</div>'
        f'<div class="sel"><span>{esc(app)}</span>{ico("down", 12)}</div>'
        f'<div class="fl">{esc(S["AppIdleMinutes"])}</div><div class="num"><span>5</span>'
        f'<span>{ico("up", 12)}&nbsp;&nbsp;{ico("down", 12)}</span></div></div>'
    )
    return panel(ico("filter"), S["Conditions"], S["ConditionsSectionHint"], card1 + card2, add)


def widget_panel(S):
    def toggle(label, on):
        cls = "switch on" if on else "switch"
        return f'<div class="tog"><span>{esc(label)}</span><span class="{cls}"></span></div>'

    body = (
        f'<div class="card">{toggle(S["EnableWidget"], True)}{toggle(S["ShowOnAllScreens"], False)}</div>'
        + field(S["WidgetMode"], S["WidgetCountdown"])
        + field(S["WidgetSize"], S["WidgetMedium"])
        + field(S["WidgetPosition"], S["WidgetAnchorBottomRight"])
    )
    return panel(ico("box"), S["Widget"], S["WidgetHint"], body)


def panel(icon, title, hint, body, head_extra=""):
    return (
        f'<section class="panel"><header class="ph"><span class="pico">{icon}</span>'
        f'<div class="pt"><b>{esc(title)}</b><small>{esc(hint)}</small></div>{head_extra}</header>'
        f"{body}</section>"
    )


def app_window(S, cfg, state, dialog=""):
    if state == "empty":
        title_block = f'<big>{esc(S["NoScheduleTitle"])}</big><span>{esc(S["NoScheduleHint"])}</span>'
        buttons = (
            f'<button class="btn acc">{esc(S["ScheduleButton"])}</button>'
            f'<button class="btn dis">{esc(S["CancelButton"])}</button>'
        )
    else:
        when = cfg["when"]
        cond = f'{S["CpuShort"]} &lt; 20% {S["And"]} {cfg["example_app"]} (VideoRender.exe): {S["AppIdleShort"]} ≥ 5 min'
        title_block = (
            f'<big>01:35:19</big><span>{esc(S["Action_Shutdown"])} · {esc(when)}</span>'
            f'<em>{esc(S["Status_Scheduled"])} · {cond}</em>'
        )
        buttons = (
            f'<button class="btn acc">{esc(S["ReplaceButton"])}</button>'
            f'<button class="btn">{esc(S["CancelButton"])}</button>'
        )
    header = (
        f'<div class="hdr"><img src="{ICON_URI}" alt=""><div class="htxt">'
        f'<small>{esc(S["AppTitle"])}</small>{title_block}</div>'
        f'<div style="display:flex;gap:10px">{buttons}</div></div>'
    )
    cols = (
        f'<div class="cols">{schedule_panel(S, cfg, False)}{conditions_panel(S, cfg)}'
        f'{widget_panel(S)}</div>'
    )
    return header + cols + dialog


def dialog(S):
    options = [
        (S["NetworkBelow"], S["NetworkConditionHint"], "laptop"),
        (S["CpuBelow"], S["CpuConditionHint"], "laptop"),
        (S["BatteryBelow"], S["BatteryConditionHint"], "box"),
        (S["AppIdleBelow"], S["AppIdleConditionHint"], "timer"),
    ]
    rows = "".join(
        f'<div class="opt"><span class="pico">{ico(icon, 16)}</span><div><b>{esc(t)}</b>'
        f'<small>{esc(h)}</small></div></div>'
        for t, h, icon in options
    )
    return (
        f'<div class="dlg" style="left:352px;top:118px"><h2>{esc(S["AddConditionDialogTitle"])}</h2>'
        f'<div class="sub">{esc(S["ConditionsSectionHint"])}</div>{rows}'
        f'<div class="foot"><button class="btn">{esc(S["CancelButton"])}</button></div></div>'
    )


def copy_block(S, cfg, index, x, y, w, rtl):
    shot = cfg["shots"][index]
    scale = cfg.get("headline_scale", 1.0)
    length = len(shot["h"])
    size = 68 if length <= 34 else 60 if length <= 48 else 52
    return (
        f'<div class="copy" style="{place(x, y, w, rtl)}">'
        f'<div class="eyebrow"><i></i>{esc(S["AppTitle"].upper())}</div>'
        f'<h1 style="font-size:{round(size * scale)}px">{esc(shot["h"])}</h1>'
        f'<p>{esc(shot["p"])}</p></div>'
    )


def page(body, code, rtl, theme="dark", extra_css="", bg="#1b1918", w=1920, h=1080):
    size = f"width:{w}px;height:{h}px"
    return (
        f'<!doctype html><html lang="{code}" dir="{"rtl" if rtl else "ltr"}" '
        f'style="{size};background:{bg}"><head><meta charset="utf-8">'
        f"<style>{BASE_CSS}{extra_css}</style></head>"
        f'<body class="{theme}" style="{size}"><div class="stage" style="{size}">'
        f"{body}</div></body></html>"
    )


def window_at(content, x, y, rtl, w=1148, h=710, extra=""):
    return f'<div class="win" style="{place(x, y, w, rtl, f"height:{h}px;{extra}")}">{content}</div>'


def shot_full(S, cfg, rtl, index, state="full", dialog_html="", theme="dark"):
    body = copy_block(S, cfg, index, 110, 300, 560, rtl)
    body += window_at(app_window(S, cfg, state, dialog_html), 700, 185, rtl)
    return page(body, cfg["code"], rtl, theme)


def shot_clock(S, cfg, rtl):
    body = copy_block(S, cfg, 1, 110, 340, 560, rtl)
    inner = (
        f'<div class="ph"><span class="pico">{ico("calendar")}</span><div class="pt"><b>{esc(S["Schedule"])}</b>'
        f'<small>{esc(S["ScheduleHint"])}</small></div></div>'
    )
    wd = cfg["weekdays"]
    chips = "".join(f'<span class="chip">{esc(c)}</span>' for c in cfg["chips"])
    days = (
        f'<span class="day on">{esc(S["Today"])}</span><span class="day">{esc(S["Tomorrow"])}</span>'
        f'<span class="day">{esc(wd[0])} 23</span><span class="day">{esc(wd[1])} 24</span>'
        f'<span class="day">{ico("calendar", 16)}</span>'
    )
    card_body = (
        field(S["Action"], S["Action_Shutdown"])
        + f'<div class="chk"><span class="box"></span>{esc(S["ForceAdmin"])}</div>'
        + clock_block(cfg, 300)
        + f'<div class="chips">{chips}</div><div class="days">{days}</div>'
    )
    body += (
        f'<div class="card-shot" style="{place(1086, 120, 584, rtl, "height:836px;padding:26px")}">'
        f"{inner}{card_body}</div>"
    )
    return page(body, cfg["code"], rtl)


def shot_conditions(S, cfg, rtl):
    body = copy_block(S, cfg, 2, 110, 380, 560, rtl)
    inner = conditions_panel(S, cfg).replace('class="panel"', 'class="panel" style="height:100%"')
    body += f'<div class="card-shot" style="{place(1086, 190, 588, rtl, "height:700px;padding:0;background:transparent;border:0;box-shadow:none")}">{inner}</div>'
    return page(body, cfg["code"], rtl)


def shot_condition_types(S, cfg, rtl):
    return shot_full(S, cfg, rtl, 3, state="empty", dialog_html=dialog(S))


def shot_widget(S, cfg, rtl):
    body = copy_block(S, cfg, 4, 110, 360, 560, rtl)
    ghost = app_window(S, cfg, "full")
    body += (
        f'<div class="win" style="{place(820, 120, 1148, rtl, "height:710px;opacity:.42;filter:blur(5px);transform-origin:0 0;transform:scale(.871)")}">'
        f"{ghost}</div>"
    )
    float_html = (
        f'<img src="{ICON_URI}" alt=""><div class="ft"><small>{esc(S["Action_Shutdown"])}</small>'
        f'<big>01:35:18</big></div><div class="fb"><span>{ico("laptop", 20)}</span>'
        f'<span>{ico("timer", 20)}</span></div>'
    )
    body += f'<div class="float" style="{place(1050, 640, 718, rtl, "height:184px")}">{float_html}</div>'
    return page(body, cfg["code"], rtl)


def shot_light(S, cfg, rtl):
    body = copy_block(S, cfg, 5, 110, 330, 560, rtl)
    body += window_at(app_window(S, cfg, "full"), 700, 185, rtl)
    return page(body, cfg["code"], rtl, theme="light")


def brand_card(S, cfg):
    title = esc(S["AppTitle"])
    body = (
        f'<div class="brand"><img src="{ICON_URI}" alt="" style="width:270px;height:270px;'
        f'border-radius:60px;margin-bottom:60px">'
        f'<h1 style="font-size:84px">{title}</h1>'
        f'<p style="margin:18px 0 0;font-size:34px;color:#a9a399">{esc(cfg["tagline"])}</p></div>'
    )
    return page(body, cfg["code"], cfg.get("rtl", False))


def logo_square(S, cfg, size):
    title = S["AppTitle"]
    font = 64 if len(title) <= 22 else 52 if len(title) <= 30 else 42
    body = (
        f'<div class="brand" style="justify-content:flex-start;padding-top:{round(size * 0.22)}px">'
        f'<img src="{ICON_URI}" alt="" style="width:{round(size * 0.417)}px;height:{round(size * 0.417)}px">'
        f'<h1 style="margin-top:{round(size * 0.07)}px;font-size:{font}px;max-width:{round(size * 0.9)}px;'
        f'font-weight:500">{esc(title)}</h1></div>'
    )
    css = "body{background:#25211d}"
    return page(body, cfg["code"], cfg.get("rtl", False), extra_css=css, bg="#25211d", w=size, h=size)


def logo_poster(S, cfg):
    title = S["AppTitle"]
    font = 58 if len(title) <= 22 else 48 if len(title) <= 30 else 40
    body = (
        f'<div class="brand" style="justify-content:flex-start;padding-top:295px">'
        f'<img src="{ICON_URI}" alt="" style="width:300px;height:300px">'
        f'<h1 style="margin-top:56px;font-size:{font}px;max-width:560px;font-weight:500;line-height:1.18">'
        f"{esc(title)}</h1></div>"
    )
    css = "body{background:#25211d}"
    return page(body, cfg["code"], cfg.get("rtl", False), extra_css=css, bg="#25211d", w=720, h=1080)


def render_png(html, png, width, height, profile, html_file):
    browser = next((b for b in BROWSERS if b.exists()), None)
    if browser is None:
        raise SystemExit("No se encontró Chrome ni Edge.")
    html_file.write_text(html, encoding="utf-8")
    subprocess.run(
        [
            str(browser),
            "--headless=new",
            "--disable-gpu",
            "--hide-scrollbars",
            "--force-device-scale-factor=1",
            f"--user-data-dir={profile}",
            f"--window-size={width},{height}",
            "--virtual-time-budget=1500",
            f"--screenshot={png}",
            html_file.as_uri(),
        ],
        check=True,
        capture_output=True,
    )
    image = Image.open(png)
    if image.size != (width, height):
        raise SystemExit(f"{png.name}: tamaño {image.size}, se esperaba {(width, height)}")


def still_scene(src, dst):
    return f"[{src}]scale=1920:1080,setsar=1,format=yuv420p,fps=30[{dst}]"


def build_trailer(scenes, out_file, work):
    """scenes: lista de PNG (1920x1080) en el orden del tráiler."""
    total_music = float(subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(MUSIC)],
        capture_output=True, text=True, check=True,
    ).stdout)
    durations = list(SCENE_SECONDS)
    last = total_music + (len(scenes) - 1) * SCENE_FADE - sum(durations)
    durations.append(round(last, 3))

    cmd = ["ffmpeg", "-y", "-v", "error"]
    for png, seconds in zip(scenes, durations):
        cmd += ["-loop", "1", "-framerate", "30", "-t", f"{seconds:.3f}", "-i", str(png)]
    cmd += ["-i", str(MUSIC)]

    filters = [still_scene(f"{i}:v", f"v{i}") for i in range(len(scenes))]
    chain = "v0"
    offset = 0.0
    for i in range(1, len(scenes)):
        offset += durations[i - 1] - SCENE_FADE
        label = f"x{i}"
        filters.append(
            f"[{chain}][v{i}]xfade=transition=fade:duration={SCENE_FADE}:offset={offset:.3f}[{label}]"
        )
        chain = label
    audio = len(scenes)
    filters.append(
        f"[{audio}:a]loudnorm=I=-16:TP=-1.5:LRA=11,"
        f"afade=t=out:st={total_music - 1.5:.3f}:d=1.5[a]"
    )
    cmd += [
        "-filter_complex", ";".join(filters),
        "-map", f"[{chain}]", "-map", "[a]",
        "-r", "30", "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-t", f"{total_music:.3f}", "-movflags", "+faststart",
        str(out_file),
    ]
    subprocess.run(cmd, check=True)


def build_language(code, out_root, work, trailer_only=False):
    out = out_root / code
    (out / "screenshots").mkdir(parents=True, exist_ok=True)
    (out / "logos").mkdir(parents=True, exist_ok=True)
    tmp = work / code
    tmp.mkdir(parents=True, exist_ok=True)

    if trailer_only:
        s = lambda name: out / "screenshots" / f"{name}.png"
        scenes = [
            out / "trailer-thumbnail.png",
            s("02-clock"),
            s("04-condition-types"),
            s("03-conditions"),
            s("01-schedule"),
            s("05-widget"),
            s("06-light-theme"),
            out / "trailer-thumbnail.png",
        ]
        build_trailer(scenes, out / f"trailer{code.upper()}.mp4", tmp)
        shutil.rmtree(tmp, ignore_errors=True)
        return

    S = load_strings(code)
    cfg = load_copy(code)
    cfg["code"] = code
    rtl = cfg.get("rtl", False)
    profile = work / "chrome-profile"

    renders = {
        "01-schedule": lambda: shot_full(S, cfg, rtl, 0),
        "02-clock": lambda: shot_clock(S, cfg, rtl),
        "03-conditions": lambda: shot_conditions(S, cfg, rtl),
        "04-condition-types": lambda: shot_condition_types(S, cfg, rtl),
        "05-widget": lambda: shot_widget(S, cfg, rtl),
        "06-light-theme": lambda: shot_light(S, cfg, rtl),
    }
    def draw(html, png, width, height):
        render_png(html, png, width, height, profile, tmp / f"{png.stem}.html")

    for name in SHOTS:
        draw(renders[name](), out / "screenshots" / f"{name}.png", 1920, 1080)

    thumb = out / "trailer-thumbnail.png"
    draw(brand_card(S, cfg), thumb, 1920, 1080)

    logo_big = out / "logos" / "logo-1x1-1080.png"
    draw(logo_square(S, cfg, 1080), logo_big, 1080, 1080)
    Image.open(logo_big).resize((300, 300), Image.LANCZOS).save(out / "logos" / "logo-1x1-300.png")

    draw(logo_poster(S, cfg), out / "logos" / "poster-2x3-720x1080.png", 720, 1080)

    s = lambda name: out / "screenshots" / f"{name}.png"
    scenes = [
        thumb,
        s("02-clock"),
        s("04-condition-types"),
        s("03-conditions"),
        s("01-schedule"),
        s("05-widget"),
        s("06-light-theme"),
        thumb,
    ]
    build_trailer(scenes, out / f"trailer{code.upper()}.mp4", tmp)
    shutil.rmtree(tmp, ignore_errors=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--langs", default=",".join(NEW_LANGS), help="códigos separados por comas")
    parser.add_argument("--out", default=str(ROOT / "promo"), help="carpeta de salida")
    parser.add_argument("--trailers-only", action="store_true",
                        help="regenera solo los tráileres con las capturas ya existentes")
    args = parser.parse_args()

    out_root = Path(args.out).resolve()
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        for code in args.langs.split(","):
            print(f"[{code}] generando…", flush=True)
            build_language(code.strip(), out_root, work, trailer_only=args.trailers_only)
    print("Listo.")


if __name__ == "__main__":
    main()
