# Changelog

Le novità di WFastPic, versione per versione. La più recente è in cima. ([English](CHANGELOG.en.md))
I dettagli tecnici (cause dei bug, file toccati) sono in [`doc/correzioni-e-novita.md`](doc/correzioni-e-novita.md).

## [1.1.0] — 2026-10-14

Ricerca dei doppioni come in Picasa, anche tra cartelle diverse, e una nota chiara su cosa resta sul PC.

### Novità
- **Doppioni, come "Mostra file duplicati" di Picasa**: nuovo filtro «Doppioni» (anche con Tab) con le foto
  presenti più volte, divise per gruppi, anche con copie nelle altre cartelle che hai già aperto ("anche in
  «Puglia»": quelle cartelle vengono solo lette, mai toccate). Trova i file identici e anche la stessa foto
  salvata di nuovo più piccola o ricompressa (copia WhatsApp, foto esportata); due scatti diversi di una
  raffica non sono doppioni. In ogni gruppo la copia da tenere ha la spunta verde: quella che hai scelto tu,
  altrimenti l'originale più grande con la data di scatto. "Scarta le copie" (con conferma) o `Shift+Space`
  sulla copia da tenere segnano le altre come scarti: niente viene cancellato senza passare da «Gestisci
  scarti».
- **Nota sulla privacy** ([PRIVACY.md](PRIVACY.md)): cosa resta sul PC, cosa esce, verso chi, quando e come
  spegnerlo. Si apre anche da Informazioni → "Privacy".
- **Moduli per le segnalazioni su GitHub**: "Problema" e "Proposta", in italiano e inglese, con il
  promemoria di non allegare foto personali, percorsi o nomi di persone.

### Altro
- Il pacchetto di installazione viene costruito da GitHub direttamente dai sorgenti pubblicati, e ogni
  modifica viene compilata e provata in automatico.

## [1.0.0] — 2026-10-10

Prima versione stabile: voto automatico delle foto, suggerimenti di scarto, persone più importanti,
segnalazione degli errori senza dati personali, programma di installazione e avviso di versione nuova.

### Novità
- **Voto automatico delle foto, tutto sul PC**: dopo le miniature e i volti, WFastPic misura ogni foto
  (nitidezza, anche dei volti, mosso, esposizione) e le dà da 1 a 5 stelle. Sono stelline **vuote viola
  in basso a destra**, ben diverse dalle tue (piene gialle, a sinistra), che non vengono mai toccate.
  Il perché è nel suggerimento sulla miniatura e nei dettagli del viewer: "nitida", "mossa o sfocata",
  "la più nitida della raffica", "ci sei tu"…
- **"Da scartare?"**, nuovo filtro (anche con Tab): le foto mosse, sfocate, quasi nere o quasi tutte
  bianche. Le scarti una a una con Canc o tutte insieme con "Scarta tutte" (con conferma); niente viene
  scartato da solo e lo scarto resta reversibile.
- **Chi conta di più**: nel menu delle Persone "Preferita" (stella) e "Chi è" (Sono io, Famiglia,
  Amico, Conoscente). Le foto nitide in cui ci sei tu prendono 5 stelle e sono segnate in griglia; poi
  contano le preferite e la famiglia, gli amici, chi ha solo un nome. Tu e le preferite in cima all'elenco.
- **Viewer, colonna dei dettagli**: sezione "Voto" (il tuo e quello automatico, con i motivi) e sezione
  **"Volti"** come in Picasa: le persone nella foto con il nome (con "?" se è solo una proposta); clic su
  un volto per dire chi è, confermare o correggere.
- **"Segnala il problema"** nel dialogo degli errori inattesi: apre GitHub nel browser con la
  segnalazione già scritta, che controlli e invii tu. Dal testo mostrato, copiato o inviato vengono tolti
  percorsi e nomi delle foto, nome utente e nome del PC; il report completo resta solo sul tuo PC.
- **Programma di installazione**: oltre allo zip portatile, ogni release ha un setup
  (`WFastPic-<versione>-win-x64-setup.exe`). Non chiede permessi di amministratore, mette WFastPic nel
  menu Start (e sul desktop se vuoi), si aggiorna installando la versione nuova sopra e si disinstalla da
  "App installate" lasciando foto, sidecar e impostazioni.
- **Avviso di versione nuova**: quando su GitHub c'è una release più recente, in basso a destra compare
  in piccolo "Nuova versione X disponibile", che apre la pagina per scaricarla. Non si scarica niente da
  solo e non si invia nessun dato; si spegne dalle Informazioni.

## [0.9.2] — 2026-10-10

### Novità
- **App in italiano e inglese**: tutta l'interfaccia (menu, dialoghi, messaggi, mappe, date) ha la sua
  versione inglese. All'avvio segue la lingua di Windows; si cambia da "Informazioni" (in basso a
  destra → Lingua: Automatica / Italiano / English) e si applica al riavvio. I nomi dei luoghi sulla mappa
  seguono la lingua (quelli già salvati restano in italiano, in inglese si chiedono di nuovo).
