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
    double? Longitude = null,
    bool GpsManuale = false,
    double? Altitude = null,
    double? Direction = null,
    int? NumeroVolti = null,
    bool PersoneAnalizzate = false);

/// <summary>Una foto di un'altra cartella, letta in sola lettura per cercarvi i doppioni della cartella aperta.</summary>
public sealed record FotoAltraCartella(
    string PathRelativo,
    long Size,
    DateTime? CaptureDate,
    string Stato,
    int Rating,
    bool Modificata,
    ulong? Impronta,
    double? Rapporto);

public sealed record SyncResult(int Nuove, int Aggiornate, int Invariate);

/// <summary>Stato del nome su un volto: nessuno, proposto in automatico, confermato, "non è questa persona".</summary>
public static class StatoPersona
{
    public const int Nessuno = 0;
    public const int Proposto = 1;
    public const int Confermato = 2;
    public const int Rifiutato = 3;
}

/// <summary>Un volto di una cartella con impronta (128 float) e miniatura JPEG, per il riconoscimento persone.</summary>
public sealed record VoltoSalvato(
    long Id, long PhotoId,
    double X0, double Y0, double X1, double Y1,
    byte[]? Impronta, byte[]? Miniatura,
    long? PersonaId, int Stato);

/// <summary>Un volto pronto da salvare (riquadro normalizzato sulla foto non raddrizzata).</summary>
public sealed record VoltoDaSalvare(
    double X0, double Y0, double X1, double Y1, float Punteggio,
    byte[] Impronta, byte[] Miniatura, long? PersonaId, int Stato);

/// <summary>Misure tecniche salvate per il voto automatico (vedi MisuraQualita in Core.Processing).</summary>
public sealed record QualitaSalvata(double Nitidezza, double? NitidezzaVolti, double Bruciati, double Chiusi, double Luminosita);

