using Microsoft.Data.Sqlite;
using WFastPic.Core.Library;
using WFastPic.Core.Metadata;
using WFastPic.Core.Persistence;
using Xunit;

namespace WFastPic.Core.Tests;

public class MetadataAndGpsTests : IDisposable
{
    private readonly string _tempFolder;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public MetadataAndGpsTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        _tempFolder = Path.Combine(Path.GetTempPath(), "WFastPic_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    [Fact]
    public void PhotoMetadataReader_LeggeCampioniHeic()
    {
        var sampleFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tools/HeicSmokeTest/samples"));
        if (!Directory.Exists(sampleFolder)) return;
        var files = Directory.GetFiles(sampleFolder, "*.heic");
        int conGps = 0;
        foreach (var f in files)
        {
            var meta = PhotoMetadataReader.Read(f);
            if (meta.Gps.HasValue) conGps++;
            _output.WriteLine($"{Path.GetFileName(f)}: Date={meta.CaptureDate}, Gps={meta.Gps}");
        }
        _output.WriteLine($"Totale con GPS: {conGps}/{files.Length}");
        Assert.True(conGps > 0, $"Trovati {conGps}/{files.Length} con GPS");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void PhotoMetadataReader_RitornaNullPerFileNonEsistente()
    {
        var meta = PhotoMetadataReader.Read(Path.Combine(_tempFolder, "inesistente.jpg"));
        Assert.Null(meta.CaptureDate);
        Assert.Null(meta.Gps);
    }

    [Fact]
    public void LibraryDatabase_PersisteELeggeCoordinateGps()
    {
        using (var db = LibrarySidecarDatabase.OpenOrCreate(_tempFolder))
        {
            var scanned = new List<ScannedPhoto>
            {
                new(new PhotoIdentity("foto1.jpg", 1000, DateTime.UtcNow),
                    new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
                    new GeoCoordinate(40.8518, 14.2681)),
                new(new PhotoIdentity("foto2.jpg", 2000, DateTime.UtcNow),
                    null,
                    null),
            };

            var sync = db.SyncScan(scanned);
            Assert.Equal(2, sync.Nuove);

            var photos = db.GetAllPhotos();
            Assert.Equal(2, photos.Count);

            var p1 = photos.First(p => p.PathRelativo == "foto1.jpg");
            Assert.NotNull(p1.Latitude);
            Assert.NotNull(p1.Longitude);
            Assert.Equal(40.8518, p1.Latitude!.Value, 4);
            Assert.Equal(14.2681, p1.Longitude!.Value, 4);

            var p2 = photos.First(p => p.PathRelativo == "foto2.jpg");
            Assert.Null(p2.Latitude);
            Assert.Null(p2.Longitude);
        }
    }

    [Fact]
    public void LibraryDatabase_MigraAutomaticamenteDatabaseEsistenteSenzaColonneGps()
    {
        // Crea prima un database con il vecchio schema (senza colonne latitude/longitude)
        var cartellaSidecar = Path.Combine(_tempFolder, LibrarySidecarDatabase.CartellaSidecar);
        Directory.CreateDirectory(cartellaSidecar);
        var dbPath = Path.Combine(cartellaSidecar, LibrarySidecarDatabase.NomeFileDatabase);

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE photos (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path_relativo TEXT NOT NULL UNIQUE,
                    size INTEGER NOT NULL,
                    last_write_utc TEXT NOT NULL,
                    capture_date TEXT NULL
                );
                CREATE TABLE selection_state (
                    photo_id INTEGER PRIMARY KEY REFERENCES photos(id) ON DELETE CASCADE,
                    stato TEXT NOT NULL DEFAULT 'None',
                    rating INTEGER NOT NULL DEFAULT 0,
                    last_modified TEXT NOT NULL
                );
                CREATE TABLE edits (
                    photo_id INTEGER PRIMARY KEY REFERENCES photos(id) ON DELETE CASCADE,
                    recipe_json TEXT NOT NULL,
                    schema_version INTEGER NOT NULL
                );
                INSERT INTO photos (id, path_relativo, size, last_write_utc, capture_date)
                VALUES (1, 'vecchia.jpg', 500, '2026-08-01T10:00:00.0000000Z', NULL);
                INSERT INTO selection_state (photo_id, stato, rating, last_modified)
                VALUES (1, 'Selected', 5, '2026-08-01T10:00:00.0000000Z');
                """;
            cmd.ExecuteNonQuery();
        }

        // Ora apriamo con LibrarySidecarDatabase: deve eseguire la migrazione senza errori
        using (var db = LibrarySidecarDatabase.OpenOrCreate(_tempFolder))
        {
            var photos = db.GetAllPhotos();
            Assert.Single(photos);
            var p = photos[0];
            Assert.Equal("vecchia.jpg", p.PathRelativo);
            Assert.Equal("Selected", p.Stato);
            Assert.Equal(5, p.Rating);
            Assert.Null(p.Latitude);
            Assert.Null(p.Longitude);

            // Verifichiamo che possiamo aggiornare con GPS
            var scanned = new List<ScannedPhoto>
            {
                new(new PhotoIdentity("vecchia.jpg", 600, DateTime.UtcNow),
                    new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
                    new GeoCoordinate(41.9028, 12.4964))
            };
            var sync = db.SyncScan(scanned);
            Assert.Equal(1, sync.Aggiornate);

            var updatedPhotos = db.GetAllPhotos();
            Assert.Equal(41.9028, updatedPhotos[0].Latitude!.Value, 4);
            Assert.Equal(12.4964, updatedPhotos[0].Longitude!.Value, 4);
            // La selezione e il rating precedenti devono rimanere intatti!
            Assert.Equal("Selected", updatedPhotos[0].Stato);
            Assert.Equal(5, updatedPhotos[0].Rating);
        }
    }
}
