# Correzioni e novità — sessione di sviluppo ottobre 2026

Registro delle modifiche fatte dopo la v0.8, con il motivo di ciascuna e dove si trova nel codice.
Le scelte di architettura restano quelle di `AGENTS.md` (ADR-001).

## Correzioni

### Crash `OverflowException` durante la lettura della cartella
- **Sintomo:** `TimeSpan overflowed because the duration is too long` in `MainWindow.CaricaCartellaAsync`.
- **Causa:** il limitatore degli aggiornamenti di progresso (max uno ogni 100 ms) partiva da
  `TimeSpan.MinValue`; sottrarlo da un tempo positivo va in overflow.
- **Fix:** `ultimoAggiornamento` ora è `TimeSpan?` (`null` al primo giro). `MainWindow.xaml.cs`.

### Selezione con Space che "non si vedeva" nella griglia
- **Causa:** il `ListViewItem` segna Space come già gestito prima che arrivi alla `ListView`, quindi
  `Griglia_KeyDown` non scattava e lo stato non cambiava.
- **Fix:** gestore registrato in codice con `AddHandler(KeyDownEvent, ..., handledEventsToo: true)`
  (stesso pattern già usato per la rotella); tolto l'attributo `KeyDown` dallo XAML per non farlo scattare due volte.
- Bordo giallo di selezione più evidente: 5 px più un velo giallo leggero su tutta la cella.

### Space in modalità ritaglio / raddrizza
- In geometria tutti i tasti non previsti venivano scartati. Ora Space seleziona/deseleziona la foto
  corrente anche lì (non cambia foto, quindi non interferisce con la geometria). `Viewer/ViewerView.xaml.cs`,
  `GestisciTastoGeometria`. Navigazione e scarto restano disattivati in ritaglio.

### Miniature ritagliate
- Griglia e filmstrip del viewer usavano `Stretch="UniformToFill"` su celle quadrate, tagliando le foto.
  Ora `Uniform`: si vede sempre la foto intera (bande scure ai lati se non è quadrata).
- Conseguenza nota: il bordo giallo copre la cella, non la sola foto.

### Persone: i nomi dati durante l'analisi non venivano applicati alle foto successive
- **Sintomo:** molti volti di Laura non riconosciuti.
- **Dati reali (cartella Puglia, 236 volti analizzati):** su 168 volti senza nome, 105 somigliavano a
  Laura con somiglianza ≥ 0,42 (la soglia di proposta) e non erano stati proposti.
- **Causa:** `AvviaAnalisiPersone` leggeva l'elenco delle persone una sola volta all'inizio. Un nome dato
  mentre l'analisi era in corso non valeva per le foto analizzate dopo.
- **Fix** (`MainWindow.Volti.cs`, `MainWindow.Persone.cs`):
  - le persone si rileggono ogni 5 foto durante l'analisi;
  - a fine analisi, e all'apertura di una cartella già analizzata, parte un giro di
    `RiproponiPersone` sui volti ancora senza nome (estratto da `AssegnaEAggiornaAsync`).
- Le proposte restano "da confermare": non si assegna mai un nome senza conferma.
- **Non modificato:** soglie (`SogliaProposta` 0,42, `SogliaGruppo` 0,40). Per Laura il 22 % delle
  coppie di volti già confermati sta sotto 0,42 (occhiali, cappelli, angolazioni): se dopo il fix mancano
  ancora riconoscimenti, abbassare la soglia di proposta verso 0,38 è il passo successivo.

### Foto verticali stirate nel viewer (proporzioni sbagliate)
- **Sintomo:** una foto verticale (es. `20260808_112049.jpg`) appariva in un riquadro orizzontale, con il
  soggetto allargato. Le miniature erano corrette.
- **Causa:** il file è 4000×3000 con orientamento EXIF 6 (da ruotare di 90°). `PreviewDecoder` dava a WIC
  le dimensioni di scala già "ruotate" (1536×2048), ma WIC applica la scala **prima** della rotazione:
  il file veniva ridotto a 1536×2048 e poi ruotato, uscendo 2048×1536 con i pixel stirati.
