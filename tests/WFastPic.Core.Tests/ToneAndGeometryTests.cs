using WFastPic.Core.Processing;
using Xunit;

namespace WFastPic.Core.Tests;

public class ToneAndGeometryTests
{
    // Gradiente grigio 256x1: pixel i ha tutti i canali = i.
    private static byte[] Gradiente()
    {
        var bgra = new byte[256 * 4];
        for (var i = 0; i < 256; i++)
        {
            bgra[i * 4] = (byte)i; bgra[i * 4 + 1] = (byte)i; bgra[i * 4 + 2] = (byte)i; bgra[i * 4 + 3] = 255;
        }
        return bgra;
    }

    private static byte[] Applica(EditRecipe ricetta)
    {
        var bgra = Gradiente();
        ToneProcessor.ApplyInPlace(bgra, 256, 1, ricetta);
        return bgra;
    }

    [Fact]
    public void Serializer_migra_ricetta_v1_con_autocorrect_in_contrasto_e_colore()
    {
        var v1 = "{\"SchemaVersion\":1,\"CropX\":0,\"CropY\":0,\"CropWidth\":1,\"CropHeight\":1,\"StraightenAngleDegrees\":2,\"AutoCorrect\":true}";

        var ricetta = EditRecipeSerializer.FromJson(v1);

        Assert.Equal(EditRecipe.CurrentSchemaVersion, ricetta.SchemaVersion);
        Assert.True(ricetta.AutoContrast);
        Assert.True(ricetta.AutoColor);
        Assert.False(ricetta.AutoCorrect);
        Assert.Equal(2, ricetta.StraightenAngleDegrees);
    }

    [Fact]
    public void Tono_con_ricetta_neutra_non_modifica_il_buffer()
    {
        Assert.Equal(Gradiente(), Applica(EditRecipe.Default));
    }

    [Fact]
    public void LuceDiRiempimento_schiarisce_i_mezzitoni_e_lascia_fermi_bianco_e_nero()
    {
        var r = Applica(new EditRecipe { FillLight = 1 });

        Assert.True(r[128 * 4] > 128);
        Assert.Equal(0, r[0]);
        Assert.Equal(255, r[255 * 4]);
    }

    [Fact]
    public void Evidenzia_scurisce_le_alte_luci()
    {
        var r = Applica(new EditRecipe { Highlights = 1 });

        Assert.True(r[240 * 4] < 240);
        Assert.True(Math.Abs(r[10 * 4] - 10) <= 1);
    }

    [Fact]
    public void Ombre_scurisce_le_zone_scure()
    {
        var r = Applica(new EditRecipe { Shadows = 1 });

        Assert.True(r[40 * 4] < 40);
    }

    [Theory]
    [InlineData(1.0, 0, 0, 0)]
    [InlineData(0, 1.0, 0, 0)]
    [InlineData(0, 0, 1.0, 0)]
    [InlineData(1.0, 1.0, 1.0, 1.0)]
    [InlineData(1.0, 1.0, 1.0, -1.0)]
    public void Le_curve_tonali_sono_monotone(double fill, double high, double shad, double temp)
    {
        var r = Applica(new EditRecipe { FillLight = fill, Highlights = high, Shadows = shad, Temperature = temp });

        for (var i = 1; i < 256; i++)
        {
            Assert.True(r[i * 4 + 1] >= r[(i - 1) * 4 + 1], $"canale G non monotono a {i}");
        }
    }

    [Fact]
    public void Temperatura_calda_alza_il_rosso_e_abbassa_il_blu()
    {
        var r = Applica(new EditRecipe { Temperature = 1 });
        var i = 128 * 4;

        Assert.True(r[i + 2] > 128); // R
        Assert.True(r[i] < 128);     // B
        Assert.Equal(128, r[i + 1]); // G invariato
    }

    [Fact]
    public void AutoContrasto_allunga_un_istogramma_compresso_senza_spostare_i_colori()
    {
        var bgra = new byte[400 * 4];
        for (var i = 0; i < 400; i++)
        {
            var v = (byte)(100 + i % 40);
            bgra[i * 4] = v; bgra[i * 4 + 1] = v; bgra[i * 4 + 2] = v; bgra[i * 4 + 3] = 255;
        }

        ToneProcessor.ApplyInPlace(bgra, 400, 1, new EditRecipe { AutoContrast = true });

        byte min = 255, max = 0;
        for (var i = 0; i < 400; i++)
        {
            min = Math.Min(min, bgra[i * 4]);
            max = Math.Max(max, bgra[i * 4]);
            Assert.Equal(bgra[i * 4], bgra[i * 4 + 2]); // resta grigio: niente viraggi
        }

        Assert.True(max - min > 180, $"gamma troppo stretta: {min}-{max}");
    }

    [Fact]
    public void AutoColore_riduce_una_dominante_blu()
    {
        var bgra = Gradiente();
        for (var i = 0; i < 256; i++) bgra[i * 4] = (byte)Math.Min(255, i * 1.15);

        double Scarto(byte[] b)
        {
            double sb = 0, sr = 0;
            for (var i = 0; i < 256; i++) { sb += b[i * 4]; sr += b[i * 4 + 2]; }
            return Math.Abs(sb - sr) / 256;
        }

        var prima = Scarto(bgra);
        ToneProcessor.ApplyInPlace(bgra, 256, 1, new EditRecipe { AutoColor = true });

        Assert.True(Scarto(bgra) < prima / 2);
    }

    [Fact]
    public void Tono_su_immagine_a_tinta_unita_non_esplode()
    {
        var bgra = new byte[2 * 2 * 4];
        for (var i = 0; i < bgra.Length; i += 4) { bgra[i] = 128; bgra[i + 1] = 128; bgra[i + 2] = 128; bgra[i + 3] = 255; }

        var eccezione = Record.Exception(() =>
            ToneProcessor.ApplyInPlace(bgra, 2, 2, new EditRecipe { AutoContrast = true, AutoColor = true }));
        Assert.Null(eccezione);
    }

