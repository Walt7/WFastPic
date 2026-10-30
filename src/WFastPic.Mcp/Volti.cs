using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;

namespace WFastPic.Mcp;

/// <summary>Un volto con impronta di una cartella catalogata, con la persona già risolta (unioni comprese).</summary>
public sealed record VoltoLetto(
    string Rif, string Sidecar, long Id, string Foto, DateTime? Data,
    double X0, double Y0, double X1, double Y1,
    float[] Impronta, long? PersonaId, string? Persona, int Stato);

public sealed record PersonaNelGiorno(string Nome, IReadOnlyList<string> Foto, bool SoloProposta);

/// <summary>Un giorno: quante foto con volti, chi c'era e quanti volti sono ancora senza nome.</summary>
public sealed record GiornoConPersone(DateTime Giorno, int FotoConVolti, IReadOnlyList<PersonaNelGiorno> Persone, int VoltiSenzaNome, IReadOnlyList<string> EsempiSenzaNome);

public sealed record CartellaDelGruppo(string Cartella, int Foto);

public sealed record SomiglianzaPersona(string Nome, double Somiglianza);

/// <summary>Volti ancora senza nome che si somigliano: un gruppo da nominare, con dove e quando compaiono.</summary>
public sealed record GruppoVolti(
    string Id, IReadOnlyList<VoltoLetto> Volti, int Foto, DateTime? Dal, DateTime? Al,
    IReadOnlyList<CartellaDelGruppo> Cartelle, IReadOnlyList<string> Esempi, SomiglianzaPersona? SimileA);

/// <summary>Volti proposti in automatico per una persona con nome, da confermare.</summary>
public sealed record ProposteDaConfermare(string Persona, int Volti, int Foto, IReadOnlyList<string> Esempi);

/// <summary>
/// Volti di tutte le cartelle catalogate, per nominarli dall'assistente. Lettura senza blob delle miniature e in sola
/// lettura sui sidecar. Ogni volto ha un riferimento stabile «chiave-id» (chiave = cartella del sidecar, id = riga
/// del volto): resta valido tra una chiamata e l'altra e anche dopo un riavvio del server. Con le cartelle annidate
/// la stessa foto vale una volta sola, nel sidecar più in alto, come in <see cref="Ricerca.Cerca"/>.
/// </summary>
public static class Volti
{
    /// <summary>Sotto questa somiglianza non si suggerisce nemmeno «forse è…» (la proposta automatica vuole 0,42).</summary>
    public const float SogliaSuggerimento = 0.35f;

    private static readonly Lock Lucchetto = new();
    private static readonly Dictionary<string, string> CartellePerChiave = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IReadOnlyList<string>> GruppiNoti = new(StringComparer.OrdinalIgnoreCase);
    private static (string Firma, List<GruppoVolti> Gruppi)? _ultimiGruppi;

