# Scribe

A standalone Windows notebook app for handwritten notes, built around the pen
first. Imports your OneNote notebooks, and keeps everything in plain folders and
JSON files on your own disk.

Ships as a **single `Scribe.exe`**. No installer, no .NET runtime to install, no
other files to copy.

---

## Build

The project targets Windows but builds from Linux, macOS or Windows:

```bash
./build.sh            # writes dist/Scribe.exe
./build.sh out/dir    # or choose the output folder
```

Requires the .NET 10 SDK. From WSL you can drop the result straight onto the
Windows side:

```bash
cp dist/Scribe.exe /mnt/c/Users/<you>/Desktop/
```

## The drawing surface

This is the part the app is built around.

- **Pressure-varying strokes.** Ink is rendered as a true outline whose width
  follows stylus pressure, so downstrokes thicken and strokes taper as the nib
  lands and lifts. WPF's stock ink stamps a fixed-size tip along the path, which
  is why default WPF ink looks like a marker; Scribe replaces that renderer
  (`Ink/StrokeGeometryBuilder.cs`).
- **Wet ink on the pen thread.** The line under the nib is drawn by a
  `DynamicRenderer` running on WPF's dedicated pen thread, so it keeps up with
  the digitiser even while the UI thread is laying out or saving.
- **Real palm rejection.** A resting palm registers as touch, never as pen, so
  touch simply does not draw. One finger pans, two fingers pinch to zoom. No
  contact-size heuristics, so no false positives. If you have no pen, the
  behaviour can be switched in code (`ScribeInkCanvas.TouchMode`).
- **Jitter smoothing.** Raw pen data is polygonal at speed; strokes are
  resampled along a Catmull-Rom spline and pressure is averaged over a short
  window so width does not pulse.
- **The pen's eraser end erases**, whatever tool is selected.

### Tools

| Key | Tool | Notes |
|-----|------|-------|
| `P` | Pen | Pressure-sensitive |
| `H` | Highlighter | Even width, translucent, sits under nothing |
| `E` | Eraser | Press again for a partial eraser that rubs out part of a stroke |
| `S` | Lasso | Select, move and resize ink |
| `T` | Text | Click anywhere to drop a text container |
| `Space` | Pan | Or drag with the middle mouse button |

`Ctrl+Z` / `Ctrl+Y` undo and redo, `Ctrl+N` new page, `Ctrl+F` search,
`Ctrl+S` save (pages also autosave). `Ctrl` + mouse wheel zooms.

## Importing from OneNote

Press **Import** in the top right, tick the notebooks you want, and import.
Nothing in OneNote is modified or removed.

Handwriting comes across as **real strokes with pressure preserved**, not as
pictures of writing: OneNote stores ink as base64 ISF inside `<one:InkData>`,
which is the same format WPF's own ink reads.

Requirements and limits:

- Needs the **OneNote desktop app** (the one that ships with Office, sometimes
  listed as "OneNote 2016"). The Store version does not expose the required
  interface.
- Only notebooks **currently open in OneNote** are listed. Open a notebook in
  OneNote and press Refresh if one is missing.
- Section groups are flattened into section names as `Group — Section`.
- Text arrives as plain text; character-level formatting (bold, colour, size)
  is not carried over. Page position, ink and images are.

For a large library there is a scripted path that skips the dialog:

```
Scribe.exe --import-onenote "D:\Notebooks"
```

It writes an `import-log.txt` alongside the notebooks.

## Where your notes live

By default `Documents\Scribe Notebooks`, as an ordinary folder tree:

```
Scribe Notebooks/
  My Notebook/
    notebook.json
    Quick Notes/
      section.json
      Meeting 2026-08-11.json
      assets/ab12cd.png
```

One JSON file per page, named after the page. You can open the folder in
Explorer, sync it with OneDrive or Dropbox, keep it in git, or read it with any
tool — the format is documented in [docs/FORMAT.md](docs/FORMAT.md). Writes go
through a temp file and an atomic swap, so a crash or a sync client reading
mid-write never sees half a page.

Right-click a notebook or section for **Reveal in File Explorer**.

## Themes

Dark by default; the sun/moon button in the toolbar switches.

Ink colour is adapted for display only — black handwriting shows as near-white
on a dark page and stays black in the file. A notebook written in dark mode
still looks right in light mode, and imported OneNote ink (almost always black)
is legible rather than invisible.

## Checking the ink engine

Ink cannot be exercised by UI automation: WPF collects it through the stylus
stack, which ignores injected mouse input. Instead there is a render test that
draws sample strokes through the identical code path the pen uses:

```
Scribe.exe --render-selftest out.png
```

The sheet shows pressure response, tapering, smoothing, the highlighter, and a
stroke round-tripped through ISF and the JSON format — the OneNote import path,
verifiable without OneNote.

## Layout of the source

| Path | What is in it |
|------|----------------|
| `Ink/` | The ink engine: geometry, renderers, tools, theme adaptation |
| `Controls/` | Page canvas, page background, text containers, prompt dialog |
| `Storage/` | Folder-format workspace, stroke codec, settings |
| `Import/` | OneNote COM binding and the importer |
| `Themes/` | Palettes, control styles, icon geometry |
| `Diagnostics/` | The ink render self-test |
| `tools/` | Icon generator, notes on re-deriving the OneNote vtable |

The app icon is generated, not hand-drawn — `tools/make_icon.py` rasterises it
with the same variable-width stroke maths the app uses, so the mark is a picture
of what Scribe does.

## Known gaps

- Text formatting is plain; no bold/italic/colour runs yet in the editor.
- Images import and display but cannot yet be inserted or resized in the app.
- Search covers page titles and typed text, not handwriting recognition.
- No page reordering by drag yet; order is held in `section.json`.
