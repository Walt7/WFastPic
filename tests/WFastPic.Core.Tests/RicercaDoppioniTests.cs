using WFastPic.Core.Library;
using WFastPic.Core.Processing;
using Xunit;

namespace WFastPic.Core.Tests;

public class RicercaDoppioniTests
{
    private static readonly DateTime T0 = new(2026, 8, 5, 15, 22, 0);

    // ---------------------------------------------------------------- identiche

    [Fact]
    public void Identiche_si_confrontano_solo_i_file_con_la_stessa_dimensione()
    {
        var confronti = new List<(long, long)>();
        var contenuto = new Dictionary<long, string> { [1] = "a", [2] = "a", [3] = "b", [4] = "a" };

        var identiche = RicercaDoppioni.TrovaIdentiche(
            [(1, 100), (2, 100), (3, 100), (4, 200)],
            (a, b) => { confronti.Add((a, b)); return contenuto[a] == contenuto[b]; });

        var gruppo = Assert.Single(identiche);
        Assert.Equal([1L, 2L], gruppo);
        // La 4 ha un'altra dimensione: mai letta, anche se il contenuto "sarebbe" uguale.
        Assert.DoesNotContain(confronti, c => c.Item1 == 4 || c.Item2 == 4);
    }

    [Fact]
    public void Identiche_con_dimensioni_tutte_diverse_non_leggono_niente()
    {
        var identiche = RicercaDoppioni.TrovaIdentiche(
            [(1, 100), (2, 101), (3, 102)],
            (_, _) => throw new InvalidOperationException("non deve leggere i file"));

        Assert.Empty(identiche);
    }

    [Fact]
    public void Stesso_contenuto_confronta_i_file_byte_per_byte()
    {
        var cartella = Directory.CreateTempSubdirectory("wfastpic-doppioni-").FullName;
        try
        {
            var dati = new byte[200_000];
            new Random(7).NextBytes(dati);
            var a = Path.Combine(cartella, "a.jpg");
            var b = Path.Combine(cartella, "b.jpg");
            var c = Path.Combine(cartella, "c.jpg");
            File.WriteAllBytes(a, dati);
            File.WriteAllBytes(b, dati);
            dati[^1] ^= 1; // differenza nell'ultimo blocco
            File.WriteAllBytes(c, dati);

            Assert.True(RicercaDoppioni.StessoContenuto(a, b));
            Assert.False(RicercaDoppioni.StessoContenuto(a, c));
        }
        finally
        {
            Directory.Delete(cartella, recursive: true);
        }
    }

    // ---------------------------------------------------------------- quasi uguali

    [Fact]
    public void Copia_senza_data_con_impronta_vicina_e_un_doppione()
    {
        var originale = new FotoPerDoppioni(1, 3_000_000, T0, 0b1011_0110UL, 0.75);
        var copiaWhatsApp = new FotoPerDoppioni(2, 200_000, null, 0b1011_0111UL, 0.75);

        Assert.True(RicercaDoppioni.QuasiUguali(originale, copiaWhatsApp));
    }

    [Fact]
    public void Due_scatti_con_date_diverse_non_sono_doppioni_anche_se_simili()
    {
        var primo = new FotoPerDoppioni(1, 3_000_000, T0, 0xFFUL, 0.75);
        var secondo = new FotoPerDoppioni(2, 3_100_000, T0.AddSeconds(1), 0xFFUL, 0.75);

        Assert.False(RicercaDoppioni.QuasiUguali(primo, secondo));
    }

    [Fact]
    public void Proporzioni_diverse_non_sono_doppioni()
    {
        var intera = new FotoPerDoppioni(1, 3_000_000, null, 0xFFUL, 4.0 / 3);
        var ritagliata = new FotoPerDoppioni(2, 2_000_000, null, 0xFFUL, 16.0 / 9);

        Assert.False(RicercaDoppioni.QuasiUguali(intera, ritagliata));
    }

    [Fact]
    public void Impronte_lontane_non_sono_doppioni()
    {
        var a = new FotoPerDoppioni(1, 1, null, 0UL, 1);
        var b = new FotoPerDoppioni(2, 1, null, (1UL << (RicercaDoppioni.DistanzaMassima + 1)) - 1, 1);

        Assert.False(RicercaDoppioni.QuasiUguali(a, b));
    }

    // ---------------------------------------------------------------- gruppi

    [Fact]
    public void Gruppi_a_catena_e_tipo_del_gruppo()
    {
        var foto = new[]
        {
            new FotoPerDoppioni(10, 500, T0, 0b0000UL, 1),
            new FotoPerDoppioni(11, 500, T0, 0b0000UL, 1),
            new FotoPerDoppioni(20, 900, null, 0b000111UL, 1),
            new FotoPerDoppioni(21, 800, null, 0b111111UL, 1), // 3 bit dalla 20, 6 dalla 10: a catena
            new FotoPerDoppioni(30, 700, null, ulong.MaxValue, 1),
            new FotoPerDoppioni(40, 600, T0, null, null),
            new FotoPerDoppioni(41, 600, T0, null, null),
        };
        IReadOnlyList<IReadOnlyList<long>> identiche = [[10L, 11L], [40L, 41L]];

        var gruppi = RicercaDoppioni.Raggruppa(foto, identiche);

        Assert.Equal(2, gruppi.Count);
        Assert.Equal([10L, 11L, 20L, 21L], gruppi[0].Foto);
        Assert.False(gruppi[0].Identiche);
        Assert.Equal([40L, 41L], gruppi[1].Foto);
        Assert.True(gruppi[1].Identiche);
    }