- **Nuova icona**: il sole è ora un sole-obiettivo, con anello, diaframma e raggi come le lamelle di un
  obiettivo.
- README con i badge di GitHub; il progetto si presenta come un tributo a Picasa (non affiliato a Google).

### Correzioni
- **Lo zip della release non partiva**: `dotnet publish` non copiava i file XAML compilati (`*.xbf` e
  `WFastPic.App.pri`) e l'app si chiudeva subito dopo l'avvio. Ora il progetto li include nel pacchetto.
  (La release 0.9.1 aveva lo stesso difetto ed è stata sostituita da questa.)

## [0.9.1] — 2026-10-10

### Novità
- **Export più veloce**: le foto si elaborano 4 alla volta (3 alle dimensioni originali) invece che una
  per volta: circa il doppio più veloce a 2048 px e fino a 3 volte alle dimensioni originali.
- **Tempo previsto nell'export**: "N di M · circa X rimanenti", e a fine export quanto ci è voluto.
- **Persone, "È la stessa persona di…"** con le faccine: avatar tondo grande, nome e numero di foto per
  ogni persona, al posto di un menu di sole parole. Dopo l'unione, l'elenco e le foto sotto si
  aggiornano: se stavi guardando quel gruppo, vedi la persona che resta con tutte le sue foto.
- **Ritaglio e raddrizzamento insieme**, come in Picasa: durante il ritaglio il cursore per raddrizzare
  sta sotto la foto (più largo e preciso, con le tacche ogni 5° e "0°" per toglierlo).
- **Ritaglio orizzontale/verticale**: il riquadro si gira (4:3 ↔ 3:4, 16:9 ↔ 9:16) col pulsante
  accanto a "Proporzioni" o col tasto `O`.
- **Informazioni su WFastPic** (in basso a destra): versione, licenza, link al progetto e alle novità,
  crediti, cartella dei dati dell'app.
- Licenza MIT inclusa nel repository.

### Correzioni
- **Foto verticali stirate**: le foto con orientamento EXIF a 90° (tipiche dei telefoni, in JPEG)
  comparivano allargate nel viewer, nell'editor e nell'export. Ora hanno le proporzioni giuste.
- Persone: l'analisi dei volti lavorava su quelle stesse immagini stirate; per riconoscimenti più
  fedeli su foto così, usa "Rianalizza le persone".

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
- **Si riprende da dove si era rimasti**: al riavvio si torna sull'ultima foto guardata, nel viewer
  se era aperto.

**Persone**
- Riconoscimento delle persone stile Picasa (opzionale, chiesto al primo uso, tutto sul PC): gruppi
  di volti simili, nomi validi in tutte le cartelle, proposte automatiche, filtro per persona.
- Elenco Persone nel pannello laterale, in ordine alfabetico, con il numero di foto anche nelle
  altre cartelle. Rinomina, "È la stessa persona di…" (con Annulla), cambia immagine, elimina.

**Altro**
- Proporzione "originale" (quelle della foto) tra i ritagli.
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
