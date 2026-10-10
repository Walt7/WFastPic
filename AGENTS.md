# AGENTS.md — WFastPic

Guida per agenti AI (Claude Code e simili) che lavorano su questo repository.

## Cos'è WFastPic

Successore open source moderno di Picasa: selezione velocissima e correzione rapida di grandi
collezioni fotografiche HEIC su Windows. Riferimento UX: Picasa. Non Lightroom, non un file browser
generico (FastStone).

Principio guida di ogni scelta tecnica: **selezionare e sistemare centinaia di foto di una vacanza
nel minor numero possibile di azioni, senza mai interrompere il flusso dell'utente.**

## Stato del progetto

Fase 0-5 del piano originale (`~/.claude/plans/cheerful-sleeping-biscuit.md`) **completate**:
smoke test HEIC, scaffold, scan+sidecar, griglia+selezione, viewer+editing minimo, export JPEG.

Aggiunte fuori piano, su richiesta esplicita durante lo sviluppo:
- Raggruppamento raffiche (foto scattate a pochi secondi di distanza) con azione "tieni questa,
  scarta il resto del gruppo" (Shift+Space).
- Icona/logo app.
- Sidebar cartelle recenti (persistita su disco) + barra percorso stile Explorer.
- Badge presenza video abbinato (Apple Live Photo / Motion Photo) in griglia, filmstrip e
  sull'angolo della foto nel viewer — solo rilevamento, **nessuna riproduzione** (resta fuori scope).
- Export: watermark testuale configurabile, tag personalizzati scritti come Keywords EXIF,
  cartella di output scelta o default `<cartella>/Export`.
- Caricamento stile Picasa: overlay con barra determinata "N di M · circa X rimanenti" durante la
  lettura cartella (ritardo 300ms, niente flash), poi griglia subito usabile con barra miniature in
  basso; le celle visibili hanno priorità. Riaprire una cartella già vista salta la lettura EXIF
  delle foto invariate. Ordine griglia/viewer = data di scatto (foto senza data in fondo).
- "Gestisci scarti": l'elenco viene dallo stato in memoria (non dal DB, la coda di scrittura è
  asincrona); eliminazione solo verso il Cestino di Windows con conferma in due click, mai
  cancellazione irreversibile; "Sposta in Scarti/" rinomina "nome (1).ext" in caso di collisione.
  Il video abbinato (.mov/.mp4) segue sempre la foto, con lo stesso nome base; se uno dei due file
  è bloccato non si sposta nessuno dei due.
- Viewer stile Picasa: filmstrip con la foto in osservazione sempre al centro (padding calcolato,
  quante miniature entrano nella larghezza, max ±12), più grande, a piena opacità, bordo bianco e
  linea verde "corrente"; le altre attenuate. Colori coerenti con la griglia (giallo = selezionata,
  azzurro = stessa raffica, verde = corrente). Posizione "N di M" nella barra in alto. In
  ritaglio/raddrizza il filmstrip sparisce, i margini si allargano per le maniglie e l'area foto ha
  un clip: zoom e rotazione non disegnano mai sopra pannello o barre. Rotella = zoom verso il
  cursore (100-400%); con foto zoomata si trascina col tasto sinistro, sempre entro i bordi
  (`PanZoomGeometry`, testata). In ritaglio zoom e trascinamento sono disattivati.
- Editor, robustezza: le ricette si rileggono da `SelectionManager.GetRecipeJson` (ricetta ancora
  in coda inclusa), mai direttamente dal DB; l'export chiama prima `SvuotaAsync`. Chiudere l'app o
  cambiare cartella col viewer aperto salva la ricetta. Durante il decode della foto successiva
  l'editing è bloccato (`EditingPronto`). In ritaglio, come Picasa, si lavora solo su quella foto:
  niente navigazione/selezione/scarto, frecce = raddrizza ±0,1° (Shift ±1°); il riquadro trascinato
  si ferma esattamente sul bordo dell'immagine ruotata (`StraightenGeometry.AvvicinaRect`) e
  raddrizzando si adatta sempre al riquadro voluto, quindi tornando verso 0° si riallarga.

Non ancora implementato / verificato: nessuna fase residua del piano. Resta da fare solo verifica
manuale interattiva utente su una cartella reale (drag crop, straighten, export end-to-end) — un
agente CLI non ha accesso a mouse/tastiera su questa app desktop nativa.

## Decisioni architetturali (ADR-001) — stato effettivo

- **Stack:** C# + WinUI 3 (Windows App SDK 2.4.0), unpackaged, `net10.0-windows10.0.19041.0`.
  Decoding HEIC via WIC (`Windows.Graphics.Imaging.BitmapDecoder`) — **nessun fallback libheif**:
  il verdetto Fase 0 ha confermato che WIC basta sui file reali testati.
- **Win2D non usato.** Crop/raddrizzamento nel Viewer sono overlay/RenderTransform WinUI puri
  (niente GPU canvas) finche si e in "modalita geometria"; poi Applica e regolazioni tonali usano la
  stessa pipeline CPU dell'export (`RecipeApplier` = raddrizza → ritaglia → `ToneProcessor`), eseguita
  su thread di background sulla preview. Il crop resta sempre dentro l'immagine ruotata
  (`StraightenGeometry`, niente angoli vuoti). Se in futuro serve editing più pesante (esposizione, WB, ecc.), Win2D resta
  l'opzione da valutare per allora.
