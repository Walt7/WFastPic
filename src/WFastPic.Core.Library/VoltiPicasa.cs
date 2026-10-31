using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WFastPic.Core.Library;

/// <summary>Un volto marcato da Picasa: file nella stessa cartella dell'ini, riquadro normalizzato 0..1, id del contatto.</summary>
public sealed record VoltoPicasa(string File, double X0, double Y0, double X1, double Y1, string Contatto);

/// <summary>
/// Cosa si può prendere da Picasa: volti di WFastPic abbinati con il nome, e quanti volti di Picasa restano fuori
/// (senza nome, in foto non ancora analizzate da WFastPic, o non trovati da WFastPic).
/// </summary>
public sealed record PianoPicasa<T>(
    IReadOnlyList<(T Volto, string Nome)> Abbinati, int VoltiPicasa, int SenzaNome, int InFotoNonAnalizzate, int NonTrovati, int FileIni,
    IReadOnlyDictionary<string, int> CartelleNonAnalizzate)
{
    public IEnumerable<(string Nome, int Volti)> PerNome =>
        Abbinati.GroupBy(a => a.Nome).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2);
}

/// <summary>Un riquadro normalizzato 0..1 (sulla foto già girata secondo l'EXIF).</summary>
public readonly record struct Riquadro(double X0, double Y0, double X1, double Y1);

/// <summary>
/// I volti che Picasa ha lasciato nei suoi file <c>Picasa.ini</c> / <c>.picasa.ini</c> (uno per cartella): per ogni foto
/// <c>faces=rect64(&lt;16 hex&gt;),&lt;id contatto&gt;;…</c>, e i nomi nelle sezioni <c>[Contacts]</c>/<c>[Contacts2]</c>
/// (<c>id=Nome;email;</c>) o nel <c>contacts.xml</c> globale. Gli id sono globali: un nome trovato in un ini vale per
/// tutti. rect64 = quattro valori a 16 bit (sinistra, alto, destra, basso) su 65535, già sulla foto girata come la mostra
/// Picasa (verificato su 185 volti reali: combaciano con quelli di WFastPic senza correzioni). Solo lettura.
/// </summary>
public static partial class VoltiPicasa
{
    /// <summary>Id che Picasa usa per «volto senza nome».</summary>
    public const string SenzaNome = "ffffffffffffffff";

    /// <summary>Sotto questa sovrapposizione (intersezione / unione) due riquadri non sono lo stesso volto.</summary>
    public const double SogliaSovrapposizione = 0.25;

    public static readonly string[] NomiFile = ["Picasa.ini", ".picasa.ini"];

    [GeneratedRegex(@"rect64\(([0-9a-fA-F]{1,16})\),([0-9a-fA-F]+)")]
    private static partial Regex VoltoRegex();