- **Fix:** le dimensioni di scala si calcolano sul file non ruotato (`decoder.PixelWidth/PixelHeight`).
  Stessa correzione nel ripiego di `ThumbnailExtractor`. `PreviewDecoder` serve anche editing ed export,
  quindi anche questi ricevevano pixel stirati su foto con orientamento EXIF.
- **Test:** `DecodingOrientamentoTests` crea un JPEG 400×300 con orientamento 6 e controlla che la preview
  sia verticale 3:4, sia senza riduzione sia con riduzione. Il test fallisce senza la correzione.
- Le ricette di editing già salvate non cambiano (sono frazioni 0..1 della foto); eventuali ritagli fatti
  su foto che apparivano stirate vanno verificati.

### Export lento: ora più foto in parallelo
- **Misura** (12 core, cartella Puglia, HEIC 12 MP): per foto la decodifica costa ~1,4 s (HEVC ~0,9 s +
  gestione colore sRGB ~0,4 s), la ricetta ~0 ms, la codifica JPEG 50-130 ms. Il lavoro era tutto in
  sequenza: una foto alla volta, quindi quasi tutto il tempo era decodifica.
- **Fix** (`Exporter.EseguiAsync`): foto elaborate in parallelo con `Parallel.ForEachAsync`.
  - 4 foto insieme (3 alle dimensioni originali, dove ogni foto occupa centinaia di MB), limitato a metà dei core.
  - Risultati nell'ordine dell'elenco; nomi dei file unici (scelta del nome sotto lock); progresso
    monotono (il conteggio non torna mai indietro); l'annullamento non avvia nuove foto e completa
    quelle in corso (mai file a metà).
- **Effetto misurato** (24 foto, 2048 px): 1385 → 748 ms/foto (**≈1,9×**); alle dimensioni originali
  1609 → 529 ms/foto (**≈3×**). Oltre 4 foto insieme non si guadagna (la decodifica HEVC satura) e la
  memoria sale.
- Non toccato: la gestione colore (serve alle foto Display P3 degli iPhone: senza, i colori escono spenti).

### Avvisi/errori di Visual Studio dopo il pull
- `CS0246` su `RitaglioSuggerito` / `CartellaArchivio` / `ArchivioCartelle` e `WMC1006` su
  `WFastPic.Core.Persone.dll`: non sono errori di codice. `dotnet build WFastPic.slnx` dà 0 errori;
  VS aveva la cache indietro rispetto ai file nuovi arrivati col pull.
- **Rimedio:** chiudere VS, eliminare `.vs`, `bin`, `obj`, poi Build → Rebuild Solution.
- `MSB3021`/`MSB3027` ("file in uso") compaiono se `WFastPic.App.exe` è aperta durante la compilazione.

## Novità