- **CommunityToolkit.Mvvm non usato.** Code-behind pragmatico con `INotifyPropertyChanged` manuale
  (`PhotoGridItem`). Tech debt nota, non refactored perché la Grid è stabile così com'è.
- **Regola di layering non negoziabile:** nessun progetto sotto `src/WFastPic.Core.*` referenzia
  WinUI/XAML. `Core.Decoding` ed `Core.Export` sono Windows-targeted (`net10.0-windows...`) solo
  per usare WinRT imaging APIs (WIC) — non per XAML — eccezione già accettata.
- **Nessuna libreria fotografica centrale.** Sidecar SQLite per cartella (`.wfastpic/library.db`,
  WAL, `Pooling=false`). Identità foto: `PathRelativo + Size + LastWriteTimeUtc`, mai hash di massa.
- **Editing sempre non distruttivo.** `EditRecipe` (JSON versionato) in tabella `edits`, applicata
  al volo in Viewer/export. Gli originali HEIC non vengono mai scritti.
- **Licenza progetto:** MIT.

## Struttura repository effettiva

```
src/
├─ WFastPic.App/               (WinUI 3, XAML + code-behind, no MVVM toolkit)
├─ WFastPic.Core.Library/       (scan cartelle, identità foto, raggruppamento raffiche, video abbinato)
├─ WFastPic.Core.Decoding/      (thumbnail/preview/full decode via WIC — Windows-targeted)
├─ WFastPic.Core.Processing/    (EditRecipe, crop/rotate/auto-correct — pure, testabile)
├─ WFastPic.Core.Metadata/      (lettura data scatto EXIF)
├─ WFastPic.Core.Selection/     (stato selezione/rating/scarto, coda scrittura async)
├─ WFastPic.Core.Persistence/   (sidecar SQLite)
└─ WFastPic.Core.Export/        (bake ricetta + watermark + tag EXIF + encode JPEG — Windows-targeted)
tests/
└─ WFastPic.Core.Tests/         (xUnit, 70 test — Library/Selection/Processing)
tools/
├─ HeicSmokeTest/                (Fase 0, smoke test decoding)
└─ ScanSmokeTest/
assets/                          (icon.ico, logo.png)
```

## Performance: vincoli non negoziabili

- Nessun decode a piena risoluzione quando basta una thumbnail o una preview. Tre livelli distinti:
  thumbnail (grid, `ThumbnailExtractor`, embedded WIC thumb), preview (`PreviewDecoder`, 2048px,
  Viewer), full/preset (solo in export).
- Decoding sempre asincrono, mai sul thread UI. Thumbnail loader: coda + 4 worker paralleli,
  cache disco (`.wfastpic/thumbcache/`).
- Filmstrip Viewer: solo finestra ±12 indici caricata, mai l'intera libreria.
- Il ciclo `←/→ Space` non deve mai avere latenza percepibile, nemmeno su ~1300 foto reali
  (cartella di riferimento: `E:\Fotocamera\2026\Puglia`).

## Shortcut da tastiera implementati (mappa di riferimento)

`←/→` naviga · `Space` seleziona · `Shift+Space` seleziona e scarta il resto della raffica ·
`Delete`/`Backspace` scarta (mai cancellazione fisica) · `0-5` rating · `Tab` filtro
tutte/selezionate/scarti · `Ctrl+A` seleziona tutte visibili · `Shift+rotella` dimensione cella
griglia · `Enter` / doppio click apri viewer. Nel Viewer: `←/→` naviga · `Space` seleziona ·
`Delete` scarta · `0-5` rating · `X` ritaglia (di nuovo = applica) · `L` raddrizza (in ritaglio
frecce = ±0,1°, Shift ±1°) · `Invio` applica / `Esc` annulla ritaglio · `U` "Ho fortuna" (auto contrasto+colore) ·
`Ctrl+Z`/`Ctrl+Y` annulla/ripeti · `B` (tenuto premuto) mostra l'originale · `Esc` chiudi. Non introdurre nuovi shortcut senza verificarne il conflitto
con questa mappa.

Nota: `Z`/`F` zoom, `R` ruota rigida, `M` riproduci video, `Ctrl+C/V` copia ricetta — presenti nella mappa di design originale ma **non ancora implementati**.

## Convenzioni per chi implementa

- Test prima o insieme al codice per la logica in `Core.*` (xUnit) — 70 test attuali coprono
  scan/identità, raffiche, selezione, ricetta/crop/rotate/tono/geometria raddrizzamento, ordinamento
  cronologico, riapertura veloce, scarti, concorrenza sidecar, stima avanzamento, zoom/trascinamento viewer.
- `LibrarySidecarDatabase` è condiviso tra thread UI e coda di scrittura: ogni metodo pubblico nuovo
  deve prendere `_lock` (`using var scope = _lock.EnterScope();`).
- Nessuna dipendenza da WinUI nei progetti `Core.*` — se serve, è un segnale che la logica è nel
  posto sbagliato. `Core.Decoding`/`Core.Export` sono Windows-targeted solo per WinRT imaging.
- Pattern ricorrente WinUI: se un evento (wheel, keydown) sparisce perché un controllo figlio
  (ScrollViewer, Slider) lo marca `Handled` prima che arrivi al gestore voluto, usare
  `AddHandler(..., handledEventsToo: true)` sull'antenato giusto invece di attributi XAML.
- Build: `dotnet build WFastPic.slnx`. Test: `dotnet test tests/WFastPic.Core.Tests`.
