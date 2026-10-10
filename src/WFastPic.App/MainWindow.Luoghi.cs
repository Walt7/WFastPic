using System.Text.Json;
using Microsoft.UI.Xaml;
using WFastPic.Core.Metadata;
using WFastPic.Core.Persistence;
using static WFastPic.App.MappeComuni;

namespace WFastPic.App;

/// <summary>
/// Luoghi stile Picasa: nome del posto (geocodifica inversa con cache nel sidecar), dispositivo e
/// dati di ripresa per il popup della mappa. Il pannello Luogo dell'editor sta in ViewerView.Luogo.cs,
/// che chiede i nomi dei luoghi da qui (IViewerHost).
/// </summary>
public partial class MainWindow
{
    private static readonly HttpClient HttpNominatim = RisolutoreLuoghi.CreaClientNominatim();

    private bool _nomiLuoghiOnline = true;
    private LibrarySidecarDatabase? _dbLuoghi;
    private RisolutoreLuoghi? _risolutoreLuoghi;

    // ---------------------------------------------------------------- servizi comuni

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
}
