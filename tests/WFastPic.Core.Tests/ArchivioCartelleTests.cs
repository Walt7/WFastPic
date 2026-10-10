using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;
using Xunit;

namespace WFastPic.Core.Tests;

public class ArchivioCartelleTests
{
    [Fact]
    public void Raggruppa_per_anno_dal_piu_recente_con_le_cartelle_senza_date_in_fondo()
    {
        var cartelle = new[]
        {
            new CartellaArchivio(@"E:\Foto\Natale", 40, new DateTime(2025, 12, 24), new DateTime(2025, 12, 26), true),
            new CartellaArchivio(@"E:\Foto\Puglia", 1300, new DateTime(2026, 7, 12), new DateTime(2026, 7, 20), true),
            new CartellaArchivio(@"E:\Foto\Nuova", null, null, null, true),
            new CartellaArchivio(@"E:\Foto\Pasqua", 80, new DateTime(2026, 4, 5), new DateTime(2026, 4, 6), true),
        };

        var gruppi = ArchivioCartelle.RaggruppaPerAnno(cartelle);

        Assert.Equal([2026, 2025, null], gruppi.Select(g => g.Anno));
        Assert.Equal(["Puglia", "Pasqua"], gruppi[0].Cartelle.Select(c => ArchivioCartelle.Nome(c.Percorso)));
        Assert.Equal("Senza data", ArchivioCartelle.TitoloAnno(gruppi[2].Anno));
    }

    [Theory]
    [InlineData(1300, 37, "1.300 foto · 37 selezionate")]
    [InlineData(1300, 1, "1.300 foto · 1 selezionata")]
    [InlineData(45, 0, "45 foto")]
    [InlineData(1, null, "1 foto")]
    [InlineData(2500, 1200, "2.500 foto · 1.200 selezionate")]
    public void Dettaglio_con_numero_di_foto_e_selezionate(int numero, int? selezionate, string atteso)
    {
        var cartella = new CartellaArchivio(@"E:\Foto\X", numero, null, null, true, selezionate);
        Assert.Equal(atteso, ArchivioCartelle.Dettaglio(cartella));
    }

    [Theory]
    [InlineData("2026-07-12", "2026-07-20", "12–20 lug 2026")]
    [InlineData("2026-06-30", "2026-07-02", "30 giu – 2 lug 2026")]
    [InlineData("2026-07-12", "2026-07-12", "12 lug 2026")]
    [InlineData("2025-12-30", "2026-01-02", "dic 2025 – gen 2026")]
    public void Periodo_di_scatto_sulla_seconda_riga(string primo, string ultimo, string atteso)
    {
        var cartella = new CartellaArchivio(@"E:\Foto\X", 10, DateTime.Parse(primo), DateTime.Parse(ultimo), true);
        Assert.Equal(atteso, ArchivioCartelle.PeriodoCartella(cartella));
    }
    [Fact]
    public void Dettaglio_per_cartelle_mai_aperte_o_sparite()
    {
        Assert.Equal("Non ancora aperta", ArchivioCartelle.Dettaglio(new CartellaArchivio(@"E:\X", null, null, null, true)));
        Assert.Equal("Cartella non trovata", ArchivioCartelle.Dettaglio(new CartellaArchivio(@"E:\X", 3, null, null, false)));
    }

    [Theory]
    [InlineData(".wfastpic", FileAttributes.Directory, true)]
    [InlineData("$RECYCLE.BIN", FileAttributes.Directory, true)]
    [InlineData("System Volume Information", FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System, true)]
    [InlineData("Puglia", FileAttributes.Directory, false)]
    [InlineData("Export", FileAttributes.Directory, false)]
    public void Sfogliando_si_nascondono_cartelle_di_sistema_e_sidecar(string nome, FileAttributes attributi, bool nascosta) =>
        Assert.Equal(nascosta, ArchivioCartelle.DaNascondere(nome, attributi));

    [Fact]
    public void Riepilogo_dal_sidecar_senza_mai_crearlo_nelle_cartelle_mai_aperte()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "wfastpic-riepilogo-" + Guid.NewGuid());
        Directory.CreateDirectory(cartella);
        try
        {
            Assert.Null(LibrarySidecarDatabase.LeggiRiepilogo(cartella));
            Assert.False(Directory.Exists(Path.Combine(cartella, LibrarySidecarDatabase.CartellaSidecar)));

            File.WriteAllBytes(Path.Combine(cartella, "a.jpg"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(cartella, "b.jpg"), [1, 2, 3]);
            var identita = FolderScanner.Scan(cartella).Select(s => s.Identity).ToList();
            using (var db = LibrarySidecarDatabase.OpenOrCreate(cartella))
            {
                db.SyncScan([
                    new ScannedPhoto(identita[0], new DateTime(2026, 7, 12, 9, 0, 0)),
                    new ScannedPhoto(identita[1], new DateTime(2026, 7, 20, 18, 0, 0)),
                ]);
                db.UpdateStato(db.GetAllPhotos()[0].Id, StatoSelezione.Selected);
            }

            var riepilogo = LibrarySidecarDatabase.LeggiRiepilogo(cartella);
            Assert.NotNull(riepilogo);
            Assert.Equal(2, riepilogo.NumeroFoto);
            Assert.Equal(1, riepilogo.NumeroSelezionate);
            Assert.Equal(new DateTime(2026, 7, 12, 9, 0, 0), riepilogo.PrimoScatto);
            Assert.Equal(new DateTime(2026, 7, 20, 18, 0, 0), riepilogo.UltimoScatto);
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }
}
