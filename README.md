<p align="center">
  <img src="assets/icon-256.png" width="96" alt="Logo di WFastPic">
</p>

# WFastPic

**Scegliere e sistemare in fretta centinaia di foto, su Windows.** Versione attuale: **0.9**
([novità](CHANGELOG.md)).

WFastPic è il successore open source di Picasa, pensato per le grandi raccolte di foto HEIC (iPhone,
droni, fotocamere): apri la cartella della vacanza, tieni le migliori con la barra spaziatrice,
scarti le altre, dai una sistemata veloce ed esporti. Tutto da tastiera, senza mai aspettare.

Non è un Lightroom e non è un gestore di file: è lo strumento per passare da 1300 scatti alle 80
foto da tenere, nel minor numero di gesti possibile.

## Cosa fa

**Scegliere**
- Griglia veloce anche con migliaia di foto, in ordine di scatto, divisibile per giorno.
- `Space` seleziona, `Canc` scarta, `0`–`5` stelle, `Tab` cambia filtro.
- **Raffiche** riconosciute da sole (foto scattate a pochi secondi): `Shift+Space` tiene questa e
  scarta le altre del gruppo. Nell'editor le foto della stessa raffica compaiono in piccolo accanto
  alla foto, per confrontarle al volo.
- Scarti mai cancellati per sbaglio: vanno nel Cestino di Windows (con conferma) o nella cartella
  `Scarti/`, sempre insieme al video abbinato (Live Photo).

**Sistemare** (sempre senza toccare l'originale)
- Ritaglia con proporzioni e ritagli suggeriti che non tagliano i volti; raddrizza; gira di 90° e
  specchia.
- "Ho fortuna", contrasto e colore automatici, luce di riempimento, alte luci, ombre, temperatura.
- Annulla / ripeti, `B` per vedere l'originale, schermo intero con `F11`.
- Colonna dei dettagli: giorno della settimana, data e ora di scatto, file, luogo con mini-mappa,
  dispositivo e dati di ripresa (anche altezza e gimbal dei droni DJI).

**Ritrovare**
- Cartelle raggruppate per anno, come in Picasa.
- Mappa delle foto, nomi dei luoghi, posizione correggibile a mano.
- Persone (opzionale): volti simili raggruppati, ai quali dai un nome che vale in tutte le cartelle.

**Esportare**
- JPEG alle dimensioni originali o ridimensionati, qualità a scelta, filigrana, tag.
- Data di scatto e posizione GPS conservate (o rimosse, se preferisci).

## Requisiti

- Windows 10 (1809 o successivo) o Windows 11, x64 o ARM64.
- Per le foto HEIC servono le estensioni **HEIF** e **HEVC** di Windows: di solito ci sono già; se le
  foto non si aprono, si installano dal Microsoft Store.

## Compilare e avviare

Serve l'SDK di [.NET 10](https://dotnet.microsoft.com/download). Visual Studio non è obbligatorio.

```bash
dotnet build WFastPic.slnx
```

L'app si trova poi in `src/WFastPic.App/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WFastPic.App.exe`.

Test:

```bash
dotnet test tests/WFastPic.Core.Tests
```

## Scorciatoie principali

| Dove | Tasti |
|---|---|
| Griglia e editor | `←` / `→` foto precedente / successiva · `Space` seleziona · `Canc` scarta · `0`–`5` stelle |
| Griglia | `Shift+Space` tieni e scarta il resto della raffica · `Tab` filtro · `Ctrl+A` seleziona le visibili · `Shift+rotella` dimensione miniature · `Invio` apri |
| Editor | `X` ritaglia · `L` raddrizza · `R` / `Shift+R` gira · `U` Ho fortuna · `B` originale · `Ctrl+Z` / `Ctrl+Y` · `F11` schermo intero · `Esc` chiudi |

L'elenco completo è nell'app, sotto "Scorciatoie" nella barra in basso.

## Le tue foto restano tue

- Le foto originali non vengono mai modificate: selezioni, stelle e modifiche stanno in una cartella
  nascosta `.wfastpic` accanto alle foto.
- Tutto gira sul PC. Escono solo le richieste che puoi spegnere o che scegli tu:
  - i nomi dei luoghi chiesti a OpenStreetMap (disattivabile dalla mappa) e i riquadri delle mappe;
  - i modelli per il riconoscimento delle persone, scaricati una volta sola da Hugging Face (OpenCV)
    solo se attivi l'opzione. Le foto non escono mai.

## Per chi sviluppa

- Architettura, convenzioni e stato del progetto: [`AGENTS.md`](AGENTS.md).
- Novità di ogni versione: [`CHANGELOG.md`](CHANGELOG.md).
- C# + WinUI 3 (Windows App SDK), decodifica HEIC con WIC, sidecar SQLite per cartella. La logica sta
  nei progetti `WFastPic.Core.*`, senza dipendenze da WinUI, ed è coperta da test xUnit.

## Licenza

MIT.
