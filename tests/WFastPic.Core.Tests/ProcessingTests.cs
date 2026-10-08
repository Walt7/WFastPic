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
            AutoContrast = true,
            AutoColor = true,
            FillLight = 0.4,
            Highlights = 0.3,
            Shadows = 0.2,
            Temperature = -0.5,
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
