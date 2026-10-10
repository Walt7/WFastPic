using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WFastPic.Core.Metadata;
using WFastPic.Core.Persistence;

namespace WFastPic.App;

/// <summary>
/// Luoghi stile Picasa: nome del posto (geocodifica inversa con cache nel sidecar), dispositivo e
/// dati di ripresa (altitudine, direzione, drone), mini-mappa nell'editor con correzione della
/// posizione. La correzione vive solo nel sidecar: l'originale non viene mai scritto.
/// </summary>
public partial class MainWindow
{
    private static readonly HttpClient HttpNominatim = RisolutoreLuoghi.CreaClientNominatim();
    private static bool _cartellaWebViewImpostata;

    private bool _nomiLuoghiOnline = true;
    private LibrarySidecarDatabase? _dbLuoghi;
    private RisolutoreLuoghi? _risolutoreLuoghi;

    // ---------------------------------------------------------------- servizi comuni

    /// <summary>WebView2 della mappa grande e della mini-mappa: stessa cartella dati e stesso User-Agent.</summary>
    internal static async Task PreparaWebViewAsync(WebView2 webView)
    {
        if (!_cartellaWebViewImpostata)
        {
            var cartella = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WFastPic", "WebView2");
            Directory.CreateDirectory(cartella);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", cartella);
            _cartellaWebViewImpostata = true;
        }

        await webView.EnsureCoreWebView2Async();
        // Le policy delle mappe OpenStreetMap chiedono un User-Agent che identifichi l'applicazione.
        webView.CoreWebView2.Settings.UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 WFastPic/1.0";
    }

    /// <summary>
    /// Nome del luogo per le coordinate, dalla cache della cartella o da OpenStreetMap. Con i nomi
    /// online disattivati si usa solo quanto già in cache: nessuna coordinata esce dal PC.
    /// </summary>
    private async Task<string?> NomeLuogoAsync(double latitudine, double longitudine, CancellationToken ct = default)
    {
        var db = _db;
        if (db is null) return null;

        string? DaCache(string chiave)
        {
            try { return db.GetNomeLuogo(chiave); }
            catch (ObjectDisposedException) { return null; } // cartella cambiata nel frattempo
        }

        if (!_nomiLuoghiOnline) return DaCache(NomeLuogo.Chiave(latitudine, longitudine));

        if (!ReferenceEquals(_dbLuoghi, db) || _risolutoreLuoghi is null)
        {
            _dbLuoghi = db;
            _risolutoreLuoghi = new RisolutoreLuoghi(HttpNominatim, DaCache, (chiave, nome) =>
            {
                try { db.SetNomeLuogo(chiave, nome); }
                catch (ObjectDisposedException) { }
            });
        }

        return await _risolutoreLuoghi.NomeAsync(latitudine, longitudine, ct);
    }

    private void NomiLuoghiCheck_Click(object sender, RoutedEventArgs e) =>
        _nomiLuoghiOnline = MapNomiLuoghiCheck.IsChecked == true;

    /// <summary>Righe brevi per popup e pannello: dispositivo, altitudine, decollo, direzione, inclinazione.</summary>
    internal static List<string> RigheRipresa(PhotoGridItem foto, DatiRipresa dati)
    {
        var righe = new List<string>();

        if (dati.Dispositivo is string dispositivo)
        {
            var (icona, tipo) = dati.Tipo switch
            {
                TipoDispositivo.Drone => ("🚁", " · drone"),
                TipoDispositivo.Telefono => ("📱", " · telefono"),
                _ => ("📷", ""),
            };
            righe.Add($"{icona} {dispositivo}{tipo}");
        }

        // Una posizione corretta a mano non ha più l'altitudine/direzione del file.
        var altitudine = foto.GpsManuale ? null : foto.Altitude ?? dati.AltitudineMetri;
        if (altitudine is double alt) righe.Add($"⛰ {alt.ToString("F0", CultureInfo.CurrentCulture)} m s.l.m.");
        if (dati.AltezzaDalDecolloMetri is double decollo) righe.Add($"🛫 {decollo.ToString("F0", CultureInfo.CurrentCulture)} m dal decollo");

        var direzione = foto.GpsManuale ? null : foto.Direction ?? dati.DirezioneGradi;
        if (direzione is double dir) righe.Add($"🧭 direzione {dir:F0}° {DatiRipresa.PuntoCardinale(dir)}");

        if (dati.InclinazioneGradi is double incl)
            righe.Add($"📐 camera {incl:F0}°{(incl <= -80 ? " (verso il basso)" : incl >= -5 ? " (orizzontale)" : "")}");

        return righe;
    }

