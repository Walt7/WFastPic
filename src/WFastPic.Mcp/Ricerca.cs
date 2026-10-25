using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WFastPic.Mcp;

/// <summary>Filtri comuni a "cerca_foto" e "prepara_pacchetto".</summary>
public sealed record FiltriFoto(
    string? Cartella = null,
    IReadOnlyList<string>? Persone = null,
    bool AncheProposte = false,
    DateTime? Dal = null,
    DateTime? Al = null,
    string? Luogo = null,
    bool SoloSelezionate = false,
    int VotoMinimo = 0,
    bool IncludiScarti = false,
    IReadOnlySet<string>? Percorsi = null);

public sealed record FotoTrovata(
    string Percorso, string Cartella, DateTime? Data, string Stato, int Voto,
    double? Latitudine, double? Longitudine, string? Luogo, IReadOnlyList<string> Persone);

public sealed record CartellaNota(string Percorso, int Foto, DateTime? Primo, DateTime? Ultimo, int Selezionate);

public sealed record PersonaNotaMcp(long Id, string Nome, string Soprannome, string Cerchia, bool Preferita);

/// <summary>
/// Ricerca in sola lettura nei sidecar delle cartelle note (e nell'archivio persone): non crea mai un
/// sidecar, non tocca lo schema, non scrive nulla. Una cartella madre e le sue sottocartelle possono avere
/// ognuna il proprio sidecar: la stessa foto compare una volta sola (vale il sidecar più in alto).
/// </summary>
public static class Ricerca
{
    private const string Sidecar = ".wfastpic";
    private const string FileDb = "library.db";
    private const int StatoProposto = 1, StatoConfermato = 2;

    // WFASTPIC_DATA sposta la cartella dati (prove); di norma è LocalAppData\WFastPic, come l'app.
    public static string DatiApp =>
        Environment.GetEnvironmentVariable("WFASTPIC_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WFastPic");

    public static string PercorsoPersone => Path.Combine(DatiApp, "persone.db");

