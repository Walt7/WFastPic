using System.Globalization;
using Microsoft.UI.Xaml.Controls;
using WFastPic.Core.Metadata;

namespace WFastPic.App;

/// <summary>Servizi comuni alla mappa grande e al pannello Luogo del viewer.</summary>
internal static class MappeComuni
{
    private static bool _cartellaWebViewImpostata;

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

    internal static string TestoCoordinate(double lat, double lon) =>
        string.Create(CultureInfo.InvariantCulture, $"📍 {lat:F5}, {lon:F5}");

    internal static string Js(double? valore) => valore?.ToString("R", CultureInfo.InvariantCulture) ?? "null";
}
