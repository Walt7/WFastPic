using System.Text.Json;

namespace WFastPic.Core.Library;

/// <summary>Una release più nuova di quella in uso: numero e pagina da cui scaricarla.</summary>
public sealed record VersioneDisponibile(Version Versione, string Indirizzo);

/// <summary>
/// "C'è una versione nuova?": legge la risposta di GitHub sull'ultima release (api.github.com/repos/…/
/// releases/latest) e la confronta con quella in uso. Puro: la richiesta in rete la fa l'app, che non
/// manda niente di suo (solo la richiesta della pagina pubblica, come un browser).
/// </summary>
public static class ControlloVersione
{
    public const string IndirizzoUltimaRelease = "https://api.github.com/repos/Walt7/WFastPic/releases/latest";

    /// <summary>La release descritta da <paramref name="json"/>, se più nuova di <paramref name="inUso"/>; altrimenti null.</summary>
    public static VersioneDisponibile? Confronta(string json, Version inUso)
    {
        ArgumentNullException.ThrowIfNull(inUso);
        try
        {
            using var documento = JsonDocument.Parse(json);
            var radice = documento.RootElement;
            // Bozze e anteprime non vanno proposte a tutti.
            if (Vero(radice, "draft") || Vero(radice, "prerelease")) return null;
            if (!radice.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } testo) return null;
            if (Leggi(testo) is not { } disponibile) return null;
            if (disponibile <= Normalizza(inUso)) return null;

            var indirizzo = radice.TryGetProperty("html_url", out var url) && url.GetString() is { } u
                            && u.StartsWith("https://github.com/", StringComparison.Ordinal)
                ? u
                : "https://github.com/Walt7/WFastPic/releases/latest";
            return new VersioneDisponibile(disponibile, indirizzo);
        }
        catch (JsonException)
        {
            return null; // risposta inattesa (limite di richieste, proxy…): nessun avviso
        }
    }

    /// <summary>"v1.2.0", "1.2", "v1.2.0-beta" → 1.2.0 (il suffisso si ignora); null se non è una versione.</summary>
    public static Version? Leggi(string tag)
    {
        var testo = tag.Trim().TrimStart('v', 'V');
        var fine = testo.IndexOfAny(['-', '+', ' ']);
        if (fine >= 0) testo = testo[..fine];
        return Version.TryParse(testo, out var versione) ? Normalizza(versione) : null;
    }

    /// <summary>1.2 e 1.2.0.0 valgono come 1.2.0: Version le considera diverse.</summary>
    private static Version Normalizza(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static bool Vero(JsonElement radice, string nome) =>
        radice.TryGetProperty(nome, out var valore) && valore.ValueKind == JsonValueKind.True;
}
