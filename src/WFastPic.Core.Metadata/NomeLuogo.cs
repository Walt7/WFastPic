using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using WFastPic.Core.Localizzazione;

namespace WFastPic.Core.Metadata;

/// <summary>
/// Geocodifica inversa stile Picasa: coordinate → "Polignano a Mare, Puglia". Usa Nominatim di
/// OpenStreetMap rispettandone la policy (User-Agent identificativo, al massimo una richiesta al
/// secondo) e una cache persistente per cella di ~100 m: ogni punto si chiede una volta sola, poi
/// funziona anche offline. Le coordinate escono verso openstreetmap.org: chi lo usa deve poterlo
/// disattivare.
/// </summary>
public sealed class RisolutoreLuoghi
{
    private static readonly TimeSpan IntervalloMinimo = TimeSpan.FromMilliseconds(1100);

    private readonly HttpClient _http;
    private readonly Func<string, string?> _leggiCache;
    private readonly Action<string, string> _scriviCache;
    private readonly ConcurrentDictionary<string, string?> _memoria = new();
    private readonly SemaphoreSlim _unaAllaVolta = new(1, 1);
    private DateTime _ultimaRichiestaUtc = DateTime.MinValue;

    public RisolutoreLuoghi(HttpClient http, Func<string, string?> leggiCache, Action<string, string> scriviCache)
    {
        _http = http;
        _leggiCache = leggiCache;
        _scriviCache = scriviCache;
    }

    public static HttpClient CreaClientNominatim()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://nominatim.openstreetmap.org/"), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WFastPic/1.0 (+https://github.com/Walt7/WFastPic)");
        return http;
    }

    /// <summary>Nome del luogo, o null se sconosciuto/non raggiungibile (mai un'eccezione).</summary>
    public async Task<string?> NomeAsync(double latitudine, double longitudine, CancellationToken ct = default)
    {
        var chiave = NomeLuogo.Chiave(latitudine, longitudine);
        if (_memoria.TryGetValue(chiave, out var noto)) return noto;
        if (_leggiCache(chiave) is string salvato) return _memoria[chiave] = salvato;

        try
        {
            await _unaAllaVolta.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            // Nel frattempo un'altra richiesta per la stessa cella può averlo già risolto.
            if (_memoria.TryGetValue(chiave, out noto)) return noto;

            var attesa = _ultimaRichiestaUtc + IntervalloMinimo - DateTime.UtcNow;
            if (attesa > TimeSpan.Zero) await Task.Delay(attesa, ct).ConfigureAwait(false);

            var url = string.Create(CultureInfo.InvariantCulture,
                $"reverse?format=jsonv2&zoom=16&accept-language={(Testi.Corrente == Lingua.Inglese ? "en" : "it")}&lat={latitudine:F6}&lon={longitudine:F6}");
            var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
            _ultimaRichiestaUtc = DateTime.UtcNow;

            var nome = NomeLuogo.DaNominatim(json);
            if (nome is not null) _scriviCache(chiave, nome);
            return _memoria[chiave] = nome;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
        {
            // Offline o servizio non disponibile: nessun nome, si riproverà alla prossima apertura.
            _ultimaRichiestaUtc = DateTime.UtcNow;
            return null;
        }
        finally
        {
            _unaAllaVolta.Release();
        }
    }
}

public static class NomeLuogo
{
    /// <summary>
    /// Cella di ~100 m (3 decimali): foto scattate nello stesso posto condividono il nome. I nomi inglesi
    /// hanno la loro chiave ("en|"): cambiando lingua si chiedono di nuovo, senza toccare quelli italiani già salvati.
    /// </summary>
    public static string Chiave(double latitudine, double longitudine) =>
        (Testi.Corrente == Lingua.Inglese ? "en|" : "") +
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(latitudine, 3):F3},{Math.Round(longitudine, 3):F3}");

    /// <summary>
    /// Dalla risposta "reverse" di Nominatim (jsonv2) al nome da mostrare: eventuale punto
    /// d'interesse (spiaggia, monumento…), località e regione, es. "Lama Monachile, Polignano a Mare, Puglia".
    /// </summary>
    public static string? DaNominatim(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("address", out var indirizzo) || indirizzo.ValueKind != JsonValueKind.Object)
            return null;

        string? Primo(params string[] chiavi)
        {
            foreach (var chiave in chiavi)
            {
                if (indirizzo.TryGetProperty(chiave, out var valore) && valore.GetString() is { Length: > 0 } testo)
                    return testo;
            }
            return null;
        }

        var puntoInteresse = Primo("tourism", "attraction", "beach", "natural", "historic", "leisure", "bay", "peak");
        var localita = Primo("city", "town", "village", "hamlet", "municipality", "suburb");
        var area = Primo("state", "region", "county") ?? Primo("country");

        var parti = new[] { puntoInteresse, localita, area }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parti.Count == 0)
            return doc.RootElement.TryGetProperty("display_name", out var completo) ? completo.GetString() : null;

        return string.Join(", ", parti);
    }
}
