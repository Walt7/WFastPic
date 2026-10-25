using WFastPic.Core.Processing;
using Xunit;

namespace WFastPic.Core.Tests;

public class OrientamentoTests
{
    // Ogni pixel ha un valore diverso (indice nel canale blu e verde): si vede dove finisce.
    private static byte[] Immagine(int w, int h)
    {
        var bgra = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            bgra[i * 4] = (byte)i; bgra[i * 4 + 1] = (byte)(i / 256); bgra[i * 4 + 2] = 7; bgra[i * 4 + 3] = 255;
        }
        return bgra;
    }

    private static int Pixel(byte[] bgra, int w, int x, int y) => bgra[(y * w + x) * 4] + bgra[(y * w + x) * 4 + 1] * 256;

    [Fact]
    public void Ruota_orario_porta_l_angolo_in_alto_a_sinistra_in_alto_a_destra()
    {
        var src = Immagine(3, 2); // 0 1 2 / 3 4 5
        var (dst, w, h) = Orientamento.Applica(src, 3, 2, new EditRecipe { RotationDegrees = 90 });

        Assert.Equal((2, 3), (w, h));
        // Orario: la colonna di sinistra (0, 3) diventa la riga in alto, letta dal basso: 3 0.
        Assert.Equal(3, Pixel(dst, w, 0, 0));
        Assert.Equal(0, Pixel(dst, w, 1, 0));
        Assert.Equal(5, Pixel(dst, w, 0, 2));
        Assert.Equal(2, Pixel(dst, w, 1, 2));
    }

    [Fact]
    public void Specchia_verticale_inverte_le_righe_e_due_volte_torna_come_prima()
    {
        var src = Immagine(3, 2); // 0 1 2 / 3 4 5
        var ricetta = Orientamento.SpecchiaVerticale(EditRecipe.Default);
        var (dst, w, h) = Orientamento.Applica(src, 3, 2, ricetta);

        Assert.Equal((3, 2), (w, h));
        Assert.Equal(3, Pixel(dst, w, 0, 0));
        Assert.Equal(5, Pixel(dst, w, 2, 0));
        Assert.Equal(0, Pixel(dst, w, 0, 1));
        Assert.Equal(2, Pixel(dst, w, 2, 1));

        var due = Orientamento.Applica(src, 3, 2, Orientamento.SpecchiaVerticale(ricetta)).Bgra;
        Assert.Equal(src, due);
    }

    [Fact]
    public void Specchia_verticale_porta_con_se_il_ritaglio()
    {
        // Ritaglio nella metà alta: dopo lo specchio verticale è nella metà bassa.
        var r = Orientamento.SpecchiaVerticale(new EditRecipe { CropX = 0, CropY = 0, CropWidth = 1, CropHeight = 0.5 });
        Assert.Equal(0.5, r.CropY, 6);
        Assert.Equal(0.5, r.CropHeight, 6);
        Assert.Equal(0, r.CropX, 6);
    }

    [Fact]
    public void Specchia_inverte_le_colonne()
    {
        var src = Immagine(3, 2);
        var (dst, w, h) = Orientamento.Applica(src, 3, 2, new EditRecipe { FlipHorizontal = true });

        Assert.Equal((3, 2), (w, h));
        Assert.Equal(2, Pixel(dst, w, 0, 0));
        Assert.Equal(0, Pixel(dst, w, 2, 0));
        Assert.Equal(5, Pixel(dst, w, 0, 1));
    }

    [Fact]
    public void Quattro_rotazioni_o_due_specchi_tornano_alla_ricetta_di_partenza()
    {
        var r = new EditRecipe { CropX = 0.1, CropY = 0.2, CropWidth = 0.5, CropHeight = 0.6, StraightenAngleDegrees = 3.5 };

        var giro = Orientamento.RuotaOrario(Orientamento.RuotaOrario(Orientamento.RuotaOrario(Orientamento.RuotaOrario(r))));
        AssertRicetteUguali(r, giro);
        AssertRicetteUguali(r, Orientamento.RuotaAntiorario(Orientamento.RuotaOrario(r)));
        AssertRicetteUguali(r, Orientamento.Specchia(Orientamento.Specchia(r)));
        Assert.Equal(EditRecipe.Default, Orientamento.Specchia(Orientamento.Specchia(EditRecipe.Default)));
    }

    [Fact]
    public void Specchia_inverte_il_raddrizzamento()
    {
        var r = Orientamento.Specchia(new EditRecipe { StraightenAngleDegrees = 2.5 });
        Assert.Equal(-2.5, r.StraightenAngleDegrees);
        Assert.True(r.FlipHorizontal);
    }

    public static TheoryData<string> Operazioni => new() { "orario", "antiorario", "specchia" };

    /// <summary>
    /// Girare la ricetta e applicarla = applicarla e girare il risultato: un ritaglio già fatto resta
    /// lo stesso ritaglio, solo girato. Ritagli allineati ai pixel per un confronto esatto.
    /// </summary>
    [Theory]
    [MemberData(nameof(Operazioni))]
    public void Girare_la_ricetta_equivale_a_girare_il_risultato(string operazione)
    {
        const int w = 8, h = 4;
        var src = Immagine(w, h);
        var partenze = new[]
        {
            new EditRecipe { CropX = 0.25, CropY = 0.25, CropWidth = 0.5, CropHeight = 0.5 },
            new EditRecipe { RotationDegrees = 90, CropX = 0.25, CropY = 0.125, CropWidth = 0.5, CropHeight = 0.75 },
            new EditRecipe { FlipHorizontal = true, RotationDegrees = 270, CropX = 0, CropY = 0.25, CropWidth = 0.75, CropHeight = 0.5 },
        };

        foreach (var r in partenze)
        {
            var (prima, pw, ph) = RecipeApplier.Applica(src, w, h, r);

            var (girata, operazioneSulRisultato) = operazione switch
            {
                "orario" => (Orientamento.RuotaOrario(r), new EditRecipe { RotationDegrees = 90 }),
                "antiorario" => (Orientamento.RuotaAntiorario(r), new EditRecipe { RotationDegrees = 270 }),
                _ => (Orientamento.Specchia(r), new EditRecipe { FlipHorizontal = true }),
            };

            var atteso = Orientamento.Applica(prima, pw, ph, operazioneSulRisultato);
            var ottenuto = RecipeApplier.Applica(src, w, h, girata);

            Assert.Equal((atteso.Larghezza, atteso.Altezza), (ottenuto.Larghezza, ottenuto.Altezza));
            Assert.Equal(atteso.Bgra, ottenuto.Bgra);
        }
    }

    [Fact]
    public void Rettangolo_dei_volti_segue_la_foto_girata()
    {
        var r = new EditRecipe { RotationDegrees = 90 };
        // Volto in alto a sinistra → girando in senso orario finisce in alto a destra.
        var (x0, y0, x1, y1) = Orientamento.TrasformaRettangolo(0.1, 0.1, 0.3, 0.2, r);
        Assert.Equal(0.8, x0, 6); Assert.Equal(0.1, y0, 6); Assert.Equal(0.9, x1, 6); Assert.Equal(0.3, y1, 6);

        var s = Orientamento.TrasformaRettangolo(0.1, 0.1, 0.3, 0.2, new EditRecipe { FlipHorizontal = true });
        Assert.Equal(0.7, s.X0, 6); Assert.Equal(0.9, s.X1, 6);
    }

    [Fact]
    public void Serializer_legge_ricette_vecchie_senza_orientamento_e_conserva_quelle_nuove()
    {
        var vecchia = EditRecipeSerializer.FromJson("{\"SchemaVersion\":2,\"CropX\":0,\"CropY\":0,\"CropWidth\":1,\"CropHeight\":1}");
        Assert.False(vecchia.HasOrientation);

        var nuova = new EditRecipe { RotationDegrees = 270, FlipHorizontal = true };
        Assert.Equal(nuova, EditRecipeSerializer.FromJson(EditRecipeSerializer.ToJson(nuova)));
        Assert.True(nuova.HasOrientation);
        Assert.False(nuova.HasGeometry);
    }

    private static void AssertRicetteUguali(EditRecipe atteso, EditRecipe ottenuto)
    {
        Assert.Equal(atteso.RotationDegrees, ottenuto.RotationDegrees);
        Assert.Equal(atteso.FlipHorizontal, ottenuto.FlipHorizontal);
        Assert.Equal(atteso.StraightenAngleDegrees, ottenuto.StraightenAngleDegrees, 9);
        Assert.Equal(atteso.CropX, ottenuto.CropX, 9);
        Assert.Equal(atteso.CropY, ottenuto.CropY, 9);
        Assert.Equal(atteso.CropWidth, ottenuto.CropWidth, 9);
        Assert.Equal(atteso.CropHeight, ottenuto.CropHeight, 9);
    }
}
