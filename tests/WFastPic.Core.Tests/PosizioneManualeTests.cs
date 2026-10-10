using WFastPic.Core.Library;
using WFastPic.Core.Metadata;
using WFastPic.Core.Persistence;
using Xunit;

namespace WFastPic.Core.Tests;

public class PosizioneManualeTests
{
    private static (string Cartella, LibrarySidecarDatabase Db, PhotoIdentity Identita) CreaDbConUnaFoto(GeoCoordinate? gpsExif)
    {
        var cartella = Path.Combine(Path.GetTempPath(), "wfastpic-gps-" + Guid.NewGuid());
        Directory.CreateDirectory(cartella);
        File.WriteAllBytes(Path.Combine(cartella, "a.jpg"), [1, 2, 3]);
        var identita = FolderScanner.Scan(cartella).Single().Identity;
        var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
        db.SyncScan([new ScannedPhoto(identita, null, gpsExif)]);
        return (cartella, db, identita);
    }

    [Fact]
    public void Posizione_corretta_a_mano_sopravvive_a_riscansione_e_rilettura_EXIF()
    {
        var (cartella, db, identita) = CreaDbConUnaFoto(new GeoCoordinate(40.0, 17.0));
        try
        {
            var id = db.GetAllPhotos().Single().Id;
            db.SetPosizioneManuale(id, 41.5, 16.5);

            // File modificato (nuova data) con il GPS originale nell'EXIF + "Riscansiona GPS".
            var modificata = identita with { LastWriteUtc = identita.LastWriteUtc.AddMinutes(1) };
            db.SyncScan([new ScannedPhoto(modificata, null, new GeoCoordinate(40.0, 17.0))]);
            db.UpdateGpsBatch([(id, new GeoCoordinate(40.0, 17.0))]);

            var foto = db.GetAllPhotos().Single();
            Assert.True(foto.GpsManuale);
            Assert.Equal((41.5, 16.5), (foto.Latitude, foto.Longitude));
        }
        finally
        {
            db.Dispose();
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public void Rimuovere_la_posizione_e_una_scelta_che_resta()
    {
        var (cartella, db, identita) = CreaDbConUnaFoto(new GeoCoordinate(40.0, 17.0));
        try
        {
            var id = db.GetAllPhotos().Single().Id;
            db.SetPosizioneManuale(id, null, null);
            db.SyncScan([new ScannedPhoto(identita, null, new GeoCoordinate(40.0, 17.0))]);

            var foto = db.GetAllPhotos().Single();
            Assert.True(foto.GpsManuale);
            Assert.Null(foto.Latitude);
        }
        finally
        {
            db.Dispose();
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public void Ripristino_torna_alla_posizione_del_file_e_alle_riscansioni()
    {
        var (cartella, db, _) = CreaDbConUnaFoto(new GeoCoordinate(40.0, 17.0));
        try
        {
            var id = db.GetAllPhotos().Single().Id;
            db.SetPosizioneManuale(id, 41.5, 16.5);

            db.RipristinaPosizioneOriginale(id, new GeoCoordinate(40.0, 17.0));
            db.UpdateGpsBatch([(id, new GeoCoordinate(40.1, 17.1, 5.0, 90.0))]);

            var foto = db.GetAllPhotos().Single();
            Assert.False(foto.GpsManuale);
            Assert.Equal((40.1, 17.1), (foto.Latitude, foto.Longitude));
            Assert.Equal((5.0, 90.0), (foto.Altitude, foto.Direction));
        }
        finally
        {
            db.Dispose();
            Directory.Delete(cartella, recursive: true);
        }
    }
}