### Report degli errori inattesi — `ErrorReporter`
- File: `src/WFastPic.App/ErrorReporter.cs`, attivato in `App.OnLaunched`.
- Intercetta `Application.UnhandledException` (UI, l'app resta aperta), `AppDomain.UnhandledException`
  e `TaskScheduler.UnobservedTaskException`.
- Ogni errore scrive `%LocalAppData%\WFastPic\reports\report-<data>.txt` (origine, versione, OS, runtime,
  memoria, stack completo); si tengono gli ultimi 30.
- Dialogo con messaggio e dettagli, pulsanti "Copia report", "Apri cartella report", "Chiudi". Un solo
  dialogo alla volta: una raffica di errori finisce comunque tutta su disco.
- Nessun invio in rete. Il gestore non lancia mai eccezioni a sua volta.

### Tempo previsto nell'esportazione
- Nel dialogo di export il testo di avanzamento mostra `N di M · circa X rimanenti · file`; a fine export
  `Completato: N foto esportate in <cartella> (in X)`. Stima a velocità media dall'inizio
  (`StimaAvanzamento.TempoRimanente`, dopo almeno 5 foto e 1 s); `StimaAvanzamento.Durata` formatta secondi/minuti
  ed è condivisa con scansione e miniature. Test `StimaAvanzamento_Durata`.

### Barra di stato
- Formato: `Selezionate M di N · Scarti K` (N = tutte le foto della cartella, indipendente dal filtro).

### Pannello cartelle nascosto nell'editor
- Aprendo una foto nel viewer il pannello cartelle (`RootSplitView`) si chiude; alla chiusura torna
  com'era. Se era già chiuso, resta chiuso. Le impostazioni salvano lo stato reale, non quello temporaneo.
  `MainWindow.ViewerHost.cs` (`ApriViewer`, evento `Viewer.Chiuso`).

### Unione dei volti: "È la stessa persona di…" — con annulla
Tasto destro su una persona o un gruppo nel pannello Persone → **È la stessa persona di…** → altra persona.

| Caso | Risultato |
|---|---|
| gruppo senza nome + persona con nome | i volti prendono quel nome e diventano campioni di riconoscimento |
| due gruppi senza nome | si chiede il nome, assegnato a tutti i volti |
| due persone con nome | dialogo "tieni A / tieni B"; l'altra confluisce nella prima in **tutte** le cartelle |

- **Annulla:** dopo ogni unione (e dopo "Dai un nome…") compare nel pannello una riga con il pulsante
  *Annulla*, che ripristina stato dei volti e campioni; se l'unione era tra persone con nome, le separa.
  Disponibile finché non si cambia cartella o si fa un'altra azione.
- **Archivio globale** (`ArchivioPersone`, `persone.db`): nuova colonna `unita_a` (migrazione automatica).
  Una persona unita non si cancella, punta a quella in cui è confluita. Nuovi metodi: `Unisci`,
  `AnnullaUnione`, `Unioni` (catene risolte, rifiuta le unioni circolari), `Elimina`, `EsisteNome`.
  `Elenco()` nasconde le persone unite; `TrovaOCrea` di un nome unito porta alla persona che resta.
- I volti nei sidecar delle cartelle non vengono riscritti: l'alias si risolve in lettura
  (`CostruisciElencoPersone`), quindi l'unione vale subito ovunque e si annulla con una sola scrittura.
- Test: `Archivio_persone_unione_reversibile` (catene, circolarità, annulla). Suite: 152 test.

### Integrazione con l'elenco Persone a righe (sviluppato in parallelo)
Due sessioni hanno sviluppato insieme il pannello Persone; nell'integrazione:
- **Modello di unione unico: quello reversibile** (`unita_a`, alias risolto in lettura, Annulla). La
  tabella `unioni` e la riscrittura dei sidecar alla riapertura, dell'altra versione, non ci sono più.
- **Dall'altra versione restano:** elenco a righe con tutte le persone con nome di tutte le cartelle
  (`LeggiFotoPerPersona`), chi non è nella cartella aperta attenuato e cliccabile per aprirla,
  "Apri la cartella", "Cambia immagine" (`ArchivioPersone.ImpostaAvatar`), "Elimina la persona",
  rinomina con il nome di un'altra persona = unione (senza chiedere quale nome tenere), numero di
  selezionate e periodo nel pannello cartelle.
- **Menu unico "È la stessa persona di…"** per gruppi senza nome e persone con nome (sostituisce "Unisci con").
- **Elimina la persona** cancella anche le persone confluite in lei; gli id non vengono riusati, quindi i
  volti con un id che non esiste più tornano senza nome quando la loro cartella viene aperta
  (`SostituisciPersona`, unico caso in cui il sidecar viene riscritto). L'Annulla in sospeso sparisce.
- Test: `Archivio_persone_avatar_ed_eliminazione_con_le_unite`. Suite: 138 test.
## Verifica

- `dotnet build WFastPic.slnx`: 0 errori di codice (con l'app chiusa).
- `dotnet test tests/WFastPic.Core.Tests`: 152 superati.
- **Da verificare a mano** (nessun accesso a mouse/tastiera da CLI): bordo giallo e Space in griglia e in
  ritaglio, pannello cartelle nascosto nel viewer, menu di unione e pulsante Annulla, proposte di Laura
  dopo "Rianalizza le persone" o alla riapertura della cartella.
