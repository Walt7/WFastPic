using WFastPic.Core.Processing;
using Xunit;

namespace WFastPic.Core.Tests;

public class SuggerimentiRitaglioTests
{
    // Cielo uniforme con un soggetto colorato e dettagliato in alto a destra.
    private static byte[] FotoConSoggetto(int w, int h, int sx0, int sy0, int sx1, int sy1)
    {
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                var soggetto = x >= sx0 && x < sx1 && y >= sy0 && y < sy1;
                var scacchi = ((x / 6) + (y / 6)) % 2 == 0;
                bgra[o] = soggetto ? (byte)(scacchi ? 20 : 60) : (byte)200;
                bgra[o + 1] = soggetto ? (byte)(scacchi ? 40 : 160) : (byte)170;
                bgra[o + 2] = soggetto ? (byte)(scacchi ? 220 : 120) : (byte)140;
                bgra[o + 3] = 255;
            }
        }
        return bgra;
    }

    [Fact]
    public void Propone_ritagli_che_contengono_il_soggetto_e_tagliano_lo_sfondo_vuoto()
    {
        const int w = 600, h = 400;
        var bgra = FotoConSoggetto(w, h, 400, 60, 520, 180);

        var proposte = SuggerimentiRitaglio.Calcola(bgra, w, h);

        Assert.NotEmpty(proposte);
        foreach (var p in proposte)
        {
            // Il soggetto (normalizzato) sta dentro il ritaglio.
            Assert.True(p.X0 <= 400.0 / w + 0.02 && p.X1 >= 520.0 / w - 0.02, $"{p.Etichetta}: X {p.X0:F2}-{p.X1:F2}");
            Assert.True(p.Y0 <= 60.0 / h + 0.02 && p.Y1 >= 180.0 / h - 0.02, $"{p.Etichetta}: Y {p.Y0:F2}-{p.Y1:F2}");
            Assert.InRange(p.X0, 0, 1); Assert.InRange(p.X1, 0, 1);
            Assert.InRange(p.Y0, 0, 1); Assert.InRange(p.Y1, 0, 1);
        }

        // Almeno una proposta taglia davvero: non è la foto intera.
        Assert.Contains(proposte, p => (p.X1 - p.X0) * (p.Y1 - p.Y0) < 0.9);
    }

    [Fact]
    public void Le_proporzioni_sono_rispettate_e_orientate_come_la_foto()
    {
        const int w = 400, h = 600; // verticale
        var bgra = FotoConSoggetto(w, h, 150, 200, 250, 350);

        var proposte = SuggerimentiRitaglio.Calcola(bgra, w, h);

        foreach (var p in proposte)
        {
            var rapportoPixel = (p.X1 - p.X0) * w / ((p.Y1 - p.Y0) * h);
            Assert.Equal(p.Rapporto, rapportoPixel, 1);
        }
        Assert.Contains(proposte, p => p.Etichetta == "9:16");
    }

    [Fact]
    public void Con_foto_raddrizzata_i_ritagli_restano_dentro_l_immagine_ruotata()
    {
        const int w = 600, h = 400;
        const double angolo = 7;
        var bgra = FotoConSoggetto(w, h, 380, 40, 560, 160);

        var proposte = SuggerimentiRitaglio.Calcola(bgra, w, h, angolo);

        Assert.NotEmpty(proposte);
        foreach (var p in proposte)
            Assert.True(StraightenGeometry.ContainsRect(w, h, angolo, p.X0 * w, p.Y0 * h, p.X1 * w, p.Y1 * h), p.Etichetta);
    }

    [Fact]
    public void Con_volti_ogni_proposta_li_tiene_interi_anche_se_lo_sfondo_attira_di_piu()
    {
        const int w = 600, h = 400;
        // Dettaglio vistoso in basso a sinistra; il volto (rilevato) in alto a destra su sfondo piatto.
        var bgra = FotoConSoggetto(w, h, 20, 250, 220, 390);
        var volto = new ZonaImportante(460.0 / w, 70.0 / h, 520.0 / w, 140.0 / h);

        var proposte = SuggerimentiRitaglio.Calcola(bgra, w, h, volti: [volto]);

        Assert.NotEmpty(proposte);
        foreach (var p in proposte)
        {
            Assert.True(p.X0 <= volto.X0 && p.X1 >= volto.X1 && p.Y0 <= volto.Y0 && p.Y1 >= volto.Y1,
                $"{p.Etichetta} taglia il volto: {p.X0:F2},{p.Y0:F2}-{p.X1:F2},{p.Y1:F2}");
        }
    }

    [Fact]
    public void Miniatura_rispetta_lato_massimo_e_proporzioni()
    {
        var bgra = FotoConSoggetto(600, 400, 0, 0, 1, 1);
        var ritaglio = new RitaglioSuggerito("16:9", 16.0 / 9, 0, 0.1, 1, 0.1 + 600.0 * 9 / 16 / 400);

        var (_, tw, th) = SuggerimentiRitaglio.Miniatura(bgra, 600, 400, ritaglio, 96);

        Assert.Equal(96, tw);
        Assert.Equal(54, th);
    }
}
