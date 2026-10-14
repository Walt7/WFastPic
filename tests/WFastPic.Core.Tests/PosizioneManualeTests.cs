using WFastPic.Core.Library;
using WFastPic.Core.Metadata;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;
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
    public void Misure_di_qualita_restano_finche_il_file_e_il_calcolo_non_cambiano()
    {
        var (cartella, db, identita) = CreaDbConUnaFoto(null);
        try
        {
            var id = db.GetAllPhotos().Single().Id;
            Assert.Empty(db.GetQualita(1));

            db.SalvaQualita(id, 1, new QualitaSalvata(0.8, 0.7, 0.01, 0.02, 120));
            Assert.Equal(new QualitaSalvata(0.8, 0.7, 0.01, 0.02, 120), db.GetQualita(1)[id]);
            Assert.Empty(db.GetQualita(2)); // calcolo cambiato: si rimisura

            db.SyncScan([new ScannedPhoto(identita, null, null)]);
            Assert.Single(db.GetQualita(1));

            db.SyncScan([new ScannedPhoto(identita with { LastWriteUtc = identita.LastWriteUtc.AddMinutes(1) }, null, null)]);
            Assert.Empty(db.GetQualita(1));
        }
        finally
        {
            db.Dispose();
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public void Impronte_per_i_doppioni_salvate_a_64_bit_e_azzerate_se_il_file_cambia()
    {
        var (cartella, db, identita) = CreaDbConUnaFoto(null);
        try
        {
            var id = db.GetAllPhotos().Single().Id;
            const ulong impronta = 0xF0E1_D2C3_B4A5_9687; // bit alto acceso: in SQLite è un intero negativo

            db.SalvaImpronta(id, 1, impronta, 0.75);
            Assert.Equal((impronta, 0.75), db.GetImpronte(1)[id]);
            Assert.Empty(db.GetImpronte(2));

            db.SyncScan([new ScannedPhoto(identita with { Size = identita.Size + 1 }, null, null)]);
            Assert.Empty(db.GetImpronte(1));
        }
        finally
        {
            db.Dispose();
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public void Foto_di_un_altra_cartella_per_i_doppioni_in_sola_lettura()
    {
        var (cartella, db, _) = CreaDbConUnaFoto(null);
        var senzaSidecar = Directory.CreateTempSubdirectory("wfastpic-mai-aperta-").FullName;
        try
        {
            var id = db.GetAllPhotos().Single().Id;
            db.UpdateStato(id, StatoSelezione.Selected);
            db.SalvaImpronta(id, 1, 0xF0E1_D2C3_B4A5_9687, 0.75);

            var foto = Assert.Single(LibrarySidecarDatabase.LeggiPerDoppioni(cartella, 1));
            Assert.Equal((0xF0E1_D2C3_B4A5_9687UL, 0.75), (foto.Impronta, foto.Rapporto));
            Assert.Equal(StatoSelezione.Selected, foto.Stato);
            Assert.Null(Assert.Single(LibrarySidecarDatabase.LeggiPerDoppioni(cartella, 2)).Impronta);

            // Una cartella mai aperta resta intatta: niente sidecar creato.
            Assert.Empty(LibrarySidecarDatabase.LeggiPerDoppioni(senzaSidecar, 1));
            Assert.False(Directory.Exists(Path.Combine(senzaSidecar, LibrarySidecarDatabase.CartellaSidecar)));
        }
        finally
        {
            db.Dispose();
            Directory.Delete(cartella, recursive: true);
            Directory.Delete(senzaSidecar, recursive: true);
        }
    }

    [Fact]
    public void Importanza_delle_persone_nell_archivio()
    {
        var percorso = Path.Combine(Path.GetTempPath(), "wfastpic-persone-" + Guid.NewGuid() + ".db");
        try
        {
            using (var archivio = ArchivioPersone.Apri(percorso))
            {
                var anna = archivio.TrovaOCrea("Anna", null);
                var bruno = archivio.TrovaOCrea("Bruno", null);

                archivio.ImpostaCerchia(anna, CerchiaPersona.Io);
                archivio.ImpostaCerchia(bruno, CerchiaPersona.Io); // una sola persona è "io"
                archivio.ImpostaPreferita(anna, true);
            }

            using var riaperto = ArchivioPersone.Apri(percorso);
            var persone = riaperto.Elenco().ToDictionary(p => p.Nome);
            Assert.False(persone["Anna"].SonoIo);
            Assert.True(persone["Bruno"].SonoIo);
            Assert.True(persone["Anna"].Preferita);
            Assert.Equal(CerchiaPersona.Nessuna, persone["Anna"].Cerchia);

            riaperto.ImpostaCerchia(persone["Anna"].Id, CerchiaPersona.Amico);
            Assert.True(riaperto.Elenco().Single(p => p.Nome == "Bruno").SonoIo); // le altre cerchie non toccano "io"
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(percorso) + "*")) File.Delete(f);
        }
    }

    [Fact]
    public void Volti_salvati_restano_finche_il_file_non_cambia()
    {
        var (cartella, db, identita) = CreaDbConUnaFoto(null);
        try
        {
            var id = db.GetAllPhotos().Single().Id;
            Assert.Null(db.GetVolti(id));
            Assert.Null(db.GetAllPhotos().Single().NumeroVolti);

            db.SalvaVolti(id, [(0.1, 0.2, 0.3, 0.4), (0.5, 0.5, 0.6, 0.7)]);
            Assert.Equal(2, db.GetVolti(id)!.Count);
            Assert.Equal(2, db.GetAllPhotos().Single().NumeroVolti);

            // Riapertura senza modifiche: i volti restano.
            db.SyncScan([new ScannedPhoto(identita, null)]);
            Assert.Equal(2, db.GetAllPhotos().Single().NumeroVolti);

            // File modificato: va rianalizzato.
            db.SyncScan([new ScannedPhoto(identita with { LastWriteUtc = identita.LastWriteUtc.AddMinutes(5) }, null)]);
            Assert.Null(db.GetVolti(id));
            Assert.Null(db.GetAllPhotos().Single().NumeroVolti);

            // "Nessun volto" è un risultato, non "da analizzare".
            db.SalvaVolti(id, []);
            Assert.Equal(0, db.GetAllPhotos().Single().NumeroVolti);
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