    public static List<string> CartelleRecenti()
    {
        try
        {
            var file = Path.Combine(DatiApp, "recent-folders.json");
            return File.Exists(file) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public static bool HaSidecar(string cartella) => File.Exists(Path.Combine(cartella, Sidecar, FileDb));

    private static SqliteConnection ApriLettura(string file)
    {
        var connessione = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        connessione.Open();
        return connessione;
    }

    // ---------------------------------------------------------------- persone

    public static List<PersonaNotaMcp> ElencoPersone()
    {
        var risultato = new List<PersonaNotaMcp>();
        if (!File.Exists(PercorsoPersone)) return risultato;
        try
        {
            using var c = ApriLettura(PercorsoPersone);
            var colonne = Colonne(c, "persone");
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"""
                SELECT id, nome, {(colonne.Contains("soprannome") ? "soprannome" : "''")},
                       {(colonne.Contains("cerchia") ? "cerchia" : "0")}, {(colonne.Contains("preferita") ? "preferita" : "0")}
                FROM persone WHERE {(colonne.Contains("unita_a") ? "unita_a IS NULL" : "1=1")} ORDER BY nome COLLATE NOCASE;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                risultato.Add(new PersonaNotaMcp(r.GetInt64(0), r.GetString(1), r.GetString(2), NomeCerchia(r.GetInt32(3)), r.GetInt64(4) != 0));
        }
        catch (Exception ex) when (ex is SqliteException or IOException) { }
        return risultato;
    }

    private static string NomeCerchia(int cerchia) => cerchia switch
    {
        4 => "io", 3 => "famiglia", 2 => "amico", 1 => "conoscente", _ => "",
    };

    /// <summary>Persona unita → persona in cui è confluita, e nomi (nome + soprannome) di tutte.</summary>
    private static (Dictionary<long, long> Unioni, Dictionary<long, string> Nomi) LeggiUnioniENomi()
    {
        var unioni = new Dictionary<long, long>();
        var nomi = new Dictionary<long, string>();
        if (!File.Exists(PercorsoPersone)) return (unioni, nomi);
        try
        {
            using var c = ApriLettura(PercorsoPersone);
            var colonne = Colonne(c, "persone");
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT id, nome, {(colonne.Contains("soprannome") ? "soprannome" : "''")}, {(colonne.Contains("unita_a") ? "unita_a" : "NULL")} FROM persone;";
            using var r = cmd.ExecuteReader();
            var diretti = new Dictionary<long, long>();
            while (r.Read())
            {
                var id = r.GetInt64(0);
                nomi[id] = r.GetString(1) + (r.GetString(2).Length > 0 ? "\n" + r.GetString(2) : "");
                if (!r.IsDBNull(3)) diretti[id] = r.GetInt64(3);
            }
            foreach (var id in diretti.Keys)
            {
                var corrente = id;
                for (var i = 0; i < 50 && diretti.TryGetValue(corrente, out var prossimo); i++) corrente = prossimo;
                unioni[id] = corrente;
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException) { }
        return (unioni, nomi);
    }

    private static HashSet<string> Colonne(SqliteConnection c, string tabella)
    {
        var colonne = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tabella});";
        using var r = cmd.ExecuteReader();
        while (r.Read()) colonne.Add(r.GetString(1));
        return colonne;
    }

    // ---------------------------------------------------------------- cartelle

    /// <summary>Cartelle con sidecar: quelle note all'app più, se serve, quella chiesta e le sue sottocartelle.</summary>
    public static List<string> CartelleConSidecar(string? filtro)
    {
        var note = CartelleRecenti().Where(Directory.Exists).ToList();
        if (!string.IsNullOrWhiteSpace(filtro) && Path.IsPathRooted(filtro) && Directory.Exists(filtro))
        {
            // Una cartella mai aperta nell'app ma con sidecar (es. su un disco esterno): si trova comunque.
            note.Add(Path.GetFullPath(filtro));
            note.AddRange(SottocartelleConSidecar(filtro));
        }

        var distinte = note
            .Select(p => Path.TrimEndingDirectorySeparator(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(HaSidecar);

        if (string.IsNullOrWhiteSpace(filtro)) return distinte.OrderBy(p => p.Length).ToList();
        return distinte.Where(p => Corrisponde(p, filtro)).OrderBy(p => p.Length).ToList();
    }

    // Percorso completo = la cartella e tutto quel che c'è sotto; altrimenti il testo cerca nel nome del percorso.
    private static bool Corrisponde(string cartella, string filtro)
    {
        if (Path.IsPathRooted(filtro))
        {
            var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(filtro));
            return string.Equals(cartella, f, StringComparison.OrdinalIgnoreCase)
                   || cartella.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        return cartella.Contains(filtro, StringComparison.CurrentCultureIgnoreCase);
    }

    private static IEnumerable<string> SottocartelleConSidecar(string radice)
    {
        var pila = new Stack<string>();
        pila.Push(radice);
        while (pila.Count > 0)
        {
            var corrente = pila.Pop();
            string[] figli;
            try { figli = Directory.GetDirectories(corrente); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
            foreach (var figlio in figli)
            {
                var nome = Path.GetFileName(figlio);
                if (nome.StartsWith('.') || nome.StartsWith('$')) continue;
                yield return figlio;
                pila.Push(figlio);
            }
        }
    }

    public static List<CartellaNota> ElencoCartelle()
    {
        var risultato = new List<CartellaNota>();
        foreach (var cartella in CartelleRecenti())
        {
            if (!HaSidecar(cartella)) continue;
            try
            {
                using var c = ApriLettura(Path.Combine(cartella, Sidecar, FileDb));
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    SELECT COUNT(*), MIN(capture_date), MAX(capture_date),
                           (SELECT COUNT(*) FROM selection_state WHERE stato = 'Selected') FROM photos;
                    """;
                using var r = cmd.ExecuteReader();
                if (r.Read())
                    risultato.Add(new CartellaNota(cartella, r.GetInt32(0),
                        r.IsDBNull(1) ? null : DateTime.Parse(r.GetString(1), CultureInfo.InvariantCulture),
                        r.IsDBNull(2) ? null : DateTime.Parse(r.GetString(2), CultureInfo.InvariantCulture),
                        r.GetInt32(3)));
            }
            catch (Exception ex) when (ex is SqliteException or IOException or FormatException) { }
        }
        return risultato;
    }

    // ---------------------------------------------------------------- foto

    public static List<FotoTrovata> Cerca(FiltriFoto filtri)
    {
        var (unioni, nomi) = LeggiUnioniENomi();
        long Risolvi(long id) => unioni.TryGetValue(id, out var resta) ? resta : id;

        // Per ogni persona chiesta: gli id (con nome o soprannome che contiene il testo). Tutte devono comparire.
        var richieste = new List<HashSet<long>>();
        foreach (var testo in filtri.Persone ?? [])
        {
            if (string.IsNullOrWhiteSpace(testo)) continue;
            var ids = nomi
                .Where(kv => kv.Value.Split('\n').Any(n => n.Contains(testo.Trim(), StringComparison.CurrentCultureIgnoreCase)))
                .Select(kv => Risolvi(kv.Key)).ToHashSet();
            richieste.Add(ids);
        }

        var viste = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var risultato = new List<FotoTrovata>();
        foreach (var cartella in CartelleConSidecar(filtri.Cartella))
        {
            try { CercaInCartella(cartella, filtri, richieste, Risolvi, nomi, viste, risultato); }
            catch (Exception ex) when (ex is SqliteException or IOException or FormatException) { /* sidecar bloccato o di un'altra versione */ }
        }
        return risultato.OrderBy(f => f.Data ?? DateTime.MaxValue).ThenBy(f => f.Percorso, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CercaInCartella(
        string cartella, FiltriFoto filtri, List<HashSet<long>> richieste, Func<long, long> risolvi,
        Dictionary<long, string> nomi, HashSet<string> viste, List<FotoTrovata> risultato)
    {
        using var c = ApriLettura(Path.Combine(cartella, Sidecar, FileDb));
        var colonne = Colonne(c, "photos");

        // Luoghi: celle di ~100 m con il nome in cache (italiano e inglese).
        var luoghi = new Dictionary<string, string>();
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT chiave, nome FROM luoghi;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) luoghi[r.GetString(0).Replace("en|", "")] = r.GetString(1);
        }
        catch (SqliteException) { }

        // Volti: solo persone confermate (e proposte se richiesto), senza blob.
        var persone = new Dictionary<long, HashSet<long>>();
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT photo_id, persona_id FROM volti WHERE persona_id IS NOT NULL AND stato_persona IN ($a, $b);";
            cmd.Parameters.AddWithValue("$a", StatoConfermato);
            cmd.Parameters.AddWithValue("$b", filtri.AncheProposte ? StatoProposto : StatoConfermato);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var foto = r.GetInt64(0);
                if (!persone.TryGetValue(foto, out var set)) persone[foto] = set = [];
                set.Add(risolvi(r.GetInt64(1)));
            }
        }
        catch (SqliteException) { }

