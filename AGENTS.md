# AGENTS.md — WFastPic

Guida per agenti AI (Claude Code e simili) che lavorano su questo repository.

## Cos'è WFastPic

Tributo open source a Picasa: selezione velocissima e correzione rapida di grandi
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
  ritaglio/raddrizza il filmstrip sparisce e al suo posto, sotto la foto, compare il cursore per
  raddrizzare (come Picasa: ritaglio e raddrizzamento insieme), i margini si allargano per le maniglie e l'area foto ha
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
- Viewer a schermo intero (`F11` o pulsante nel pannello): spariscono barra in alto, filmstrip e
  suggerimenti, restano foto e pannello di modifica; `Esc` esce prima dallo schermo intero.
- Export stile Picasa: posizione + nome cartella, "dimensioni originali" o "ridimensiona a" N px
  (slider + valore esatto), qualità a preset, filigrana; opzioni ricordate in `ImpostazioniStore`.
  Gira in background, annullabile, "Esporta N foto", errori elencati, a fine export apre la cartella.
  Mai gli scarti; riesportando sostituisce (mai nella cartella delle foto: lì numera `_2`). Decode
  convertito in sRGB (le HEIC iPhone sono Display P3). **Metadati scritti con query WIC dirette**
  (`/app1/ifd/...`, rational = UInt64 con numeratore nei 32 bit bassi): i nomi `System.GPS.*`
  vengono ignorati in silenzio dall'encoder JPEG. Scrive data scatto, GPS (+ altitudine, direzione),
  tag come XPKeywords; opzione "Rimuovi la posizione GPS".
- Luoghi: posizione corretta a mano nell'editor (pannello "Luogo" con mini-mappa Leaflet) salvata
  solo nel sidecar (`gps_manuale`): riscansioni ed EXIF non la sovrascrivono, l'export la usa.
  Nomi dei luoghi via Nominatim/OpenStreetMap (`RisolutoreLuoghi`: 1 richiesta/s, cache `luoghi`
  per cella di ~100 m, disattivabile dalla mappa). Dati di ripresa letti su richiesta
  (`DatiRipresa`): dispositivo e tipo (telefono/drone/fotocamera), altitudine, direzione, per i
  droni DJI altezza dal decollo e inclinazione gimbal dall'XMP. Altitudine e direzione stanno anche
  nel sidecar (colonne `altitude`, `direction`).
- Mappa: raggruppamento automatico (Leaflet.markercluster) in segnaposto a goccia con il numero,
  miniatura dentro il segnaposto per le foto isolate (pagina servita da host virtuale sulla cartella
  `.wfastpic`, miniature da `thumbcache/`), freccia della direzione della camera, popup con luogo e
  dati di ripresa. Riquadri OpenStreetMap / satellite Esri (le basemap CARTO ora chiedono una chiave).
- Ritagli suggeriti stile Picasa nel pannello di ritaglio (`SuggerimentiRitaglio`, testata): una
  proposta per proporzione (come la foto, 3:2/4:3, 16:9, 1:1, orientate come la foto) dalla mappa di
  interesse (gradiente + contrasto colore, immagini integrali su ~128 px) con premio per i terzi;
  dentro l'immagine raddrizzata. Volti rilevati offline con il `FaceDetector` di Windows
  (`RilevatoreVolti`): mai tagliati, con aria sopra la testa, composizione sul terzo superiore.
  Miniature cliccabili, calcolo in background, ricalcolo a slider di raddrizzamento fermo.
- Pannello cartelle stile Picasa (`MainWindow.Cartelle.cs`): fisso accanto alla griglia (SplitView
  Inline, stato ricordato), "le mie cartelle" raggruppate per anno di scatto con numero di foto e
  periodo letti dal sidecar in sola lettura (`LibrarySidecarDatabase.LeggiRiepilogo`, non crea mai
  il sidecar), cartella aperta evidenziata, menu "Apri in Esplora file"/"Togli dall'elenco".
  "Sfoglia il PC" è secondario: dischi e sottocartelle letti in background, cartelle nascoste, di
  sistema e `.wfastpic` escluse (`ArchivioCartelle`, testata).
- Volti: ricerca in background a griglia pronta (miniature finite), una foto alla volta, in pausa
  col viewer aperto; risultato nel sidecar (tabella `volti`, `photos.volti_cercati`, azzerati se il
  file cambia); filtro griglia "Con volti"; i ritagli suggeriti riusano i volti salvati
  (`StraightenGeometry.RuotaRettangolo` li porta sulla tela raddrizzata). Solo rilevamento: il
  riconoscimento delle persone (chi è chi) richiederebbe un modello a parte.
- Correzione posizione a mappa grande al posto della foto (mappa/satellite + nomi, zoom da strada,
  partenza vicino alle altre foto della cartella); Esc annulla.
