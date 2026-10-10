using System.Globalization;

namespace WFastPic.Core.Library;

/// <summary>Una cartella dell'elenco, con le informazioni lette dal suo sidecar (se è già stata aperta).</summary>
public sealed record CartellaArchivio(string Percorso, int? NumeroFoto, DateTime? PrimoScatto, DateTime? UltimoScatto, bool Esiste);

/// <summary>
/// Pannello cartelle stile Picasa: le cartelle di foto raggruppate per anno di scatto (le più recenti
/// in alto), ognuna con nome, numero di foto e periodo. Logica pura, la UI la mostra soltanto.
/// </summary>
public static class ArchivioCartelle
{
    private static readonly CultureInfo Italiano = CultureInfo.GetCultureInfo("it-IT");

    /// <summary>Gruppi per anno dell'ultimo scatto, dal più recente; in fondo le cartelle senza date.</summary>
    public static List<(int? Anno, List<CartellaArchivio> Cartelle)> RaggruppaPerAnno(IEnumerable<CartellaArchivio> cartelle) =>
        cartelle
            .GroupBy(c => c.UltimoScatto?.Year)
            .OrderBy(g => g.Key is null)
            .ThenByDescending(g => g.Key)
            .Select(g => (g.Key, g
                .OrderByDescending(c => c.UltimoScatto)
                .ThenBy(c => Nome(c.Percorso), StringComparer.CurrentCultureIgnoreCase)
                .ToList()))
            .ToList();

    /// <summary>"Puglia" da "E:\Fotocamera\2026\Puglia"; per una radice di disco "E:\".</summary>
    public static string Nome(string percorso)
    {
        var pulito = percorso.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var nome = Path.GetFileName(pulito);
        return string.IsNullOrEmpty(nome) ? percorso : nome;
    }

    public static string TitoloAnno(int? anno) => anno?.ToString(CultureInfo.InvariantCulture) ?? "Senza data";

    /// <summary>"1.300 foto · 12–20 lug 2026", "45 foto · 30 giu – 2 lug 2026", "Non ancora aperta".</summary>
    public static string Dettaglio(CartellaArchivio cartella)
    {
        if (!cartella.Esiste) return "Cartella non trovata";
        if (cartella.NumeroFoto is not int numero) return "Non ancora aperta";

        var foto = numero == 1 ? "1 foto" : string.Format(Italiano, "{0:N0} foto", numero);
        return Periodo(cartella.PrimoScatto, cartella.UltimoScatto) is string periodo ? $"{foto} · {periodo}" : foto;
    }

    public static string? Periodo(DateTime? primo, DateTime? ultimo)
    {
        if (primo is not DateTime a || ultimo is not DateTime b) return null;
        if (a.Date == b.Date) return a.ToString("d MMM yyyy", Italiano);
        if (a.Year == b.Year && a.Month == b.Month) return $"{a.Day}–{b.ToString("d MMM yyyy", Italiano)}";
        if (a.Year == b.Year) return $"{a.ToString("d MMM", Italiano)} – {b.ToString("d MMM yyyy", Italiano)}";
        return $"{a.ToString("MMM yyyy", Italiano)} – {b.ToString("MMM yyyy", Italiano)}";
    }

    /// <summary>
    /// Sottocartelle da non mostrare sfogliando il PC: nascoste o di sistema ($RECYCLE.BIN, System
    /// Volume Information…) e il sidecar ".wfastpic" di WFastPic.
    /// </summary>
    public static bool DaNascondere(string nome, FileAttributes attributi) =>
        nome.StartsWith('.') || nome.StartsWith('$')
        || attributi.HasFlag(FileAttributes.Hidden) || attributi.HasFlag(FileAttributes.System);
}