    public static Riquadro Rect64(string esadecimale)
    {
        var v = ulong.Parse(esadecimale, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double Parte(int spostamento) => ((v >> spostamento) & 0xFFFF) / 65535.0;
        return new Riquadro(Parte(48), Parte(32), Parte(16), Parte(0));
    }

    /// <summary>Volti e contatti di un file ini. Righe rovinate o riquadri vuoti si saltano.</summary>
    public static (List<VoltoPicasa> Volti, Dictionary<string, string> Contatti) Leggi(string fileIni)
    {
        var volti = new List<VoltoPicasa>();
        var contatti = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string sezione = "";
        foreach (var grezza in File.ReadLines(fileIni))
        {
            var riga = grezza.Trim();
            if (riga.StartsWith('[') && riga.EndsWith(']'))
            {
                sezione = riga[1..^1];
                continue;
            }
            var uguale = riga.IndexOf('=');
            if (uguale <= 0) continue;
            var chiave = riga[..uguale].Trim();
            var valore = riga[(uguale + 1)..];

            if (sezione.Equals("Contacts", StringComparison.OrdinalIgnoreCase) || sezione.Equals("Contacts2", StringComparison.OrdinalIgnoreCase))
            {
                var nome = valore.Split(';')[0].Trim();
                if (nome.Length > 0) contatti[chiave] = nome;
            }
            else if (chiave.Equals("faces", StringComparison.OrdinalIgnoreCase) && sezione.Length > 0)
            {
                foreach (Match m in VoltoRegex().Matches(valore))
                {
                    var r = Rect64(m.Groups[1].Value);
                    if (r.X1 <= r.X0 || r.Y1 <= r.Y0) continue;
                    volti.Add(new VoltoPicasa(sezione, r.X0, r.Y0, r.X1, r.Y1, m.Groups[2].Value.ToLowerInvariant()));
                }
            }
        }
        return (volti, contatti);
    }

    /// <summary>I file ini sotto <paramref name="radice"/> (sottocartelle comprese, cataloghi .wfastpic esclusi).</summary>
    public static IEnumerable<string> TrovaIni(string radice)
    {
        var pila = new Stack<string>([radice]);
        while (pila.Count > 0)
        {
            var cartella = pila.Pop();
            string[] file, figli;
            try
            {
                file = Directory.GetFiles(cartella);
                figli = Directory.GetDirectories(cartella);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
            foreach (var f in file)
                if (NomiFile.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)) yield return f;
            foreach (var figlio in figli)
            {
                var nome = Path.GetFileName(figlio);
                if (nome.Equals(".wfastpic", StringComparison.OrdinalIgnoreCase) || nome.StartsWith('$')) continue;
                if (new DirectoryInfo(figlio).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                pila.Push(figlio);
            }
        }
    }

    /// <summary>Contatti del <c>contacts.xml</c> di Picasa (id → nome); vuoto se manca o non si legge.</summary>
    public static Dictionary<string, string> LeggiContattiXml(string file)
    {
        var contatti = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(file)) return contatti;
            foreach (var c in XDocument.Load(file).Descendants("contact"))
                if (c.Attribute("id")?.Value is { Length: > 0 } id && c.Attribute("name")?.Value.Trim() is { Length: > 0 } nome)
                    contatti[id] = nome;
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException) { }
        return contatti;
    }

    /// <summary>Dove Picasa tiene i contatti globali (su questo PC).</summary>
    public static string ContattiXmlPredefinito => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Picasa2", "contacts", "contacts.xml");

