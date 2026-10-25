using System.Text.Json;
using WFastPic.Core.Export;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;
using WFastPic.Mcp;
using Xunit;

namespace WFastPic.Core.Tests;

/// <summary>
/// Casi nati con la scansione ricorsiva (cartelle madre): sottocartelle illeggibili, rinomine che cambiano solo
/// le maiuscole, cartelle di export, import dei sidecar figli, ricerca e pacchetti del server MCP, selezioni.
/// </summary>
public class RevisioneCartelleMadreTests : IDisposable
{
    private readonly string _radice = Path.Combine(Path.GetTempPath(), "wfastpic-rev-" + Guid.NewGuid());
    private readonly string? _datiPrima = Environment.GetEnvironmentVariable("WFASTPIC_DATA");
    private readonly string _dati;

    public RevisioneCartelleMadreTests()
    {
        _dati = Path.Combine(_radice, "dati");
        Directory.CreateDirectory(_dati);
        Environment.SetEnvironmentVariable("WFASTPIC_DATA", _dati);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("WFASTPIC_DATA", _datiPrima);
        try { Directory.Delete(_radice, recursive: true); } catch (IOException) { }
    }

    private string Cartella(params string[] parti)
    {
        var percorso = Path.Combine([_radice, .. parti]);
        Directory.CreateDirectory(percorso);
        return percorso;
    }

    private static void Foto(string cartella, string nome, params byte[] contenuto) =>
        File.WriteAllBytes(Path.Combine(cartella, nome), contenuto.Length > 0 ? contenuto : [1, 2, 3]);

    [Fact]
    public void Sottocartella_non_letta_non_cancella_le_sue_foto_dal_sidecar()
    {
        var madre = Cartella("Madre");
        Foto(Cartella("Madre", "Giorno 2"), "IMG_1.jpg");
        Foto(madre, "IMG_0.jpg");

        using var db = LibrarySidecarDatabase.OpenOrCreate(madre);
        db.SyncScan(FolderScanner.Scan(madre));
        var giorno2 = db.GetAllPhotos().Single(f => f.PathRelativo.StartsWith("Giorno 2"));
        db.UpdateRating(giorno2.Id, 4);

        // Seconda apertura: "Giorno 2" non si legge (permessi, rete). Le sue foto restano, voto compreso.
        var senzaGiorno2 = FolderScanner.Scan(madre).Where(f => !f.Identity.PathRelativo.StartsWith("Giorno 2")).ToList();
        db.SyncScan(senzaGiorno2, ["Giorno 2"]);

        var rimasta = db.GetAllPhotos().Single(f => f.Id == giorno2.Id);
        Assert.Equal(4, rimasta.Rating);
    }

    [Fact]
    public void Rinomina_solo_maiuscole_tiene_la_stessa_riga()
    {
        var madre = Cartella("Madre");
        Foto(Cartella("Madre", "giorno 2"), "IMG_1.jpg");

        using var db = LibrarySidecarDatabase.OpenOrCreate(madre);
        db.SyncScan(FolderScanner.Scan(madre));
        var prima = db.GetAllPhotos().Single();
        db.UpdateRating(prima.Id, 5);

        Directory.Move(Path.Combine(madre, "giorno 2"), Path.Combine(madre, "tmp"));
        Directory.Move(Path.Combine(madre, "tmp"), Path.Combine(madre, "Giorno 2"));
        db.SyncScan(FolderScanner.Scan(madre));

        var dopo = Assert.Single(db.GetAllPhotos());
        Assert.Equal(prima.Id, dopo.Id);
        Assert.Equal(5, dopo.Rating);
        Assert.Equal(Path.Combine("Giorno 2", "IMG_1.jpg"), dopo.PathRelativo);
    }

