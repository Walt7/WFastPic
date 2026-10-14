using System.Numerics;
using System.Text.RegularExpressions;

namespace WFastPic.Core.Library;

/// <summary>
/// Foto da confrontare: <paramref name="Impronta"/> è l'impronta percettiva della miniatura (null = non
/// ancora calcolata), <paramref name="Rapporto"/> larghezza/altezza della miniatura.
/// </summary>
public sealed record FotoPerDoppioni(long Id, long Size, DateTime? CaptureDate, ulong? Impronta, double? Rapporto);

/// <summary>
/// Foto doppie tra loro. <paramref name="Foto"/>: quelle della cartella aperta; <paramref name="Altrove"/>:
/// le copie in altre cartelle. <paramref name="Identiche"/>: tutti i file byte per byte uguali.
/// </summary>
public sealed record GruppoDoppioni(IReadOnlyList<long> Foto, bool Identiche, IReadOnlyList<long> Altrove)
{
    public GruppoDoppioni(IReadOnlyList<long> foto, bool identiche) : this(foto, identiche, []) { }
}

/// <summary>Quello che conta per scegliere quale copia tenere (vedi <see cref="RicercaDoppioni.ScegliMigliore"/>).</summary>
public sealed record CandidatoDoppione(
    long Id,
    string Nome,
    long Size,
    bool Selezionata = false,
    bool Scartata = false,
    int Voto = 0,
    bool Modificata = false,
    bool HaDataScatto = false,
    long Pixel = 0,
    int VotoAutomatico = 0);

/// <summary>
/// Ricerca delle foto doppie come "Mostra file duplicati" di Picasa, senza mai cancellare niente:
/// - identiche: stessa dimensione del file, poi confronto del contenuto solo tra quelle (mai un hash di
///   tutta la libreria);
/// - quasi uguali: la stessa foto ridimensionata o ricompressa, riconosciuta dall'impronta percettiva
///   delle miniature. Le raffiche non sono doppioni: due scatti con date diverse non lo sono mai.
/// </summary>
public static partial class RicercaDoppioni
{
    /// <summary>Bit diversi ammessi tra due impronte: oltre, sono foto diverse (anche se simili).</summary>
    public const int DistanzaMassima = 6;

    /// <summary>Differenza relativa di proporzioni ammessa: una copia ritagliata non è un doppione.</summary>
    public const double TolleranzaRapporto = 0.03;

