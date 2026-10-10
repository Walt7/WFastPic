using Windows.Graphics.Imaging;
using WFastPic.Core.Export;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;
using Xunit;

namespace WFastPic.Core.Tests;

public class ExportTests
{
    // ---------------------------------------------------------------- nomi file

    [Fact]
    public void Nome_output_con_sostituzione_riusa_il_nome_di_un_export_precedente()
    {
        var usati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var percorso = NomiFileExport.PercorsoOutput(@"C:\Foto\Export", @"C:\Foto", "IMG_1", true, usati, _ => true);

        Assert.Equal(@"C:\Foto\Export\IMG_1.jpg", percorso);
    }

    [Fact]
    public void Nome_output_senza_sostituzione_numera_se_il_file_esiste()
    {
        var usati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var percorso = NomiFileExport.PercorsoOutput(@"C:\Foto\Export", @"C:\Foto", "IMG_1", false, usati,
            p => p.EndsWith("IMG_1.jpg", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(@"C:\Foto\Export\IMG_1_2.jpg", percorso);
    }

    [Fact]
    public void Nome_output_nella_cartella_delle_foto_non_sostituisce_mai()
    {
        var usati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var percorso = NomiFileExport.PercorsoOutput(@"C:\Foto\", @"C:\Foto", "IMG_1", true, usati,
            p => p.Equals(@"C:\Foto\IMG_1.jpg", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(@"C:\Foto\IMG_1_2.jpg", percorso);
    }

    [Fact]
    public void Due_foto_con_lo_stesso_nome_base_nello_stesso_export_non_si_sovrascrivono()
    {
        var usati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var a = NomiFileExport.PercorsoOutput(@"C:\Out", @"C:\Foto", "IMG_1", true, usati, _ => false);
        var b = NomiFileExport.PercorsoOutput(@"C:\Out", @"C:\Foto", "IMG_1", true, usati, _ => false);

        Assert.NotEqual(a, b);
    }

    // ---------------------------------------------------------------- filigrana

    [Theory]
    [InlineData(PosizioneWatermark.AltoSinistra)]
    [InlineData(PosizioneWatermark.AltoDestra)]
    [InlineData(PosizioneWatermark.BassoSinistra)]
    [InlineData(PosizioneWatermark.BassoDestra)]
    [InlineData(PosizioneWatermark.Centro)]
    public void Filigrana_sempre_dentro_la_foto_anche_se_il_testo_e_piu_largo(PosizioneWatermark posizione)
    {
        var (x, y) = WatermarkRenderer.CalcolaPosizione(posizione, 400, 300, 380, 40, 20);

        Assert.InRange(x, 0, 400 - 380);
        Assert.InRange(y, 0, 300 - 40);
    }

    [Fact]
    public void Filigrana_modifica_solo_una_zona_del_buffer()
    {
        const int w = 600, h = 400;
        var bgra = Enumerable.Repeat((byte)100, w * h * 4).ToArray();

        WatermarkRenderer.Applica(bgra, w, h, "© Test", 1.0, PosizioneWatermark.BassoDestra);

        var modificati = 0;
        for (var i = 0; i < bgra.Length; i += 4) if (bgra[i] != 100) modificati++;
        Assert.InRange(modificati, 1, w * h / 4);
        Assert.Equal(100, bgra[0]); // angolo opposto intatto
    }

    // ---------------------------------------------------------------- export end-to-end (WIC)

    [Fact]
    public async Task Export_ridimensiona_esclude_gli_scarti_e_riesportando_sostituisce()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "wfastpic-export-" + Guid.NewGuid());
        Directory.CreateDirectory(cartella);
        try
        {
            await CreaJpegAsync(Path.Combine(cartella, "a.jpg"), 800, 600);
            await CreaJpegAsync(Path.Combine(cartella, "b.jpg"), 800, 600);

            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(FolderScanner.Scan(cartella));
            var b = db.GetAllPhotos().Single(p => p.PathRelativo == "b.jpg");
            db.UpdateStato(b.Id, StatoSelezione.Reject);

            var output = Path.Combine(cartella, "Export");
            var opzioni = new ExportOptions
            {
                CartellaOutput = output,
                SoloSelezionate = false,
                LatoLungoMassimo = 400,
                TestoWatermark = "© Test",
            };

            var risultati = await Exporter.EseguiAsync(db, opzioni);
            var unico = Assert.Single(risultati); // lo scarto non si esporta
            Assert.True(unico.Successo, unico.Errore);
            Assert.Equal((400u, 300u), await DimensioniAsync(unico.PercorsoOutput));

            // Seconda volta: stesso file sostituito, niente "a_2.jpg" e niente ".part" rimasti.
            await Exporter.EseguiAsync(db, opzioni);
            Assert.Equal("a.jpg", Path.GetFileName(Assert.Single(Directory.GetFiles(output))));
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public async Task Export_scrive_la_posizione_corretta_a_mano_e_la_toglie_se_richiesto()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "wfastpic-export-" + Guid.NewGuid());
        Directory.CreateDirectory(cartella);
        try
        {
            await CreaJpegAsync(Path.Combine(cartella, "a.jpg"), 300, 200);
            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(FolderScanner.Scan(cartella));
            db.SetPosizioneManuale(db.GetAllPhotos().Single().Id, 40.6327, -17.9418);

            var output = Path.Combine(cartella, "Export");
            var risultato = (await Exporter.EseguiAsync(db, new ExportOptions { CartellaOutput = output, SoloSelezionate = false })).Single();
            Assert.True(risultato.Successo, risultato.Errore);

            var gps = WFastPic.Core.Metadata.PhotoMetadataReader.Read(risultato.PercorsoOutput).Gps;
            Assert.NotNull(gps);
            Assert.Equal(40.6327, gps.Value.Latitude, 4);
            Assert.Equal(-17.9418, gps.Value.Longitude, 4);

            var senza = (await Exporter.EseguiAsync(db, new ExportOptions { CartellaOutput = output, SoloSelezionate = false, RimuoviGps = true })).Single();
            Assert.Null(WFastPic.Core.Metadata.PhotoMetadataReader.Read(senza.PercorsoOutput).Gps);
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public async Task Export_mantiene_data_di_scatto_e_posizione_del_file_e_scrive_i_tag()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "wfastpic-export-" + Guid.NewGuid());
        Directory.CreateDirectory(cartella);
        try
        {
            var scatto = new DateTime(2026, 7, 15, 10, 30, 5);
            await CreaJpegAsync(Path.Combine(cartella, "a.jpg"), 300, 200, scatto, new PosizioneExport(41.1171, 16.8719, 87.25, 314.5));
            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(FolderScanner.Scan(cartella));

            var risultato = (await Exporter.EseguiAsync(db, new ExportOptions
            {
                CartellaOutput = Path.Combine(cartella, "Export"),
                SoloSelezionate = false,
                Tag = ["vacanza", "puglia"],
            })).Single();
            Assert.True(risultato.Successo, risultato.Errore);

            var exif = WFastPic.Core.Metadata.PhotoMetadataReader.Read(risultato.PercorsoOutput);
            Assert.Equal(scatto, exif.CaptureDate);
            Assert.NotNull(exif.Gps);
            Assert.Equal(41.1171, exif.Gps.Value.Latitude, 4);
            Assert.Equal(16.8719, exif.Gps.Value.Longitude, 4);
            Assert.Equal(87.25, exif.Gps.Value.Altitude!.Value, 2);
            Assert.Equal(314.5, WFastPic.Core.Metadata.PhotoMetadataReader.LeggiDatiRipresa(risultato.PercorsoOutput).DirezioneGradi!.Value, 2);

            var ifd0 = MetadataExtractor.ImageMetadataReader.ReadMetadata(risultato.PercorsoOutput)
                .OfType<MetadataExtractor.Formats.Exif.ExifIfd0Directory>().Single();
            Assert.Equal("vacanza;puglia", ifd0.GetDescription(MetadataExtractor.Formats.Exif.ExifDirectoryBase.TagWinKeywords));
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }

    [Fact]
    public async Task Export_annullato_prima_di_iniziare_non_esporta_nulla()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "wfastpic-export-" + Guid.NewGuid());
        Directory.CreateDirectory(cartella);
        try
        {
            await CreaJpegAsync(Path.Combine(cartella, "a.jpg"), 100, 100);
            using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);
            db.SyncScan(FolderScanner.Scan(cartella));

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var risultati = await Exporter.EseguiAsync(db,
                new ExportOptions { CartellaOutput = Path.Combine(cartella, "Export"), SoloSelezionate = false }, ct: cts.Token);

            Assert.Empty(risultati);
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }

    private static async Task CreaJpegAsync(string percorso, int w, int h, DateTime? data = null, PosizioneExport? gps = null)
    {
        var bgra = new byte[w * h * 4];
        for (var i = 0; i < bgra.Length; i += 4) { bgra[i] = 40; bgra[i + 1] = 120; bgra[i + 2] = 200; bgra[i + 3] = 255; }
        await JpegEncoder.SalvaAsync(percorso, bgra, w, h, 90, data, gps);
    }

    private static async Task<(uint, uint)> DimensioniAsync(string percorso)
    {
        using var fs = File.OpenRead(percorso);
        var decoder = await BitmapDecoder.CreateAsync(fs.AsRandomAccessStream());
        return (decoder.PixelWidth, decoder.PixelHeight);
    }
}
