<p align="center">
  <img src="assets/icon-256.png" width="96" alt="WFastPic logo">
</p>

# WFastPic

**The open source successor to Google Picasa for Windows: pick and fix hundreds of photos fast.**
Current version: **0.9.1** · [Download](https://github.com/Walt7/WFastPic/releases/latest) ·
[Changelog](CHANGELOG.en.md) · [Italiano](README.md)

Google stopped developing Picasa in 2016, and it does not read the HEIC photos phones take today.
WFastPic keeps the way Picasa worked — folders by year, the space bar to keep the good shots,
one-click fixes, people, map, export — and is built for large photo collections, especially **HEIC**
(iPhones, drones, cameras): open the vacation folder, keep the best, reject the rest, make a few quick
fixes and export. All from the keyboard, without ever waiting.

It is not Lightroom and not a file manager: it is the tool for going from 1,300 shots to the 80 photos
worth keeping, in as few gestures as possible.

> The app interface is in Italian. Everything else (this page, the changelog) is available in both languages.

## From Picasa to WFastPic

| In Picasa | In WFastPic |
|---|---|
| Folders grouped by year | Folder panel by year, with photo count and period |
| Stars and "kept" photos | `Space` selects, `0`–`5` stars, `Delete` rejects (never an irreversible delete) |
| "I'm Feeling Lucky" | `U`: automatic contrast and color in one go |
| Crop and straighten | Crop with ratios and suggested crops, straightening together with cropping |
| People (named faces) | Face recognition on your PC, names shared across folders, "Same person as…" |
| Photos on a map | Photo map and place names |
| Export | Resized JPEGs, watermark, tags, date and GPS preserved |
| — | Bursts detected automatically, HEIC photos, Live Photos, DJI drone data |

**What is not there (by choice or not yet):** collages and slideshows, web albums, video playback,
advanced photo editing (curves, masks). WFastPic does not upload your photos anywhere.

## What it does

**Pick**
- Fast grid even with thousands of photos, in shooting order, optionally split by day.
- `Space` selects, `Delete` rejects, `0`–`5` stars, `Tab` switches the filter.
- **Bursts** detected automatically (photos taken seconds apart): `Shift+Space` keeps this one and
  rejects the rest of the group. In the editor the photos of the same burst appear small next to the
  photo, so you can compare them at a glance.
- Rejects are never deleted by accident: they go to the Windows Recycle Bin (with confirmation) or to a
  `Scarti/` folder, always together with the paired video (Live Photo).

**Fix** (the original is never touched)
- Crop with ratios and suggested crops that do not cut faces; straighten; rotate by 90° and flip.
- "I'm Feeling Lucky", automatic contrast and color, fill light, highlights, shadows, temperature.
- Undo / redo, `B` to see the original, full screen with `F11`.
- Details column: weekday, date and time, file, place with mini-map, device and shooting data
  (including DJI drone altitude and gimbal).

**Find again**
- Folders grouped by year, like Picasa.
- Photo map, place names, location correctable by hand.
- People (optional): similar faces grouped, to which you give a name that works across all folders.

**Export**
- JPEG at original or reduced size, chosen quality, watermark, tags.
- Shooting date and GPS position preserved (or removed, if you prefer).

## Requirements

- Windows 10 (1809 or later) or Windows 11, x64 or ARM64.
- HEIC photos need the Windows **HEIF** and **HEVC** extensions: they are usually already installed; if
  photos do not open, install them from the Microsoft Store.

## Build and run

You need the [.NET 10](https://dotnet.microsoft.com/download) SDK. Visual Studio is optional.

```bash
dotnet build WFastPic.slnx
```

The app is then in `src/WFastPic.App/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WFastPic.App.exe`.

Tests:

```bash
dotnet test tests/WFastPic.Core.Tests
```

## Main shortcuts

| Where | Keys |
|---|---|
| Grid and editor | `←` / `→` previous / next photo · `Space` select · `Delete` reject · `0`–`5` stars |
| Grid | `Shift+Space` keep and reject the rest of the burst · `Tab` filter · `Ctrl+A` select visible · `Shift+wheel` thumbnail size · `Enter` open |
| Editor | `X` crop · `L` straighten · `R` / `Shift+R` rotate · `U` I'm Feeling Lucky · `B` original · `Ctrl+Z` / `Ctrl+Y` · `F11` full screen · `Esc` close |

The full list is in the app, under "Scorciatoie" (shortcuts) in the bottom bar.

## Your photos stay yours

- Original photos are never modified: selections, stars and edits live in a hidden `.wfastpic` folder
  next to the photos.
- Everything runs on your PC. The only requests that leave it are ones you can turn off or choose:
  - place names asked from OpenStreetMap (can be turned off from the map) and the map tiles;
  - the face recognition models, downloaded once from Hugging Face (OpenCV) only if you enable the
    option. Your photos never leave your PC.

## For developers

- Architecture, conventions and project status: [`AGENTS.md`](AGENTS.md) (in Italian).
- What is new in each version: [`CHANGELOG.en.md`](CHANGELOG.en.md).
- C# + WinUI 3 (Windows App SDK), HEIC decoding with WIC, one SQLite sidecar per folder. The logic lives
  in the `WFastPic.Core.*` projects, with no WinUI dependency, and is covered by xUnit tests.

## License

[MIT](LICENSE): you can use, modify and redistribute it freely, keeping the copyright notice.
Not affiliated with Google.
