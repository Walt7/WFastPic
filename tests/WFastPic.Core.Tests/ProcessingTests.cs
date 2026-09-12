using WFastPic.Core.Processing;
using Xunit;

namespace WFastPic.Core.Tests;

public class ProcessingTests
{
    [Fact]
    public void EditRecipe_default_e_a_tutta_immagine_senza_correzioni()
    {
        var recipe = EditRecipe.Default;

        Assert.Equal(0, recipe.CropX);
        Assert.Equal(0, recipe.CropY);
        Assert.Equal(1, recipe.CropWidth);
        Assert.Equal(1, recipe.CropHeight);
        Assert.Equal(0, recipe.StraightenAngleDegrees);
        Assert.False(recipe.AutoCorrect);
    }

    [Fact]
    public void Serializer_round_trip_preserva_tutti_i_campi()
    {
        var originale = new EditRecipe
        {
            CropX = 0.1,
            CropY = 0.2,
            CropWidth = 0.5,
            CropHeight = 0.6,
            StraightenAngleDegrees = 3.5,
            AutoCorrect = true,
        };

        var json = EditRecipeSerializer.ToJson(originale);
        var risultato = EditRecipeSerializer.FromJson(json);

        Assert.Equal(originale, risultato);
    }

    [Fact]
    public void Serializer_json_corrotto_torna_ricetta_default_senza_eccezioni()
    {
        var risultato = EditRecipeSerializer.FromJson("{ non e' json valido");
        Assert.Equal(EditRecipe.Default, risultato);
    }

    [Fact]
    public void CropCalculator_converte_rettangolo_normalizzato_in_pixel()
    {
        var recipe = new EditRecipe { CropX = 0.25, CropY = 0.25, CropWidth = 0.5, CropHeight = 0.5 };

        var (x, y, w, h) = CropCalculator.ToPixelRect(recipe, larghezzaImmagine: 1000, altezzaImmagine: 800);

        Assert.Equal(250, x);
        Assert.Equal(200, y);
        Assert.Equal(500, w);
        Assert.Equal(400, h);
    }

    [Fact]
    public void CropCalculator_clampa_un_rettangolo_che_esce_dai_bordi()
    {
        var recipe = new EditRecipe { CropX = 0.8, CropY = 0.8, CropWidth = 0.5, CropHeight = 0.5 };

        var (x, y, w, h) = CropCalculator.ToPixelRect(recipe, larghezzaImmagine: 1000, altezzaImmagine: 1000);

        Assert.True(x + w <= 1000);
        Assert.True(y + h <= 1000);
    }

    [Fact]
    public void AutoCorrectProcessor_allunga_un_istogramma_compresso_verso_tutta_la_gamma()
    {
        const int width = 4;
        const int height = 4;
        var bgra = new byte[width * height * 4];

        // Tutti i pixel in una fascia compressa e scura (100-140): dopo la correzione l'istogramma
        // deve occupare una gamma molto piu ampia (piu vicina a 0-255).
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var v = (byte)(100 + (i / 4 % 40));
            bgra[i] = v; bgra[i + 1] = v; bgra[i + 2] = v; bgra[i + 3] = 255;
        }

        AutoCorrectProcessor.ApplyInPlace(bgra, width, height);

        byte min = 255, max = 0;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            min = Math.Min(min, bgra[i]);
            max = Math.Max(max, bgra[i]);
        }

        Assert.True(max - min > 100, $"Gamma dopo correzione troppo stretta: {min}-{max}");
    }

    [Fact]
    public void AutoCorrectProcessor_non_esplode_su_immagine_a_tinta_unita()
    {
        var bgra = new byte[2 * 2 * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 128; bgra[i + 1] = 128; bgra[i + 2] = 128; bgra[i + 3] = 255;
        }

        var eccezione = Record.Exception(() => AutoCorrectProcessor.ApplyInPlace(bgra, 2, 2));
        Assert.Null(eccezione);
    }

    [Fact]
    public void PixelOps_crop_estrae_il_rettangolo_corretto()
    {
        // Immagine 4x4, ogni pixel colorato col proprio indice nel canale B per riconoscerlo dopo il ritaglio.
        const int larghezza = 4, altezza = 4;
        var bgra = new byte[larghezza * altezza * 4];
        for (var y = 0; y < altezza; y++)
            for (var x = 0; x < larghezza; x++)
                bgra[(y * larghezza + x) * 4] = (byte)(y * larghezza + x);

        var ritagliato = PixelOps.Crop(bgra, larghezza, altezza, x: 1, y: 1, larghezzaCrop: 2, altezzaCrop: 2);

        Assert.Equal(2 * 2 * 4, ritagliato.Length);
        Assert.Equal(1 * larghezza + 1, ritagliato[0]); // pixel (1,1) originale
        Assert.Equal(1 * larghezza + 2, ritagliato[4]); // pixel (2,1) originale
    }

    [Fact]
    public void PixelOps_rotate_di_zero_gradi_non_modifica_il_buffer()
    {
        var bgra = new byte[] { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255 };
        var ruotato = PixelOps.Rotate(bgra, 2, 2, 0);

        Assert.Same(bgra, ruotato);
    }

    [Fact]
    public void PixelOps_rotate_mantiene_le_stesse_dimensioni()
    {
        var bgra = new byte[8 * 8 * 4];
        var ruotato = PixelOps.Rotate(bgra, 8, 8, 5.0);

        Assert.Equal(bgra.Length, ruotato.Length);
    }

    [Fact]
    public void RecipeApplier_con_ricetta_default_non_altera_dimensioni_ne_pixel()
    {
        var bgra = new byte[] { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255 };
        var (risultato, w, h) = RecipeApplier.Applica(bgra, 2, 2, EditRecipe.Default);

        Assert.Equal(2, w);
        Assert.Equal(2, h);
        Assert.Equal(bgra, risultato);
    }

    [Fact]
    public void RecipeApplier_applica_il_crop_e_restituisce_dimensioni_ridotte()
    {
        const int larghezza = 4, altezza = 4;
        var bgra = new byte[larghezza * altezza * 4];
        var ricetta = new EditRecipe { CropX = 0.5, CropY = 0.5, CropWidth = 0.5, CropHeight = 0.5 };

        var (_, w, h) = RecipeApplier.Applica(bgra, larghezza, altezza, ricetta);

        Assert.Equal(2, w);
        Assert.Equal(2, h);
    }
}
