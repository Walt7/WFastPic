using Microsoft.Data.Sqlite;
using WFastPic.Core.Library;
using WFastPic.Core.Metadata;

namespace WFastPic.Core.Persistence;

public sealed record PhotoRecord(
    long Id,
    string PathRelativo,
    long Size,
    DateTime LastWriteUtc,
    DateTime? CaptureDate,
    string Stato,
    int Rating,
    double? Latitude = null,
    double? Longitude = null);

public sealed record SyncResult(int Nuove, int Aggiornate, int Invariate);

/// <summary>
/// Sidecar SQLite per cartella (.wfastpic/library.db). Nessuna libreria fotografica centrale:
/// ogni cartella scansionata ha il proprio database indipendente, in WAL per letture concorrenti
/// non bloccanti durante le scritture di selezione/editing.
/// Thread-safe: la stessa istanza è usata dalla coda di scrittura di SelectionManager (thread
/// di background) e dal thread UI; una SqliteConnection non lo è, quindi ogni accesso è serializzato.
/// </summary>
public sealed class LibrarySidecarDatabase : IDisposable
{
    public const string CartellaSidecar = ".wfastpic";
    public const string NomeFileDatabase = "library.db";

    private readonly SqliteConnection _connection;
    private readonly Lock _lock = new();

    public string CartellaFoto { get; }

    private LibrarySidecarDatabase(SqliteConnection connection, string cartellaFoto)
    {
        _connection = connection;
        CartellaFoto = cartellaFoto;
    }