    [Fact]
    public void Cartella_di_export_segnata_non_viene_scansionata_ma_una_con_foto_non_si_segna()
    {
        var madre = Cartella("Madre");
        Foto(madre, "IMG_0.jpg");
        var perInstagram = Cartella("Madre", "Per Instagram");
        var conOriginali = Cartella("Madre", "Giorno 1");
        Foto(conOriginali, "IMG_1.jpg");

        FolderScanner.SegnaCartellaExport(perInstagram);
        FolderScanner.SegnaCartellaExport(conOriginali);
        Foto(perInstagram, "IMG_0.jpg"); // copia esportata

        var percorsi = FolderScanner.Scan(madre).Select(f => f.Identity.PathRelativo).Order().ToList();
        Assert.Equal([Path.Combine("Giorno 1", "IMG_1.jpg"), "IMG_0.jpg"], percorsi);
        Assert.False(File.Exists(Path.Combine(conOriginali, FolderScanner.FileSegnoExport)));
    }

    [Fact]
    public void Export_non_sostituisce_mai_dove_ci_possono_essere_originali()
    {
        var madre = Cartella("Madre");
        var giorno = Cartella("Madre", "Giorno 2");
        var export = Cartella("Madre", "Export");
        var segnata = Cartella("Madre", "Per Instagram");
        FolderScanner.SegnaCartellaExport(segnata);

        Assert.True(NomiFileExport.ContieneOriginali(giorno, madre));
        Assert.True(NomiFileExport.ContieneOriginali(madre, madre));
        Assert.False(NomiFileExport.ContieneOriginali(export, madre));
        Assert.False(NomiFileExport.ContieneOriginali(segnata, madre));

        // Un'altra cartella catalogata (con sidecar), anche se non è quella aperta.
        var altra = Cartella("Altra");
        using (LibrarySidecarDatabase.OpenOrCreate(altra)) { }
        Assert.True(NomiFileExport.ContieneOriginali(Cartella("Altra", "Sotto"), madre));

        var percorso = NomiFileExport.PercorsoOutput(giorno, madre, "IMG_1", sostituisciEsistenti: true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), p => p.EndsWith("IMG_1.jpg"));
        Assert.Equal(Path.Combine(giorno, "IMG_1_2.jpg"), percorso);
    }

    [Theory]
    [InlineData(41.0 + 13.0 / 60)]          // minuti quasi interi: niente 41°12'60"
    [InlineData(12.999999999)]
    [InlineData(-73.98765)]
    public void Gps_dell_export_ha_secondi_sotto_i_60(double valore)
    {
        var dms = JpegEncoder.GradiMinutiSecondi(valore);
        double Parte(ulong r) => (uint)r / (double)(uint)(r >> 32);
        var (gradi, minuti, secondi) = (Parte(dms[0]), Parte(dms[1]), Parte(dms[2]));

        Assert.InRange(minuti, 0, 59);
        Assert.InRange(secondi, 0, 59.99995);
        Assert.Equal(Math.Abs(valore), gradi + minuti / 60 + secondi / 3600, 6);
    }

    [Fact]
    public void Sidecar_figlio_si_segna_importato_solo_quando_le_sue_foto_ci_sono()
    {
        var madre = Cartella("Madre");
        var figlia = Cartella("Madre", "Giorno 1");
        Foto(figlia, "IMG_1.jpg");
        using (var dbFiglia = LibrarySidecarDatabase.OpenOrCreate(figlia))
        {
            dbFiglia.SyncScan(FolderScanner.Scan(figlia));
            dbFiglia.UpdateStato(dbFiglia.GetAllPhotos().Single().Id, StatoSelezione.Selected);
        }

        using var db = LibrarySidecarDatabase.OpenOrCreate(madre);
        // Prima apertura con "Giorno 1" illeggibile: niente da riversare, e non va segnata.
        db.SyncScan([], ["Giorno 1"]);
        Assert.Equal(0, db.ImportaDaSidecarFigli());

        db.SyncScan(FolderScanner.Scan(madre));
        Assert.Equal(1, db.ImportaDaSidecarFigli());
        Assert.Equal(StatoSelezione.Selected, db.GetAllPhotos().Single().Stato);
    }

    [Fact]
    public void Mcp_la_cartella_madre_vince_anche_quando_esclude_la_foto()
    {
        var madre = Cartella("Foto");
        var figlia = Cartella("Foto", "Matrimonio");
        Foto(figlia, "IMG_1.jpg", 1);
        Foto(figlia, "IMG_2.jpg", 2, 2);

        using (var dbFiglia = LibrarySidecarDatabase.OpenOrCreate(figlia))
            dbFiglia.SyncScan(FolderScanner.Scan(figlia)); // vecchio: nessuno scarto
        using (var db = LibrarySidecarDatabase.OpenOrCreate(madre))
        {
            db.SyncScan(FolderScanner.Scan(madre));
            db.UpdateStato(db.GetAllPhotos().Single(f => f.PathRelativo.EndsWith("IMG_2.jpg")).Id, StatoSelezione.Reject);
        }
        File.WriteAllText(Path.Combine(_dati, "recent-folders.json"), JsonSerializer.Serialize(new[] { madre, figlia }));

        var trovate = Ricerca.Cerca(new FiltriFoto(Cartella: madre));
        Assert.Equal(["IMG_1.jpg"], trovate.Select(f => Path.GetFileName(f.Percorso)));

        // Solo la madre è nota: cercando la sottocartella le sue foto si trovano lo stesso.
        File.WriteAllText(Path.Combine(_dati, "recent-folders.json"), JsonSerializer.Serialize(new[] { madre }));
        Assert.Single(Ricerca.Cerca(new FiltriFoto(Cartella: figlia)));
        Assert.Single(Ricerca.Cerca(new FiltriFoto(Cartella: "Matrimonio")));
    }

    [Fact]
    public void Mcp_pacchetto_nomi_dei_video_distinti_e_solo_foto_catalogate()
    {
        var foto = Cartella("Foto");
        Foto(foto, "IMG_1.jpg", 1);
        Foto(foto, "IMG_1.heic", 2, 2);
        Foto(foto, "IMG_1.mov", 9);
        using (var db = LibrarySidecarDatabase.OpenOrCreate(foto)) db.SyncScan(FolderScanner.Scan(foto));
        File.WriteAllText(Path.Combine(_dati, "recent-folders.json"), JsonSerializer.Serialize(new[] { foto }));

        var estranea = Path.Combine(Cartella("Altrove"), "segreta.jpg");
        File.WriteAllBytes(estranea, [5]);

        var esito = Pacchetto.Prepara(
            [Path.Combine(foto, "IMG_1.jpg"), Path.Combine(foto, "IMG_1.heic"), estranea],
            "Prova", Cartella("uscita"), zip: false, includiVideo: true);

        Assert.Equal(2, esito.Foto);
        Assert.Equal(2, esito.Video);
        Assert.Single(esito.Mancanti, m => m.StartsWith(estranea));
        var nomi = Directory.GetFiles(esito.Percorso).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["IMG_1 (1).heic", "IMG_1 (1).mov", "IMG_1.jpg", "IMG_1.mov"], nomi);

        // La destinazione dentro una cartella catalogata è rifiutata.
        Assert.Throws<InvalidOperationException>(() =>
            Pacchetto.Prepara([Path.Combine(foto, "IMG_1.jpg")], "Prova", Path.Combine(foto, "Condividi"), zip: false, includiVideo: false));
    }

    [Fact]
    public void Selezioni_file_rovinato_non_viene_sovrascritto()
    {
        var file = Path.Combine(_dati, "selezioni.json");
        File.WriteAllText(file, "{ non è json");

        Assert.Empty(Selezioni.Elenco());
        Assert.Throws<InvalidOperationException>(() => Selezioni.Salva("Nuova", [Path.Combine(_radice, "a.jpg")], false));
        Assert.Equal("{ non è json", File.ReadAllText(file));
        Assert.True(File.Exists(file + ".bak"));

        File.Delete(file);
        Selezioni.Salva("Nuova", [Path.Combine(_radice, "a.jpg")], false);
        Assert.Single(Selezioni.Elenco());
    }

    [Fact]
    public void Cestino_solo_su_dischi_fissi()
    {
        Assert.True(RejectManager.HaCestino(Path.Combine(Path.GetTempPath(), "x.jpg")));
        Assert.False(RejectManager.HaCestino(@"\\server\condivisa\x.jpg"));
    }
}
