"""Prepara la ficha de Microsoft Store en el formato de importación por idioma.

Genera promo/store/ImportarIdiomas/ con la misma estructura que el ejemplo de Visor:

    ImportarIdiomas/
      Shutdown-<idioma>/
        listingData-<id>.csv          listado de ese idioma (una columna de idioma)
        comun/logo-300x300.png
        <idioma>/
          box-1x1-1080x1080.png, captura-1…6, hero-16x9-1920x1080.png,
          poster-2x3-720x1080.png, trailer-<idioma>-1920x1080.mp4, trailer-miniatura-1920x1080.png

Las rutas de los recursos en cada CSV son relativas a ImportarIdiomas, como en el ejemplo.
Textos en tools/promo/store/listing.json; materiales en promo/<idioma>/ (build_promo.py).

Uso desde la raíz del repo:
    python tools/promo/build_store.py "C:\\Users\\Nacho\\Downloads\\listingData-….csv"
"""

import argparse
import csv
import json
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_promo  # noqa: E402  (mismas plantillas de textos y conversión a tradicional)

ROOT = HERE.parents[1]
PROMO = ROOT / "promo"
IMPORT_ROOT = PROMO / "store" / "ImportarIdiomas"
LISTING = HERE / "store" / "listing.json"
PRODUCT_CSV = "listingData-9MW5FN3N9H39-1152921505702283852.csv"
DEV_STUDIO_FIELD = "DevStudio"
SCREENSHOT_NAMES = [
    "captura-1-horario", "captura-2-reloj", "captura-3-condiciones",
    "captura-4-tipos", "captura-5-widget", "captura-6-claro",
]
TEXT_FIELDS = ["Title", "ShortTitle", "ShortDescription", "Description", "ReleaseNotes"]
OLD_STORE_FOLDERS = ["es", "en-us", "de", "ar", "zh", "zh-hans", "ko", "fr", "hi", "it", "ja", "pl", "pt", "uk"]

# folder: nombre de la carpeta del idioma (como en el ejemplo)
# src: carpeta de promo/ con los materiales
# copy: textos de las capturas (build_promo copy/<copy>.json); None = listing.json["es"]["Captions"]
# column: columna del CSV del Centro de partners
# text: clave en listing.json de los textos del idioma
LOCALES = [
    dict(folder="es", src="es", copy=None, column="es", text=None),
    dict(folder="en", src="en", copy="en", column="en-us", text="en-us"),
    dict(folder="de", src="de", copy="de", column="de", text="de"),
    dict(folder="ar", src="ar", copy="ar", column="ar", text="ar"),
    dict(folder="zh-Hant", src="zh-Hant", copy="zh-Hant", column="zh", text="zh", traditional=True),
    dict(folder="zh-Hans", src="zh", copy="zh", column="zh-hans", text="zh"),
    dict(folder="ko", src="ko", copy="ko", column="ko", text="ko"),
    dict(folder="fr", src="fr", copy="fr", column="fr", text="fr"),
    dict(folder="hi", src="hi", copy="hi", column="hi", text="hi"),
    dict(folder="it", src="it", copy="it", column="it", text="it"),
    dict(folder="ja", src="ja", copy="ja", column="ja", text="ja"),
    dict(folder="pl", src="pl", copy="pl", column="pl", text="pl"),
    dict(folder="pt", src="pt", copy="pt", column="pt", text="pt"),
    dict(folder="uk", src="uk", copy="uk", column="uk", text="uk"),
]


def read_original(path):
    with path.open(encoding="utf-8-sig", newline="") as f:
        return list(csv.reader(f))


def text_values(loc, rows, listing):
    """Textos y características del idioma, por nombre de campo del CSV."""
    header = rows[0]
    values = {}
    is_original = loc["folder"] in ("es", "en")
    if is_original:
        index = header.index(loc["column"])
        for row in rows[1:]:
            if row and row[0] and row[index]:
                values[row[0]] = row[index]
    if loc["text"] is None:
        return values

    text = listing[loc["text"]]
    if loc.get("traditional"):
        text = build_promo.convert_tree(text)
    for field in TEXT_FIELDS:
        if text.get(field) and not (is_original and values.get(field)):
            values[field] = text[field]
    for i, feature in enumerate(text.get("Features", []), 1):
        if not (is_original and values.get(f"Feature{i}")):
            values[f"Feature{i}"] = feature
    for i, term in enumerate(text.get("SearchTerms", []), 1):
        if not (is_original and values.get(f"SearchTerm{i}")):
            values[f"SearchTerm{i}"] = term
    return values


def captions(loc, listing):
    if loc["copy"] is None:
        return listing["es"]["Captions"]
    return [shot["h"] for shot in build_promo.load_copy(loc["copy"])["shots"]]


