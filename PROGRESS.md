# Scribe Development Progress

## Requested Features
- [x] Image Pasting (Clipboard paste, file save to assets, canvas placement, move/resize/interact)
- [x] LaTeX Formatter (Formula editing, formatting, real-time rendering on page via WpfMath)
- [x] Table Support (Create tables, edit cells, add/remove rows and columns, Tab and arrow navigation)
- [x] Copy / Paste Tables (Copy table TSV/HTML to clipboard, paste table from TSV/Markdown/HTML/Scribe format)
- [x] Typography & Formatting:
  - [x] Handwritten font style option (Segoe Print, Ink Free, Segoe Script, Comic Sans MS, cursive)
  - [x] Font size options (11, 12, 13, 14, 15, 16, 18, 20, 24, 28, 32, 40 pt)
  - [x] Header and Title options (Title, Heading 1, Heading 2, Heading 3, Body, Handwritten presets)
  - [x] Bold, Italic, Underline, and Strikethrough options (with Ctrl+B, Ctrl+I, Ctrl+U, Ctrl+T shortcuts)
- [x] UI/Toolbar integration for new features (Formatting toolbar, table insertion tools, latex dialog/insert, image paste)
- [x] File format and storage serialization / deserialization tests

## Status Log
- Initial assessment completed.
- Added WpfMath package for high-fidelity LaTeX equation rendering.
- Implemented `PageLatexControl` and `LatexEditDialog` with live preview and math snippet buttons.
- Implemented `PageTableControl` and `TableInsertDialog` with cell editing, dynamic row/column operations, and TSV/HTML/Markdown copy-paste.
- Implemented `PageImageControl` with drag-to-move, corner resize handle, context menu, and clipboard paste.
- Implemented rich text `PageTextBox` with inline runs, bold, italic, underline, strikethrough, font size, font family (including handwritten), and heading styles.
- Updated `MainWindow.xaml` toolbar with Insert group (Table, LaTeX, Image) and Text Formatting strip.
- Updated `docs/FORMAT.md` and `README.md`.
- Verified single-file standalone build with `./build.sh` (`dist/Scribe.exe`).