    private static string TestoCoordinate(double lat, double lon) =>
        string.Create(CultureInfo.InvariantCulture, $"📍 {lat:F5}, {lon:F5}");

    // ---------------------------------------------------------------- mappa grande: popup

    /// <summary>Il popup di una foto chiede luogo e dati di ripresa: si leggono solo per quella foto.</summary>
    private async Task InviaDettagliPopupAsync(long photoId)
    {
        if (!_perId.TryGetValue(photoId, out var foto)) return;

        var dati = await Task.Run(() => PhotoMetadataReader.LeggiDatiRipresa(foto.PercorsoCompleto));
        var luogo = foto is { Latitude: double lat, Longitude: double lon } ? await NomeLuogoAsync(lat, lon) : null;

        if (MapWebView.CoreWebView2 is null) return;
        MapWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            action = "dettagli",
            id = photoId,
            luogo,
            righe = RigheRipresa(foto, dati),
        }));
    }

    // ---------------------------------------------------------------- editor: pannello Luogo

    private CancellationTokenSource? _ctsLuogo;
    private bool _miniMappaPronta;
    private Task<bool>? _preparazioneMiniMappa;
    private bool _luogoInModifica;
    private (double Lat, double Lon)? _posizioneProposta;

    /// <summary>
    /// Aggiorna il pannello per la foto corrente. Subito coordinate e mini-mappa; poi, in background,
    /// dati di ripresa dal file e (dopo una breve pausa, per non interrogare OpenStreetMap a ogni ←/→)
    /// il nome del luogo. Una navigazione successiva annulla il lavoro della foto precedente.
    /// </summary>
    private async void AggiornaPannelloLuogo()
    {
        _ctsLuogo?.Cancel();
        _ctsLuogo = new CancellationTokenSource();
        var ct = _ctsLuogo.Token;

        ImpostaModificaLuogo(false);
        var foto = _fotoViewerCorrente;
        if (foto is null) return;

        var coordinate = foto is { Latitude: double lat0, Longitude: double lon0 } ? TestoCoordinate(lat0, lon0) : null;
        ViewerLuogoNome.Text = coordinate is null ? "Nessuna posizione" : foto.GpsManuale ? "Posizione corretta a mano" : "…";
        ViewerLuogoDettagli.Text = coordinate ?? "";
        ViewerLuogoCorreggiButton.Content = foto.HasGps ? "Correggi" : "Aggiungi";
        ViewerLuogoRimuoviButton.IsEnabled = foto.HasGps;
        ViewerLuogoRipristinaButton.Visibility = foto.GpsManuale ? Visibility.Visible : Visibility.Collapsed;

        _ = MostraSuMiniMappaAsync(modifica: false);

        try
        {
            var dati = await Task.Run(() => PhotoMetadataReader.LeggiDatiRipresa(foto.PercorsoCompleto), ct);
            if (ct.IsCancellationRequested) return;

            var righe = RigheRipresa(foto, dati);
            if (coordinate is not null) righe.Insert(0, coordinate);
            ViewerLuogoDettagli.Text = string.Join("\n", righe);

            if (foto is { Latitude: double lat, Longitude: double lon })
            {
                await Task.Delay(350, ct);
                var nome = await NomeLuogoAsync(lat, lon, ct);
                if (!ct.IsCancellationRequested)
                    ViewerLuogoNome.Text = nome ?? (foto.GpsManuale ? "Posizione corretta a mano" : "Luogo sconosciuto");
            }
        }
        catch (OperationCanceledException)
        {
            // Foto cambiata: il pannello è già di un'altra.
        }
    }

    private async Task<bool> PreparaMiniMappaAsync()
    {
        if (_miniMappaPronta) return true;
        return await (_preparazioneMiniMappa ??= CreaMiniMappaAsync());
    }

    private async Task<bool> CreaMiniMappaAsync()
    {
        try
        {
            await PreparaWebViewAsync(ViewerMiniMappa);
            ViewerMiniMappa.CoreWebView2.WebMessageReceived += MiniMappa_WebMessageReceived;

            var caricata = new TaskCompletionSource();
            void Completata(CoreWebView2 s, CoreWebView2NavigationCompletedEventArgs a)
            {
                s.NavigationCompleted -= Completata;
                caricata.TrySetResult();
            }
            ViewerMiniMappa.CoreWebView2.NavigationCompleted += Completata;

            // Da un host virtuale e non con NavigateToString: senza un'origine vera le richieste dei
            // riquadri non hanno Referer e i server di OpenStreetMap possono rifiutarle.
            var cartellaWeb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WFastPic", "web");
            Directory.CreateDirectory(cartellaWeb);
            await File.WriteAllTextAsync(Path.Combine(cartellaWeb, "minimappa.html"), HtmlMiniMappa);
            ViewerMiniMappa.CoreWebView2.SetVirtualHostNameToFolderMapping("app.wfastpic", cartellaWeb, CoreWebView2HostResourceAccessKind.Allow);
            ViewerMiniMappa.CoreWebView2.Navigate("https://app.wfastpic/minimappa.html");
            await caricata.Task;

            _miniMappaPronta = true;
            return true;
        }
        catch
        {
            // WebView2 non disponibile: il pannello mostra comunque testo e pulsanti.
            _preparazioneMiniMappa = null;
            return false;
        }
    }

    /// <summary>Sempre la foto corrente (non quella che ha chiesto): la mappa può essere pronta dopo un ←/→.</summary>
    private async Task MostraSuMiniMappaAsync(bool modifica)
    {
        if (!await PreparaMiniMappaAsync()) return;
        var foto = _fotoViewerCorrente;
        if (foto is null) return;

        static string Js(double? valore) => valore?.ToString("R", CultureInfo.InvariantCulture) ?? "null";

        var (lat, lon) = modifica && _posizioneProposta is { } p ? (p.Lat, p.Lon) : (foto.Latitude, foto.Longitude);
        var direzione = foto.GpsManuale || modifica ? null : foto.Direction;
        await ViewerMiniMappa.CoreWebView2.ExecuteScriptAsync(
            $"mostra({Js(lat)}, {Js(lon)}, {Js(direzione)}, {(modifica ? "true" : "false")})");
    }

    private void MiniMappa_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!_luogoInModifica) return;
        try
        {
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            var radice = doc.RootElement;
            if (radice.GetProperty("action").GetString() != "posizione") return;

            var lat = radice.GetProperty("lat").GetDouble();
            var lon = radice.GetProperty("lon").GetDouble();
            _posizioneProposta = (lat, lon);
            ViewerLuogoDettagli.Text = $"Nuova posizione:\n{TestoCoordinate(lat, lon)}";
        }
        catch
        {
            // Messaggio non valido: ignorato.
        }
    }

    private void ImpostaModificaLuogo(bool attiva)
    {
        _luogoInModifica = attiva;
        ViewerLuogoAzioni.Visibility = attiva ? Visibility.Collapsed : Visibility.Visible;
        ViewerLuogoModifica.Visibility = attiva ? Visibility.Visible : Visibility.Collapsed;
        ViewerLuogoSuggerimento.Visibility = attiva ? Visibility.Visible : Visibility.Collapsed;
        if (!attiva) _posizioneProposta = null;
    }

    private void ViewerLuogoCorreggi_Click(object sender, RoutedEventArgs e)
    {
        var foto = _fotoViewerCorrente;
        if (foto is null) return;

        ImpostaModificaLuogo(true);
        _posizioneProposta = foto is { Latitude: double lat, Longitude: double lon } ? (lat, lon) : null;
        _ = MostraSuMiniMappaAsync(modifica: true);
    }

    private void ViewerLuogoSalva_Click(object sender, RoutedEventArgs e)
    {
        var foto = _fotoViewerCorrente;
        if (foto is not null && _posizioneProposta is { } p)
        {
            foto.Latitude = p.Lat;
            foto.Longitude = p.Lon;
            foto.Altitude = null;
            foto.Direction = null;
            foto.GpsManuale = true;
            _selectionManager?.SetPosizioneManuale(foto.Id, p.Lat, p.Lon);
        }

        AggiornaPannelloLuogo();
        ViewerOverlay.Focus(FocusState.Programmatic);
    }

    private void ViewerLuogoAnnulla_Click(object sender, RoutedEventArgs e)
    {
        AggiornaPannelloLuogo();
        ViewerOverlay.Focus(FocusState.Programmatic);
    }

    // "Nessuna posizione" è una scelta che resta (es. foto di casa): non torna con la riscansione.
    private void ViewerLuogoRimuovi_Click(object sender, RoutedEventArgs e)
    {
        var foto = _fotoViewerCorrente;
        if (foto is null || !foto.HasGps) return;

        foto.Latitude = null;
        foto.Longitude = null;
        foto.Altitude = null;
        foto.Direction = null;
        foto.GpsManuale = true;
        _selectionManager?.SetPosizioneManuale(foto.Id, null, null);
        AggiornaPannelloLuogo();
    }

    private async void ViewerLuogoRipristina_Click(object sender, RoutedEventArgs e)
    {
        var foto = _fotoViewerCorrente;
        if (foto is null) return;

        var gps = await Task.Run(() => PhotoMetadataReader.Read(foto.PercorsoCompleto).Gps);
        foto.Latitude = gps?.Latitude;
        foto.Longitude = gps?.Longitude;
        foto.Altitude = gps?.Altitude;
        foto.Direction = gps?.Direction;
        foto.GpsManuale = false;
        _selectionManager?.RipristinaPosizioneOriginale(foto.Id, gps);

        if (foto == _fotoViewerCorrente) AggiornaPannelloLuogo();
    }

    /// <summary>
    /// Mini-mappa del pannello: punto rosso con la freccia della direzione della camera. In modifica il
    /// segnaposto si trascina e un clic lo sposta; ogni nuova posizione torna all'app con postMessage.
    /// </summary>
    private const string HtmlMiniMappa = """
        <!DOCTYPE html>
        <html lang="it">
        <head>
            <meta charset="utf-8" />
            <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
            <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
            <style>
                html, body, #m { height: 100%; width: 100%; margin: 0; background: #222; }
                .leaflet-control-attribution { font-size: 8px; }
                .modifica { cursor: crosshair; }
            </style>
        </head>
        <body>
            <div id="m"></div>
            <script>
                const map = L.map('m', { zoomControl: false }).setView([41.9, 12.5], 5);
                L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                    maxZoom: 19, attribution: '&copy; OpenStreetMap'
                }).addTo(map);

                let marker = null;
                let modifica = false;

                // Punto rosso sul luogo esatto + freccia verso dove guardava la camera (0° = nord).
                function icona(dir) {
                    const freccia = dir === null ? '' :
                        `<svg width="60" height="60" viewBox="-30 -30 60 60" style="position:absolute;left:-23px;top:-23px;transform:rotate(${dir}deg);overflow:visible">
                           <path d="M0 -6 L0 -22" stroke="#fff" stroke-width="5" stroke-linecap="round"/>
                           <path d="M0 -6 L0 -22" stroke="#D32F2F" stroke-width="3" stroke-linecap="round"/>
                           <path d="M-6 -19 L0 -29 L6 -19 Z" fill="#D32F2F" stroke="#fff" stroke-width="1.5"/>
                         </svg>`;
                    return L.divIcon({
                        className: '',
                        html: `<div style="position:relative;width:14px;height:14px">${freccia}
                                 <div style="position:absolute;inset:0;border-radius:50%;background:#EA4335;border:2px solid #fff;box-shadow:0 1px 3px rgba(0,0,0,.6)"></div></div>`,
                        iconSize: [14, 14], iconAnchor: [7, 7]
                    });
                }

                function invia(p) { window.chrome.webview.postMessage({ action: 'posizione', lat: p.lat, lon: p.lng }); }

                function mostra(lat, lon, dir, mod) {
                    modifica = mod;
                    document.getElementById('m').classList.toggle('modifica', mod);
                    if (marker) { map.removeLayer(marker); marker = null; }
                    if (lat === null) return;
                    marker = L.marker([lat, lon], { icon: icona(dir), draggable: mod }).addTo(map);
                    marker.on('dragend', () => invia(marker.getLatLng()));
                    map.setView([lat, lon], map.getZoom() < 10 ? 14 : map.getZoom());
                }

                map.on('click', e => {
                    if (!modifica) return;
                    if (!marker) {
                        marker = L.marker(e.latlng, { icon: icona(null), draggable: true }).addTo(map);
                        marker.on('dragend', () => invia(marker.getLatLng()));
                    } else {
                        marker.setLatLng(e.latlng);
                    }
                    invia(e.latlng);
                });
            </script>
        </body>
        </html>
        """;
}
