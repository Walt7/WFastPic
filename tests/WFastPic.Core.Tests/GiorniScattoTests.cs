using WFastPic.Core.Library;
using Xunit;

namespace WFastPic.Core.Tests;

public class GiorniScattoTests
{
    [Fact]
    public void Raggruppa_per_giorno_mantenendo_l_ordine_e_mette_le_foto_senza_data_in_fondo()
    {
        DateTime?[] date =
        [
            new DateTime(2026, 7, 15, 9, 0, 0),
            new DateTime(2026, 7, 15, 23, 59, 0),
            new DateTime(2026, 7, 16, 0, 1, 0),
            null,
            new DateTime(2026, 7, 16, 18, 0, 0),
            null,
        ];

        var gruppi = GiorniScatto.Raggruppa(Enumerable.Range(0, date.Length), i => date[i]);

        Assert.Equal(3, gruppi.Count);
        Assert.Equal(new DateOnly(2026, 7, 15), gruppi[0].Giorno);
        Assert.Equal([0, 1], gruppi[0].Elementi);
        Assert.Equal([2, 4], gruppi[1].Elementi);
        Assert.Null(gruppi[2].Giorno);
        Assert.Equal([3, 5], gruppi[2].Elementi);
    }

    [Fact]
    public void Titolo_in_italiano_con_numero_di_foto()
    {
        Assert.Equal("Mercoledì 15 luglio 2026 · 37 foto", GiorniScatto.Titolo(new DateOnly(2026, 7, 15), 37));
        Assert.Equal("Senza data · 1 foto", GiorniScatto.Titolo(null, 1));
    }
}