- Durante il ritaglio la griglia di composizione segue il riquadro di ritaglio.
- Simboli delle modifiche sulle miniature (griglia e filmstrip): ritaglio, raddrizzamento,
  colore/tono (`EditRecipe.HasCrop/HasStraighten/HasTone`); ricette lette tutte insieme all'apertura
  della cartella (`GetAllRecipeJson`), aggiornati dal vivo durante l'editing (`MostraBuffer`).
- Persone stile Picasa (opzione chiesta al primo uso, interruttore nella sezione Persone): progetto
  `WFastPic.Core.Persone` (ONNX Runtime). YuNet (rilevamento + 5 punti, decodifica a mano come
  OpenCV FaceDetectorYN, ingresso fisso 640) → allineamento per similitudine su 112×112 → SFace
  (impronta 128, somiglianza coseno). Modelli OpenCV Zoo scaricati al primo uso in
  LocalAppData\WFastPic\modelli con verifica SHA-256 (`ModelliPersone`), mai nel repo. Volti con
  impronta e miniatura nel sidecar (`volti`, `volti_cercati = 2`); persone (nome, avatar, campioni)
  nell'archivio globale `persone.db` (`ArchivioPersone`), valide per tutte le cartelle. Gruppi senza
  nome per legame medio (centroidi oltre 300 volti), proposte automatiche oltre 0,42, conferma/
  rinomina/togli/ignora dal menu, filtro griglia per persona, "Rianalizza le persone".
  Elenco Persone in cima al pannello cartelle a righe come Picasa: tutte le persone con nome in
  ordine alfabetico, foto contate anche nei sidecar delle altre cartelle note
  (`LeggiFotoPerPersona`, sola lettura); chi non è nella cartella aperta è attenuato e il clic apre
  la sua cartella già filtrata. Gestione nomi: rinomina (nome di un'altra persona = unione), "È la
  stessa persona di…" (nome + senza nome, due senza nome, due con nome), "Cambia immagine",
  "Elimina la persona". Unione reversibile: la persona unita resta in `persone.db` con `unita_a` e
  l'alias si risolve in lettura (`CostruisciElencoPersone`), i sidecar non vengono riscritti; dopo un
  nome o un'unione il pannello offre "Annulla". Solo i volti di persone eliminate tornano senza nome
  nel sidecar all'apertura della cartella (`SostituisciPersona`). Dettagli in `doc/correzioni-e-novita.md`.
  Senza l'opzione resta il rilevatore di Windows (solo volti, per ritagli e filtro "Con volti").
- Proporzioni del ritaglio: anche "originale" (quelle della foto).
- Versione dell'app nel `<Version>` del csproj, mostrata nel titolo della finestra ("v. 0.9.2");
  novità di ogni versione in `CHANGELOG.md`.
- Griglia divisa per giorni (opzionale, "Per giorni"): vista raggruppata allineata a `_visibili`,
  che resta l'unico elenco di riferimento.
- Aspetto Windows 11: tema scuro fisso (`App.xaml`, palette e stili `BarraButtonStyle` condivisi),
  barra del titolo disegnata dall'app sopra il Mica (`InizializzaBarraTitolo`, nascosta a schermo
  intero), barra strumenti a tre zone con etichette che spariscono se stretta
  (`BarraStrumenti_SizeChanged`; gli AdaptiveTrigger non scattano dentro una Window), griglia su card
  scura arrotondata, stato vuoto (benvenuto / cartella vuota / filtro vuoto, `AggiornaStatoVuoto`),
  scorciatoie in un flyout nella barra in basso. Viewer: barra "← Griglia · nome N di M · zoom/griglia",
  editor su card alta quanto il viewer (schede a segmenti, pulsanti con icona e scorciatoia), filmstrip
  e suggerimenti nella colonna della foto, così la miniatura corrente sta sotto il centro della foto.
- Gira di 90° e specchia (`Orientamento`, testato): `RotationDegrees` + `FlipHorizontal` nella ricetta,
  primo passo di `RecipeApplier` (ritaglio e raddrizzamento sono sulla foto già girata); girando una
  foto già ritagliata il riquadro gira con lei, specchiando si inverte anche il raddrizzamento.
  Miniature girate con RenderTransform. Pulsanti nel pannello, `R` / `Shift+R`.
- Viewer a tre colonne: modifiche a sinistra, dettagli a destra (giorno della settimana, data, ora,
  file, raffica, luogo e mini-mappa), entrambe comprimibili e ricordate. Foto della stessa raffica in
  piccolo sopra la foto se orizzontale, a fianco se verticale (`ViewerView.Dettagli.cs`).
