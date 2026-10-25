namespace WFastPic.Core.Localizzazione;

internal static partial class TestiEn
{
    /// <summary>Testi costruiti nel codice della finestra principale (stato, export, informazioni, mappa, errori).</summary>
    private static void FinestraCodice(Dictionary<string, string> d)
    {
        // Barra di stato, caricamento, stato vuoto
        d["Apri"] = "Open";
        d["Togli la selezione"] = "Unselect";
        d["Seleziona"] = "Select";
        d["Rimetti tra le foto"] = "Put back";
        d["Scarta"] = "Reject";
        d["Seleziona tutte"] = "Select all";
        d["Inverti selezione"] = "Invert selection";
        d["Salva selezione…"] = "Save selection…";
        d["Mostra in Esplora file"] = "Show in File Explorer";
        d["Nome della selezione"] = "Selection name";
        d["Salva selezione"] = "Save selection";
        d["Salva"] = "Save";
        d["Annulla"] = "Cancel";
        d["{0} da salvare. Selezioni già salvate: {1}."] = "{0} to save. Already saved selections: {1}.";
        d["{0} da salvare."] = "{0} to save.";
        d["Esiste già una selezione «{0}»"] = "A selection «{0}» already exists";
        d["Vuoi sostituirla con queste {0}?"] = "Replace it with these {0}?";
        d["Sostituisci"] = "Replace";
        d["Selezione «{0}» salvata: {1}"] = "Selection «{0}» saved: {1}";
        d["Impossibile salvare la selezione: {0}"] = "Cannot save the selection: {0}";
        d["Perdi la selezione fatta"] = "You will lose your selection";
        d["Hai {0} selezionate: questa operazione cambia la selezione e non si può annullare. Vuoi salvarla prima?"] = "You have {0} selected: this changes the selection and cannot be undone. Save it first?";
        d["Salva e continua"] = "Save and continue";
        d["Continua senza salvare"] = "Continue without saving";
        d["Selezioni salvate"] = "Saved selections";
        d["Lettura foto…"] = "Reading photos…";
        d["Miniature"] = "Thumbnails";
        d["Impossibile aprire «{0}»: {1}"] = "Cannot open «{0}»: {1}";
        d["Errore nel salvare le modifiche: {0}"] = "Error saving the changes: {0}";
        d["{0} · {1} selezionate · {2} scarti"] = "{0} · {1} selected · {2} rejected";
        d["Benvenuto in WFastPic"] = "Welcome to WFastPic";
        d["Apri una cartella di foto per iniziare: scegli le migliori con Space, scarta con Canc, sistema ed esporta. Le cartelle aperte restano nel pannello a sinistra, divise per anno."] =
            "Open a photo folder to start: pick the best with Space, reject with Delete, fix and export. Folders you open stay in the left panel, grouped by year.";
        d["Nessuna foto in questa cartella"] = "No photos in this folder";
        d["Scegli un'altra cartella dal pannello a sinistra o aprine una nuova."] = "Choose another folder from the left panel or open a new one.";
        d["Nessuna foto con questo filtro"] = "No photos match this filter";
        d["Premi Tab o scegli «Tutte» nel filtro in alto per rivederle tutte."] = "Press Tab or choose «All» in the filter at the top to see them all again.";

        // Foto (plurale) e cartelle
        d["1 foto"] = "1 photo";
        d["{0:N0} foto"] = "{0:N0} photos";
        d["Cartella non trovata: {0} (disco scollegato o cartella spostata)"] = "Folder not found: {0} (disk unplugged or folder moved)";
        d["Cartella non trovata"] = "Folder not found";
        d["Non ancora aperta"] = "Not opened yet";
        d["Senza data"] = "No date";
        d["{0} · 1 selezionata"] = "{0} · 1 selected";
        d["{0} · {1:N0} selezionate"] = "{0} · {1:N0} selected";

        // Avanzamento e durate
        d["{0} di {1}"] = "{0} of {1}";
        d["circa {0} rimanenti"] = "about {0} remaining";
        d["{0} s"] = "{0} s";
        d["{0} min"] = "{0} min";

        // Gestione scarti
        d["{0} in scarto"] = "{0} rejected";
        d["{0} in scarto ({1} con video abbinato, che verrà spostato insieme)"] = "{0} rejected ({1} with a paired video, which will be moved along)";
        d["Conferma: {0} nel Cestino"] = "Confirm: {0} to the Recycle Bin";
        d["video"] = "video";
        d["nel Cestino"] = "to the Recycle Bin";
        d["in Scarti/"] = "to Scarti/";
        d["{0} spostate {1}, {2} non riuscite:"] = "{0} moved {1}, {2} failed:";
        d["… e altre {0}"] = "… and {0} more";

        // Export
        d["Solo le foto selezionate ({0})"] = "Selected photos only ({0})";
        d["Tutte tranne gli scarti ({0})"] = "All except rejects ({0})";
        d["Le foto verranno salvate in {0}"] = "Photos will be saved in {0}";
        d["Esporta {0}"] = "Export {0}";
        d["Annulla esportazione"] = "Cancel export";
        d["Indica un percorso completo, ad esempio D:\\Foto."] = "Enter a full path, for example D:\\Photos.";
        d["Il nome della cartella contiene caratteri non validi."] = "The folder name contains invalid characters.";
        d["Preparazione…"] = "Preparing…";
        d["Errore: {0}"] = "Error: {0}";
        d["Esportazione annullata: {0} esportate in {1}"] = "Export cancelled: {0} exported to {1}";
        d["Completato: {0} esportate in {1} (in {2})"] = "Done: {0} exported to {1} (in {2})";
        d["{0} esportate, {1} non riuscite:"] = "{0} exported, {1} failed:";
        d["Annullamento… si completa la foto in corso"] = "Cancelling… the current photo is being completed";

        // Informazioni
        d["Versione {0}"] = "Version {0}";
        d["Scegliere e sistemare in fretta centinaia di foto, su Windows. Un tributo open source a Picasa, pensato per le grandi raccolte HEIC: le foto originali non vengono mai modificate."] =
            "Pick and fix hundreds of photos fast, on Windows. An open source tribute to Picasa, built for large HEIC collections: original photos are never modified.";
        d["Progetto su GitHub"] = "Project on GitHub";
        d["Novità di questa versione"] = "What's new in this version";
        d["CHANGELOG.md"] = "CHANGELOG.en.md";
        d["PRIVACY.md"] = "PRIVACY.en.md";
        d["Privacy"] = "Privacy";
        d["Licenza"] = "License";
        d["MIT · © 2026 Walter. Libero da usare, modificare e ridistribuire."] = "MIT · © 2026 Walter. Free to use, modify and redistribute.";
        d["Grazie a"] = "Thanks to";
        d["Mappe © contributori di OpenStreetMap · nomi dei luoghi da Nominatim · immagini satellitari Esri · Leaflet e Leaflet.markercluster · modelli YuNet e SFace di OpenCV · ONNX Runtime · SQLite · Windows App SDK. Ispirato a Picasa, marchio di Google LLC: WFastPic non è affiliato a Google."] =
            "Maps © OpenStreetMap contributors · place names from Nominatim · satellite imagery Esri · Leaflet and Leaflet.markercluster · YuNet and SFace models from OpenCV · ONNX Runtime · SQLite · Windows App SDK. Inspired by Picasa, a trademark of Google LLC: WFastPic is not affiliated with Google.";
        d["Dati dell'app"] = "App data";
        d["Impostazioni, persone e modelli stanno qui; selezioni e modifiche di ogni cartella nella sua cartella nascosta .wfastpic."] =
            "Settings, people and models are stored here; selections and edits of each folder are in its hidden .wfastpic folder.";
        d["Lingua"] = "Language";
        d["Automatica (lingua di Windows)"] = "Automatic (Windows language)";
        d["Riavvia WFastPic per applicare la lingua."] = "Restart WFastPic to apply the language.";

        // Mappa
        d["Scansione metadati GPS in corso…"] = "Scanning GPS metadata…";
        d["{0} selezionate su mappa ({1} senza GPS)"] = "{0} selected on the map ({1} without GPS)";
        d["{0} selezionate su mappa"] = "{0} selected on the map";
        d["{0} geolocalizzate"] = "{0} geotagged";
        d["Nessuna delle foto selezionate dispone di coordinate GPS. Clicca 'Tutte le foto' o 'Riscansiona GPS'."] =
            "None of the selected photos has GPS coordinates. Click 'All photos' or 'Rescan GPS'.";
        d["Errore mappa"] = "Map error";
        d["Impossibile caricare la mappa: {0}"] = "Cannot load the map: {0}";
        d["Data sconosciuta"] = "Unknown date";
        d["Satellite"] = "Satellite";
        d["Immagini"] = "Imagery";
        d["★ Selezionata"] = "★ Selected";
        d["Apri nel Viewer"] = "Open in the Viewer";
        d["Nomi dei luoghi"] = "Place names";

        // Dati di ripresa
        d["drone"] = "drone";
        d["telefono"] = "phone";
        d["{0} m s.l.m."] = "{0} m a.s.l.";
        d["{0} m dal decollo"] = "{0} m above takeoff";
        d["direzione {0}° {1}"] = "heading {0}° {1}";
        d["camera {0}°"] = "camera {0}°";
        d["(verso il basso)"] = "(pointing down)";
        d["(orizzontale)"] = "(level)";
        d["N"] = "N"; d["NE"] = "NE"; d["E"] = "E"; d["SE"] = "SE"; d["S"] = "S"; d["SO"] = "SW"; d["O"] = "W"; d["NO"] = "NW";

        // Errori inattesi
        d["WFastPic — report errore"] = "WFastPic — error report";
        d["Data:"] = "Date:";
        d["Origine:"] = "Source:";
        d["Versione:"] = "Version:";
        d["Memoria:"] = "Memory:";
        d["(eccezione nulla)"] = "(null exception)";
        d["Le foto originali non sono state toccate. Il report è stato salvato; puoi continuare a lavorare."] =
            "Your original photos were not touched. The report has been saved; you can keep working.";
        d["Si è verificato un errore inatteso"] = "An unexpected error occurred";
        d["Copia report"] = "Copy report";
        d["Apri cartella report"] = "Open report folder";
        d["Segnala il problema"] = "Report the problem";
        d["Il testo qui sopra è quello che puoi inviare: percorsi e nomi delle foto, nome utente e nome del PC sono già stati tolti. " +
          "«Segnala il problema» apre GitHub nel browser con la segnalazione già scritta: la controlli e la invii tu (serve un account GitHub)."] =
            "The text above is what you can send: photo paths and names, user name and PC name have already been removed. " +
            "«Report the problem» opens GitHub in the browser with the report already written: you check it and send it yourself (a GitHub account is needed).";
        d["Cosa stavi facendo quando è comparso l'errore? (scrivilo qui)"] = "What were you doing when the error appeared? (write it here)";

        // Avviso di versione nuova
        d["Nuova versione {0} disponibile"] = "New version {0} available";
        d["Apre la pagina della nuova versione su GitHub"] = "Opens the new version page on GitHub";
        d["Aggiornamenti"] = "Updates";
        d["Avvisami quando c'è una versione nuova"] = "Tell me when a new version is available";
        d["All'avvio WFastPic chiede a GitHub qual è l'ultima versione, come quando si apre la pagina nel browser: " +
          "non invia foto né dati dell'app e non scarica niente da solo. Se ce n'è una nuova compare un piccolo avviso in basso."] =
            "At startup WFastPic asks GitHub for the latest version, like opening the page in a browser: " +
            "it sends no photos or app data and downloads nothing on its own. If there is a new one, a small notice appears at the bottom.";
    }

