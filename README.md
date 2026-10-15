# WFastPic — sito

Questo branch contiene **solo** il sito statico di WFastPic, pubblicato su
**https://wfastpic.walterdev.eu**. Il codice dell'app è sul branch
[`master`](https://github.com/Walt7/WFastPic/tree/master).

È un branch orfano: niente sorgenti né storia di `master`, così sul server il clone resta piccolo.

## Struttura

| File | Cosa |
|---|---|
| `index.html` | pagina inglese (`/`) |
| `it/index.html` | pagina italiana (`/it/`) |
| `heic-photos-windows/`, `it/foto-heic-windows/` | guida: aprire le foto HEIC su Windows (EN/IT) |
| `picasa-alternative/`, `it/alternativa-picasa/` | guida: le alternative a Picasa (EN/IT) |
| `style.css` | stile comune, tema scuro come l'app |
| `img/` | icone, screenshot (JPEG leggeri da `assets/screenshot-*.png` di master), anteprime per WhatsApp e social `anteprima-it/en.jpg` |
| `sorgenti/anteprima-social.html` | sorgente delle anteprime 1200×630 (istruzioni per rigenerarle dentro il file) |
| `robots.txt`, `sitemap.xml` | per i motori di ricerca |
| `google….html` | verifica di Google Search Console: non cancellarlo |
| `deploy/deploy.sh` | script lanciato dal webhook sul VPS |
| `contatore/` | contatore di visite in Go (vedi sotto) |
| `nginx-wfastpic.conf` | configurazione nginx del sito (copia di quella attiva sul server): blocca anche README e LICENSE |

HTML statico, senza build: percorsi relativi, nessuna dipendenza. Richieste esterne: il contatore di visite
del sito stesso (vedi sotto), i badge di
shields.io e, nella pagina italiana, le date dell'ultima release e dell'ultimo commit lette da
api.github.com.

## Pubblicare

Un push su `sito` fa partire il webhook del VPS, che esegue `deploy/deploy.sh`
(`git fetch` + `git reset --hard origin/sito` nella cartella servita da nginx). nginx non serve
mai `.git`, i file nascosti, `deploy/`, la sua configurazione, `README.md` e `LICENSE`.

Anteprima in locale:

```bash
python -m http.server 8765
```

e poi http://localhost:8765.

## Contatore di visite

`contatore/` è un piccolo servizio Go (systemd `wfastpic-contatore`, solo su `127.0.0.1:8090`) che
scrive in MySQL un evento per ogni pagina vista e per ogni clic su "Scarica". Le pagine lo chiamano
con un `POST /api/visita` (nginx fa da proxy). Niente cookie e niente dati personali: il visitatore
è un hash di IP + user agent + un sale casuale che cambia ogni giorno e non viene mai salvato.
Crawler e generatori di anteprime (WhatsApp, Facebook, …) non vengono contati.

- Numeri: **https://wfastpic.walterdev.eu/statistiche/** (password in `CONTATORE_PASSWORD`).
- Le proprie visite: aprire il sito una volta con `?nonconta` da ogni browser (`?conta` per annullare).
- Configurazione in `/etc/wfastpic-contatore.env`, fuori dal repo: `CONTATORE_DSN`
  (`utente:password@tcp(127.0.0.1:3306)/wfastpic`) e `CONTATORE_PASSWORD`. La tabella `eventi` la
  crea il servizio al primo avvio.
- Test: `cd contatore && go test ./...`.

## A ogni release

La versione è scritta a mano in tutte e due le pagine: nel link del pulsante "Scarica"
(`releases/download/vX.Y.Z/WFastPic-X.Y.Z-win-x64-setup.exe`), nel testo "Versione X.Y.Z", nel
nome del setup nella sezione Installare e in `softwareVersion` del JSON-LD. Va aggiornata insieme
a `<Version>` del csproj su `master`. I badge invece si aggiornano da soli.

Se aggiungi o togli pagine, aggiorna `sitemap.xml` e l'elenco `pagineValide` in `contatore/main.go`; se cambiano gli screenshot dell'app,
rigenera i JPEG in `img/`.

## Licenza

[MIT](LICENSE), come l'app. Le foto negli screenshot sono con licenza CC0 da Wikimedia Commons
([crediti](https://github.com/Walt7/WFastPic/blob/master/assets/crediti-foto-screenshot.md)).
