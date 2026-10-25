# Changelog

What is new in WFastPic, version by version. The newest is on top. ([Italiano](CHANGELOG.md))
Technical details (bug causes, files touched) are in [`doc/correzioni-e-novita.md`](doc/correzioni-e-novita.md) (in Italian).
The app interface is in Italian: names of buttons and menus below are given as they appear in the app.

## Unreleased

### New
- **Parent folders**: reading now includes subfolders (skipping hidden, system, `Scarti` and `Export`), so you can
  catalogue a whole collection such as `E:\Fotocamera`. The first time you open it, work already done in the
  subfolders (selections, ratings, edits, positions, people, measurements) is carried into the parent, once and
  without overwriting what you already touched there; the subfolders' data is not modified.
- **Reload** («Ricarica») in a folder's menu: rereads it from disk.
- **People in groups** (Me, Family, Friends, Acquaintances, Other people, Unknown people) and a **nickname** for each person.
- **Right-click menu on grid photos**: open, select, reject, «Seleziona tutte» (Select all), «Inverti selezione»
  (Invert selection), show in File Explorer and, on a selected photo, **«Salva selezione…» (Save selection…)**: the
  selected photos are saved under a name, to find them again or prepare a package to share. Select all and Invert
  warn that your selection will be lost and offer to save it first. A folder's saved selections appear in the
  filter menu under «Selezioni salvate» (Saved selections): picking one shows only those photos.
- **MCP server** (`src/WFastPic.Mcp`): an assistant can search by folder, people, dates and place, save named
  selections and prepare folders or zips of copies to share. Read-only on your photos.

### Other
- The export option is now called «Sovrascrivi i file già esportati con lo stesso nome» (Overwrite already exported files with the same name).

## [1.1.0] — 2026-10-14

Duplicate search like in Picasa, also across folders, and a clear note on what stays on your PC.

### New
- **Duplicates, like Picasa's "Show duplicate files"**: new «Doppioni» filter (also with Tab) with the
  photos that are in the folder more than once, split into groups, also with copies in the other folders you
  have already opened ("anche in «Puglia»": those folders are only read, never touched). It finds identical
  files and also the same photo saved again smaller or recompressed (WhatsApp copy, exported photo); two
  different shots of a burst are not duplicates. In each group the copy to keep has a green tick: the one
  you chose, otherwise the largest original with the shooting date. "Scarta le copie" (with confirmation) or
  `Shift+Space` on the copy to keep mark the others as rejects: nothing is deleted without going through
  «Gestisci scarti».
- **Privacy note** ([PRIVACY.en.md](PRIVACY.en.md)): what stays on your PC, what leaves it, to whom, when and
  how to turn it off. Also opens from About → "Privacy".
- **GitHub issue forms**: "Bug report" and "Feature request", in Italian and English, reminding not to
  attach personal photos, paths or people's names.

### Other
- The installer is built by GitHub directly from the published sources, and every change is compiled and
  tested automatically.

## [1.0.0] — 2026-10-10

First stable version: automatic photo rating, reject suggestions, most important people, error
reports without personal data, an installer and a new version notice.

### New
- **Automatic photo rating, all on your PC**: after thumbnails and faces, WFastPic measures every photo
  (sharpness, faces included, camera shake, exposure) and gives it 1 to 5 stars. They are **hollow
  purple stars at the bottom right**, clearly different from yours (filled yellow, on the left), which
  are never touched. The reason is in the thumbnail tooltip and in the viewer details: "sharp", "shaken
  or blurry", "the sharpest of the burst", "you are in it"…
- **"To reject?"**, a new filter (also with Tab): shaken, blurry, almost black or almost all white
  photos. Reject them one by one with Delete or all together with "Reject all" (with confirmation);
  nothing is rejected on its own and rejecting stays reversible.
- **Who matters most**: in the People menu, "Favourite" (star) and "Who is it" (It's me, Family, Friend,
  Acquaintance). Sharp photos with you in them get 5 stars and are marked in the grid; then come
  favourites and family, friends, people with just a name. You and your favourites are at the top.
- **Viewer, details column**: a "Rating" section (yours and the automatic one, with the reasons) and a
  **"Faces"** section as in Picasa: the people in the photo with their name ("?" if only suggested);
  click a face to say who it is, confirm or correct it.
- **"Report the problem"** in the unexpected error dialog: opens GitHub in the browser with the report
  already written, which you check and send yourself. Photo paths and names, user name and PC name are
  removed from the text shown, copied or sent; the full report stays only on your PC.
- **Installer**: besides the portable zip, every release has a setup (`WFastPic-<version>-win-x64-setup.exe`).
  It needs no administrator rights, puts WFastPic in the Start menu (and on the desktop if you want),
  updates by installing the new version over it and uninstalls from "Installed apps", keeping photos,
  sidecars and settings.
- **New version notice**: when GitHub has a newer release, a small "New version X available" appears at
  the bottom right and opens the download page. Nothing is downloaded on its own and no data is sent;
  turn it off from About.

## [0.9.2] — 2026-10-10

### New
- **App in Italian and English**: the whole interface (menus, dialogs, messages, maps, dates) has an
  English version. At startup it follows the Windows language; change it from "About" (bottom right →
  Language: Automatic / Italiano / English), applied at the next start. Place names on the map follow the
  language (names already saved stay in Italian; in English they are requested again).
