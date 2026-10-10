using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WFastPic.Core.Metadata;
using static WFastPic.App.MappeComuni;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// Pannello "Luogo" dell'editor, come i Luoghi di Picasa: nome del posto, dati di ripresa, mini-mappa
/// e correzione della posizione (anche a mappa grande al posto della foto). La correzione vive solo
/// nel sidecar: l'originale non viene mai scritto.
/// </summary>
public sealed partial class ViewerView
{
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
        ViewerLuogoNome.Text = coordinate is null ? T("Nessuna posizione") : foto.GpsManuale ? T("Posizione corretta a mano") : "…";
        ViewerLuogoDettagli.Text = coordinate ?? "";
        ViewerLuogoCorreggiButton.Content = foto.HasGps ? T("Correggi") : T("Aggiungi");
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
                var nome = await Host.NomeLuogoAsync(lat, lon, ct);
                if (!ct.IsCancellationRequested)
                    ViewerLuogoNome.Text = nome ?? (foto.GpsManuale ? T("Posizione corretta a mano") : T("Luogo sconosciuto"));
            }
        }
        catch (OperationCanceledException)
        {
            // Foto cambiata: il pannello è già di un'altra.
        }
    }

    private Task<bool>? _preparazioneMappaGrande;
    private bool _mappaGrandePronta;

    private async Task<bool> PreparaMiniMappaAsync()
    {
        if (_miniMappaPronta) return true;
        _miniMappaPronta = await (_preparazioneMiniMappa ??= CaricaPaginaMappaAsync(ViewerMiniMappa, grande: false));
        if (!_miniMappaPronta) _preparazioneMiniMappa = null;
        return _miniMappaPronta;
    }

    private async Task<bool> PreparaMappaGrandeAsync()
    {
        if (_mappaGrandePronta) return true;
        _mappaGrandePronta = await (_preparazioneMappaGrande ??= CaricaPaginaMappaAsync(ViewerMappaGrande, grande: true));
        if (!_mappaGrandePronta) _preparazioneMappaGrande = null;
        return _mappaGrandePronta;
    }

    /// <summary>Stessa pagina per mini-mappa e mappa grande di correzione ("?grande" mostra lo zoom e avvicina).</summary>
    private async Task<bool> CaricaPaginaMappaAsync(WebView2 webView, bool grande)
    {
        try
        {
            await PreparaWebViewAsync(webView);
            webView.CoreWebView2.WebMessageReceived += MiniMappa_WebMessageReceived;

            var caricata = new TaskCompletionSource();
            void Completata(CoreWebView2 s, CoreWebView2NavigationCompletedEventArgs a)
            {
                s.NavigationCompleted -= Completata;
                caricata.TrySetResult();
            }
            webView.CoreWebView2.NavigationCompleted += Completata;

            // Da un host virtuale e non con NavigateToString: senza un'origine vera le richieste dei
            // riquadri non hanno Referer e i server di OpenStreetMap possono rifiutarle.
            var cartellaWeb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WFastPic", "web");
            Directory.CreateDirectory(cartellaWeb);
            await File.WriteAllTextAsync(Path.Combine(cartellaWeb, "minimappa.html"), Segnaposti(HtmlMiniMappa));
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.wfastpic", cartellaWeb, CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.Navigate(grande ? "https://app.wfastpic/minimappa.html?grande" : "https://app.wfastpic/minimappa.html");
            await caricata.Task;
            return true;
        }
        catch
        {
            // WebView2 non disponibile: il pannello mostra comunque testo e pulsanti.
            return false;
        }
    }

    /// <summary>Sempre la foto corrente (non quella che ha chiesto): la mappa può essere pronta dopo un ←/→.</summary>
    private async Task MostraSuMiniMappaAsync(bool modifica)
    {
        if (!await PreparaMiniMappaAsync()) return;
        var foto = _fotoViewerCorrente;
        if (foto is null) return;

        var (lat, lon) = modifica && _posizioneProposta is { } p ? (p.Lat, p.Lon) : (foto.Latitude, foto.Longitude);
        var direzione = foto.GpsManuale || modifica ? null : foto.Direction;
        await ViewerMiniMappa.CoreWebView2.ExecuteScriptAsync(
            $"mostra({Js(lat)}, {Js(lon)}, {Js(direzione)}, {(modifica ? "true" : "false")})");
    }

    /// <summary>
    /// Mappa grande di correzione al posto della foto. Senza posizione si parte dall'ultima posizione
    /// usata nella cartella (le foto di una vacanza sono quasi sempre vicine), altrimenti dall'Italia.
    /// </summary>
    private async Task ApriMappaGrandeAsync()
    {
        ViewerMappaGrandeOverlay.Visibility = Visibility.Visible;
        if (!await PreparaMappaGrandeAsync() || !_luogoInModifica) return;

        var foto = _fotoViewerCorrente;
        if (foto is null) return;

        var vicina = _posizioneProposta is null
            ? Host.Tutte.Where(f => f.HasGps).MinBy(f => Math.Abs(((f.CaptureDate ?? DateTime.MaxValue) - (foto.CaptureDate ?? DateTime.MaxValue)).TotalSeconds))
            : null;
        var centro = vicina is { Latitude: double vlat, Longitude: double vlon } ? $"centra({Js(vlat)}, {Js(vlon)});" : "";

        var (lat, lon) = _posizioneProposta is { } p ? ((double?)p.Lat, (double?)p.Lon) : (null, null);
        await ViewerMappaGrande.CoreWebView2.ExecuteScriptAsync($"{centro} mostra({Js(lat)}, {Js(lon)}, null, true)");
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
            var testo = TestoCoordinate(lat, lon);
            ViewerLuogoDettagli.Text = $"Nuova posizione:\n{testo}";
            ViewerMappaGrandeInfo.Text = T("Nuova posizione {0} · Salva per confermare", testo);
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
        if (!attiva)
        {
            _posizioneProposta = null;
            ViewerMappaGrandeOverlay.Visibility = Visibility.Collapsed;
            ViewerMappaGrandeInfo.Text = T("Trascina il segnaposto o clicca nel punto giusto. In alto a destra: mappa o satellite.");
        }
    }

    private void ViewerLuogoCorreggi_Click(object sender, RoutedEventArgs e)
    {
        var foto = _fotoViewerCorrente;
        if (foto is null) return;

        ImpostaModificaLuogo(true);
        _posizioneProposta = foto is { Latitude: double lat, Longitude: double lon } ? (lat, lon) : null;
        _ = MostraSuMiniMappaAsync(modifica: true);
        _ = ApriMappaGrandeAsync();
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
            Host.SelectionManager?.SetPosizioneManuale(foto.Id, p.Lat, p.Lon);
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
        Host.SelectionManager?.SetPosizioneManuale(foto.Id, null, null);
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
        Host.SelectionManager?.RipristinaPosizioneOriginale(foto.Id, gps);

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
                // "?grande" = mappa di correzione: zoom visibile e vista ravvicinata per piazzare il punto.
                const grande = location.search.includes('grande');
                const map = L.map('m', { zoomControl: grande }).setView([41.9, 12.5], 5);
                const stradale = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                    maxZoom: 19, attribution: '&copy; OpenStreetMap'
                });
                const satellite = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
                    maxZoom: 19, attribution: '@@Immagini@@ &copy; Esri'
                });
                const nomi = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}', {
                    maxZoom: 19
                });
                stradale.addTo(map);
                L.control.layers({ '@@Mappa@@': stradale, '@@Satellite@@': satellite }, { '@@Nomi dei luoghi@@': nomi },
                    { position: 'topright', collapsed: !grande }).addTo(map);

                // Senza posizione: si parte vicino alle altre foto della cartella, già ingranditi.
                function centra(lat, lon) { map.setView([lat, lon], 15); }

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
                    map.setView([lat, lon], map.getZoom() < 10 ? (grande ? 17 : 14) : map.getZoom());
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
