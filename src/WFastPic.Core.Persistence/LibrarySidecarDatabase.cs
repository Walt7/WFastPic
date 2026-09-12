using Microsoft.Data.Sqlite;
using WFastPic.Core.Library;

namespace WFastPic.Core.Persistence;

public sealed record PhotoRecord(
    long Id,
    string PathRelativo,
    long Size,
    DateTime LastWriteUtc,
    DateTime? CaptureDate,
    string Stato,
    int Rating);

public sealed record SyncResult(int Nuove, int Aggiornate, int Invariate);

/// <summary>
/// Sidecar SQLite per cartella (.wfastpic/library.db). Nessuna libreria fotografica centrale:
/// ogni cartella scansionata ha il proprio database indipendente, in WAL per letture concorrenti
/// non bloccanti durante le scritture di selezione/editing.
/// </summary>
public sealed class LibrarySidecarDatabase : IDisposable
{
    public const string CartellaSidecar = ".wfastpic";
    public const string NomeFileDatabase = "library.db";

    private readonly SqliteConnection _connection;

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
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
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
                capture_date TEXT NULL
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
    }

    /// <summary>
    /// Allinea il database al risultato di una scansione cartella. Confronta solo
    /// PathRelativo + Size + LastWriteUtc (nessun hash): file invariati non toccano il DB.
    /// </summary>
    public SyncResult SyncScan(IReadOnlyList<ScannedPhoto> fotoScansionate)
    {
        int nuove = 0, aggiornate = 0, invariate = 0;

        using var transaction = _connection.BeginTransaction();

        foreach (var foto in fotoScansionate)
        {
            using var select = _connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT id, size, last_write_utc FROM photos WHERE path_relativo = $path;";
            select.Parameters.AddWithValue("$path", foto.Identity.PathRelativo);

            using var reader = select.ExecuteReader();
            if (reader.Read())
            {
                var id = reader.GetInt64(0);
                var sizeEsistente = reader.GetInt64(1);
                var lastWriteEsistente = DateTime.Parse(reader.GetString(2)).ToUniversalTime();
                reader.Close();

                var invariato = sizeEsistente == foto.Identity.Size
                    && lastWriteEsistente == foto.Identity.LastWriteUtc;

                if (invariato)
                {
                    invariate++;
                    continue;
                }

                using var update = _connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE photos SET size = $size, last_write_utc = $lastWrite, capture_date = $captureDate
                    WHERE id = $id;
                    """;
                update.Parameters.AddWithValue("$size", foto.Identity.Size);
                update.Parameters.AddWithValue("$lastWrite", foto.Identity.LastWriteUtc.ToString("O"));
                update.Parameters.AddWithValue("$captureDate", (object?)foto.CaptureDate?.ToString("O") ?? DBNull.Value);
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
                    INSERT INTO photos (path_relativo, size, last_write_utc, capture_date)
                    VALUES ($path, $size, $lastWrite, $captureDate);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("$path", foto.Identity.PathRelativo);
                insert.Parameters.AddWithValue("$size", foto.Identity.Size);
                insert.Parameters.AddWithValue("$lastWrite", foto.Identity.LastWriteUtc.ToString("O"));
                insert.Parameters.AddWithValue("$captureDate", (object?)foto.CaptureDate?.ToString("O") ?? DBNull.Value);
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

        transaction.Commit();
        return new SyncResult(nuove, aggiornate, invariate);
    }

    public IReadOnlyList<PhotoRecord> GetAllPhotos()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.path_relativo, p.size, p.last_write_utc, p.capture_date,
                   s.stato, s.rating
            FROM photos p
            JOIN selection_state s ON s.photo_id = p.id
            ORDER BY p.path_relativo;
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
                Rating: reader.GetInt32(6)));
        }

        return risultato;
    }

    /// <summary>
    /// Aggiorna solo lo stato di selezione (None|Selected|Reject). "Reject" non cancella mai il file:
    /// è solo un marcatore in DB, la disposizione fisica passa da "Gestisci scarti…".
    /// </summary>
    public void UpdateStato(long photoId, string stato)
    {
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
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.path_relativo, p.size, p.last_write_utc, p.capture_date,
                   s.stato, s.rating
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
                Rating: reader.GetInt32(6)));
        }

        return risultato;
    }

    /// <summary>
    /// Rimuove la riga di un file dal DB (usato dopo che il file fisico è stato spostato/eliminato
    /// da "Gestisci scarti…" — non cancella mai il file da solo).
    /// </summary>
    public void RemovePhoto(long photoId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM photos WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Ricetta di editing salvata per la foto, o null se non è mai stata modificata.</summary>
    public string? GetRecipeJson(long photoId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT recipe_json FROM edits WHERE photo_id = $id;";
        cmd.Parameters.AddWithValue("$id", photoId);
        return cmd.ExecuteScalar() as string;
    }

    public void SaveRecipe(long photoId, string recipeJson, int schemaVersion)
    {
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

    public void Dispose() => _connection.Dispose();
}
