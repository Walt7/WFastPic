using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace WFastPic.App;

public sealed partial class MainWindow
{
    private bool _mapInitialized;
    private bool _mapEventsHooked;
    private bool _mapMostraSoloSelezionate;

    private async void MappaButton_Click(object sender, RoutedEventArgs e)
    {
        await MostraMappaAsync();
    }

    private async Task MostraMappaAsync()
    {
        if (!_mapEventsHooked)
        {
            MapOverlay.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(MapOverlay_KeyDown), handledEventsToo: true);
            _mapEventsHooked = true;
        }

        MapOverlay.Visibility = Visibility.Visible;
        MapOverlay.Focus(FocusState.Programmatic);
        MapNomiLuoghiCheck.IsChecked = _nomiLuoghiOnline;

        // Se non ci sono coordinate GPS caricate per nessuna foto, eseguiamo prima una scansione automatica
        if (_tutte.Count > 0 && !_tutte.Any(p => p.HasGps))
        {
            await EseguiScansioneGpsAsync();
        }

        // Se ci sono foto selezionate, impostiamo di default "Solo selezionate"
        _mapMostraSoloSelezionate = _conteggioSelezionate > 0;
        AggiornaPulsantiFiltroMappa();

        await RicaricaMappaAsync();
    }

    private async void MapFiltroSelezionate_Click(object sender, RoutedEventArgs e)
    {
        _mapMostraSoloSelezionate = true;
        AggiornaPulsantiFiltroMappa();
        await RicaricaMappaAsync();
    }

    private async void MapFiltroTutte_Click(object sender, RoutedEventArgs e)
    {
        _mapMostraSoloSelezionate = false;
        AggiornaPulsantiFiltroMappa();
        await RicaricaMappaAsync();
    }

    private async void MapRescanButton_Click(object sender, RoutedEventArgs e)
    {
        await EseguiScansioneGpsAsync(forzaTutte: true);
        await RicaricaMappaAsync();
    }

    private void AggiornaPulsantiFiltroMappa()
    {
        var activeBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 214, 0)); // Giallo
        var normalBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 170, 170, 170)); // Grigio

        if (_mapMostraSoloSelezionate)
        {
            MapFiltroSelezionateButton.Foreground = activeBrush;
            MapFiltroSelezionateButton.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            MapFiltroTutteButton.Foreground = normalBrush;
            MapFiltroTutteButton.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }
        else
        {
            MapFiltroSelezionateButton.Foreground = normalBrush;
            MapFiltroSelezionateButton.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            MapFiltroTutteButton.Foreground = activeBrush;
            MapFiltroTutteButton.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        }
    }

    private async Task EseguiScansioneGpsAsync(bool forzaTutte = false)
    {
        MapLoadingRing.IsActive = true;
        MapStatusText.Text = "Scansione metadati GPS in corso…";
        MapInfoBar.IsOpen = false;

        // Le posizioni corrette a mano nell'editor non si riscansionano mai.
        var daScansionare = (forzaTutte ? _tutte : _tutte.Where(p => !p.HasGps)).Where(p => !p.GpsManuale).ToList();
        if (daScansionare.Count == 0)
        {
            MapLoadingRing.IsActive = false;
            return;
        }

        var updates = new List<(long Id, WFastPic.Core.Metadata.GeoCoordinate? Gps)>();

        await Task.Run(() =>
        {
            foreach (var item in daScansionare)
            {
                var meta = WFastPic.Core.Metadata.PhotoMetadataReader.Read(item.PercorsoCompleto);
                if (meta.Gps.HasValue)
                {
                    updates.Add((item.Id, meta.Gps));
                }
            }

            if (updates.Count > 0 && _db is not null)
            {
                _db.UpdateGpsBatch(updates);
            }
        });

        foreach (var (id, gps) in updates)
        {
            if (_perId.TryGetValue(id, out var item))
            {
                item.Latitude = gps?.Latitude;
                item.Longitude = gps?.Longitude;
                item.Altitude = gps?.Altitude;
                item.Direction = gps?.Direction;
            }
        }

        MapLoadingRing.IsActive = false;
    }

    private async Task RicaricaMappaAsync()
    {
        List<PhotoGridItem> fotoGps;
        if (_mapMostraSoloSelezionate)
        {
            fotoGps = _tutte.Where(p => p.IsSelected && p.HasGps).ToList();
            var selezionateSenzaGps = _tutte.Count(p => p.IsSelected && !p.HasGps);
            if (selezionateSenzaGps > 0)
            {
                MapStatusText.Text = $"{fotoGps.Count} selezionate su mappa ({selezionateSenzaGps} senza GPS)";
            }
            else
            {
                MapStatusText.Text = $"{fotoGps.Count} foto selezionate su mappa";
            }
        }
        else
        {
            fotoGps = _tutte.Where(p => p.HasGps).ToList();
            MapStatusText.Text = $"{fotoGps.Count} foto geolocalizzate";
        }

        if (fotoGps.Count == 0)
        {
            MapInfoBar.IsOpen = true;
            if (_mapMostraSoloSelezionate && _conteggioSelezionate > 0)
            {
                MapInfoBar.Message = "Nessuna delle foto selezionate dispone di coordinate GPS. Clicca 'Tutte le foto' o 'Riscansiona GPS'.";
            }
            else
            {
                MapInfoBar.Message = "Nessuna foto con coordinate GPS trovata in questa cartella.";
            }
            MapWebView.Visibility = Visibility.Collapsed;
            return;
        }

        MapInfoBar.IsOpen = false;
        MapWebView.Visibility = Visibility.Visible;
        MapLoadingRing.IsActive = true;

        try
        {
            if (!_mapInitialized)
            {
                await PreparaWebViewAsync(MapWebView);
                MapWebView.WebMessageReceived += MapWebView_WebMessageReceived;
                _mapInitialized = true;
            }

            // Pagina servita da un host virtuale sulla cartella .wfastpic: le miniature già in
            // thumbcache/ si caricano come file, senza codificarle in base64 nella pagina (che con
            // NavigateToString avrebbe un limite di 2 MB).
            if (_db is null) return;
            var cartellaSidecar = Path.Combine(_db.CartellaFoto, ".wfastpic");
            try { MapWebView.CoreWebView2.ClearVirtualHostNameToFolderMapping(HostMappa); } catch { /* non ancora mappato */ }
            MapWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(HostMappa, cartellaSidecar, CoreWebView2HostResourceAccessKind.Allow);
            await File.WriteAllTextAsync(Path.Combine(cartellaSidecar, "mappa.html"), GeneraHtmlMappa(fotoGps));
            MapWebView.CoreWebView2.Navigate($"https://{HostMappa}/mappa.html?v={Environment.TickCount64}");
        }
        catch (Exception ex)
        {
            MapInfoBar.Title = "Errore mappa";
            MapInfoBar.Message = $"Impossibile caricare la mappa: {ex.Message}";
            MapInfoBar.IsOpen = true;
        }
        finally
        {
            MapLoadingRing.IsActive = false;
        }
    }

    private void MapCloseButton_Click(object sender, RoutedEventArgs e)
    {
        ChiudiMappa();
    }

    private void ChiudiMappa()
    {
        MapOverlay.Visibility = Visibility.Collapsed;
        Griglia.Focus(FocusState.Programmatic);
    }

    private void MapOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ChiudiMappa();
            e.Handled = true;
        }
    }

    private void MapWebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = args.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("id", out var idProp)) return;
            var photoId = idProp.GetInt64();
            var azione = doc.RootElement.TryGetProperty("action", out var a) ? a.GetString() : null;

            if (azione == "dettagli")
            {
                _ = InviaDettagliPopupAsync(photoId);
            }
            else if (azione == "open_viewer")
            {
                if (_perId.TryGetValue(photoId, out var item))
                {
                    ChiudiMappa();
                    Griglia.SelectedItem = item;
                    Griglia.ScrollIntoView(item);
                    ApriViewer(item);
                }
            }
        }
        catch
        {
            // Messaggio non valido
        }
    }

    private const string HostMappa = "mappa.wfastpic";

    /// <summary>
    /// Mappa stile Google/Picasa: le foto vicine si raggruppano da sole (Leaflet.markercluster) in un
    /// segnaposto a goccia con il numero di foto; una foto abbastanza distante dalle altre mostra la
    /// propria miniatura dentro il segnaposto. Zoomando i gruppi si separano; foto nello stesso punto
    /// esatto (raffiche) si aprono a ventaglio all'ultimo zoom. Giallo = contiene foto selezionate.
    /// </summary>
    private static string GeneraHtmlMappa(IReadOnlyList<PhotoGridItem> fotoGps)
    {
        var markersList = new List<object>();
        foreach (var f in fotoGps)
        {
            if (f.Latitude.HasValue && f.Longitude.HasValue)
            {
                markersList.Add(new
                {
                    id = f.Id,
                    lat = f.Latitude.Value,
                    lon = f.Longitude.Value,
                    title = f.PathRelativo,
                    date = f.CaptureDate?.ToString("dd/MM/yyyy HH:mm") ?? "Data sconosciuta",
                    isSelected = f.IsSelected,
                    thumb = "thumbcache/" + Uri.EscapeDataString(ThumbnailCacheLoader.NomeFileCache(f)),
                    dir = f.GpsManuale ? null : f.Direction,
                });
            }
        }

        var markersJson = JsonSerializer.Serialize(markersList);

        return $$"""
            <!DOCTYPE html>
            <html lang="it">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
                <link rel="stylesheet" href="https://unpkg.com/leaflet.markercluster@1.5.3/dist/MarkerCluster.css" />
                <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
                <script src="https://unpkg.com/leaflet.markercluster@1.5.3/dist/leaflet.markercluster.js"></script>
                <style>
                    html, body, #map { height: 100%; width: 100%; margin: 0; padding: 0; background: #181818;
                        font-family: "Segoe UI", sans-serif; }
                    .leaflet-popup-content-wrapper { background: #252526; color: #fff; border-radius: 8px;
                        box-shadow: 0 4px 16px rgba(0,0,0,.6); border: 1px solid #444; }
                    .leaflet-popup-tip { background: #252526; }
                    .popup-img { display: block; width: 220px; height: 165px; object-fit: cover; border-radius: 4px;
                        margin-bottom: 8px; background: #333; }
                    .popup-badge { display: inline-block; background: #FFD600; color: #000; font-weight: bold;
                        font-size: 11px; padding: 2px 6px; border-radius: 3px; margin-bottom: 6px; }
                    .popup-title { font-weight: 600; font-size: 13px; margin-bottom: 2px; word-break: break-all; }
                    .popup-date { color: #aaa; font-size: 12px; }
                    .popup-btn { background: #0078D4; color: #fff; border: 0; border-radius: 4px; padding: 6px 14px;
                        font-size: 12px; font-weight: 600; cursor: pointer; margin-top: 8px; }
                    .popup-btn:hover { background: #106EBE; }

                    /* Segnaposto a goccia stile Google: SVG con la punta in basso sul punto esatto. */
                    .pin { position: relative; filter: drop-shadow(0 2px 3px rgba(0,0,0,.55)); }
                    .pin svg { position: absolute; left: 0; top: 0; z-index: 0; }
                    .pin .conteggio { position: absolute; z-index: 1; left: 0; right: 0; text-align: center; color: #fff;
                        font-weight: 700; font-size: 13px; }
                    .pin .foto { position: absolute; z-index: 1; border-radius: 50%; object-fit: cover; background: #555; }
                    .pin .punto { position: absolute; z-index: 1; border-radius: 50%; background: #1e1e1e; }
                    /* Freccia della direzione: parte dal punto esatto (la punta della goccia). */
                    .pin .freccia { position: absolute; z-index: 2; overflow: visible; pointer-events: none; }
                    .popup-luogo { color: #fff; font-size: 12px; margin: 2px 0 4px; }
                    .popup-righe div { color: #ccc; font-size: 12px; }
                </style>
            </head>
            <body>
                <div id="map"></div>
                <script>
                    const markersData = {{markersJson}};
                    const map = L.map('map', { zoomControl: true });

                    // Come Picasa: mappa stradale o satellite. (Le basemap CARTO ora chiedono una chiave API.)
                    const stradale = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                        maxZoom: 19,
                        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
                    });
                    const satellite = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
                        maxZoom: 19,
                        attribution: 'Immagini &copy; Esri, Maxar, Earthstar Geographics'
                    });
                    stradale.addTo(map);
                    L.control.layers({ 'Mappa': stradale, 'Satellite': satellite }, null, { position: 'topright' }).addTo(map);
                    const GOCCIA = 'M12 0C5.4 0 0 5.4 0 12c0 8.6 10.4 22.4 11.2 23.5a1 1 0 0 0 1.6 0C13.6 34.4 24 20.6 24 12 24 5.4 18.6 0 12 0z';

                    function svgGoccia(w, h, colore, bordo) {
                        return `<svg width="${w}" height="${h}" viewBox="-1 -1 26 38"><path d="${GOCCIA}" fill="${colore}" stroke="${bordo}" stroke-width="1.5"/></svg>`;
                    }

                    // Foto isolata: goccia con la miniatura rotonda dentro (se non è in cache, un punto).
                    function iconaFoto(item) {
                        const w = 50, h = 72, d = 40, x = (w - d) / 2, y = 5;
                        const colore = item.isSelected ? '#FFD600' : '#FFFFFF';
                        // La mappa ha sempre il nord in alto: ruotare di "dir" gradi la mostra giusta a ogni zoom.
                        const freccia = item.dir === null || item.dir === undefined ? '' :
                            `<svg class="freccia" width="60" height="60" viewBox="-30 -30 60 60"
                                  style="left:${w / 2 - 30}px;top:${h - 30}px;transform:rotate(${item.dir}deg)">
                               <path d="M0 0 L0 -20" stroke="#fff" stroke-width="5" stroke-linecap="round"/>
                               <path d="M0 0 L0 -20" stroke="#D32F2F" stroke-width="3" stroke-linecap="round"/>
                               <path d="M-6 -17 L0 -28 L6 -17 Z" fill="#D32F2F" stroke="#fff" stroke-width="1.5"/>
                             </svg>`;
                        return L.divIcon({
                            className: '',
                            html: `<div class="pin" style="width:${w}px;height:${h}px">${svgGoccia(w, h, colore, '#333')}${freccia}
                                     <img class="foto" src="${item.thumb}" style="left:${x}px;top:${y}px;width:${d}px;height:${d}px"
                                          onerror="this.outerHTML='<div class=&quot;punto&quot; style=&quot;left:19px;top:14px;width:12px;height:12px&quot;></div>'" />
                                   </div>`,
                            iconSize: [w, h], iconAnchor: [w / 2, h], popupAnchor: [0, -h + 6]
                        });
                    }

                    // Gruppo: goccia rossa (gialla se contiene selezionate) con il numero di foto.
                    function iconaGruppo(cluster) {
                        const figli = cluster.getAllChildMarkers();
                        const n = figli.length;
                        const conSelezionate = figli.some(m => m.options.item.isSelected);
                        const w = n < 10 ? 38 : n < 100 ? 44 : 50, h = Math.round(w * 1.45);
                        const colore = conSelezionate ? '#F9A825' : '#EA4335';
                        return L.divIcon({
                            className: '',
                            html: `<div class="pin" style="width:${w}px;height:${h}px">${svgGoccia(w, h, colore, '#8B1A10')}
                                     <div class="conteggio" style="top:${Math.round(w * 0.3)}px">${n}</div></div>`,
                            iconSize: [w, h], iconAnchor: [w / 2, h]
                        });
                    }

                    const gruppi = L.markerClusterGroup({
                        maxClusterRadius: 70,
                        showCoverageOnHover: false,
                        spiderfyOnMaxZoom: true,
                        iconCreateFunction: iconaGruppo
                    });

                    function escape(s) { const d = document.createElement('div'); d.textContent = s; return d.innerHTML; }

                    markersData.forEach(item => {
                        const marker = L.marker([item.lat, item.lon], { icon: iconaFoto(item), item: item, title: item.title });
                        const badge = item.isSelected ? '<div class="popup-badge">★ Selezionata</div>' : '';
                        marker.bindPopup(`
                            <img class="popup-img" src="${item.thumb}" onerror="this.style.display='none'" />
                            ${badge}
                            <div class="popup-title">${escape(item.title)}</div>
                            <div class="popup-luogo" id="luogo-${item.id}">…</div>
                            <div class="popup-date">📅 ${escape(item.date)}</div>
                            <div class="popup-date">📍 ${item.lat.toFixed(5)}, ${item.lon.toFixed(5)}</div>
                            <div class="popup-righe" id="righe-${item.id}"></div>
                            <button class="popup-btn" onclick="openPhoto(${item.id})">Apri nel Viewer</button>`, { minWidth: 220 });
                        // Luogo, dispositivo, altitudine, direzione: chiesti all'app solo all'apertura del popup.
                        marker.on('popupopen', () => {
                            if (dettagli[item.id]) riempi(dettagli[item.id]);
                            else window.chrome.webview.postMessage({ action: 'dettagli', id: item.id });
                        });
                        gruppi.addLayer(marker);
                    });

                    const dettagli = {};
                    function riempi(d) {
                        const luogo = document.getElementById('luogo-' + d.id);
                        if (luogo) luogo.textContent = d.luogo ? '🏷 ' + d.luogo : '';
                        const righe = document.getElementById('righe-' + d.id);
                        if (righe) righe.innerHTML = d.righe.map(r => `<div>${escape(r)}</div>`).join('');
                    }
                    window.chrome.webview.addEventListener('message', e => {
                        if (e.data && e.data.action === 'dettagli') { dettagli[e.data.id] = e.data; riempi(e.data); }
                    });
                    map.addLayer(gruppi);

                    // Inquadra tutte le foto; se l'area cambia dimensione prima che l'utente tocchi la mappa
                    // (overlay appena mostrato) si inquadra di nuovo invece di restare a zoom mondiale.
                    let toccata = false;
                    map.on('mousedown wheel', () => toccata = true);
                    function inquadra() {
                        if (markersData.length > 0) map.fitBounds(gruppi.getBounds().pad(0.1), { maxZoom: 16 });
                        else map.setView([41.9028, 12.4964], 6);
                    }
                    inquadra();
                    new ResizeObserver(() => { map.invalidateSize(); if (!toccata) inquadra(); }).observe(document.getElementById('map'));

                    function openPhoto(id) { window.chrome.webview.postMessage({ action: 'open_viewer', id: id }); }
                </script>
            </body>
            </html>
            """;
    }
}