    [Fact]
    public void RecipeApplier_non_modifica_il_buffer_in_ingresso_quando_applica_il_tono()
    {
        var bgra = Gradiente();
        var copia = (byte[])bgra.Clone();

        RecipeApplier.Applica(bgra, 256, 1, new EditRecipe { FillLight = 1 });

        Assert.Equal(copia, bgra);
    }

    [Theory]
    [InlineData(1000, 800, 5)]
    [InlineData(1000, 800, -8)]
    [InlineData(600, 900, 3.5)]
    public void FitRect_sulla_tela_intera_da_il_rettangolo_inscritto_massimo_con_stesse_proporzioni(double w, double h, double angolo)
    {
        var rad = Math.Abs(angolo) * Math.PI / 180;
        var c = Math.Cos(rad); var s = Math.Sin(rad);
        var atteso = Math.Min(w / (w * c + h * s), h / (w * s + h * c));

        var (x0, y0, x1, y1) = StraightenGeometry.FitRect(w, h, angolo, 0, 0, w, h);

        Assert.Equal(atteso, (x1 - x0) / w, 3);
        Assert.Equal(atteso, (y1 - y0) / h, 3);
        Assert.True(StraightenGeometry.ContainsRect(w, h, angolo, x0, y0, x1, y1));
    }

    [Fact]
    public void Simboli_modifiche_distinguono_ritaglio_raddrizzamento_e_tono()
    {
        var soloRaddrizzata = new EditRecipe { StraightenAngleDegrees = 2.5 };
        Assert.True(soloRaddrizzata.HasStraighten);
        Assert.False(soloRaddrizzata.HasCrop);
        Assert.False(soloRaddrizzata.HasTone);

        var ritagliataEColore = new EditRecipe { CropX = 0.1, CropWidth = 0.8, AutoColor = true };
        Assert.True(ritagliataEColore.HasCrop);
        Assert.False(ritagliataEColore.HasStraighten);
        Assert.True(ritagliataEColore.HasTone);

        // La ricetta salvata e riletta conserva i simboli.
        var riletta = EditRecipeSerializer.FromJson(EditRecipeSerializer.ToJson(ritagliataEColore));
        Assert.True(riletta.HasCrop && riletta.HasTone);
    }

    [Fact]
    public void FitRect_non_tocca_un_rettangolo_gia_valido()
    {
        var (x0, y0, x1, y1) = StraightenGeometry.FitRect(1000, 800, 5, 400, 300, 600, 500);

        Assert.Equal((400.0, 300.0, 600.0, 500.0), (x0, y0, x1, y1));
    }

    [Fact]
    public void FitRect_con_angolo_zero_non_cambia_nulla()
    {
        var (x0, y0, x1, y1) = StraightenGeometry.FitRect(1000, 800, 0, 0, 0, 1000, 800);

        Assert.Equal((0.0, 0.0, 1000.0, 800.0), (x0, y0, x1, y1));
    }

    [Fact]
    public void AvvicinaRect_trascinando_oltre_il_bordo_si_ferma_sul_bordo_dell_immagine_ruotata()
    {
        const double w = 1000, h = 800, angolo = 5;
        var da = (400.0, 300.0, 600.0, 500.0);
        var verso = (400.0, 300.0, 1000.0, 800.0); // angolo BR trascinato nell'angolo vuoto

        var (x0, y0, x1, y1) = StraightenGeometry.AvvicinaRect(w, h, angolo, da, verso);

        Assert.True(StraightenGeometry.ContainsRect(w, h, angolo, x0, y0, x1, y1));
        Assert.Equal((400.0, 300.0), (x0, y0)); // l'ancora non si muove
        // Sul bordo: un pixel più in là lungo lo stesso percorso è già fuori.
        Assert.False(StraightenGeometry.ContainsRect(w, h, angolo, x0, y0, x1 + 4, y1 + 3));
        Assert.True(x1 > 900);
    }

    [Fact]
    public void AvvicinaRect_conserva_le_proporzioni()
    {
        var da = (400.0, 300.0, 560.0, 420.0);    // 4:3
        var verso = (400.0, 300.0, 1200.0, 900.0); // 4:3, fuori tela

        var (x0, y0, x1, y1) = StraightenGeometry.AvvicinaRect(1000, 800, -4, da, verso);

        Assert.Equal(4.0 / 3, (x1 - x0) / (y1 - y0), 6);
    }

    [Fact]
    public void AvvicinaRect_con_destinazione_valida_la_restituisce_invariata()
    {
        var verso = (100.0, 100.0, 300.0, 300.0);
        Assert.Equal(verso, StraightenGeometry.AvvicinaRect(1000, 800, 3, (200, 200, 250, 250), verso));
    }

    [Fact]
    public void RecipeApplier_con_raddrizzamento_non_lascia_angoli_trasparenti()
    {
        const int w = 200, h = 150;
        var bgra = new byte[w * h * 4];
        for (var i = 0; i < bgra.Length; i += 4) { bgra[i] = 50; bgra[i + 1] = 100; bgra[i + 2] = 150; bgra[i + 3] = 255; }

        // Crop a tutta tela + 6 gradi: senza correzione gli angoli sarebbero vuoti (alpha 0).
        var (out_, ow, oh) = RecipeApplier.Applica(bgra, w, h, new EditRecipe { StraightenAngleDegrees = 6 });

        Assert.True(ow < w && oh < h);
        for (var i = 3; i < ow * oh * 4; i += 4)
            Assert.Equal(255, out_[i]);
    }
}
