using WFastPic.Core.Library;
using WFastPic.Core.Metadata;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;
using Xunit;

namespace WFastPic.Core.Tests;

/// <summary>Logica Core usata dalla griglia: ordinamento, riapertura veloce, scarti, concorrenza, avanzamento.</summary>
public sealed class GrigliaSupportTests : IDisposable
{
    private readonly string _cartella = Path.Combine(Path.GetTempPath(), "wfastpic-test-" + Guid.NewGuid());

    public GrigliaSupportTests() => Directory.CreateDirectory(_cartella);

    public void Dispose()
    {
        try { Directory.Delete(_cartella, recursive: true); }
        catch (IOException) { }
    }

    private static ScannedPhoto Foto(string nome, DateTime? scatto) =>
        new(new PhotoIdentity(nome, 100, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), scatto);

    private void CreaFile(params string[] nomi)
    {
        foreach (var nome in nomi) File.WriteAllBytes(Path.Combine(_cartella, nome), [1, 2, 3]);
    }

    [Fact]
    public void GetAllPhotos_ordina_per_data_di_scatto_e_mette_in_fondo_le_foto_senza_data()
    {
        using var db = LibrarySidecarDatabase.OpenOrCreate(_cartella);
        db.SyncScan(
        [
            Foto("IMG_0001.HEIC", new DateTime(2026, 7, 2, 9, 0, 0)), // numerazione ripartita: scattata dopo
            Foto("IMG_9999.HEIC", new DateTime(2026, 7, 1, 18, 0, 0)),
            Foto("PXL_0001.jpg", new DateTime(2026, 7, 1, 18, 0, 5)), // secondo telefono, stessa raffica
            Foto("senza-data.jpg", null),
        ]);

        var ordine = db.GetAllPhotos().Select(p => p.PathRelativo).ToList();

        Assert.Equal(["IMG_9999.HEIC", "PXL_0001.jpg", "IMG_0001.HEIC", "senza-data.jpg"], ordine);
    }

    [Fact]
    public void RisolutoreMetadatiNoti_riusa_solo_le_foto_con_identita_invariata()
    {
        using var db = LibrarySidecarDatabase.OpenOrCreate(_cartella);
        var scatto = new DateTime(2026, 7, 1, 10, 0, 0);
        var originale = Foto("a.jpg", scatto) with { Gps = new GeoCoordinate(41.1, 16.8) };
        db.SyncScan([originale]);

        var risolutore = db.CreaRisolutoreMetadatiNoti();

        var nota = risolutore(originale.Identity);
        Assert.NotNull(nota);
        Assert.Equal(scatto, nota.CaptureDate);
        Assert.Equal(new GeoCoordinate(41.1, 16.8), nota.Gps);
        Assert.Null(risolutore(originale.Identity with { Size = 999 }));
        Assert.Null(risolutore(originale.Identity with { LastWriteUtc = DateTime.UtcNow }));
        Assert.Null(risolutore(new PhotoIdentity("nuova.jpg", 100, DateTime.UtcNow)));
    }

    [Fact]
    public void Riapertura_con_metadati_noti_non_modifica_il_sidecar_e_riporta_il_progresso()
    {
        CreaFile("a.jpg", "b.jpg", "note.txt");
        using var db = LibrarySidecarDatabase.OpenOrCreate(_cartella);
        db.SyncScan(FolderScanner.Scan(_cartella));

        var avanzamenti = new List<(int, int)>();
        var riscansione = FolderScanner.Scan(
            _cartella, db.CreaRisolutoreMetadatiNoti(), new ProgressoSincrono(avanzamenti.Add));
        var risultato = db.SyncScan(riscansione);

        Assert.Equal(new SyncResult(0, 0, 2), risultato);
        Assert.Equal([(0, 2), (1, 2), (2, 2)], avanzamenti);
    }

    [Fact]
    public void SpostaInScarti_non_sovrascrive_un_file_omonimo_gia_presente()
    {
        CreaFile("scarto.jpg");
        Directory.CreateDirectory(Path.Combine(_cartella, "Scarti"));
        File.WriteAllBytes(Path.Combine(_cartella, "Scarti", "scarto.jpg"), [9]);
        using var db = LibrarySidecarDatabase.OpenOrCreate(_cartella);
        db.SyncScan(FolderScanner.Scan(_cartella));

        var destinazione = new RejectManager(db).SpostaInScarti(db.GetAllPhotos().Single());

        Assert.Equal(Path.Combine(_cartella, "Scarti", "scarto (1).jpg"), destinazione);
        Assert.Equal([9], File.ReadAllBytes(Path.Combine(_cartella, "Scarti", "scarto.jpg")));
        Assert.True(File.Exists(destinazione));
        Assert.Empty(db.GetAllPhotos());
    }

    [Fact]
    public void DestinazioniLibere_salta_i_suffissi_gia_usati()
    {
        CreaFile("x.heic", "x (1).heic");

        Assert.Equal((Path.Combine(_cartella, "x (2).heic"), null), RejectManager.DestinazioniLibere(_cartella, "x.heic", null));
        Assert.Equal((Path.Combine(_cartella, "y.heic"), null), RejectManager.DestinazioniLibere(_cartella, "y.heic", null));
    }

    [Fact]
    public void DestinazioniLibere_tiene_foto_e_video_con_lo_stesso_nome_base()
    {
        // Il nome della foto è libero ma quello del video no: si passa a "(1)" per entrambi.
        CreaFile("IMG_1.MOV");

        var (foto, video) = RejectManager.DestinazioniLibere(_cartella, "IMG_1.HEIC", ".MOV");

        Assert.Equal(Path.Combine(_cartella, "IMG_1 (1).HEIC"), foto);
        Assert.Equal(Path.Combine(_cartella, "IMG_1 (1).MOV"), video);
    }