    public static double Sovrapposizione(Riquadro a, Riquadro b)
    {
        var inter = Math.Max(0, Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0)) * Math.Max(0, Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0));
        var unione = (a.X1 - a.X0) * (a.Y1 - a.Y0) + (b.X1 - b.X0) * (b.Y1 - b.Y0) - inter;
        return unione > 0 ? inter / unione : 0;
    }

    /// <summary>
    /// Coppie (indice Picasa, indice WFastPic) dello stesso volto in una foto: si prende sempre la coppia più sovrapposta
    /// ancora libera, così due volti vicini non finiscono sullo stesso riquadro.
    /// </summary>
    public static List<(int Picasa, int Nostro)> Abbina(IReadOnlyList<Riquadro> picasa, IReadOnlyList<Riquadro> nostri, double soglia = SogliaSovrapposizione)
    {
        var coppie = new List<(int, int, double)>();
        for (var i = 0; i < picasa.Count; i++)
            for (var j = 0; j < nostri.Count; j++)
                if (Sovrapposizione(picasa[i], nostri[j]) is var s && s >= soglia) coppie.Add((i, j, s));

        var (presiP, presiN) = (new HashSet<int>(), new HashSet<int>());
        var risultato = new List<(int, int)>();
        foreach (var (i, j, _) in coppie.OrderByDescending(c => c.Item3))
        {
            if (presiP.Contains(i) || presiN.Contains(j)) continue;
            presiP.Add(i);
            presiN.Add(j);
            risultato.Add((i, j));
        }
        return risultato;
    }

    /// <summary>
    /// Abbina i volti di Picasa sotto <paramref name="radice"/> ai volti trovati da WFastPic (<paramref name="volti"/>,
    /// con il percorso completo della foto): per ogni volto abbinato il nome di Picasa, con le varianti dello stesso nome
    /// (maiuscole, spazi) già unite sotto la grafia più usata. Volti senza nome, di foto non analizzate o che WFastPic non
    /// ha trovato (troppo piccoli, di profilo) vengono solo contati.
    /// </summary>
    public static PianoPicasa<T> Pianifica<T>(string radice, IReadOnlyList<(T Volto, string Foto, Riquadro Riquadro)> volti, IReadOnlyDictionary<string, string>? altriContatti = null)
    {
        var contatti = new Dictionary<string, string>(altriContatti ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var perIni = new List<(string Cartella, List<VoltoPicasa> Volti)>();
        foreach (var ini in TrovaIni(radice))
        {
            try
            {
                var (v, c) = Leggi(ini);
                foreach (var (id, nome) in c) contatti.TryAdd(id, nome);
                if (v.Count > 0) perIni.Add((Path.GetDirectoryName(ini)!, v));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        var nostriPerFoto = volti.GroupBy(v => v.Foto, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        // Grafia di ogni nome = la più usata tra le varianti con la stessa chiave.
        var grafie = perIni.SelectMany(p => p.Volti).Select(v => contatti.GetValueOrDefault(v.Contatto))
            .Where(n => n is not null).GroupBy(n => Chiave(n!))
            .ToDictionary(g => g.Key, g => g.GroupBy(n => n!).OrderByDescending(x => x.Count()).ThenByDescending(x => x.Key.Any(char.IsUpper)).First().Key);

        var abbinati = new List<(T, string)>();
        var (totale, senzaNome, fotoSenzaVolti, nonTrovati) = (0, 0, 0, 0);
        var nonAnalizzate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cartella, voltiIni) in perIni)
        {
            foreach (var foto in voltiIni.GroupBy(v => v.File, StringComparer.OrdinalIgnoreCase))
            {
                var lista = foto.ToList();
                totale += lista.Count;
                var conNome = lista.Where(v => contatti.ContainsKey(v.Contatto)).ToList();
                senzaNome += lista.Count - conNome.Count;
                if (conNome.Count == 0) continue;
                if (!nostriPerFoto.TryGetValue(Path.Combine(cartella, foto.Key), out var nostri))
                {
                    fotoSenzaVolti += conNome.Count;
                    nonAnalizzate[cartella] = nonAnalizzate.GetValueOrDefault(cartella) + conNome.Count;
                    continue;
                }
                var coppie = Abbina(conNome.Select(v => new Riquadro(v.X0, v.Y0, v.X1, v.Y1)).ToList(), nostri.Select(n => n.Riquadro).ToList());
                nonTrovati += conNome.Count - coppie.Count;
                foreach (var (p, n) in coppie) abbinati.Add((nostri[n].Volto, grafie[Chiave(contatti[conNome[p].Contatto])]));
            }
        }
        return new PianoPicasa<T>(abbinati, totale, senzaNome, fotoSenzaVolti, nonTrovati, perIni.Count, nonAnalizzate);
    }

    /// <summary>Chiave per riconoscere lo stesso nome scritto diverso: spazi e maiuscole non contano.</summary>
    public static string Chiave(string nome) => string.Join(' ', nome.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>Picasa a volte usa l'indirizzo email come nome (contatto Google senza nome).</summary>
    public static bool EEmail(string nome) => nome.Contains('@') && !nome.Contains(' ');

    /// <summary>
    /// Nomi che potrebbero essere la stessa persona ma non si uniscono da soli: stesse parole in altro ordine
    /// («Rodeghiero Pierluigi» / «Pierluigi Rodeghiero») o solo il nome di battesimo («walter» / «Walter Rodeghiero»).
    /// </summary>
    public static List<(string A, string B)> ForseUguali(IEnumerable<string> nomi)
    {
        var elenco = nomi.Select(n => (Nome: n, Parole: Chiave(n).Split(' '))).DistinctBy(n => Chiave(n.Nome)).ToList();
        var risultato = new List<(string, string)>();
        for (var i = 0; i < elenco.Count; i++)
            for (var j = i + 1; j < elenco.Count; j++)
            {
                var (a, b) = (elenco[i], elenco[j]);
                var stesseParole = a.Parole.Length > 1 && a.Parole.Order().SequenceEqual(b.Parole.Order());
                var soloNome = (a.Parole.Length == 1 && b.Parole.Length > 1 && b.Parole[0] == a.Parole[0])
                               || (b.Parole.Length == 1 && a.Parole.Length > 1 && a.Parole[0] == b.Parole[0]);
                if (stesseParole || soloNome) risultato.Add((a.Nome, b.Nome));
            }
        return risultato;
    }
}
