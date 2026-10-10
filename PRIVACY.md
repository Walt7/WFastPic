# Privacy

[English](PRIVACY.en.md)

WFastPic lavora sul tuo PC. Non ha account, pubblicità, statistiche d'uso né telemetria: nessuno sa
chi lo usa né come. **Le foto, i volti, i nomi delle persone e i nomi dei file non escono mai dal PC**:
anche il riconoscimento delle persone e il voto automatico girano interamente in locale.

## Cosa resta sul PC

- **Le foto originali**: non vengono mai modificate né inviate.
- **Accanto alle foto**, nella cartella nascosta `.wfastpic` di ogni cartella aperta: selezioni, stelle,
  modifiche, miniature, volti trovati (riquadro, piccola miniatura e impronta numerica), misure per il voto
  automatico, nomi dei luoghi già trovati.
- **In `%LocalAppData%\WFastPic`**: impostazioni, persone con nome (nome, immagine, impronte dei volti),
  modelli per il riconoscimento delle persone e report degli errori.

## Cosa esce dal PC, e quando

Solo queste richieste, tutte legate a una funzione che stai usando:

| Funzione | Verso | Cosa viene inviato | Quando | Come evitarlo |
|---|---|---|---|---|
| Nomi dei luoghi ("Polignano a Mare, Puglia") | `nominatim.openstreetmap.org` | le coordinate GPS della foto e la lingua | quando mostri il luogo di una foto con GPS; una volta per zona di ~100 m, poi resta salvato | mappa → togli "Nomi dei luoghi (OpenStreetMap)" |
| Mappa e mini-mappa | `tile.openstreetmap.org`, `server.arcgisonline.com` (Esri) | quali zone della mappa stai guardando | solo con la mappa aperta | non aprire la mappa |
| Libreria della mappa (Leaflet) | `unpkg.com` | niente di tuo | all'apertura della mappa | non aprire la mappa |
| Modelli per le persone (38 MB) | `huggingface.co` | niente di tuo | una volta sola, se attivi il riconoscimento delle persone | non attivarlo |
| Avviso di versione nuova | `api.github.com` | niente di tuo | a ogni avvio | Informazioni → togli "Avvisami quando c'è una versione nuova" |
| "Segnala il problema" | `github.com` | il report dell'errore, già ripulito da percorsi, nomi di file, nome utente e nome del PC | solo se premi il pulsante **e** invii tu la segnalazione su GitHub | non premerlo |

Come per qualunque richiesta su internet, questi servizi vedono l'indirizzo IP del PC. Le loro regole:
[OpenStreetMap Foundation](https://osmfoundation.org/wiki/Privacy_Policy),
[Esri](https://www.esri.com/en-us/privacy/overview),
[Hugging Face](https://huggingface.co/privacy),
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).

## Cancellare i dati

Disinstallando WFastPic i tuoi dati restano, di proposito (selezioni e persone non si perdono con un
aggiornamento). Per toglierli tutti: cancella la cartella `%LocalAppData%\WFastPic` e le cartelle nascoste
`.wfastpic` accanto alle foto. Le foto non vengono toccate.

## Modifiche

Se una versione nuova cambia qualcosa di quanto scritto qui, lo trovi in questa pagina e nel
[CHANGELOG](CHANGELOG.md).
