using Microsoft.Data.Sqlite;
using WFastPic.Core.Library;

namespace WFastPic.Core.Persistence;

/// <summary>
/// Catalogare una cartella madre (es. tutta E:\Fotocamera) senza perdere il lavoro già fatto nelle
/// sottocartelle: la prima volta, selezioni, voti, ricette, posizioni a mano, volti (con le persone
/// assegnate), misure e impronte dei loro sidecar vengono riversati in questo, in sola lettura sui
/// sidecar figli. Una sottocartella si importa una volta sola (<c>sidecar_importati</c>): da lì in poi
/// vale questo sidecar. Mai sovrascrive quello che qui è già stato toccato.
/// </summary>
public sealed partial class LibrarySidecarDatabase
{
    private sealed record VoltoLetto(double X0, double Y0, double X1, double Y1, byte[]? Impronta, byte[]? Miniatura, double? Punteggio, long? PersonaId, int StatoPersona);

    private sealed class FotoFiglio
    {
        public required string Path { get; init; }
        public required long Size { get; init; }
        public required string LastWrite { get; init; }
        public string Stato { get; set; } = "None";
        public int Rating { get; set; }
        public string? Ricetta { get; set; }
        public int SchemaRicetta { get; set; }
        public bool GpsManuale { get; init; }
        public double? Lat { get; init; }
        public double? Lon { get; init; }
        public int VoltiCercati { get; init; }
        public List<VoltoLetto> Volti { get; } = [];
        public (int Versione, double Nit, double? NitVolti, double Bru, double Chi, double Lum)? Qualita { get; set; }
        public (int Versione, long Impronta, double Rapporto)? Impronta { get; set; }
    }

    /// <summary>Importa i sidecar delle sottocartelle non ancora importati; restituisce quante cartelle.</summary>
    public int ImportaDaSidecarFigli()
    {
        var figli = TrovaSidecarFigli(CartellaFoto).ToList();
        if (figli.Count == 0) return 0;

        HashSet<string> fatti;
        using (var scope = Entra())
        {
            fatti = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT percorso FROM sidecar_importati;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) fatti.Add(reader.GetString(0));
        }