    [Fact]
    public void SpostaInScarti_porta_con_se_il_video_abbinato()
    {
        CreaFile("IMG_1.jpg", "IMG_1.MOV", "IMG_2.jpg", "IMG_2.mov");
        using var db = LibrarySidecarDatabase.OpenOrCreate(_cartella);
        db.SyncScan(FolderScanner.Scan(_cartella));
        var foto = db.GetAllPhotos().Single(p => p.PathRelativo == "IMG_1.jpg");

        new RejectManager(db).SpostaInScarti(foto);

        var scarti = Path.Combine(_cartella, "Scarti");
        Assert.True(File.Exists(Path.Combine(scarti, "IMG_1.jpg")));
        Assert.Equal(["IMG_1.MOV"], Directory.GetFiles(scarti, "*.MOV").Select(Path.GetFileName)); // maiuscole preservate
        Assert.False(File.Exists(Path.Combine(_cartella, "IMG_1.MOV")));
        Assert.True(File.Exists(Path.Combine(_cartella, "IMG_2.mov"))); // il video di un'altra foto non si tocca
    }

    [Fact]
    public void SpostaInScarti_con_video_bloccato_non_sposta_niente()
    {
        CreaFile("IMG_1.jpg", "IMG_1.mov");
        using var db = LibrarySidecarDatabase.OpenOrCreate(_cartella);
        db.SyncScan(FolderScanner.Scan(_cartella));
        var foto = db.GetAllPhotos().Single();

        using (File.Open(Path.Combine(_cartella, "IMG_1.mov"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => new RejectManager(db).SpostaInScarti(foto));
        }

        Assert.True(File.Exists(Path.Combine(_cartella, "IMG_1.jpg")));
        Assert.True(File.Exists(Path.Combine(_cartella, "IMG_1.mov")));
        Assert.Single(db.GetAllPhotos());
    }

    [Fact]
    public async Task SelectionManager_DisposeAsync_ripetuto_non_lancia_e_persiste_la_coda()
    {
        CreaFile("a.jpg");
        using var db = LibrarySidecarDatabase.OpenOrCreate(_cartella);
        db.SyncScan(FolderScanner.Scan(_cartella));
        var id = db.GetAllPhotos().Single().Id;

        var manager = new SelectionManager(db);
        manager.SetStato(id, StatoSelezione.Selected);
        await manager.DisposeAsync();
        await manager.DisposeAsync();

        Assert.Equal(StatoSelezione.Selected, db.GetAllPhotos().Single().Stato);
    }

    [Fact]
    public async Task Sidecar_regge_letture_e_scritture_da_piu_thread()
    {
        CreaFile(Enumerable.Range(0, 20).Select(i => $"f{i}.jpg").ToArray());
        using var db = LibrarySidecarDatabase.OpenOrCreate(_cartella);
        db.SyncScan(FolderScanner.Scan(_cartella));
        var ids = db.GetAllPhotos().Select(p => p.Id).ToList();

        // Simula coda di scrittura (background) + thread UI che legge scarti/ricette nello stesso momento.
        var scrittori = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var giro = 0; giro < 50; giro++)
                foreach (var id in ids) db.UpdateRating(id, (giro + t) % 6);
        }));
        var lettori = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var giro = 0; giro < 50; giro++)
            {
                db.GetByStato(StatoSelezione.Reject);
                db.GetRecipeJson(ids[giro % ids.Count]);
            }
        }));

        await Task.WhenAll(scrittori.Concat(lettori));
        Assert.Equal(20, db.GetAllPhotos().Count);
    }

    [Theory]
    [InlineData(0, 100, 10, null)]   // nessun dato
    [InlineData(4, 100, 10, null)]   // troppo pochi campioni
    [InlineData(100, 100, 10, null)] // finito
    [InlineData(50, 100, 10, 10.0)]  // metà in 10 s → altri 10 s
    [InlineData(25, 100, 5, 15.0)]
    public void StimaAvanzamento_TempoRimanente(int completate, int totale, int secondi, double? attesi)
    {
        var stima = StimaAvanzamento.TempoRimanente(completate, totale, TimeSpan.FromSeconds(secondi));
        Assert.Equal(attesi, stima?.TotalSeconds);
    }

    [Theory]
    [InlineData(0.2, "1 s")]
    [InlineData(59, "59 s")]
    [InlineData(61, "2 min")]
    [InlineData(180, "3 min")]
    public void StimaAvanzamento_Durata(double secondi, string atteso) =>
        Assert.Equal(atteso, StimaAvanzamento.Durata(TimeSpan.FromSeconds(secondi)));

    [Fact]
    public void StimaAvanzamento_Descrivi_usa_secondi_o_minuti()
    {
        Assert.Equal("3 di 100", StimaAvanzamento.Descrivi(3, 100, TimeSpan.FromSeconds(2)));
        Assert.Equal("340 di 1300 · circa 29 s rimanenti", StimaAvanzamento.Descrivi(340, 1300, TimeSpan.FromSeconds(10)));
        Assert.Equal("100 di 1300 · circa 4 min rimanenti", StimaAvanzamento.Descrivi(100, 1300, TimeSpan.FromSeconds(20)));
    }

    /// <summary>Progress&lt;T&gt; riporta in modo asincrono sul thread pool: nei test serve l'ordine esatto.</summary>
    private sealed class ProgressoSincrono(Action<(int, int)> azione) : IProgress<(int Elaborate, int Totale)>
    {
        public void Report((int Elaborate, int Totale) value) => azione(value);
    }
}