- **New icon**: the sun is now a lens-sun, with a ring, an aperture and rays like the blades of a lens.
- README with the usual GitHub badges; the project presents itself as a tribute to Picasa (not affiliated
  with Google).

### Fixed
- **The release zip did not start**: `dotnet publish` did not copy the compiled XAML files (`*.xbf` and
  `WFastPic.App.pri`) and the app closed right after launching. The project now includes them in the
  package. (Release 0.9.1 had the same defect and has been replaced by this one.)

## [0.9.1] — 2026-10-10

### New
- **Faster export**: photos are processed 4 at a time (3 at original size) instead of one by one: about
  twice as fast at 2048 px and up to 3 times as fast at original size.
- **Estimated time in export**: "N of M · about X remaining", and how long it took when finished.
- **People, "Same person as…" ("È la stessa persona di…")** with faces: a large round avatar, name and
  photo count for each person, instead of a text-only menu. After merging, the list and the photos below
  update: if you were looking at that group, you see the person who remains with all their photos.
- **Crop and straighten together**, like Picasa: while cropping, the straighten slider sits under the
  photo (wider and more precise, with marks every 5° and a "0°" button to clear it).
- **Landscape/portrait crop**: the crop box can be flipped (4:3 ↔ 3:4, 16:9 ↔ 9:16) with the button next
  to "Proporzioni" (Ratios) or with the `O` key.
- **About WFastPic** (bottom right): version, license, links to the project and the changelog, credits,
  the app data folder.
- MIT license included in the repository.

### Fixed
- **Stretched portrait photos**: photos with a 90° EXIF orientation (typical of phones, in JPEG) appeared
  stretched wide in the viewer, in the editor and in export. They now keep the right proportions.
- People: face analysis worked on those same stretched images; for more faithful recognition on such
  photos, use "Rianalizza le persone" (Re-analyze people).

## [0.9] — 2026-10-10

### New

**Editor (viewer)**
- **Rotate 90° and flip**: ↺ ↻ and Specchia (Flip) buttons in the basic corrections, `R` / `Shift+R`
  keys. A photo that is already cropped keeps the same crop, just rotated. It also applies to export, and
  thumbnails in the grid and filmstrip rotate immediately. Like every edit, the original is never touched.
- **Three columns**: edits on the left, photo details on the right, both collapsible from the "Modifiche"
  and "Dettagli" buttons at the top. The app remembers how you leave them.
- **Photo details**: weekday, date and time shot, name, file size and format, position in the burst,
  place with mini-map and shooting data.
- **Photos from the same burst** shown small next to the photo: above it if landscape, beside it if
  portrait. One click opens that photo, so you pick the best one on the fly.
- **Faster browsing**: while a photo loads you immediately see its enlarged thumbnail; the next 5 photos in
  the direction you are browsing are prepared in advance.
- New look: "← Grid · name · N of M" bar, card-style panel with segmented tabs and buttons with icon and
  shortcut, filmstrip centered under the photo, solid background.

**Main window**
- New Windows 11 look: integrated title bar, dark theme, grid on a darker background with rounded thumbnails.
- The toolbar no longer overflows the window: when narrow, By day / Map / Rejects show only their icon.
  "Esporta" (Export) is the highlighted action.
- Welcome screen with "Apri una cartella…" (Open a folder…); clear messages if the folder has no photos or
  if the filter finds nothing.
- Counts (photos, selected, rejects) in the bottom bar; all shortcuts in a "Scorciatoie" (Shortcuts) panel.
- **Resumes where you left off**: on restart you go back to the last photo you looked at, in the viewer if
  it was open.

**People**
- Picasa-style people recognition (optional, asked on first use, entirely on your PC): groups of similar
  faces, names valid across all folders, automatic suggestions, filter by person.
- People list in the side panel, alphabetical, with the photo count in other folders too. Rename,
  "Same person as…" (with Undo), change picture, delete.

**Other**
- "Original" ratio (the photo's own) among the crops.
- Folder panel hidden while using the editor, restored on exit.

### Fixed
- The "Lettura foto…" (Reading photos…) box could stay over the grid forever if reading the folder ended
  just as it was about to appear.
- While browsing, a photo already being prepared in advance was decoded a second time.
- Names given to people during analysis were not applied to the photos analyzed afterwards.
- `OverflowException` crash while reading a folder.

## [0.8] — 2026-10-10

### New
- **Picasa-style folder panel**, fixed next to the grid: folders split by year shot, with photo count and
  period; "Open in File Explorer" / "Remove from list"; "Browse PC" to open new ones.
- **Faces**: background search once the grid is ready, "With faces" filter; suggested crops never cut faces.
- **Location correction on a large map** in place of the photo (map or satellite, down to street level zoom).
- While cropping, the composition grid follows the crop box.
- Edit symbols (crop, straighten, color) on thumbnails, updated live.
- Version shown in the window title.

## Before 0.8

The first, unnumbered versions built the foundations of the app:
- fast reading of HEIC folders with a sidecar per folder (original photos are never written);
- grid with keyboard selection, stars, safe rejects (Recycle Bin or `Scarti/` folder, paired video always
  together with the photo), bursts and "keep this one, reject the others";
- viewer with filmstrip, zoom and Picasa-style editor: crop, straighten, I'm Feeling Lucky, adjustments, undo;
- suggested crops, full screen (F11), grid split by day;
- places with names from OpenStreetMap, photo map, shooting data (including DJI drones);
- Picasa-style JPEG export with resizing, watermark, tags and metadata (date, GPS).