        var importate = 0;
        foreach (var relativo in figli)
        {
            if (fatti.Contains(relativo)) continue;
            var foto = LeggiSidecarFiglio(Path.Combine(CartellaFoto, relativo));
            if (foto is null) continue; // illeggibile adesso: ci si riprova alla prossima apertura

            using var scope = Entra();
            using var transaction = _connection.BeginTransaction();
            var trovate = Riversa(foto, relativo, transaction);
            if (trovate == 0 && foto.Count > 0)
            {
                // Nessuna foto del figlio è (ancora) qui: sottocartella non letta da questa scansione o
                // file cambiati. Non si segna, altrimenti il suo lavoro non entrerebbe mai più.
                transaction.Rollback();
                continue;
            }
            using (var segna = _connection.CreateCommand())
            {
                segna.Transaction = transaction;
                segna.CommandText = "INSERT OR IGNORE INTO sidecar_importati(percorso) VALUES ($p);";
                segna.Parameters.AddWithValue("$p", relativo);
                segna.ExecuteNonQuery();
            }
            transaction.Commit();
            importate++;
        }
        return importate;
    }

    /// <summary>Percorsi relativi delle sottocartelle (a qualsiasi livello) che hanno già un sidecar.</summary>
    private static IEnumerable<string> TrovaSidecarFigli(string radice)
    {
        var pila = new Stack<string>();
        pila.Push(radice);
        while (pila.Count > 0)
        {
            var corrente = pila.Pop();
            IEnumerable<DirectoryInfo> figli;
            try { figli = new DirectoryInfo(corrente).EnumerateDirectories().Where(d => !FolderScanner.DaSaltare(d)).ToList(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }

            foreach (var figlio in figli)
            {
                if (File.Exists(Path.Combine(figlio.FullName, CartellaSidecar, NomeFileDatabase)))
                    yield return Path.GetRelativePath(radice, figlio.FullName);
                pila.Push(figlio.FullName);
            }
        }
    }

    private static List<FotoFiglio>? LeggiSidecarFiglio(string cartella)
    {
        var elenco = new Dictionary<long, FotoFiglio>();
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(cartella, CartellaSidecar, NomeFileDatabase),
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();

            var colonne = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var info = connection.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(photos);";
                using var r = info.ExecuteReader();
                while (r.Read()) colonne.Add(r.GetString(1));
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"""
                    SELECT id, path_relativo, size, last_write_utc,
                           {(colonne.Contains("gps_manuale") ? "gps_manuale" : "0")},
                           {(colonne.Contains("latitude") ? "latitude" : "NULL")},
                           {(colonne.Contains("longitude") ? "longitude" : "NULL")},
                           {(colonne.Contains("volti_cercati") ? "volti_cercati" : "0")}
                    FROM photos;
                    """;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    elenco[r.GetInt64(0)] = new FotoFiglio
                    {
                        Path = r.GetString(1),
                        Size = r.GetInt64(2),
                        LastWrite = r.GetString(3),
                        GpsManuale = r.GetInt32(4) != 0,
                        Lat = r.IsDBNull(5) ? null : r.GetDouble(5),
                        Lon = r.IsDBNull(6) ? null : r.GetDouble(6),
                        VoltiCercati = r.GetInt32(7),
                    };
                }
            }

            // Le altre tabelle possono mancare in un sidecar di un'altra versione: si prende quel che c'è.
            Prova(() =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT photo_id, stato, rating FROM selection_state;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (elenco.TryGetValue(r.GetInt64(0), out var f)) { f.Stato = r.GetString(1); f.Rating = r.GetInt32(2); }
            });
            Prova(() =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT photo_id, recipe_json, schema_version FROM edits;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (elenco.TryGetValue(r.GetInt64(0), out var f)) { f.Ricetta = r.GetString(1); f.SchemaRicetta = r.GetInt32(2); }
            });
            Prova(() =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT photo_id, x0, y0, x1, y1, impronta, miniatura, punteggio, persona_id, stato_persona FROM volti;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (!elenco.TryGetValue(r.GetInt64(0), out var f)) continue;
                    f.Volti.Add(new VoltoLetto(
                        r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4),
                        r.IsDBNull(5) ? null : (byte[])r[5],
                        r.IsDBNull(6) ? null : (byte[])r[6],
                        r.IsDBNull(7) ? null : r.GetDouble(7),
                        r.IsDBNull(8) ? null : r.GetInt64(8),
                        r.GetInt32(9)));
                }
            });
            Prova(() =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT photo_id, versione, nitidezza, nitidezza_volti, bruciati, chiusi, luminosita FROM qualita;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (elenco.TryGetValue(r.GetInt64(0), out var f))
                        f.Qualita = (r.GetInt32(1), r.GetDouble(2), r.IsDBNull(3) ? null : r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6));
            });
            Prova(() =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT photo_id, versione, impronta, rapporto FROM impronte;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (elenco.TryGetValue(r.GetInt64(0), out var f))
                        f.Impronta = (r.GetInt32(1), r.GetInt64(2), r.GetDouble(3));
            });
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return elenco.Values.ToList();
    }

    private static void Prova(Action lettura)
    {
        try { lettura(); }
        catch (SqliteException) { /* tabella o colonna di un'altra versione */ }
    }

    /// <summary>Riversa il lavoro del sidecar figlio; restituisce quante sue foto sono state trovate qui.</summary>
    private int Riversa(List<FotoFiglio> foto, string relativoFiglio, SqliteTransaction transaction)
    {
        var trovate = 0;
        // Foto di qui per percorso: stessa identità (dimensione e data) = stesso file.
        var qui = new Dictionary<string, (long Id, long Size, string LastWrite, bool GpsManuale)>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = _connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT id, path_relativo, size, last_write_utc, gps_manuale FROM photos;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) qui[r.GetString(1)] = (r.GetInt64(0), r.GetInt64(2), r.GetString(3), r.GetInt32(4) != 0);
        }

        foreach (var f in foto)
        {
            if (!qui.TryGetValue(Path.Combine(relativoFiglio, f.Path), out var q) || q.Size != f.Size || q.LastWrite != f.LastWrite) continue;
            var id = q.Id;
            trovate++;

            // Selezione e voto: solo se qui non sono stati toccati.
            if (f.Stato != "None" || f.Rating != 0)
            {
                Esegui(transaction, """
                    UPDATE selection_state SET stato = $stato, rating = $rating, last_modified = $now
                    WHERE photo_id = $id AND stato = 'None' AND rating = 0;
                    """, ("$stato", f.Stato), ("$rating", f.Rating), ("$now", DateTime.UtcNow.ToString("O")), ("$id", id));
            }

            if (f.Ricetta is not null)
            {
                Esegui(transaction, "INSERT OR IGNORE INTO edits(photo_id, recipe_json, schema_version) VALUES ($id, $j, $v);",
                    ("$id", id), ("$j", f.Ricetta), ("$v", f.SchemaRicetta));
            }

            if (f.GpsManuale && !q.GpsManuale)
            {
                Esegui(transaction, "UPDATE photos SET latitude = $lat, longitude = $lon, gps_manuale = 1 WHERE id = $id;",
                    ("$lat", (object?)f.Lat ?? DBNull.Value), ("$lon", (object?)f.Lon ?? DBNull.Value), ("$id", id));
            }

            if (f.Volti.Count > 0 || f.VoltiCercati != 0)
            {
                var giaVolti = Convert.ToInt64(Scalare(transaction,
                    "SELECT (SELECT COUNT(*) FROM volti WHERE photo_id = $id) + (SELECT volti_cercati FROM photos WHERE id = $id);", ("$id", id)));
                if (giaVolti == 0)
                {
                    foreach (var v in f.Volti)
                    {
                        Esegui(transaction, """
                            INSERT INTO volti(photo_id, x0, y0, x1, y1, impronta, miniatura, punteggio, persona_id, stato_persona)
                            VALUES ($id, $x0, $y0, $x1, $y1, $imp, $min, $pun, $per, $sta);
                            """,
                            ("$id", id), ("$x0", v.X0), ("$y0", v.Y0), ("$x1", v.X1), ("$y1", v.Y1),
                            ("$imp", (object?)v.Impronta ?? DBNull.Value), ("$min", (object?)v.Miniatura ?? DBNull.Value),
                            ("$pun", (object?)v.Punteggio ?? DBNull.Value), ("$per", (object?)v.PersonaId ?? DBNull.Value),
                            ("$sta", v.StatoPersona));
                    }
                    Esegui(transaction, "UPDATE photos SET volti_cercati = $c WHERE id = $id;", ("$c", f.VoltiCercati), ("$id", id));
                }
            }

            if (f.Qualita is { } ql)
            {
                Esegui(transaction, """
                    INSERT OR IGNORE INTO qualita(photo_id, versione, nitidezza, nitidezza_volti, bruciati, chiusi, luminosita)
                    VALUES ($id, $v, $n, $nv, $b, $c, $l);
                    """,
                    ("$id", id), ("$v", ql.Versione), ("$n", ql.Nit), ("$nv", (object?)ql.NitVolti ?? DBNull.Value),
                    ("$b", ql.Bru), ("$c", ql.Chi), ("$l", ql.Lum));
            }

            if (f.Impronta is { } im)
            {
                Esegui(transaction, "INSERT OR IGNORE INTO impronte(photo_id, versione, impronta, rapporto) VALUES ($id, $v, $i, $r);",
                    ("$id", id), ("$v", im.Versione), ("$i", im.Impronta), ("$r", im.Rapporto));
            }
        }
        return trovate;
    }

    private void Esegui(SqliteTransaction transaction, string sql, params (string Nome, object Valore)[] parametri)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        foreach (var (nome, valore) in parametri) cmd.Parameters.AddWithValue(nome, valore);
        cmd.ExecuteNonQuery();
    }

    private object? Scalare(SqliteTransaction transaction, string sql, params (string Nome, object Valore)[] parametri)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        foreach (var (nome, valore) in parametri) cmd.Parameters.AddWithValue(nome, valore);
        return cmd.ExecuteScalar();
    }
}