    public static LibrarySidecarDatabase OpenOrCreate(string cartellaFoto)
    {
        var cartellaSidecar = Path.Combine(cartellaFoto, CartellaSidecar);
        Directory.CreateDirectory(cartellaSidecar);
        var percorsoDb = Path.Combine(cartellaSidecar, NomeFileDatabase);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = percorsoDb,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        var db = new LibrarySidecarDatabase(connection, cartellaFoto);
        db.EnsureSchema();
        return db;
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS photos (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path_relativo TEXT NOT NULL UNIQUE,
                size INTEGER NOT NULL,
                last_write_utc TEXT NOT NULL,
                capture_date TEXT NULL,
                latitude REAL NULL,
                longitude REAL NULL
            );

            CREATE TABLE IF NOT EXISTS selection_state (
                photo_id INTEGER PRIMARY KEY REFERENCES photos(id) ON DELETE CASCADE,
                stato TEXT NOT NULL DEFAULT 'None',
                rating INTEGER NOT NULL DEFAULT 0,
                last_modified TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS edits (
                photo_id INTEGER PRIMARY KEY REFERENCES photos(id) ON DELETE CASCADE,
                recipe_json TEXT NOT NULL,
                schema_version INTEGER NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        // Migrazione tabelle photos create in versioni precedenti prive di coordinate GPS
        var colonneEsistenti = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var infoCmd = _connection.CreateCommand())
        {
            infoCmd.CommandText = "PRAGMA table_info(photos);";
            using var reader = infoCmd.ExecuteReader();
            while (reader.Read())
            {
                colonneEsistenti.Add(reader.GetString(1));
            }
        }

        if (!colonneEsistenti.Contains("latitude"))
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE photos ADD COLUMN latitude REAL NULL;";
            alterCmd.ExecuteNonQuery();
        }

        if (!colonneEsistenti.Contains("longitude"))
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE photos ADD COLUMN longitude REAL NULL;";
            alterCmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Allinea il database al risultato di una scansione cartella. Confronta solo
    /// PathRelativo + Size + LastWriteUtc (nessun hash): file invariati non toccano il DB.
    /// </summary>
    public SyncResult SyncScan(IReadOnlyList<ScannedPhoto> fotoScansionate)
    {
        using var scope = _lock.EnterScope();
        int nuove = 0, aggiornate = 0, invariate = 0;

        using var transaction = _connection.BeginTransaction();

        foreach (var foto in fotoScansionate)
        {
            using var select = _connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT id, size, last_write_utc, latitude, longitude FROM photos WHERE path_relativo = $path;";
            select.Parameters.AddWithValue("$path", foto.Identity.PathRelativo);

            using var reader = select.ExecuteReader();
            if (reader.Read())
            {
                var id = reader.GetInt64(0);
                var sizeEsistente = reader.GetInt64(1);
                var lastWriteEsistente = DateTime.Parse(reader.GetString(2)).ToUniversalTime();
                double? latEsistente = reader.IsDBNull(3) ? null : reader.GetDouble(3);
                double? lonEsistente = reader.IsDBNull(4) ? null : reader.GetDouble(4);
                reader.Close();

                var invariato = sizeEsistente == foto.Identity.Size
                    && lastWriteEsistente == foto.Identity.LastWriteUtc
                    && latEsistente == foto.Gps?.Latitude
                    && lonEsistente == foto.Gps?.Longitude;

                if (invariato)
                {
                    invariate++;
                    continue;
                }

                using var update = _connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE photos SET size = $size, last_write_utc = $lastWrite, capture_date = $captureDate,
                                      latitude = $lat, longitude = $lon
                    WHERE id = $id;
                    """;
                update.Parameters.AddWithValue("$size", foto.Identity.Size);
                update.Parameters.AddWithValue("$lastWrite", foto.Identity.LastWriteUtc.ToString("O"));
                update.Parameters.AddWithValue("$captureDate", (object?)foto.CaptureDate?.ToString("O") ?? DBNull.Value);
                update.Parameters.AddWithValue("$lat", (object?)foto.Gps?.Latitude ?? DBNull.Value);
                update.Parameters.AddWithValue("$lon", (object?)foto.Gps?.Longitude ?? DBNull.Value);
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
                aggiornate++;
            }
            else
            {
                reader.Close();

                using var insert = _connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO photos (path_relativo, size, last_write_utc, capture_date, latitude, longitude)
                    VALUES ($path, $size, $lastWrite, $captureDate, $lat, $lon);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("$path", foto.Identity.PathRelativo);
                insert.Parameters.AddWithValue("$size", foto.Identity.Size);
                insert.Parameters.AddWithValue("$lastWrite", foto.Identity.LastWriteUtc.ToString("O"));
                insert.Parameters.AddWithValue("$captureDate", (object?)foto.CaptureDate?.ToString("O") ?? DBNull.Value);
                insert.Parameters.AddWithValue("$lat", (object?)foto.Gps?.Latitude ?? DBNull.Value);
                insert.Parameters.AddWithValue("$lon", (object?)foto.Gps?.Longitude ?? DBNull.Value);
                var nuovoId = (long)insert.ExecuteScalar()!;

                using var insertStato = _connection.CreateCommand();
                insertStato.Transaction = transaction;
                insertStato.CommandText = """
                    INSERT INTO selection_state (photo_id, stato, rating, last_modified)
                    VALUES ($photoId, 'None', 0, $now);
                    """;
                insertStato.Parameters.AddWithValue("$photoId", nuovoId);
                insertStato.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                insertStato.ExecuteNonQuery();

                nuove++;
            }
        }

        // La scansione è l'autorità per il contenuto della cartella. Evitiamo un IN(...) per non
        // superare il limite di parametri SQLite su cartelle molto grandi.
        var percorsiScansionati = fotoScansionate.Select(f => f.Identity.PathRelativo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mancanti = new List<long>();
        using (var tutteLeFoto = _connection.CreateCommand())
        {
            tutteLeFoto.Transaction = transaction;
            tutteLeFoto.CommandText = "SELECT id, path_relativo FROM photos;";
            using var reader = tutteLeFoto.ExecuteReader();
            while (reader.Read())
            {
                if (!percorsiScansionati.Contains(reader.GetString(1)))
                    mancanti.Add(reader.GetInt64(0));
            }
        }

        foreach (var id in mancanti)
        {
            using var delete = _connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM photos WHERE id = $id;";
            delete.Parameters.AddWithValue("$id", id);
            delete.ExecuteNonQuery();
        }

        transaction.Commit();
        return new SyncResult(nuove, aggiornate, invariate);
    }

    /// <summary>
    /// Per <see cref="FolderScanner.Scan"/>: restituisce i metadati salvati di una foto la cui identità
    /// (nome + size + data modifica) non è cambiata, così la riapertura salta la lettura EXIF.
    /// Una foto modificata o nuova restituisce null e viene riletta.
    /// </summary>
    public Func<PhotoIdentity, ScannedPhoto?> CreaRisolutoreMetadatiNoti()
    {
        var noti = new Dictionary<string, ScannedPhoto>(StringComparer.OrdinalIgnoreCase);
        using (_lock.EnterScope())
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT path_relativo, size, last_write_utc, capture_date, latitude, longitude FROM photos;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var identity = new PhotoIdentity(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    DateTime.Parse(reader.GetString(2)).ToUniversalTime());
                DateTime? captureDate = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3));
                GeoCoordinate? gps = reader.IsDBNull(4) || reader.IsDBNull(5)
                    ? null
                    : new GeoCoordinate(reader.GetDouble(4), reader.GetDouble(5));
                noti[identity.PathRelativo] = new ScannedPhoto(identity, captureDate, gps);
            }
        }

        return identity => noti.TryGetValue(identity.PathRelativo, out var nota)
            && nota.Identity.Size == identity.Size
            && nota.Identity.LastWriteUtc == identity.LastWriteUtc
                ? nota with { Identity = identity }
                : null;
    }

    /// <summary>
    /// Tutte le foto in ordine cronologico di scatto (come Picasa): con più fotocamere nella stessa
    /// cartella o con la numerazione IMG_xxxx che riparte, l'ordine per nome separerebbe foto
    /// scattate insieme (e le raffiche). Le foto senza data finiscono in fondo, per nome.
    /// </summary>
    public IReadOnlyList<PhotoRecord> GetAllPhotos()
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.path_relativo, p.size, p.last_write_utc, p.capture_date,
                   s.stato, s.rating, p.latitude, p.longitude
            FROM photos p
            JOIN selection_state s ON s.photo_id = p.id
            ORDER BY p.capture_date IS NULL, p.capture_date, p.path_relativo;
            """;

        using var reader = cmd.ExecuteReader();
        var risultato = new List<PhotoRecord>();
        while (reader.Read())
        {
            risultato.Add(new PhotoRecord(
                Id: reader.GetInt64(0),
                PathRelativo: reader.GetString(1),
                Size: reader.GetInt64(2),
                LastWriteUtc: DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
                CaptureDate: reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                Stato: reader.GetString(5),
                Rating: reader.GetInt32(6),
                Latitude: reader.IsDBNull(7) ? null : reader.GetDouble(7),
                Longitude: reader.IsDBNull(8) ? null : reader.GetDouble(8)));
        }

        return risultato;
    }

    /// <summary>
    /// Aggiorna solo lo stato di selezione (None|Selected|Reject). "Reject" non cancella mai il file:
    /// è solo un marcatore in DB, la disposizione fisica passa da "Gestisci scarti…".
    /// </summary>
    public void UpdateStato(long photoId, string stato)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE selection_state SET stato = $stato, last_modified = $now WHERE photo_id = $id;
            """;
        cmd.Parameters.AddWithValue("$stato", stato);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateRating(long photoId, int rating)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE selection_state SET rating = $rating, last_modified = $now WHERE photo_id = $id;
            """;
        cmd.Parameters.AddWithValue("$rating", rating);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PhotoRecord> GetByStato(string stato)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.path_relativo, p.size, p.last_write_utc, p.capture_date,
                   s.stato, s.rating, p.latitude, p.longitude
            FROM photos p
            JOIN selection_state s ON s.photo_id = p.id
            WHERE s.stato = $stato
            ORDER BY p.path_relativo;
            """;
        cmd.Parameters.AddWithValue("$stato", stato);

        using var reader = cmd.ExecuteReader();
        var risultato = new List<PhotoRecord>();
        while (reader.Read())
        {
            risultato.Add(new PhotoRecord(
                Id: reader.GetInt64(0),
                PathRelativo: reader.GetString(1),
                Size: reader.GetInt64(2),
                LastWriteUtc: DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
                CaptureDate: reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                Stato: reader.GetString(5),
                Rating: reader.GetInt32(6),
                Latitude: reader.IsDBNull(7) ? null : reader.GetDouble(7),
                Longitude: reader.IsDBNull(8) ? null : reader.GetDouble(8)));
        }

        return risultato;
    }

    /// <summary>
    /// Rimuove la riga di un file dal DB (usato dopo che il file fisico è stato spostato/eliminato
    /// da "Gestisci scarti…" — non cancella mai il file da solo).
    /// </summary>
    public void RemovePhoto(long photoId)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM photos WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Ricetta di editing salvata per la foto, o null se non è mai stata modificata.</summary>
    public string? GetRecipeJson(long photoId)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT recipe_json FROM edits WHERE photo_id = $id;";
        cmd.Parameters.AddWithValue("$id", photoId);
        return cmd.ExecuteScalar() as string;
    }

    public void SaveRecipe(long photoId, string recipeJson, int schemaVersion)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO edits (photo_id, recipe_json, schema_version) VALUES ($id, $json, $version)
            ON CONFLICT(photo_id) DO UPDATE SET recipe_json = excluded.recipe_json, schema_version = excluded.schema_version;
            """;
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.Parameters.AddWithValue("$json", recipeJson);
        cmd.Parameters.AddWithValue("$version", schemaVersion);
        cmd.ExecuteNonQuery();
    }

    public void UpdateGpsBatch(IEnumerable<(long Id, double? Latitude, double? Longitude)> updates)
    {
        using var scope = _lock.EnterScope();
        using var transaction = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE photos SET latitude = $lat, longitude = $lon WHERE id = $id;";
        var pLat = cmd.Parameters.Add("$lat", SqliteType.Real);
        var pLon = cmd.Parameters.Add("$lon", SqliteType.Real);
        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);

        foreach (var (id, lat, lon) in updates)
        {
            pLat.Value = (object?)lat ?? DBNull.Value;
            pLon.Value = (object?)lon ?? DBNull.Value;
            pId.Value = id;
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void Dispose()
    {
        using var scope = _lock.EnterScope();
        _connection.Dispose();
    }
}