    /// <summary>
    /// Gruppi di file identici. Si confrontano solo i file con la stessa dimensione: con foto vere quasi
    /// sempre nessuno, quindi quasi nessun file viene letto.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<long>> TrovaIdentiche(
        IEnumerable<(long Id, long Size)> foto, Func<long, long, bool> stessoContenuto, CancellationToken token = default)
    {
        var risultato = new List<IReadOnlyList<long>>();
        foreach (var stessaDimensione in foto.GroupBy(f => f.Size).Where(g => g.Count() > 1))
        {
            // Ogni foto va nella prima classe il cui rappresentante ha lo stesso contenuto.
            var classi = new List<List<long>>();
            foreach (var (id, _) in stessaDimensione)
            {
                token.ThrowIfCancellationRequested();
                var classe = classi.FirstOrDefault(c => stessoContenuto(c[0], id));
                if (classe is null) classi.Add([id]);
                else classe.Add(id);
            }
            risultato.AddRange(classi.Where(c => c.Count > 1));
        }
        return risultato;
    }

    /// <summary>Confronto byte per byte, a blocchi: si ferma alla prima differenza.</summary>
    public static bool StessoContenuto(string percorsoA, string percorsoB)
    {
        using var a = new FileStream(percorsoA, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        using var b = new FileStream(percorsoB, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        if (a.Length != b.Length) return false;

        var bufferA = new byte[81920];
        var bufferB = new byte[81920];
        while (true)
        {
            var letti = a.ReadAtLeast(bufferA, bufferA.Length, throwOnEndOfStream: false);
            if (b.ReadAtLeast(bufferB, letti, throwOnEndOfStream: false) != letti) return false;
            if (letti == 0) return true;
            if (!bufferA.AsSpan(0, letti).SequenceEqual(bufferB.AsSpan(0, letti))) return false;
        }
    }

    /// <summary>
    /// Mette insieme identiche e quasi uguali (a catena: se A≈B e B≈C il gruppo è A, B, C). Gruppi in
    /// ordine di prima foto, foto nell'ordine dell'elenco. Le foto <paramref name="altrove"/> (altre
    /// cartelle, id diversi da quelli di <paramref name="foto"/>) si confrontano solo con quelle della
    /// cartella aperta, mai tra loro: entrano in un gruppo solo se c'è almeno una foto di qui.
    /// </summary>
    public static IReadOnlyList<GruppoDoppioni> Raggruppa(
        IReadOnlyList<FotoPerDoppioni> foto,
        IReadOnlyList<IReadOnlyList<long>> identiche,
        IReadOnlyList<FotoPerDoppioni>? altrove = null)
    {
        var locali = foto.Count;
        if (altrove is { Count: > 0 }) foto = [.. foto, .. altrove];

        var indice = new Dictionary<long, int>();
        for (var i = 0; i < foto.Count; i++) indice[foto[i].Id] = i;

        var padre = Enumerable.Range(0, foto.Count).ToArray();
        int Radice(int i)
        {
            while (padre[i] != i) i = padre[i] = padre[padre[i]];
            return i;
        }
        void Unisci(int a, int b) => padre[Radice(a)] = Radice(b);

        var classeIdentica = new Dictionary<int, int>();
        for (var c = 0; c < identiche.Count; c++)
        {
            var membri = identiche[c].Where(indice.ContainsKey).Select(id => indice[id]).ToList();
            foreach (var m in membri)
            {
                classeIdentica[m] = c;
                Unisci(m, membri[0]);
            }
        }

        // Gli indici crescono: prima le foto di qui, poi quelle altrove. Il secondo indice parte sempre
        // dopo il primo, che è di qui: due foto altrove non vengono mai confrontate tra loro.
        var conImpronta = Enumerable.Range(0, foto.Count).Where(i => foto[i].Impronta is not null).ToList();
        for (var a = 0; a < conImpronta.Count && conImpronta[a] < locali; a++)
        {
            for (var b = a + 1; b < conImpronta.Count; b++)
            {
                if (QuasiUguali(foto[conImpronta[a]], foto[conImpronta[b]])) Unisci(conImpronta[a], conImpronta[b]);
            }
        }

        return Enumerable.Range(0, foto.Count)
            .GroupBy(Radice)
            .Where(g => g.Count() > 1 && g.Any(m => m < locali))
            .OrderBy(g => g.Min())
            .Select(g =>
            {
                var membri = g.Order().ToList();
                var tutteIdentiche = membri.All(classeIdentica.ContainsKey)
                    && membri.Select(m => classeIdentica[m]).Distinct().Count() == 1;
                return new GruppoDoppioni(
                    membri.Where(m => m < locali).Select(m => foto[m].Id).ToList(),
                    tutteIdentiche,
                    membri.Where(m => m >= locali).Select(m => foto[m].Id).ToList());
            })
            .ToList();
    }

    /// <summary>Stessa immagine salvata due volte: impronte vicine, stesse proporzioni, stesso istante di scatto.</summary>
    public static bool QuasiUguali(FotoPerDoppioni a, FotoPerDoppioni b)
    {
        if (a.Impronta is not ulong ia || b.Impronta is not ulong ib) return false;
        if (BitOperations.PopCount(ia ^ ib) > DistanzaMassima) return false;

        if (a.Rapporto is double ra && b.Rapporto is double rb
            && Math.Abs(ra - rb) / Math.Max(ra, rb) > TolleranzaRapporto) return false;

        // Una copia WhatsApp perde la data (null): va bene. Due date diverse sono due scatti, cioè una raffica.
        return a.CaptureDate is not DateTime da || b.CaptureDate is not DateTime db
            || Math.Abs((da - db).TotalSeconds) < 1;
    }

    /// <summary>
    /// La copia da tenere: prima le scelte dell'utente (non scartata, selezionata, voto, modificata), poi
    /// l'originale (con la data di scatto, più pixel), poi il voto automatico, il file più grande e il nome
    /// che non sembra una copia ("copia", "copy", "(1)").
    /// </summary>
    public static long ScegliMigliore(IReadOnlyList<CandidatoDoppione> candidati)
    {
        if (candidati.Count == 0) throw new ArgumentException("Nessun candidato.", nameof(candidati));

        return candidati
            .OrderBy(c => c.Scartata)
            .ThenByDescending(c => c.Selezionata)
            .ThenByDescending(c => c.Voto)
            .ThenByDescending(c => c.Modificata)
            .ThenByDescending(c => c.HaDataScatto)
            .ThenByDescending(c => c.Pixel)
            .ThenByDescending(c => c.VotoAutomatico)
            .ThenByDescending(c => c.Size)
            .ThenBy(c => SembraUnaCopia(c.Nome))
            .ThenBy(c => c.Nome.Length)
            .ThenBy(c => c.Nome, StringComparer.OrdinalIgnoreCase)
            .First().Id;
    }

    /// <summary>"IMG_1234 (1).jpg", "IMG_1234 - Copia.jpg", "IMG_1234-copy.jpg".</summary>
    public static bool SembraUnaCopia(string nome) => NomeCopia().IsMatch(Path.GetFileNameWithoutExtension(nome));

    [GeneratedRegex(@"(\(\d+\)|\bcopia\b|\bcopy\b|\bkopie\b|\bcopie\b)", RegexOptions.IgnoreCase)]
    private static partial Regex NomeCopia();
}