- Scorrimento veloce nel viewer: miniatura ingrandita come segnaposto finché la preview non è pronta;
  precaricamento di 5 foto nella direzione di marcia + 1 dietro (max 2 decode in parallelo, cache 10);
  un decode in corso non viene mai ripetuto per la stessa foto (`_decodeInCorso`).
- All'avvio si torna sull'ultima foto guardata, nel viewer se era aperto (`UltimaCartella`/`UltimaFoto`).
- Gira di 90° e specchia (`Orientamento`, testato): `RotationDegrees` + `FlipHorizontal` nella ricetta,
  primo passo di `RecipeApplier` (ritaglio e raddrizzamento sono sulla foto già girata); girando una
  foto già ritagliata il riquadro gira con lei, specchiando si inverte anche il raddrizzamento.
  Miniature girate con RenderTransform. Pulsanti nel pannello, `R` / `Shift+R`.
- Viewer a tre colonne: modifiche a sinistra, dettagli a destra (giorno della settimana, data, ora,
  file, raffica, luogo e mini-mappa), entrambe comprimibili e ricordate. Foto della stessa raffica in
  piccolo sopra la foto se orizzontale, a fianco se verticale (`ViewerView.Dettagli.cs`).
- Scorrimento veloce nel viewer: miniatura ingrandita come segnaposto finché la preview non è pronta;
  precaricamento di 5 foto nella direzione di marcia + 1 dietro (max 2 decode in parallelo, cache 10);
  un decode in corso non viene mai ripetuto per la stessa foto (`_decodeInCorso`).
- Voto automatico, tutto offline e **sempre separato dal voto dell'utente** (che non tocca mai):
  `MisuraQualita` (Core.Processing, testata) misura su una preview da 512 px la nitidezza come rapporto
  tra dettaglio a metà e a un quarto di scala, nelle tessere più a fuoco e nella direzione peggiore
  (coglie il mosso, ignora il rumore), anche sui volti, più bruciati/chiusi/luminosità. Misure nel sidecar
  (tabella `qualita`, con `versione`: cambiando il calcolo si rimisura; azzerate se il file cambia),
  analisi in background dopo i volti (`MainWindow.Qualita.cs`, in pausa col viewer aperto).
  `VotoAutomatico` dà 1-5 stelle relative alla cartella con limiti assoluti: soglie tarate su ~1300 foto
  reali confrontate con le selezioni (sotto 0,60 nessuna era selezionata). In griglia stelline vuote
  viola in basso a destra (quelle dell'utente restano piene gialle a sinistra), icone "da scartare?",
  "ci sei tu", "migliore della raffica". Filtro "Da scartare?" (mosse/sfocate, quasi nere, quasi bianche)
  con "Scarta tutte" e conferma: mai scarti automatici. La raffica non basta mai a suggerire lo scarto.
- Importanza delle persone (`persone.db`: `preferita`, `cerchia` = Conoscente/Amico/Famiglia/Io in
  ordine di importanza, "Io" una persona sola): menu Persone "Preferita" e "Chi è"; nel voto tu > stella
  e famiglia > amici > con nome > conoscenti; tu e le preferite in cima all'elenco Persone.
- Dettagli del viewer: sezione Voto (il tuo / automatico con i motivi) e sezione Volti come Picasa
  (miniature con nome, "?" se proposto, cerchia; clic = "Chi è?" su quel volto, via `IViewerHost`).

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
│  ├─ MainWindow.*.cs          (griglia, cartelle, persone, mappa, export: partial della finestra)
│  └─ Viewer/                  (ViewerView: UserControl con viewer + editor, legato a MainWindow da IViewerHost)
├─ WFastPic.Core.Library/       (scan cartelle, identità foto, raggruppamento raffiche, video abbinato)
├─ WFastPic.Core.Decoding/      (thumbnail/preview/full decode via WIC — Windows-targeted)
├─ WFastPic.Core.Processing/    (EditRecipe, crop/rotate/auto-correct — pure, testabile)
├─ WFastPic.Core.Metadata/      (lettura data scatto EXIF)
├─ WFastPic.Core.Selection/     (stato selezione/rating/scarto, coda scrittura async)
├─ WFastPic.Core.Persistence/   (sidecar SQLite)
└─ WFastPic.Core.Export/        (bake ricetta + watermark + tag EXIF + encode JPEG — Windows-targeted)
tests/
└─ WFastPic.Core.Tests/         (xUnit, 197 test — Library/Selection/Processing/Metadata/Export)
tools/
├─ HeicSmokeTest/                (Fase 0, smoke test decoding)
├─ ScanSmokeTest/
└─ release.ps1                   (publish + prova di avvio + zip + setup → artifacts/<versione>/)
installer/
└─ WFastPic.iss                  (setup Inno Setup 6, compilato da tools/release.ps1)
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
frecce = ±0,1°, Shift ±1°) · `Invio` applica / `Esc` annulla ritaglio · `U` "Ho fortuna" (auto contrasto+colore) · `R` / `Shift+R` gira di 90° a destra / sinistra · in ritaglio `O` riquadro orizzontale ↔ verticale ·
`Ctrl+Z`/`Ctrl+Y` annulla/ripeti · `B` (tenuto premuto) mostra l'originale · `F11` schermo intero · `Esc` chiudi. Non introdurre nuovi shortcut senza verificarne il conflitto
con questa mappa.

