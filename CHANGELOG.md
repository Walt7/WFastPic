# Changelog

Le novità di WFastPic, versione per versione. La più recente è in cima.
I dettagli tecnici (cause dei bug, file toccati) sono in [`doc/correzioni-e-novita.md`](doc/correzioni-e-novita.md).

## [0.9] — 2026-10-10

### Novità

**Editor (viewer)**
- **Gira di 90° e specchia**: pulsanti ↺ ↻ e Specchia nelle Correzioni base, tasti `R` / `Shift+R`.
  Una foto già ritagliata resta con lo stesso ritaglio, solo girato. Vale anche per l'export, e le
  miniature in griglia e nel filmstrip si girano subito. Come tutte le modifiche, l'originale non viene toccato.
- **Tre colonne**: modifiche a sinistra, dettagli della foto a destra, entrambe comprimibili dai
  pulsanti "Modifiche" e "Dettagli" in alto. L'app ricorda come le lasci.
- **Dettagli della foto**: giorno della settimana, data e ora di scatto, nome, dimensione e formato
  del file, posizione nella raffica, luogo con mini-mappa e dati di ripresa.
- **Foto della stessa raffica** in piccolo accanto alla foto: sopra se è orizzontale, a fianco se è
  verticale. Un clic apre quella foto, così si sceglie al volo la migliore.
- **Scorrimento più veloce**: mentre la foto si carica si vede subito la sua miniatura ingrandita;
  le 5 foto successive nella direzione in cui si sta scorrendo vengono preparate in anticipo.
- Nuovo aspetto: barra "← Griglia · nome · N di M", pannello su card con schede a segmenti e
  pulsanti con icona e scorciatoia, filmstrip centrato sotto la foto, sfondo pieno.

**Finestra principale**
- Nuovo aspetto in stile Windows 11: barra del titolo integrata, tema scuro, griglia su un fondo
  più scuro con miniature arrotondate.
- La barra degli strumenti non esce più dalla finestra: se è stretta, Per giorni / Mappa / Scarti
  restano solo icona. "Esporta" è l'azione in evidenza.
- Schermata di benvenuto con "Apri una cartella…"; messaggi chiari se la cartella non ha foto o se
  il filtro non trova niente.
- Conteggi (foto, selezionate, scarti) nella barra in basso; tutte le scorciatoie in un riquadro
  "Scorciatoie".
- **Informazioni su WFastPic** (in basso a destra): versione, licenza, link al progetto e alle novità,
  crediti, cartella dei dati dell'app.
- **Si riprende da dove si era rimasti**: al riavvio si torna sull'ultima foto guardata, nel viewer
  se era aperto.

**Persone**
- Riconoscimento delle persone stile Picasa (opzionale, chiesto al primo uso, tutto sul PC): gruppi
  di volti simili, nomi validi in tutte le cartelle, proposte automatiche, filtro per persona.
- Elenco Persone nel pannello laterale, in ordine alfabetico, con il numero di foto anche nelle
  altre cartelle. Rinomina, "È la stessa persona di…" (con Annulla), cambia immagine, elimina.

**Altro**
- Proporzione "originale" (quelle della foto) tra i ritagli.
- Ritaglio: il riquadro si può girare da orizzontale a verticale (4:3 ↔ 3:4, 16:9 ↔ 9:16) col
  pulsante accanto a "Proporzioni" o col tasto `O`.
- Pannello cartelle nascosto mentre si usa l'editor, ripristinato all'uscita.

### Correzioni
- Il riquadro "Lettura foto…" poteva restare sopra la griglia per sempre, se la lettura della
  cartella finiva proprio mentre stava per comparire.
- Scorrendo, una foto che si stava già preparando in anticipo veniva decodificata una seconda volta.
- I nomi dati alle persone durante l'analisi non venivano applicati alle foto successive.
- Crash `OverflowException` durante la lettura di una cartella.

## [0.8] — 2026-10-10

### Novità
- **Pannello cartelle stile Picasa**, fisso accanto alla griglia: le cartelle divise per anno di
  scatto, con numero di foto e periodo; "Apri in Esplora file" / "Togli dall'elenco"; "Sfoglia il PC"
  per aprirne di nuove.
- **Volti**: ricerca in background a griglia pronta, filtro "Con volti"; i ritagli suggeriti non
  tagliano mai i volti.
- **Correzione della posizione a mappa grande** al posto della foto (mappa o satellite, zoom da strada).
- In ritaglio la griglia di composizione segue il riquadro.
- Simboli delle modifiche (ritaglio, raddrizzamento, colore) sulle miniature, aggiornati dal vivo.
- Versione mostrata nel titolo della finestra.

## Prima della 0.8

Le prime versioni, senza numero, hanno costruito le basi dell'app:
- lettura veloce di cartelle HEIC con sidecar per cartella (le foto originali non vengono mai scritte);
- griglia con selezione da tastiera, stelle, scarti sicuri (Cestino o cartella Scarti/, video abbinato
  sempre insieme alla foto), raffiche e "tieni questa, scarta le altre";
- viewer con filmstrip, zoom e editor stile Picasa: ritaglia, raddrizza, Ho fortuna, regolazioni, annulla;
- ritagli suggeriti, schermo intero (F11), griglia divisa per giorni;
- luoghi con nomi da OpenStreetMap, mappa delle foto, dati di ripresa (anche droni DJI);
- export JPEG stile Picasa con ridimensionamento, filigrana, tag e metadati (data, GPS).
