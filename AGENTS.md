# AGENTS.md — WFastPic

Guida per agenti AI (Claude Code e simili) che lavorano su questo repository.

## Cos'è WFastPic

Successore open source moderno di Picasa: selezione velocissima e correzione rapida di grandi
collezioni fotografiche HEIC su Windows. Riferimento UX: Picasa. Non Lightroom, non un file browser
generico (FastStone). Vedi il documento di progettazione completo per contesto, decisioni e roadmap:
`docs/design-doc.html` (se non presente, chiedere all'utente il link dell'Artifact pubblicato).

Principio guida di ogni scelta tecnica: **selezionare e sistemare centinaia di foto di una vacanza
nel minor numero possibile di azioni, senza mai interrompere il flusso dell'utente.**

## Stato del progetto

Fase corrente: **solo progettazione**. Nessun codice ancora scritto. Non avviare implementazione senza
conferma esplicita dell'utente su quale fase della roadmap (MVP → v1.0 → v1.1 → v1.2 → v2.0) affrontare.

## Decisioni architetturali vincolanti (ADR-001)

- **Stack:** C# + WinUI 3 (Windows App SDK) per la UI. Win2D per canvas GPU (zoom/pan/regolazioni).
  Decoding HEIC via Windows Imaging Component (WIC), con `libheif` come fallback.
- **Regola di layering non negoziabile:** nessun progetto sotto `src/WFastPic.Core.*` può referenziare
  WinUI/XAML. Tutta la logica di dominio vive in class library .NET pure, testabili senza avviare la UI.
  Questo è ciò che rende sostituibile sia il motore di decoding sia, in futuro, l'intera UI (es. Avalonia
  per il porting cross-platform).
- **Nessuna libreria fotografica centrale.** Le foto restano nelle cartelle originali. Selezione, rating
  e modifiche vivono in un sidecar SQLite per cartella (`.wfastpic/library.db`), mai in un database globale.
- **Editing sempre non distruttivo.** Ogni modifica è una ricetta dichiarativa (JSON versionato) applicata
  al volo in rendering/export. Gli originali HEIC non vengono mai scritti.
- **Licenza progetto:** MIT.

## Struttura repository attesa

```
src/
├─ WFastPic.App/              (WinUI 3, XAML, ViewModel — MVVM)
├─ WFastPic.Core.Library/      (scan cartelle, modello foto/sessione, filtri)
├─ WFastPic.Core.Decoding/     (IImageDecoder: WIC/HEIC, JPEG, fallback libheif)
├─ WFastPic.Core.Processing/   (pipeline non distruttiva: crop, esposizione, ecc.)
├─ WFastPic.Core.Metadata/     (EXIF/XMP/GPS, orientamento)
├─ WFastPic.Core.Selection/    (stato selezione/rating/scarto)
├─ WFastPic.Core.Persistence/  (sidecar SQLite, migrazioni schema)
└─ WFastPic.Core.Export/       (rendering finale + resize + scrittura JPEG)
tests/
├─ WFastPic.Core.Tests/        (unit, xUnit)
└─ WFastPic.Golden.Tests/      (regressione su immagini di riferimento)
docs/
```

## Performance: vincoli non negoziabili

- Nessun decode a piena risoluzione quando basta una thumbnail o una preview (tre livelli distinti:
  thumbnail 256px, preview 2048px, full-res solo per zoom 100%/editing).
- Decoding sempre asincrono, mai sul thread UI. Coda con priorità: visibile > prefetch > background.
- Preload predittivo delle foto N-1/N+1 nel viewer prima che l'utente prema freccia.
- La UI non deve mai bloccarsi, nemmeno con librerie da 10 000–50 000 foto.

## Shortcut da tastiera (mappa di riferimento)

`→/←` foto succ/prec · `Space` seleziona · `Delete` scarto · `0–5` rating · `Z` zoom 100% · `F` adatta ·
`X` crop · `L` raddrizza · `U` auto-correzione · `R`/`Shift+R` ruota · `M` riproduci filmato incorporato
(Motion Photo/Live Photo, quando presente) · `Ctrl+C`/`Ctrl+Shift+V` copia/incolla ricetta editing ·
`Ctrl+Z`/`Ctrl+Y` undo/redo · `Tab` filtro tutte/selezionate/scarti. Non introdurre nuovi shortcut senza
verificarne il conflitto con questa mappa.

## Convenzioni per chi implementa

- Test prima o insieme al codice per la logica in `Core.*` (xUnit). Golden image test per ogni
  operazione della pipeline di editing (crop, rotazione, WB) con pixel-diff.
- Nessuna dipendenza da WinUI nei progetti `Core.*` — se serve, è un segnale che la logica è nel posto
  sbagliato.
- Build CI su `windows-latest` (GitHub Actions): build + test su ogni PR.
- Attribuzione dipendenze di terze parti da mantenere aggiornata (vedi documento di design, sezione
  "Dipendenze e licenze") — in particolare `libheif` è LGPL v3, va linkata dinamicamente, mai staticamente.
