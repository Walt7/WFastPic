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

        var daScansionare = forzaTutte ? _tutte.ToList() : _tutte.Where(p => !p.HasGps).ToList();
        if (daScansionare.Count == 0)
        {
            MapLoadingRing.IsActive = false;
            return;
        }

        var updates = new List<(long Id, double? Lat, double? Lon)>();

        await Task.Run(() =>
        {
            foreach (var item in daScansionare)
            {
                var meta = WFastPic.Core.Metadata.PhotoMetadataReader.Read(item.PercorsoCompleto);
                if (meta.Gps.HasValue)
                {
                    updates.Add((item.Id, meta.Gps.Value.Latitude, meta.Gps.Value.Longitude));
                }
            }

            if (updates.Count > 0 && _db is not null)
            {
                _db.UpdateGpsBatch(updates);
            }
        });

        foreach (var (id, lat, lon) in updates)
        {
            if (_perId.TryGetValue(id, out var item))
            {
                item.Latitude = lat;
                item.Longitude = lon;
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
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var webViewDataFolder = Path.Combine(localAppData, "WFastPic", "WebView2");
                Directory.CreateDirectory(webViewDataFolder);
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewDataFolder);
                await MapWebView.EnsureCoreWebView2Async();
                MapWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 WFastPic/1.0";
                MapWebView.WebMessageReceived += MapWebView_WebMessageReceived;
                _mapInitialized = true;
            }

            var html = GeneraHtmlMappa(fotoGps);
            MapWebView.NavigateToString(html);
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
            if (doc.RootElement.TryGetProperty("id", out var idProp))
            {
                var photoId = idProp.GetInt64();
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
                <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
                <style>
                    html, body, #map {
                        height: 100%;
                        width: 100%;
                        margin: 0;
                        padding: 0;
                        background: #181818;
                        font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                    }
                    .leaflet-popup-content-wrapper {
                        background: #252526;
                        color: #ffffff;
                        border-radius: 8px;
                        box-shadow: 0 4px 16px rgba(0,0,0,0.6);
                        border: 1px solid #444;
                    }
                    .leaflet-popup-tip {
                        background: #252526;
                    }
                    .popup-badge {
                        display: inline-block;
                        background: #FFD600;
                        color: #000;
                        font-weight: bold;
                        font-size: 11px;
                        padding: 2px 6px;
                        border-radius: 3px;
                        margin-bottom: 6px;
                    }
                    .popup-title {
                        font-weight: 600;
                        font-size: 14px;
                        margin-bottom: 4px;
                        word-break: break-all;
                    }
                    .popup-date {
                        color: #aaaaaa;
                        font-size: 12px;
                        margin-bottom: 6px;
                    }
                    .popup-btn {
                        display: inline-block;
                        background: #0078D4;
                        color: white;
                        border: none;
                        border-radius: 4px;
                        padding: 6px 14px;
                        font-size: 12px;
                        cursor: pointer;
                        font-weight: 600;
                        margin-top: 6px;
                        transition: background 0.2s;
                    }
                    .popup-btn:hover {
                        background: #106EBE;
                    }
                    .custom-pin {
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        width: 28px;
                        height: 28px;
                        border-radius: 50% 50% 50% 0;
                        transform: rotate(-45deg);
                        box-shadow: 0 2px 6px rgba(0,0,0,0.5);
                    }
                    .custom-pin-inner {
                        width: 10px;
                        height: 10px;
                        border-radius: 50%;
                        background: #1e1e1e;
                        transform: rotate(45deg);
                    }
                    .pin-selected {
                        background: #FFD600;
                    }
                    .pin-normal {
                        background: #00B0FF;
                    }
                </style>
            </head>
            <body>
                <div id="map"></div>
                <script>
                    const markersData = {{markersJson}};
                    const map = L.map('map');

                    const voyager = L.tileLayer('https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png', {
                        maxZoom: 19,
                        subdomains: 'abcd',
                        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" style="color:#aaa;">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions" style="color:#aaa;">CARTO</a>'
                    });

                    const dark = L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
                        maxZoom: 19,
                        subdomains: 'abcd',
                        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" style="color:#aaa;">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions" style="color:#aaa;">CARTO</a>'
                    });

                    voyager.addTo(map);

                    L.control.layers({
                        'Mappa colori (Voyager)': voyager,
                        'Tema scuro (Dark Matter)': dark
                    }, null, { position: 'topright' }).addTo(map);

                    const markerGroup = L.featureGroup();

                    function createIcon(isSelected) {
                        const pinClass = isSelected ? 'pin-selected' : 'pin-normal';
                        return L.divIcon({
                            className: '',
                            html: `<div class="custom-pin ${pinClass}"><div class="custom-pin-inner"></div></div>`,
                            iconSize: [28, 28],
                            iconAnchor: [14, 28],
                            popupAnchor: [0, -28]
                        });
                    }

                    markersData.forEach(item => {
                        const icon = createIcon(item.isSelected);
                        const marker = L.marker([item.lat, item.lon], { icon: icon }).addTo(markerGroup);
                        const badgeHtml = item.isSelected ? '<div class="popup-badge">★ Selezionata</div>' : '';
                        const content = document.createElement('div');
                        content.innerHTML = `
                            ${badgeHtml}
                            <div class="popup-title">${item.title}</div>
                            <div class="popup-date">📅 ${item.date}</div>
                            <div class="popup-date">📍 ${item.lat.toFixed(5)}, ${item.lon.toFixed(5)}</div>
                            <button class="popup-btn" onclick="openPhoto(${item.id})">Apri nel Viewer</button>
                        `;
                        marker.bindPopup(content);
                    });

                    markerGroup.addTo(map);

                    if (markersData.length > 0) {
                        map.fitBounds(markerGroup.getBounds().pad(0.1));
                    } else {
                        map.setView([41.9028, 12.4964], 6);
                    }

                    function openPhoto(id) {
                        window.chrome.webview.postMessage({ action: 'open_viewer', id: id });
                    }
                </script>
            </body>
            </html>
            """;
    }
}