Nota: `Z`/`F` zoom, `M` riproduci video, `Ctrl+C/V` copia ricetta — presenti nella mappa di design originale ma **non ancora implementati**.

## Convenzioni per chi implementa

- **Testi dell'interfaccia in italiano e inglese** (`WFastPic.Core.Localizzazione`): il testo italiano è la
  chiave. Nel codice si scrive `T("Apri cartella…")`; con valori `T("{0} di {1}", a, b)` (formato composto,
  mai `T($"…")`); i conteggi di foto con `Foto(n)` ("1 photo"/"340 photos"); le date con `Cultura`
  (it-IT / en-GB), non con `CultureInfo` fisse. Ogni testo nuovo ha la sua voce inglese in
  `TestiEn.*.cs`, altrimenti `TraduzioniTests` fallisce (controlla anche i testi letterali negli XAML e che
  i segnaposto `{0}` coincidano). Lo XAML resta in italiano e viene tradotto da `Localizzatore.Applica` dopo
  `InitializeComponent()`; dentro i `DataTemplate` si usa `{x:Bind local:Etichette.…}`. Nelle pagine HTML
  delle mappe i testi sono `{{T("…")}}` (stringhe raw interpolate) o `@@testo@@` + `Segnaposti()`. La
  lingua si sceglie all'avvio (`Testi.Risolvi`, preferenza "auto"/"it"/"en" in `settings.json`) e non
  cambia mentre l'app gira. I test non vanno in parallelo (la lingua è uno stato globale).
- Icone e logo: `assets/sorgenti/` (`python assets/sorgenti/genera-icone.py` rigenera `icon-*.png`,
  `icon.ico`, `logo.png`).
- Pacchetto per la release: `powershell -ExecutionPolicy Bypass -File tools\release.ps1` (serve Inno
  Setup 6: `winget install JRSoftware.InnoSetup`). Fa `dotnet publish` win-x64, **avvia l'exe del
  pacchetto** (non quello di Debug: il target `IncludiRisorseXamlNelPublish` del csproj aggiunge `*.xbf` e
  `WFastPic.App.pri`, senza i quali l'app muore all'avvio con 0xC000027B), poi in `artifacts\<versione>\`
  lo zip portatile `WFastPic-<v>-win-x64.zip` e il setup `WFastPic-<v>-win-x64-setup.exe`
  (`installer\WFastPic.iss`: per utente senza admin, `AppId` da non cambiare mai). Variabile
  `WFASTPIC_LANG=it|en` per provare le due lingue senza toccare le impostazioni.
- Il viewer (`Viewer/ViewerView*`) non tocca i campi di `MainWindow`: quello che gli serve dalla
  finestra (elenco visibile, sidecar, selezione, miniature, stato/rating, nomi dei luoghi) passa da
  `IViewerHost`; la finestra lo usa con `Viewer.Apri/Chiudi/IsAperto/SalvaRicettaCorrente` e l'evento
  `Chiuso`. Una nuova dipendenza va aggiunta all'interfaccia, non aggirata.

- Test prima o insieme al codice per la logica in `Core.*` (xUnit) — 197 test attuali coprono
  scan/identità, raffiche, selezione, ricetta/crop/rotate/tono/geometria raddrizzamento, ordinamento
  cronologico, riapertura veloce, scarti, concorrenza sidecar, stima avanzamento, zoom/trascinamento viewer.
- `LibrarySidecarDatabase` è condiviso tra thread UI e coda di scrittura: ogni metodo pubblico nuovo
  deve prendere `_lock` (`using var scope = _lock.EnterScope();`).
- Nessuna dipendenza da WinUI nei progetti `Core.*` — se serve, è un segnale che la logica è nel
  posto sbagliato. `Core.Decoding`/`Core.Export` sono Windows-targeted solo per WinRT imaging.
- Pattern ricorrente WinUI: se un evento (wheel, keydown) sparisce perché un controllo figlio
  (ScrollViewer, Slider) lo marca `Handled` prima che arrivi al gestore voluto, usare
  `AddHandler(..., handledEventsToo: true)` sull'antenato giusto invece di attributi XAML.
- Build: `dotnet build WFastPic.slnx`. Test: `dotnet test tests/WFastPic.Core.Tests` (progetto test
  Windows-targeted per provare `Core.Export` end-to-end con WIC).