    /// <summary>Testi costruiti nel codice del viewer (editor).</summary>
    private static void ViewerCodice(Dictionary<string, string> d)
    {
        d["caricamento…"] = "loading…";
        d["impossibile leggere la foto"] = "cannot read the photo";
        d["Griglia: metà"] = "Grid: halves";
        d["Griglia: diagonali"] = "Grid: diagonals";
        d["Zoom {0}%"] = "Zoom {0}%";
        d["Proporzioni: {0}"] = "Ratio: {0}";
        d["libera"] = "free";
        d["originale"] = "original";
        d["originale, girata"] = "original, rotated";
        d["Come la foto"] = "Same as the photo";
        d["←/→ foto · Space seleziona · Delete scarta · 0-5 rating · rotella zoom, trascina per spostare · X ritaglia · L raddrizza · U ho fortuna · Ctrl+Z/Y annulla/ripeti · B originale · F11 schermo intero · Esc chiudi"] =
            "←/→ photo · Space select · Delete reject · 0-5 rating · wheel zoom, drag to pan · X crop · L straighten · U I'm Feeling Lucky · Ctrl+Z/Y undo/redo · B original · F11 full screen · Esc close";
        d["Trascina gli angoli o i lati per ritagliare, trascina dentro per spostare · L / frecce raddrizza · O riquadro orizzontale/verticale · Invio o X applica · Esc annulla"] =
            "Drag the corners or sides to crop, drag inside to move · L / arrows straighten · O landscape/portrait box · Enter or X apply · Esc cancel";
        d["Data di scatto sconosciuta"] = "Shooting date unknown";
        d["File modificato {0}"] = "File modified {0}";
        d["Raffica: foto {0} di {1}"] = "Burst: photo {0} of {1}";
        d["{0} byte"] = "{0} bytes";
        d["Tiene conto di 1 volto."] = "Takes 1 face into account.";
        d["Tiene conto di {0} volti."] = "Takes {0} faces into account.";
        d["Nessuna posizione"] = "No location";
        d["Posizione corretta a mano"] = "Location corrected by hand";
        d["Luogo sconosciuto"] = "Unknown place";
        d["Aggiungi"] = "Add";
        d["Correggi"] = "Fix";
        d["Nuova posizione {0} · Salva per confermare"] = "New location {0} · Save to confirm";
        d["Trascina il segnaposto o clicca nel punto giusto. In alto a destra: mappa o satellite."] =
            "Drag the marker or click the right spot. Top right: map or satellite.";
    }