    [Fact]
    public void Copie_in_altre_cartelle_si_confrontano_solo_con_quelle_di_qui()
    {
        var qui = new[]
        {
            new FotoPerDoppioni(1, 3_000_000, T0, 0b0000UL, 1),
            new FotoPerDoppioni(2, 2_000_000, null, ulong.MaxValue, 1),
        };
        var altrove = new[]
        {
            new FotoPerDoppioni(-1, 200_000, null, 0b0011UL, 1),     // copia WhatsApp della 1, in un'altra cartella
            new FotoPerDoppioni(-2, 200_000, null, 0b111_1111UL, 1),   // vicina alla -1 ma non alla 1: altrove-altrove non conta
            new FotoPerDoppioni(-3, 500, null, 0x00FF_0000_0000UL, 1),
            new FotoPerDoppioni(-4, 500, null, 0x00FF_0000_0000UL, 1), // doppioni tra loro, ma nessuno qui
        };

        var gruppi = RicercaDoppioni.Raggruppa(qui, [], altrove);

        var gruppo = Assert.Single(gruppi);
        Assert.Equal([1L], gruppo.Foto);
        Assert.Equal([-1L], gruppo.Altrove);
    }

    [Fact]
    public void Identica_in_un_altra_cartella()
    {
        var qui = new[] { new FotoPerDoppioni(1, 500, T0, null, null) };
        var altrove = new[] { new FotoPerDoppioni(-7, 500, T0, null, null) };

        var gruppo = Assert.Single(RicercaDoppioni.Raggruppa(qui, [[1L, -7L]], altrove));

        Assert.True(gruppo.Identiche);
        Assert.Equal([1L], gruppo.Foto);
        Assert.Equal([-7L], gruppo.Altrove);
    }

    // ---------------------------------------------------------------- quale tenere

    [Fact]
    public void Migliore_prima_le_scelte_dell_utente()
    {
        var migliore = RicercaDoppioni.ScegliMigliore(
        [
            new CandidatoDoppione(1, "IMG_1.heic", 3_000_000, HaDataScatto: true, Pixel: 12_000_000, VotoAutomatico: 5),
            new CandidatoDoppione(2, "IMG_1 (1).jpg", 200_000, Selezionata: true),
        ]);

        Assert.Equal(2, migliore);
    }

    [Fact]
    public void Migliore_l_originale_con_data_e_piu_pixel()
    {
        var migliore = RicercaDoppioni.ScegliMigliore(
        [
            new CandidatoDoppione(1, "IMG-20260805-WA0001.jpg", 300_000, Pixel: 1_600 * 1_200, VotoAutomatico: 4),
            new CandidatoDoppione(2, "IMG_1234.heic", 2_500_000, HaDataScatto: true, Pixel: 4_032 * 3_024, VotoAutomatico: 3),
        ]);

        Assert.Equal(2, migliore);
    }

    [Fact]
    public void Migliore_tra_identiche_il_nome_che_non_sembra_una_copia()
    {
        var migliore = RicercaDoppioni.ScegliMigliore(
        [
            new CandidatoDoppione(1, "IMG_1234 - Copia.jpg", 100),
            new CandidatoDoppione(2, "IMG_1234 (1).jpg", 100),
            new CandidatoDoppione(3, "IMG_1234.jpg", 100),
        ]);

        Assert.Equal(3, migliore);
    }

    [Fact]
    public void Una_copia_scartata_non_e_mai_la_migliore()
    {
        var migliore = RicercaDoppioni.ScegliMigliore(
        [
            new CandidatoDoppione(1, "IMG_1234.jpg", 100, Scartata: true, Voto: 5),
            new CandidatoDoppione(2, "IMG_1234 (1).jpg", 100),
        ]);

        Assert.Equal(2, migliore);
    }

    // ---------------------------------------------------------------- impronta percettiva

    [Fact]
    public void Impronta_resiste_a_ridimensionamento_e_rumore()
    {
        var (grande, gw, gh) = Scena(160, 120, rumore: 0);
        var (piccola, pw, ph) = Scena(64, 48, rumore: 6);

        var distanza = ImprontaPercettiva.Distanza(
            ImprontaPercettiva.Calcola(grande, gw, gh), ImprontaPercettiva.Calcola(piccola, pw, ph));

        Assert.True(distanza <= RicercaDoppioni.DistanzaMassima, $"distanza {distanza}");
    }

    [Fact]
    public void Impronta_distingue_scene_diverse()
    {
        var (scena, w, h) = Scena(160, 120, rumore: 0);
        var (altra, aw, ah) = Scena(160, 120, rumore: 0, specchiata: true);

        var distanza = ImprontaPercettiva.Distanza(
            ImprontaPercettiva.Calcola(scena, w, h), ImprontaPercettiva.Calcola(altra, aw, ah));

        Assert.True(distanza > RicercaDoppioni.DistanzaMassima * 2, $"distanza {distanza}");
    }

    /// <summary>Sole in alto a sinistra, orizzonte, una barca: abbastanza struttura per l'impronta.</summary>
    private static (byte[] Bgra, int W, int H) Scena(int w, int h, int rumore, bool specchiata = false)
    {
        var casuale = new Random(42);
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var fx = (specchiata ? w - 1 - x : x) / (double)w;
                var fy = y / (double)h;
                double v = fy < 0.55 ? 200 - fy * 120 : 60 + fx * 40;
                if (Math.Pow(fx - 0.2, 2) + Math.Pow(fy - 0.2, 2) < 0.01) v = 250;
                if (fx > 0.6 && fx < 0.8 && fy > 0.5 && fy < 0.65) v = 20;
                v += casuale.Next(-rumore, rumore + 1);
                var b = (byte)Math.Clamp(v, 0, 255);
                var p = (y * w + x) * 4;
                bgra[p] = bgra[p + 1] = bgra[p + 2] = b;
                bgra[p + 3] = 255;
            }
        }
        return (bgra, w, h);
    }
}