    public static string Chiave(string cartella)
    {
        var normale = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartella)).ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normale)))[..8].ToLowerInvariant();
    }

    public static string Rif(string cartella, long id) => $"{Chiave(cartella)}-{id}";

    /// <summary>Cartella del sidecar e riga del volto da un riferimento «chiave-id».</summary>
    public static (string Sidecar, long Id) Risolvi(string rif)
    {
        var parti = rif.Trim().Split('-');
        if (parti.Length != 2 || !long.TryParse(parti[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException($"Riferimento di volto non valido: «{rif}» (atteso come «a1b2c3d4-123»).");
        lock (Lucchetto)
        {
            if (!CartellePerChiave.ContainsKey(parti[0]))
                foreach (var cartella in Ricerca.CartelleConSidecar(null)) CartellePerChiave[Chiave(cartella)] = cartella;
            return CartellePerChiave.TryGetValue(parti[0], out var trovata)
                ? (trovata, id)
                : throw new ArgumentException($"Volto «{rif}» non trovato: la sua cartella non è tra quelle catalogate.");
        }
    }

    /// <summary>
    /// Tutti i volti con impronta delle cartelle catalogate (o solo di quelle foto sotto <paramref name="cartella"/>).
    /// Un volto con una persona eliminata conta come senza nome, come fa l'app all'apertura della cartella.
    /// </summary>
    public static List<VoltoLetto> Leggi(string? cartella = null)
    {
        var (unioni, nomi) = Ricerca.LeggiUnioniENomi();
        var viste = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var risultato = new List<VoltoLetto>();
        foreach (var sidecar in Ricerca.CartelleConSidecar(cartella))
        {
            lock (Lucchetto) CartellePerChiave[Chiave(sidecar)] = sidecar;
            try { LeggiSidecar(sidecar, cartella, unioni, nomi, viste, risultato); }
            catch (Exception ex) when (ex is SqliteException or IOException or FormatException) { /* sidecar bloccato o di un'altra versione */ }
        }
        return risultato;
    }

    private static void LeggiSidecar(
        string sidecar, string? filtro, Dictionary<long, long> unioni, Dictionary<long, string> nomi,
        HashSet<string> viste, List<VoltoLetto> risultato)
    {
        using var c = Ricerca.ApriLettura(Path.Combine(sidecar, LibrarySidecarDatabase.CartellaSidecar, LibrarySidecarDatabase.NomeFileDatabase));
        if (!Ricerca.Colonne(c, "volti").Contains("impronta")) return; // sidecar di una versione senza persone

        var foto = new Dictionary<long, (string Percorso, DateTime? Data)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, path_relativo, capture_date FROM photos;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var percorso = Path.GetFullPath(Path.Combine(sidecar, r.GetString(1)));
                // Già vista nel sidecar di una cartella madre: lì valgono i suoi volti.
                if (!viste.Add(percorso) || !Ricerca.FotoNellaCartella(percorso, filtro)) continue;
                foto[r.GetInt64(0)] = (percorso, r.IsDBNull(2) ? null : DateTime.Parse(r.GetString(2), CultureInfo.InvariantCulture));
            }
        }

        var chiave = Chiave(sidecar);
        using var query = c.CreateCommand();
        query.CommandText = "SELECT rowid, photo_id, x0, y0, x1, y1, impronta, persona_id, stato_persona FROM volti WHERE impronta IS NOT NULL;";
        using var reader = query.ExecuteReader();
        while (reader.Read())
        {
            if (!foto.TryGetValue(reader.GetInt64(1), out var f)) continue;
            long? persona = reader.IsDBNull(7) ? null : reader.GetInt64(7);
            var stato = reader.GetInt32(8);
            if (persona is long p)
            {
                persona = unioni.TryGetValue(p, out var resta) ? resta : p;
                if (!nomi.ContainsKey(persona.Value)) (persona, stato) = (null, StatoPersona.Nessuno);
            }
            var id = reader.GetInt64(0);
            risultato.Add(new VoltoLetto(
                $"{chiave}-{id}", sidecar, id, f.Percorso, f.Data,
                reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5),
                Impronte.DaByte((byte[])reader[6]), persona,
                persona is long n ? nomi[n].Split('\n')[0] : null, stato));
        }
    }

    /// <summary>
    /// Gruppi di volti senza nome (né proposti, né ignorati) per somiglianza, dal più numeroso per foto. Con la
    /// persona nota più simile al gruppo, se c'è, come suggerimento da verificare (non è una proposta).
    /// </summary>
    public static List<GruppoVolti> GruppiSenzaNome(string? cartella = null, int minimoVolti = 2)
    {
        var firma = Firma(cartella);
        List<GruppoVolti> gruppi;
        lock (Lucchetto)
        {
            gruppi = _ultimiGruppi is { } u && u.Firma == firma ? u.Gruppi : [];
        }

        if (gruppi.Count == 0 || firma.Length == 0)
        {
            gruppi = CalcolaGruppi(cartella);
            lock (Lucchetto)
            {
                _ultimiGruppi = (firma, gruppi);
                foreach (var g in gruppi) GruppiNoti[g.Id] = g.Volti.Select(v => v.Rif).ToList();
            }
        }
        return gruppi.Where(g => g.Volti.Count >= Math.Max(1, minimoVolti)).ToList();
    }

    private static List<GruppoVolti> CalcolaGruppi(string? cartella)
    {
        var senzaNome = Leggi(cartella).Where(v => v.PersonaId is null && v.Stato == StatoPersona.Nessuno).ToList();
        if (senzaNome.Count == 0) return [];

        var persone = PersoneNote();

        var etichette = RiconoscimentoPersone.Raggruppa(senzaNome.Select(v => v.Impronta).ToList());
        return senzaNome.Select((v, i) => (Volto: v, Gruppo: etichette[i]))
            .GroupBy(x => x.Gruppo)
            .Select(g => CreaGruppo(g.Select(x => x.Volto).ToList(), persone))
            .OrderByDescending(g => g.Foto).ThenByDescending(g => g.Volti.Count)
            .ToList();
    }

    private static GruppoVolti CreaGruppo(List<VoltoLetto> volti, IReadOnlyList<PersonaNota> persone)
    {
        // Id = il volto con il riferimento più piccolo: non dipende dall'ordine di lettura, si ritrova uguale ricalcolando.
        var id = "g" + volti.Select(v => v.Rif).Min(StringComparer.Ordinal);
        var date = volti.Where(v => v.Data is not null).Select(v => v.Data!.Value).ToList();
        var cartelle = volti.GroupBy(v => Path.GetDirectoryName(v.Foto) ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(g => new CartellaDelGruppo(g.Key, g.Select(v => v.Foto).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
            .OrderByDescending(c => c.Foto).Take(3).ToList();

        var somma = new float[Impronte.Dimensione];
        foreach (var v in volti) for (var k = 0; k < somma.Length && k < v.Impronta.Length; k++) somma[k] += v.Impronta[k];
        var simile = RiconoscimentoPersone.Riconosci(Impronte.Normalizza(somma), persone, SogliaSuggerimento);

        return new GruppoVolti(
            id, volti, volti.Select(v => v.Foto).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            date.Count > 0 ? date.Min() : null, date.Count > 0 ? date.Max() : null,
            cartelle, Esempi(volti, 4),
            simile is { } s ? new SomiglianzaPersona(s.Persona.Nome, Math.Round(s.Somiglianza, 2)) : null);
    }

    public static List<PersonaNota> PersoneNote()
    {
        if (!File.Exists(Ricerca.PercorsoPersone)) return [];
        using var archivio = ArchivioPersone.Apri(Ricerca.PercorsoPersone);
        return GestionePersone.PersoneNote(archivio);
    }

    /// <summary>
    /// Chi compare in ogni giorno dell'intervallo: persone con nome (confermate, e proposte se richiesto) con le loro
    /// foto, e quanti volti restano senza nome. Le foto scartate contano: c'erano anche loro.
    /// </summary>
    public static List<GiornoConPersone> PersoneNeiGiorni(DateTime dal, DateTime al, string? cartella, bool ancheProposte)
    {
        var fine = al.Date.AddDays(1);
        return Leggi(cartella)
            .Where(v => v.Data is { } d && d >= dal.Date && d < fine)
            .GroupBy(v => v.Data!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var conNome = g.Where(v => v.Persona is not null
                                           && (v.Stato == StatoPersona.Confermato || (ancheProposte && v.Stato == StatoPersona.Proposto)));
                var persone = conNome
                    .GroupBy(v => v.Persona!)
                    .Select(p => new PersonaNelGiorno(
                        p.Key,
                        p.Select(v => v.Foto).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                        p.All(v => v.Stato == StatoPersona.Proposto)))
                    .OrderByDescending(p => p.Foto.Count).ThenBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var senzaNome = g.Where(v => v.PersonaId is null && v.Stato == StatoPersona.Nessuno).ToList();
                return new GiornoConPersone(
                    g.Key, g.Select(v => v.Foto).Distinct(StringComparer.OrdinalIgnoreCase).Count(), persone,
                    senzaNome.Count, Esempi(senzaNome, 3));
            })
            .ToList();
    }

    /// <summary>I volti più grandi nella foto (i più riconoscibili), da foto diverse.</summary>
    public static List<string> Esempi(IEnumerable<VoltoLetto> volti, int quanti) =>
        volti.OrderByDescending(v => (v.X1 - v.X0) * (v.Y1 - v.Y0))
            .DistinctBy(v => v.Foto, StringComparer.OrdinalIgnoreCase)
            .Take(quanti).Select(v => v.Rif).ToList();

    /// <summary>I volti del gruppo come elencati da volti_da_nominare (anche se intanto i gruppi sono cambiati).</summary>
    public static IReadOnlyList<string> VoltiDelGruppo(string id)
    {
        lock (Lucchetto)
        {
            if (GruppiNoti.TryGetValue(id.Trim(), out var noti)) return noti;
        }
        // Server riavviato: ricalcolando con gli stessi dati si ritrovano gli stessi id.
        GruppiSenzaNome(null, 1);
        lock (Lucchetto)
        {
            return GruppiNoti.TryGetValue(id.Trim(), out var noti)
                ? noti
                : throw new ArgumentException($"Gruppo «{id}» non trovato: richiama volti_da_nominare (i gruppi cambiano dopo ogni nome dato).");
        }
    }

    /// <summary>Dopo una scrittura i gruppi vanno ricalcolati (gli id già elencati restano validi).</summary>
    public static void Invalida()
    {
        lock (Lucchetto) _ultimiGruppi = null;
    }

    public static List<ProposteDaConfermare> Proposte(string? cartella = null) =>
        Leggi(cartella)
            .Where(v => v.Stato == StatoPersona.Proposto && v.Persona is not null)
            .GroupBy(v => v.Persona!)
            .Select(g => new ProposteDaConfermare(g.Key, g.Count(), g.Select(v => v.Foto).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Esempi(g, 3)))
            .OrderByDescending(p => p.Volti)
            .ToList();

    /// <summary>Miniatura JPEG salvata (112×112, il volto allineato) e posizione del volto, o null se non c'è più.</summary>
    public static (string Foto, double X0, double Y0, double X1, double Y1, byte[]? Miniatura)? Dettaglio(string rif)
    {
        var (sidecar, id) = Risolvi(rif);
        using var c = Ricerca.ApriLettura(Path.Combine(sidecar, LibrarySidecarDatabase.CartellaSidecar, LibrarySidecarDatabase.NomeFileDatabase));
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT p.path_relativo, v.x0, v.y0, v.x1, v.y1, v.miniatura FROM volti v JOIN photos p ON p.id = v.photo_id WHERE v.rowid = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return (Path.GetFullPath(Path.Combine(sidecar, r.GetString(0))), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4),
            r.IsDBNull(5) ? null : (byte[])r[5]);
    }

    // I gruppi costano (raggruppare migliaia di volti): si ricalcolano solo se un sidecar o persone.db è cambiato.
    private static string Firma(string? cartella)
    {
        var parti = new StringBuilder(cartella ?? "*");
        foreach (var sidecar in Ricerca.CartelleConSidecar(cartella))
        {
            var db = Path.Combine(sidecar, LibrarySidecarDatabase.CartellaSidecar, LibrarySidecarDatabase.NomeFileDatabase);
            foreach (var file in new[] { db, db + "-wal" })
            {
                var info = new FileInfo(file);
                if (info.Exists) parti.Append('|').Append(file).Append(info.Length).Append(info.LastWriteTimeUtc.Ticks);
            }
        }
        foreach (var file in new[] { Ricerca.PercorsoPersone, Ricerca.PercorsoPersone + "-wal" })
        {
            var info = new FileInfo(file);
            if (info.Exists) parti.Append('|').Append(info.Length).Append(info.LastWriteTimeUtc.Ticks);
        }
        return parti.ToString();
    }
}