/// <summary>Per il pannello cartelle: quante foto e in che periodo, dall'ultima apertura della cartella.</summary>
public sealed record RiepilogoCartella(int NumeroFoto, DateTime? PrimoScatto, DateTime? UltimoScatto, int NumeroSelezionate = 0);

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

    /// <summary>
    /// Riepilogo di una cartella già aperta in passato, in sola lettura: non crea mai il sidecar (una
    /// cartella mai aperta resta intatta) e non tocca lo schema. null se non c'è un sidecar leggibile.
    /// </summary>
    public static RiepilogoCartella? LeggiRiepilogo(string cartellaFoto)
    {
        var percorsoDb = Path.Combine(cartellaFoto, CartellaSidecar, NomeFileDatabase);
        if (!File.Exists(percorsoDb)) return null;

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = percorsoDb,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*), MIN(p.capture_date), MAX(p.capture_date),
                       (SELECT COUNT(*) FROM selection_state s WHERE s.stato = 'Selected')
                FROM photos p;
                """;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new RiepilogoCartella(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : DateTime.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
                reader.GetInt32(3));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or FormatException)
        {
            return null; // sidecar di un'altra versione, bloccato o su un disco scollegato
        }
    }

    /// <summary>
    /// Per i doppioni tra cartelle: le foto di un'altra cartella già aperta in passato, con le impronte
    /// calcolate con <paramref name="versioneImpronte"/> (null se mancano). Sola lettura: non crea mai il
    /// sidecar e non tocca lo schema; vuoto se manca o non è leggibile (disco scollegato, altra versione).
    /// </summary>
    public static IReadOnlyList<FotoAltraCartella> LeggiPerDoppioni(string cartellaFoto, int versioneImpronte)
    {
        var percorsoDb = Path.Combine(cartellaFoto, CartellaSidecar, NomeFileDatabase);
        var risultato = new List<FotoAltraCartella>();
        if (!File.Exists(percorsoDb)) return risultato;

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = percorsoDb,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();

            // Un sidecar scritto da una versione senza doppioni non ha la tabella: solo gli identici.
            bool conImpronte;
            using (var tabella = connection.CreateCommand())
            {
                tabella.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'impronte';";
                conImpronte = Convert.ToInt64(tabella.ExecuteScalar()) > 0;
            }

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT p.path_relativo, p.size, p.capture_date, s.stato, s.rating,
                       EXISTS (SELECT 1 FROM edits e WHERE e.photo_id = p.id),
                       {(conImpronte ? "i.impronta, i.rapporto" : "NULL, NULL")}
                FROM photos p
                JOIN selection_state s ON s.photo_id = p.id
                {(conImpronte ? "LEFT JOIN impronte i ON i.photo_id = p.id AND i.versione = $versione" : "")};
                """;
            cmd.Parameters.AddWithValue("$versione", versioneImpronte);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                risultato.Add(new FotoAltraCartella(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt64(5) != 0,
                    reader.IsDBNull(6) ? null : unchecked((ulong)reader.GetInt64(6)),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7)));
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or FormatException)
        {
            risultato.Clear(); // sidecar di un'altra versione, bloccato o su un disco scollegato
        }
        return risultato;
    }

    /// <summary>
    /// Per il pannello Persone (tutte le cartelle, come Picasa): in quante foto della cartella compare
    /// ogni persona con nome, proposte comprese. Sola lettura, non crea mai il sidecar; vuoto se manca,
    /// se è di una versione senza persone o se non è leggibile.
    /// </summary>
    public static IReadOnlyDictionary<long, int> LeggiFotoPerPersona(string cartellaFoto)
    {
        var percorsoDb = Path.Combine(cartellaFoto, CartellaSidecar, NomeFileDatabase);
        var risultato = new Dictionary<long, int>();
        if (!File.Exists(percorsoDb)) return risultato;

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = percorsoDb,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT persona_id, COUNT(DISTINCT photo_id) FROM volti
                WHERE persona_id IS NOT NULL AND stato_persona IN ($proposto, $confermato)
                GROUP BY persona_id;
                """;
            cmd.Parameters.AddWithValue("$proposto", StatoPersona.Proposto);
            cmd.Parameters.AddWithValue("$confermato", StatoPersona.Confermato);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) risultato[reader.GetInt64(0)] = reader.GetInt32(1);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            risultato.Clear(); // sidecar senza colonne persone, bloccato o su un disco scollegato
        }
        return risultato;
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
                longitude REAL NULL,
                gps_manuale INTEGER NOT NULL DEFAULT 0,
                altitude REAL NULL,
                direction REAL NULL,
                volti_cercati INTEGER NOT NULL DEFAULT 0
            );

            -- Volti trovati dal rilevatore di Windows, normalizzati (0..1) sulla foto non raddrizzata.
            CREATE TABLE IF NOT EXISTS volti (
                photo_id INTEGER NOT NULL REFERENCES photos(id) ON DELETE CASCADE,
                x0 REAL NOT NULL, y0 REAL NOT NULL, x1 REAL NOT NULL, y1 REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_volti_photo ON volti(photo_id);

            -- Nomi dei luoghi (geocodifica inversa) per cella di ~100 m: ogni punto si chiede una volta sola.
            CREATE TABLE IF NOT EXISTS luoghi (
                chiave TEXT PRIMARY KEY,
                nome TEXT NOT NULL
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

            -- Misure tecniche per il voto automatico (nitidezza, esposizione), calcolate in background.
            -- Il voto si ricava ogni volta da queste, relativo alla cartella; quello dell'utente resta in selection_state.
            CREATE TABLE IF NOT EXISTS qualita (
                photo_id INTEGER PRIMARY KEY REFERENCES photos(id) ON DELETE CASCADE,
                versione INTEGER NOT NULL,
                nitidezza REAL NOT NULL,
                nitidezza_volti REAL NULL,
                bruciati REAL NOT NULL,
                chiusi REAL NOT NULL,
                luminosita REAL NOT NULL
            );

            -- Impronta percettiva della miniatura (pHash, 64 bit) e proporzioni, per i doppioni quasi uguali.
            CREATE TABLE IF NOT EXISTS impronte (
                photo_id INTEGER PRIMARY KEY REFERENCES photos(id) ON DELETE CASCADE,
                versione INTEGER NOT NULL,
                impronta INTEGER NOT NULL,
                rapporto REAL NOT NULL
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

        if (!colonneEsistenti.Contains("volti_cercati"))
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE photos ADD COLUMN volti_cercati INTEGER NOT NULL DEFAULT 0;";
            alterCmd.ExecuteNonQuery();
        }

        // Riconoscimento persone: impronta, miniatura e persona (globale) per ogni volto.
        var colonneVolti = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var infoVolti = _connection.CreateCommand())
        {
            infoVolti.CommandText = "PRAGMA table_info(volti);";
            using var reader = infoVolti.ExecuteReader();
            while (reader.Read()) colonneVolti.Add(reader.GetString(1));
        }
        foreach (var (colonna, tipo) in new[] { ("impronta", "BLOB NULL"), ("miniatura", "BLOB NULL"), ("punteggio", "REAL NULL"), ("persona_id", "INTEGER NULL"), ("stato_persona", "INTEGER NOT NULL DEFAULT 0") })
        {
            if (colonneVolti.Contains(colonna)) continue;
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE volti ADD COLUMN {colonna} {tipo};";
            alterCmd.ExecuteNonQuery();
        }

        // Posizione corretta a mano dall'utente: né la scansione né "Riscansiona GPS" la sovrascrivono.
        if (!colonneEsistenti.Contains("gps_manuale"))
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE photos ADD COLUMN gps_manuale INTEGER NOT NULL DEFAULT 0;";
            alterCmd.ExecuteNonQuery();
        }

        if (!colonneEsistenti.Contains("altitude") || !colonneEsistenti.Contains("direction"))
        {
            // Le foto già geolocalizzate vanno rilette una volta per avere altitudine e direzione:
            // azzerando la dimensione salvata l'identità non coincide più e la prossima scansione
            // rilegge l'EXIF.
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText =
                (colonneEsistenti.Contains("altitude") ? "" : "ALTER TABLE photos ADD COLUMN altitude REAL NULL;")
                + (colonneEsistenti.Contains("direction") ? "" : "ALTER TABLE photos ADD COLUMN direction REAL NULL;")
                + "UPDATE photos SET size = -1 WHERE latitude IS NOT NULL AND gps_manuale = 0;";
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
            select.CommandText = "SELECT id, size, last_write_utc, latitude, longitude, gps_manuale, altitude, direction FROM photos WHERE path_relativo = $path;";
            select.Parameters.AddWithValue("$path", foto.Identity.PathRelativo);

            using var reader = select.ExecuteReader();
            if (reader.Read())
            {
                var id = reader.GetInt64(0);
                var sizeEsistente = reader.GetInt64(1);
                var lastWriteEsistente = DateTime.Parse(reader.GetString(2)).ToUniversalTime();
                double? latEsistente = reader.IsDBNull(3) ? null : reader.GetDouble(3);
                double? lonEsistente = reader.IsDBNull(4) ? null : reader.GetDouble(4);
                var gpsManuale = reader.GetInt64(5) != 0;
                double? altEsistente = reader.IsDBNull(6) ? null : reader.GetDouble(6);
                double? dirEsistente = reader.IsDBNull(7) ? null : reader.GetDouble(7);
                reader.Close();

                // Con posizione corretta a mano il GPS letto dal file non conta (né per il confronto
                // né per l'aggiornamento): resta quella scelta dall'utente.
                var invariato = sizeEsistente == foto.Identity.Size
                    && lastWriteEsistente == foto.Identity.LastWriteUtc
                    && (gpsManuale || (latEsistente == foto.Gps?.Latitude && lonEsistente == foto.Gps?.Longitude
                        && altEsistente == foto.Gps?.Altitude && dirEsistente == foto.Gps?.Direction));

                if (invariato)
                {
                    invariate++;
                    continue;
                }

                using var update = _connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE photos SET size = $size, last_write_utc = $lastWrite, capture_date = $captureDate,
                                      latitude = CASE WHEN gps_manuale = 1 THEN latitude ELSE $lat END,
                                      longitude = CASE WHEN gps_manuale = 1 THEN longitude ELSE $lon END,
                                      altitude = CASE WHEN gps_manuale = 1 THEN altitude ELSE $alt END,
                                      direction = CASE WHEN gps_manuale = 1 THEN direction ELSE $dir END
                    WHERE id = $id;
                    -- Contenuto del file cambiato: i volti trovati prima non valgono più.
                    UPDATE photos SET volti_cercati = 0 WHERE id = $id AND $fileCambiato = 1;
                    DELETE FROM volti WHERE photo_id = $id AND $fileCambiato = 1;
                    DELETE FROM qualita WHERE photo_id = $id AND $fileCambiato = 1;
                    DELETE FROM impronte WHERE photo_id = $id AND $fileCambiato = 1;
                    """;
                update.Parameters.AddWithValue("$alt", (object?)foto.Gps?.Altitude ?? DBNull.Value);
                update.Parameters.AddWithValue("$fileCambiato",
                    sizeEsistente != foto.Identity.Size || lastWriteEsistente != foto.Identity.LastWriteUtc ? 1 : 0);
                update.Parameters.AddWithValue("$dir", (object?)foto.Gps?.Direction ?? DBNull.Value);
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
                    INSERT INTO photos (path_relativo, size, last_write_utc, capture_date, latitude, longitude, altitude, direction)
                    VALUES ($path, $size, $lastWrite, $captureDate, $lat, $lon, $alt, $dir);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("$alt", (object?)foto.Gps?.Altitude ?? DBNull.Value);
                insert.Parameters.AddWithValue("$dir", (object?)foto.Gps?.Direction ?? DBNull.Value);
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
            cmd.CommandText = "SELECT path_relativo, size, last_write_utc, capture_date, latitude, longitude, altitude, direction FROM photos;";
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
                    : new GeoCoordinate(reader.GetDouble(4), reader.GetDouble(5),
                        reader.IsDBNull(6) ? null : reader.GetDouble(6), reader.IsDBNull(7) ? null : reader.GetDouble(7));
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
                   s.stato, s.rating, p.latitude, p.longitude, p.gps_manuale, p.altitude, p.direction,
                   CASE WHEN p.volti_cercati >= 1 THEN (SELECT COUNT(*) FROM volti v WHERE v.photo_id = p.id) END,
                   p.volti_cercati
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
                Longitude: reader.IsDBNull(8) ? null : reader.GetDouble(8),
                GpsManuale: reader.GetInt64(9) != 0,
                Altitude: reader.IsDBNull(10) ? null : reader.GetDouble(10),
                Direction: reader.IsDBNull(11) ? null : reader.GetDouble(11),
                NumeroVolti: reader.IsDBNull(12) ? null : reader.GetInt32(12),
                PersoneAnalizzate: reader.GetInt64(13) >= VersioneAnalisiPersone));
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
                   s.stato, s.rating, p.latitude, p.longitude, p.gps_manuale, p.altitude, p.direction,
                   CASE WHEN p.volti_cercati >= 1 THEN (SELECT COUNT(*) FROM volti v WHERE v.photo_id = p.id) END,
                   p.volti_cercati
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
                Longitude: reader.IsDBNull(8) ? null : reader.GetDouble(8),
                GpsManuale: reader.GetInt64(9) != 0,
                Altitude: reader.IsDBNull(10) ? null : reader.GetDouble(10),
                Direction: reader.IsDBNull(11) ? null : reader.GetDouble(11),
                NumeroVolti: reader.IsDBNull(12) ? null : reader.GetInt32(12),
                PersoneAnalizzate: reader.GetInt64(13) >= VersioneAnalisiPersone));
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

    /// <summary>Tutte le ricette della cartella in una query: per i simboli delle modifiche sulle miniature.</summary>
    public IReadOnlyDictionary<long, string> GetAllRecipeJson()
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT photo_id, recipe_json FROM edits;";
        using var reader = cmd.ExecuteReader();
        var ricette = new Dictionary<long, string>();
        while (reader.Read()) ricette[reader.GetInt64(0)] = reader.GetString(1);
        return ricette;
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

    /// <summary>"Riscansiona GPS": aggiorna le posizioni lette dai file, mai quelle corrette a mano.</summary>
    public void UpdateGpsBatch(IEnumerable<(long Id, GeoCoordinate? Gps)> updates)
    {
        using var scope = _lock.EnterScope();
        using var transaction = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            UPDATE photos SET latitude = $lat, longitude = $lon, altitude = $alt, direction = $dir
            WHERE id = $id AND gps_manuale = 0;
            """;
        var pLat = cmd.Parameters.Add("$lat", SqliteType.Real);
        var pLon = cmd.Parameters.Add("$lon", SqliteType.Real);
        var pAlt = cmd.Parameters.Add("$alt", SqliteType.Real);
        var pDir = cmd.Parameters.Add("$dir", SqliteType.Real);
        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);

        foreach (var (id, gps) in updates)
        {
            pLat.Value = (object?)gps?.Latitude ?? DBNull.Value;
            pLon.Value = (object?)gps?.Longitude ?? DBNull.Value;
            pAlt.Value = (object?)gps?.Altitude ?? DBNull.Value;
            pDir.Value = (object?)gps?.Direction ?? DBNull.Value;
            pId.Value = id;
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Posizione scelta dall'utente nell'editor (come "Luoghi" di Picasa): solo nel sidecar, il file
    /// originale non viene toccato. null/null = "nessuna posizione" voluta (es. foto di casa).
    /// Altitudine e direzione del file non valgono più per il punto spostato: diventano sconosciute.
    /// </summary>
    public void SetPosizioneManuale(long photoId, double? latitude, double? longitude)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE photos SET latitude = $lat, longitude = $lon, altitude = NULL, direction = NULL, gps_manuale = 1
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$lat", (object?)latitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lon", (object?)longitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Torna alla posizione letta dal file (quella passata dal chiamante, già riletta dall'EXIF).</summary>
    public void RipristinaPosizioneOriginale(long photoId, GeoCoordinate? gpsDelFile)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE photos SET latitude = $lat, longitude = $lon, altitude = $alt, direction = $dir, gps_manuale = 0
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$lat", (object?)gpsDelFile?.Latitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lon", (object?)gpsDelFile?.Longitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$alt", (object?)gpsDelFile?.Altitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dir", (object?)gpsDelFile?.Direction ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>volti_cercati: 1 = solo rilevamento (Windows), 2 = volti con impronta per riconoscere le persone.</summary>
    public const int VersioneAnalisiPersone = 2;

    /// <summary>Salva i volti con impronta di una foto (sostituendo quelli del solo rilevamento).</summary>
    public void SalvaVoltiPersone(long photoId, IReadOnlyList<VoltoDaSalvare> volti)
    {
        using var scope = _lock.EnterScope();
        using var transaction = _connection.BeginTransaction();

        using (var cancella = _connection.CreateCommand())
        {
            cancella.Transaction = transaction;
            cancella.CommandText = "DELETE FROM volti WHERE photo_id = $id; UPDATE photos SET volti_cercati = $versione WHERE id = $id;";
            cancella.Parameters.AddWithValue("$id", photoId);
            cancella.Parameters.AddWithValue("$versione", VersioneAnalisiPersone);
            cancella.ExecuteNonQuery();
        }

        foreach (var v in volti)
        {
            using var inserisci = _connection.CreateCommand();
            inserisci.Transaction = transaction;
            inserisci.CommandText = """
                INSERT INTO volti (photo_id, x0, y0, x1, y1, punteggio, impronta, miniatura, persona_id, stato_persona)
                VALUES ($id, $x0, $y0, $x1, $y1, $punteggio, $impronta, $miniatura, $persona, $stato);
                """;
            inserisci.Parameters.AddWithValue("$id", photoId);
            inserisci.Parameters.AddWithValue("$x0", v.X0);
            inserisci.Parameters.AddWithValue("$y0", v.Y0);
            inserisci.Parameters.AddWithValue("$x1", v.X1);
            inserisci.Parameters.AddWithValue("$y1", v.Y1);
            inserisci.Parameters.AddWithValue("$punteggio", v.Punteggio);
            inserisci.Parameters.AddWithValue("$impronta", v.Impronta);
            inserisci.Parameters.AddWithValue("$miniatura", v.Miniatura);
            inserisci.Parameters.AddWithValue("$persona", (object?)v.PersonaId ?? DBNull.Value);
            inserisci.Parameters.AddWithValue("$stato", v.Stato);
            inserisci.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Tutti i volti con impronta della cartella (per raggruppare e riconoscere).</summary>
    public IReadOnlyList<VoltoSalvato> GetVoltiPersone()
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT rowid, photo_id, x0, y0, x1, y1, impronta, miniatura, persona_id, stato_persona
            FROM volti WHERE impronta IS NOT NULL;
            """;
        using var reader = cmd.ExecuteReader();
        var volti = new List<VoltoSalvato>();
        while (reader.Read())
        {
            volti.Add(new VoltoSalvato(
                reader.GetInt64(0), reader.GetInt64(1),
                reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5),
                reader.IsDBNull(6) ? null : (byte[])reader[6],
                reader.IsDBNull(7) ? null : (byte[])reader[7],
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.GetInt32(9)));
        }
        return volti;
    }

    /// <summary>Volti con impronta di una sola foto (sezione Volti nei dettagli del viewer).</summary>
    public IReadOnlyList<VoltoSalvato> GetVoltiPersone(long photoId)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT rowid, photo_id, x0, y0, x1, y1, impronta, miniatura, persona_id, stato_persona
            FROM volti WHERE impronta IS NOT NULL AND photo_id = $id ORDER BY x0;
            """;
        cmd.Parameters.AddWithValue("$id", photoId);
        using var reader = cmd.ExecuteReader();
        var volti = new List<VoltoSalvato>();
        while (reader.Read())
        {
            volti.Add(new VoltoSalvato(
                reader.GetInt64(0), reader.GetInt64(1),
                reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5),
                reader.IsDBNull(6) ? null : (byte[])reader[6],
                reader.IsDBNull(7) ? null : (byte[])reader[7],
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.GetInt32(9)));
        }
        return volti;
    }

    /// <summary>
    /// "Rianalizza le persone": dimentica volti e nomi della cartella, la prossima ricerca li ricalcola.
    /// Le persone (archivio globale) restano: i loro volti verranno riproposti in automatico.
    /// </summary>
    public void AzzeraAnalisiVolti()
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM volti; UPDATE photos SET volti_cercati = 0;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Dà (o toglie) un nome a dei volti: proposto, confermato o rifiutato.</summary>
    public void AssegnaPersona(IEnumerable<long> idVolti, long? personaId, int stato)
    {
        using var scope = _lock.EnterScope();
        using var transaction = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE volti SET persona_id = $persona, stato_persona = $stato WHERE rowid = $id;";
        var pPersona = cmd.Parameters.Add("$persona", SqliteType.Integer);
        var pStato = cmd.Parameters.Add("$stato", SqliteType.Integer);
        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        foreach (var id in idVolti)
        {
            pPersona.Value = (object?)personaId ?? DBNull.Value;
            pStato.Value = stato;
            pId.Value = id;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>
    /// Persona unita a un'altra (<paramref name="nuova"/>) o eliminata (null: i volti tornano senza nome)
    /// nell'archivio globale: allinea i volti di questa cartella.
    /// </summary>
    public void SostituisciPersona(long vecchia, long? nuova)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE volti SET persona_id = $nuova,
                             stato_persona = CASE WHEN $nuova IS NULL THEN $nessuno ELSE stato_persona END
            WHERE persona_id = $vecchia;
            """;
        cmd.Parameters.AddWithValue("$nuova", (object?)nuova ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$nessuno", StatoPersona.Nessuno);
        cmd.Parameters.AddWithValue("$vecchia", vecchia);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Volti trovati nella foto (normalizzati, foto non raddrizzata); null se non è ancora stata analizzata.</summary>
    public IReadOnlyList<(double X0, double Y0, double X1, double Y1)>? GetVolti(long photoId)
    {
        using var scope = _lock.EnterScope();
        using var cercati = _connection.CreateCommand();
        cercati.CommandText = "SELECT volti_cercati FROM photos WHERE id = $id;";
        cercati.Parameters.AddWithValue("$id", photoId);
        if (cercati.ExecuteScalar() is not long analizzata || analizzata == 0) return null;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT x0, y0, x1, y1 FROM volti WHERE photo_id = $id;";
        cmd.Parameters.AddWithValue("$id", photoId);
        using var reader = cmd.ExecuteReader();
        var volti = new List<(double, double, double, double)>();
        while (reader.Read()) volti.Add((reader.GetDouble(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3)));
        return volti;
    }

    /// <summary>Salva il risultato dell'analisi (anche "nessun volto"): la foto non verrà riesaminata.</summary>
    public void SalvaVolti(long photoId, IReadOnlyList<(double X0, double Y0, double X1, double Y1)> volti)
    {
        using var scope = _lock.EnterScope();
        using var transaction = _connection.BeginTransaction();

        using (var cancella = _connection.CreateCommand())
        {
            cancella.Transaction = transaction;
            cancella.CommandText = "DELETE FROM volti WHERE photo_id = $id; UPDATE photos SET volti_cercati = 1 WHERE id = $id;";
            cancella.Parameters.AddWithValue("$id", photoId);
            cancella.ExecuteNonQuery();
        }

        using var inserisci = _connection.CreateCommand();
        inserisci.Transaction = transaction;
        inserisci.CommandText = "INSERT INTO volti (photo_id, x0, y0, x1, y1) VALUES ($id, $x0, $y0, $x1, $y1);";
        var pId = inserisci.Parameters.Add("$id", SqliteType.Integer);
        var px0 = inserisci.Parameters.Add("$x0", SqliteType.Real);
        var py0 = inserisci.Parameters.Add("$y0", SqliteType.Real);
        var px1 = inserisci.Parameters.Add("$x1", SqliteType.Real);
        var py1 = inserisci.Parameters.Add("$y1", SqliteType.Real);
        foreach (var (x0, y0, x1, y1) in volti)
        {
            pId.Value = photoId; px0.Value = x0; py0.Value = y0; px1.Value = x1; py1.Value = y1;
            inserisci.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Misure per il voto automatico, solo quelle calcolate con <paramref name="versione"/>: cambiando il
    /// calcolo le vecchie vengono ignorate e la foto si rimisura.
    /// </summary>
    public IReadOnlyDictionary<long, QualitaSalvata> GetQualita(int versione)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT photo_id, nitidezza, nitidezza_volti, bruciati, chiusi, luminosita
            FROM qualita WHERE versione = $versione;
            """;
        cmd.Parameters.AddWithValue("$versione", versione);
        using var reader = cmd.ExecuteReader();
        var misure = new Dictionary<long, QualitaSalvata>();
        while (reader.Read())
        {
            misure[reader.GetInt64(0)] = new QualitaSalvata(
                reader.GetDouble(1), reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5));
        }
        return misure;
    }

    public void SalvaQualita(long photoId, int versione, QualitaSalvata q)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO qualita (photo_id, versione, nitidezza, nitidezza_volti, bruciati, chiusi, luminosita)
            VALUES ($id, $versione, $nitidezza, $volti, $bruciati, $chiusi, $luminosita)
            ON CONFLICT(photo_id) DO UPDATE SET versione = excluded.versione, nitidezza = excluded.nitidezza,
                nitidezza_volti = excluded.nitidezza_volti, bruciati = excluded.bruciati,
                chiusi = excluded.chiusi, luminosita = excluded.luminosita;
            """;
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.Parameters.AddWithValue("$versione", versione);
        cmd.Parameters.AddWithValue("$nitidezza", q.Nitidezza);
        cmd.Parameters.AddWithValue("$volti", (object?)q.NitidezzaVolti ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bruciati", q.Bruciati);
        cmd.Parameters.AddWithValue("$chiusi", q.Chiusi);
        cmd.Parameters.AddWithValue("$luminosita", q.Luminosita);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Impronte per i doppioni, solo quelle calcolate con <paramref name="versione"/>.</summary>
    public IReadOnlyDictionary<long, (ulong Impronta, double Rapporto)> GetImpronte(int versione)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT photo_id, impronta, rapporto FROM impronte WHERE versione = $versione;";
        cmd.Parameters.AddWithValue("$versione", versione);
        using var reader = cmd.ExecuteReader();
        var impronte = new Dictionary<long, (ulong, double)>();
        while (reader.Read())
        {
            impronte[reader.GetInt64(0)] = (unchecked((ulong)reader.GetInt64(1)), reader.GetDouble(2));
        }
        return impronte;
    }

    public void SalvaImpronta(long photoId, int versione, ulong impronta, double rapporto)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO impronte (photo_id, versione, impronta, rapporto) VALUES ($id, $versione, $impronta, $rapporto)
            ON CONFLICT(photo_id) DO UPDATE SET versione = excluded.versione, impronta = excluded.impronta,
                rapporto = excluded.rapporto;
            """;
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.Parameters.AddWithValue("$versione", versione);
        // SQLite ha solo interi con segno: i 64 bit si salvano così come sono.
        cmd.Parameters.AddWithValue("$impronta", unchecked((long)impronta));
        cmd.Parameters.AddWithValue("$rapporto", rapporto);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Nome del luogo già risolto per questa cella (vedi NomeLuogo.Chiave), o null.</summary>
    public string? GetNomeLuogo(string chiave)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT nome FROM luoghi WHERE chiave = $chiave;";
        cmd.Parameters.AddWithValue("$chiave", chiave);
        return cmd.ExecuteScalar() as string;
    }

    public void SetNomeLuogo(string chiave, string nome)
    {
        using var scope = _lock.EnterScope();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO luoghi (chiave, nome) VALUES ($chiave, $nome)
            ON CONFLICT(chiave) DO UPDATE SET nome = excluded.nome;
            """;
        cmd.Parameters.AddWithValue("$chiave", chiave);
        cmd.Parameters.AddWithValue("$nome", nome);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        using var scope = _lock.EnterScope();
        _connection.Dispose();
    }
}