def source_files(src_dir):
    shots = sorted((src_dir / "screenshots").glob("*.png"))[:6]
    thumb = src_dir / "trailer-thumbnail.png"
    if not thumb.exists():
        thumb = src_dir / "miniatura-trailer.png"
    trailer = sorted(src_dir.glob("trailer*.mp4"))[0]
    return {
        "shots": shots,
        "box": src_dir / "logos" / "logo-1x1-1080.png",
        "logo300": src_dir / "logos" / "logo-1x1-300.png",
        "poster": src_dir / "logos" / "poster-2x3-720x1080.png",
        "thumb": thumb,
        "trailer": trailer,
    }


def copy_locale(loc, rows, listing):
    folder = loc["folder"]
    base = IMPORT_ROOT / f"Shutdown-{folder}"
    lang_dir = base / folder
    common_dir = base / "comun"
    lang_dir.mkdir(parents=True)
    common_dir.mkdir(parents=True)

    src = source_files(PROMO / loc["src"])
    rel = lambda path: f"Shutdown-{folder}/{folder}/{path.name}" if path.parent == lang_dir else f"Shutdown-{folder}/comun/{path.name}"

    copies = {
        "box": lang_dir / "box-1x1-1080x1080.png",
        "poster": lang_dir / "poster-2x3-720x1080.png",
        "thumb": lang_dir / "hero-16x9-1920x1080.png",
        "trailer_thumb": lang_dir / "trailer-miniatura-1920x1080.png",
        "trailer": lang_dir / f"trailer-{folder}-1920x1080.mp4",
        "logo300": common_dir / "logo-300x300.png",
    }
    shutil.copyfile(src["box"], copies["box"])
    shutil.copyfile(src["poster"], copies["poster"])
    shutil.copyfile(src["thumb"], copies["thumb"])
    shutil.copyfile(src["thumb"], copies["trailer_thumb"])
    shutil.copyfile(src["trailer"], copies["trailer"])
    shutil.copyfile(src["logo300"], copies["logo300"])
    shot_paths = []
    for shot, name in zip(src["shots"], SCREENSHOT_NAMES):
        target = lang_dir / f"{name}.png"
        shutil.copyfile(shot, target)
        shot_paths.append(target)

    values = text_values(loc, rows, listing)
    caption_list = captions(loc, listing)
    values.update({f"DesktopScreenshot{i}": rel(p) for i, p in enumerate(shot_paths, 1)})
    values.update({f"DesktopScreenshotCaption{i}": c for i, c in enumerate(caption_list[:6], 1)})
    values["StoreLogo720x1080"] = rel(copies["poster"])
    values["StoreLogo1080x1080"] = rel(copies["box"])
    values["StoreLogo300x300"] = rel(copies["logo300"])
    values["PromoImage1920x1080"] = rel(copies["thumb"])
    values["TrailerThumbnail1"] = rel(copies["trailer_thumb"])
    values["Trailer1"] = rel(copies["trailer"])
    values["TrailerTitle1"] = values.get("Title", "")
    values["OverrideLogosForWin10"] = "False"
    if DEV_STUDIO_FIELD not in values:
        values[DEV_STUDIO_FIELD] = next((r[4] for r in rows if r and r[0] == DEV_STUDIO_FIELD and len(r) > 4), "")

    out_rows = [["Field", "ID", "Type (Tipo)", "default", loc["column"]]]
    for row in rows[1:]:
        if not row or not row[0]:
            out_rows.append(row[:] if row else [])
            continue
        out_rows.append([row[0], row[1], row[2], "", values.get(row[0], "")])
    out_csv = base / PRODUCT_CSV
    with out_csv.open("w", encoding="utf-8-sig", newline="") as f:
        csv.writer(f, lineterminator="\r\n").writerows(out_rows)
    filled = sum(1 for r in out_rows[1:] if r and r[0] and r[4])
    return out_csv, filled


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv", help="CSV de listado exportado del Centro de partners")
    args = parser.parse_args()

    rows = read_original(Path(args.csv))
    listing = json.loads(LISTING.read_text(encoding="utf-8"))

    for old in OLD_STORE_FOLDERS:
        shutil.rmtree(PROMO / "store" / old, ignore_errors=True)
    (PROMO / "store" / "listing-completed.csv").unlink(missing_ok=True)
    if IMPORT_ROOT.exists():
        shutil.rmtree(IMPORT_ROOT)
    IMPORT_ROOT.mkdir(parents=True)

    for loc in LOCALES:
        out_csv, filled = copy_locale(loc, rows, listing)
        print(f"[{loc['folder']}] {filled} campos rellenados -> {out_csv.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
