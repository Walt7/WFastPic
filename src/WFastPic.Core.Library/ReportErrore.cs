using System.Text;
using System.Text.RegularExpressions;

namespace WFastPic.Core.Library;

/// <summary>
/// Report d'errore da condividere: nessun dato personale. Prima di mostrarlo, copiarlo o aprirlo come
/// segnalazione su GitHub si tolgono percorsi (cartelle e nomi delle foto: "Puglia", "Matrimonio Anna"…),
/// nome utente di Windows e nome del PC. Resta il tipo di file (.heic, .db…), utile per capire l'errore.
/// Il report completo resta solo sul PC dell'utente.
/// </summary>
public static partial class ReportErrore
{
    public const string IndirizzoNuovaSegnalazione = "https://github.com/Walt7/WFastPic/issues/new";

    // GitHub e i browser accettano URL di qualche migliaio di caratteri: oltre si tronca il report.
    public const int LunghezzaMassimaUrl = 7500;

    public const string SegnapostoPercorso = "<percorso>";
    public const string SegnapostoUtente = "<utente>";
    public const string SegnapostoPc = "<pc>";

    // Percorso Windows (C:\…) o di rete (\\server\…) fino a virgolette, parentesi, fine riga o al numero di
    // riga delle tracce .NET (":line 42", in italiano ":riga 42"). I nomi di cartella possono contenere
    // spazi e parole qualsiasi ("Vacanze in Puglia"): meglio togliere troppo che lasciare un pezzo di nome.
    [GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\[^\\\s'""]+\\)[^'""<>|\r\n()\[\]]*?(?=['""<>|\r\n()\[\]]|:(?:line|riga)\s|$)", RegexOptions.Multiline)]
    private static partial Regex Percorso();

    [GeneratedRegex(@"\.[A-Za-z0-9]{1,5}$")]
    private static partial Regex Estensione();

    /// <summary>Testo senza percorsi, nome utente e nome del PC.</summary>
    public static string Anonimizza(string testo, string? nomeUtente = null, string? nomePc = null)
    {
        ArgumentNullException.ThrowIfNull(testo);

        var risultato = Percorso().Replace(testo, m =>
        {
            var percorso = m.Value.TrimEnd(' ', '.', ',', ';');
            var coda = m.Value[percorso.Length..];
            var estensione = Estensione().Match(percorso);
            return (estensione.Success ? $"{SegnapostoPercorso}{estensione.Value.ToLowerInvariant()}" : SegnapostoPercorso) + coda;
        });

        // Anche fuori dai percorsi (messaggi di sistema, nomi di rete): parole intere, senza maiuscole/minuscole.
        risultato = SostituisciParola(risultato, nomeUtente, SegnapostoUtente);
        risultato = SostituisciParola(risultato, nomePc, SegnapostoPc);
        return risultato;
    }

    private static string SostituisciParola(string testo, string? parola, string segnaposto)
    {
        if (string.IsNullOrWhiteSpace(parola) || parola.Length < 3) return testo;
        return Regex.Replace(testo, $@"(?<![\w]){Regex.Escape(parola)}(?![\w])", segnaposto, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Indirizzo di una nuova segnalazione su GitHub già compilata (titolo e testo). L'utente la vede nel
    /// browser e decide lui se inviarla. Report troppo lungo: tenuto l'inizio, dove c'è l'errore.
    /// </summary>
    public static string IndirizzoSegnalazione(string titolo, string introduzione, string report)
    {
        string Url(string corpo) =>
            $"{IndirizzoNuovaSegnalazione}?title={Uri.EscapeDataString(titolo)}&body={Uri.EscapeDataString(corpo)}";

        string Corpo(string r) => $"{introduzione}\n\n```\n{r.TrimEnd()}\n```\n";

        var url = Url(Corpo(report));
        if (url.Length <= LunghezzaMassimaUrl) return url;

        // Si accorcia finché ci sta (la codifica può triplicare i caratteri accentati).
        var righe = report.Split('\n');
        var tenute = new StringBuilder();
        foreach (var riga in righe)
        {
            var prova = tenute + riga + "\n";
            if (Url(Corpo(prova + "…")).Length > LunghezzaMassimaUrl) break;
            tenute.Append(riga).Append('\n');
        }
        return Url(Corpo(tenute + "…"));
    }
}
