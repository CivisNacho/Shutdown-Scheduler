# Material para Microsoft Store

Juegos completos para 13 idiomas, cada uno en su carpeta: `es/` (es-ES), `en/` (en-US), `ja/`, `hi/`, `zh/` (chino simplificado), `ko/`, `fr/`, `de/`, `pl/`, `uk/`, `ar/` (árabe, con diseño de derecha a izquierda), `pt/` (portugués neutro) e `it/`. Tema oscuro, y claro en la captura 06.

Los materiales de los idiomas nuevos los genera `tools/promo/build_promo.py` (ver «Regenerar» al final). Los textos de la interfaz que aparecen en las capturas salen de los diccionarios de localización de la app, y los textos de marketing de `tools/promo/copy/<idioma>.json`.

| Archivo | Dónde va en Partner Center | Formato |
| --- | --- | --- |
| `screenshots/01…06.png` | Ficha de la Store → Capturas de pantalla (Escritorio), en ese orden | PNG 1920×1080 |
| `trailer.mp4` | Ficha de la Store → Tráileres | MP4 H.264, 1920×1080, 30 fps, 34 s, audio AAC 192 kbps (−16 LUFS) |
| `miniatura-trailer.png` / `trailer-thumbnail.png` | Miniatura del tráiler (obligatoria al subir un tráiler) | PNG 1920×1080 |
| `logos/logo-1x1-1080.png`, `logos/logo-1x1-300.png` | Logotipos de la Store → 1:1 | PNG |
| `logos/poster-2x3-720x1080.png` | Logotipos de la Store → 2:3 (póster) | PNG |

`musica/trailer.mid` es la partitura del tráiler (General MIDI, sincronizada con los cortes de escena) y `musica/trailer.wav` su interpretación con el sintetizador MIDI de Windows (Microsoft GS Wavetable Synth), que es la pista del vídeo. Es música original compuesta para el tráiler.

La app que aparece en la condición «La app queda inactiva» es una ventana de ejemplo (`VideoRender.exe`, «Exportando vídeo…»), para no mostrar ventanas reales del equipo.

Los nombres de archivo de los idiomas nuevos son los mismos en cada carpeta (`screenshots/01-schedule.png`… `06-light-theme.png`, `trailer-thumbnail.png`, `logos/`, y `trailer<IDIOMA>.mp4`, p. ej. `trailerJA.mp4`). La música es la misma para todos los idiomas.

## Regenerar

Desde la raíz del repositorio (requiere Google Chrome o Edge, ffmpeg y Pillow):

```bash
python tools/promo/build_promo.py
python tools/promo/build_promo.py --langs ja,ar
```

Para cambiar un texto de marketing, edita `tools/promo/copy/<idioma>.json` y vuelve a ejecutar el script para ese idioma.