        using var query = c.CreateCommand();
        query.CommandText = $"""
            SELECT p.id, p.path_relativo, p.capture_date, {(colonne.Contains("latitude") ? "p.latitude" : "NULL")},
                   {(colonne.Contains("longitude") ? "p.longitude" : "NULL")}, s.stato, s.rating
            FROM photos p JOIN selection_state s ON s.photo_id = p.id;
            """;
        using var reader = query.ExecuteReader();
        while (reader.Read())
        {
            var stato = reader.GetString(5);
            var voto = reader.GetInt32(6);
            if (stato == "Reject" && !filtri.IncludiScarti) continue;
            if (filtri.SoloSelezionate && stato != "Selected") continue;
            if (voto < filtri.VotoMinimo) continue;

            DateTime? data = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            if (filtri.Dal is { } dal && (data is null || data < dal)) continue;
            if (filtri.Al is { } al && (data is null || data >= al.Date.AddDays(1))) continue;

            double? lat = reader.IsDBNull(3) ? null : reader.GetDouble(3);
            double? lon = reader.IsDBNull(4) ? null : reader.GetDouble(4);
            string? luogo = null;
            if (lat is { } la && lon is { } lo)
            {
                var chiave = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(la, 3):F3},{Math.Round(lo, 3):F3}");
                luoghi.TryGetValue(chiave, out luogo);
            }
            if (!string.IsNullOrWhiteSpace(filtri.Luogo)
                && (luogo is null || !luogo.Contains(filtri.Luogo.Trim(), StringComparison.CurrentCultureIgnoreCase))) continue;

            var idFoto = reader.GetInt64(0);
            persone.TryGetValue(idFoto, out var inFoto);
            if (richieste.Any(ids => inFoto is null || !ids.Overlaps(inFoto))) continue;

            var percorso = Path.GetFullPath(Path.Combine(cartella, reader.GetString(1)));
            if (filtri.Percorsi is not null && !filtri.Percorsi.Contains(percorso)) continue;
            if (!viste.Add(percorso)) continue; // già trovata nel sidecar della cartella madre

            risultato.Add(new FotoTrovata(percorso, cartella, data, stato, voto, lat, lon, luogo,
                (inFoto ?? []).Select(id => nomi.TryGetValue(id, out var n) ? n.Split('\n')[0] : "?").OrderBy(n => n).ToList()));
        }
    }
}