    /// <summary>Testi del pannello Persone.</summary>
    private static void PersoneCodice(Dictionary<string, string> d)
    {
        d["Attiva il riconoscimento per raggruppare i volti per persona."] = "Turn on recognition to group faces by person.";
        d["Analisi in corso: {0} di {1}"] = "Analysis in progress: {0} of {1}";
        d["Analisi delle foto in corso…"] = "Analyzing the photos…";
        d["Nessun gruppo di volti trovato in questa cartella."] = "No group of faces found in this folder.";
        d["{0} cartelle"] = "{0} folders";
        d["{0} qui"] = "{0} here";
        d["{0} da confermare"] = "{0} to confirm";
        d["Senza nome"] = "Unnamed";
        d["Dai un nome…"] = "Give a name…";
        d["Ignora questi volti"] = "Ignore these faces";
        d["Conferma le proposte ({0})"] = "Confirm the suggestions ({0})";
        d["Rinomina…"] = "Rename…";
        d["Cambia immagine…"] = "Change picture…";
        d["È la stessa persona di…"] = "Same person as…";
        d["Questo gruppo è la stessa persona di…"] = "This group is the same person as…";
        d["«{0}» è la stessa persona di…"] = "«{0}» is the same person as…";
        d["Nome della persona"] = "Person's name";
        d["I {0} volti del gruppo prendono questo nome (anche di una persona già nota); WFastPic lo proporrà anche sui volti somiglianti, in tutte le cartelle."] =
            "The {0} faces in the group get this name (even of an already known person); WFastPic will also suggest it on similar faces, in all folders.";
        d["Rinomina la persona in tutte le cartelle. Con il nome di un'altra persona, le due si uniscono."] =
            "Renames the person in all folders. With another person's name, the two are merged.";
        d["Chi è?"] = "Who is it?";
        d["Rinomina"] = "Rename";
        d["Esiste già una persona chiamata «{0}»."] = "A person named «{0}» already exists.";
        d["Nome «{0}» dato a {1} volti"] = "Name «{0}» given to {1} faces";
        d["Gruppo senza nome unito a «{0}»"] = "Unnamed group merged into «{0}»";
        d["Unisci le persone"] = "Merge people";
        d["«{0}» e «{1}» sono la stessa persona? Scegli il nome da tenere: l'unione vale in tutte le cartelle e puoi annullarla subito dopo."] =
            "Are «{0}» and «{1}» the same person? Choose the name to keep: the merge applies to all folders and you can undo it right after.";
        d["Tieni «{0}»"] = "Keep «{0}»";
        d["«{0}» unita a «{1}»"] = "«{0}» merged into «{1}»";
        d["Eliminare «{0}»?"] = "Delete «{0}»?";
        d["Il nome viene tolto da tutte le foto, in tutte le cartelle: i volti tornano senza nome. Le foto non vengono toccate."] =
            "The name is removed from all photos, in all folders: the faces become unnamed again. The photos are not touched.";
        d["Elimina"] = "Delete";
        d["Immagine di {0}"] = "Picture of {0}";
        d["Usa questa"] = "Use this";
        d["Rianalizzare le persone di questa cartella?"] = "Re-analyze the people in this folder?";
        d["Volti e impronte di tutte le foto della cartella vengono ricalcolati da zero, in background. Le persone con un nome restano: i loro volti verranno riproposti in automatico, da confermare."] =
            "Faces and fingerprints of all photos in the folder are recalculated from scratch, in the background. Named people stay: their faces will be suggested again automatically, for you to confirm.";
        d["Rianalizza"] = "Re-analyze";

        // Riconoscimento: dialogo, avanzamento
        d["Riconoscere le persone?"] = "Recognize people?";
        d["Attiva"] = "Turn on";
        d["Non ora"] = "Not now";
        d["WFastPic può riconoscere le persone nelle foto, come Picasa: raggruppa i volti simili, tu dai un nome e la ritrova da solo anche nelle altre cartelle.\n\n• Funziona tutto sul PC: nessuna foto esce.\n• Al primo uso scarica due modelli (38 MB) da Hugging Face (OpenCV).\n• Analizza le foto in background, dopo le miniature: con tante foto può richiedere qualche minuto e usare il processore. Si ferma mentre guardi le foto nel viewer.\n\nPuoi cambiare idea quando vuoi dall'interruttore nella sezione Persone a sinistra."] =
            "WFastPic can recognize the people in your photos, like Picasa: it groups similar faces, you give a name and it finds them again on its own in other folders.\n\n• Everything runs on your PC: no photo leaves it.\n• On first use it downloads two models (38 MB) from Hugging Face (OpenCV).\n• It analyzes the photos in the background, after the thumbnails: with many photos it can take a few minutes and use the processor. It pauses while you look at photos in the viewer.\n\nYou can change your mind any time with the switch in the People section on the left.";
        d["Riconoscimento persone non disponibile (modelli non scaricati): solo rilevamento dei volti."] =
            "People recognition not available (models not downloaded): face detection only.";
        d["Riconoscimento persone non disponibile su questo PC."] = "People recognition is not available on this PC.";
        d["Scarico i modelli per le persone · {0} di {1} MB"] = "Downloading the people models · {0} of {1} MB";
        d["Persone"] = "People";
        d["Volti"] = "Faces";
    }

    /// <summary>Testi comuni (chiavi usate in più punti o assenti dallo XAML).</summary>
    private static void Libreria(Dictionary<string, string> d)
    {
        d["Annulla"] = "Cancel";
        d["Chiudi"] = "Close";
        d["Salva"] = "Save";
    }
}
