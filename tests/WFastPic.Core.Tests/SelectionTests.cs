using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;
using Xunit;

namespace WFastPic.Core.Tests;

public class SelectionTests
{
    private static string CreaCartellaTemporaneaConFoto(params string[] nomiFile)
    {
        var cartella = Path.Combine(Path.GetTempPath(), "wfastpic-test-" + Guid.NewGuid());
        Directory.CreateDirectory(cartella);
        foreach (var nome in nomiFile)
        {
            File.WriteAllBytes(Path.Combine(cartella, nome), [1, 2, 3]);
        }
        return cartella;
    }

    [Fact]
    public void SyncScan_poi_UpdateStato_persiste_e_si_rilegge_correttamente()
    {
        var cartella = CreaCartellaTemporaneaConFoto("a.jpg", "b.jpg");
        try
        {
            var scansione = FolderScanner.Scan(cartella);
            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(scansione);

            var tutte = db.GetAllPhotos();
            Assert.Equal(2, tutte.Count);
            Assert.All(tutte, p => Assert.Equal(StatoSelezione.None, p.Stato));

            var prima = tutte[0];
            db.UpdateStato(prima.Id, StatoSelezione.Reject);
            db.UpdateRating(prima.Id, 4);

            var rejects = db.GetByStato(StatoSelezione.Reject);
            Assert.Single(rejects);
            Assert.Equal(prima.Id, rejects[0].Id);
            Assert.Equal(4, rejects[0].Rating);
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public void Riscansione_senza_modifiche_non_tocca_le_righe_esistenti()
    {
        var cartella = CreaCartellaTemporaneaConFoto("a.jpg");
        try
        {
            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(FolderScanner.Scan(cartella));

            var risultato = db.SyncScan(FolderScanner.Scan(cartella));

            Assert.Equal(0, risultato.Nuove);
            Assert.Equal(0, risultato.Aggiornate);
            Assert.Equal(1, risultato.Invariate);
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public void Riscansione_rimuove_dal_sidecar_le_foto_che_non_esistono_piu()
    {
        var cartella = CreaCartellaTemporaneaConFoto("a.jpg", "b.jpg");
        try
        {
            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(FolderScanner.Scan(cartella));
            File.Delete(Path.Combine(cartella, "b.jpg"));

            db.SyncScan(FolderScanner.Scan(cartella));

            var rimasta = Assert.Single(db.GetAllPhotos());
            Assert.Equal("a.jpg", rimasta.PathRelativo);
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public void RejectManager_SpostaInScarti_muove_il_file_e_rimuove_la_riga_dal_db()
    {
        var cartella = CreaCartellaTemporaneaConFoto("scarto.jpg");
        try
        {
            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(FolderScanner.Scan(cartella));
            var foto = db.GetAllPhotos().Single();
            db.UpdateStato(foto.Id, StatoSelezione.Reject);

            var rejectManager = new RejectManager(db);
            var scarti = rejectManager.GetScarti();
            Assert.Single(scarti);

            rejectManager.SpostaInScarti(scarti[0]);

            Assert.False(File.Exists(Path.Combine(cartella, "scarto.jpg")));
            Assert.True(File.Exists(Path.Combine(cartella, "Scarti", "scarto.jpg")));
            Assert.Empty(db.GetAllPhotos());
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public void RejectManager_Lascia_riporta_lo_stato_a_None_senza_toccare_il_file()
    {
        var cartella = CreaCartellaTemporaneaConFoto("dubbio.jpg");
        try
        {
            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(FolderScanner.Scan(cartella));
            var foto = db.GetAllPhotos().Single();
            db.UpdateStato(foto.Id, StatoSelezione.Reject);

            var rejectManager = new RejectManager(db);
            rejectManager.Lascia(foto);

            Assert.True(File.Exists(Path.Combine(cartella, "dubbio.jpg")));
            var aggiornata = db.GetAllPhotos().Single();
            Assert.Equal(StatoSelezione.None, aggiornata.Stato);
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }
}
