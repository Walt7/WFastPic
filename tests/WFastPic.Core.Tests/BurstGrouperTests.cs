using WFastPic.Core.Library;
using Xunit;

namespace WFastPic.Core.Tests;

public class BurstGrouperTests
{
    [Fact]
    public void Scatti_entro_la_soglia_finiscono_nello_stesso_gruppo()
    {
        var t0 = new DateTime(2026, 8, 5, 15, 22, 0);
        var input = new[]
        {
            new BurstGroupingInput(1, t0),
            new BurstGroupingInput(2, t0.AddSeconds(1)),
            new BurstGroupingInput(3, t0.AddSeconds(2)),
        };

        var gruppi = BurstGrouper.Group(input, TimeSpan.FromSeconds(3));

        Assert.Equal(3, gruppi.Count);
        Assert.Equal(gruppi[1], gruppi[2]);
        Assert.Equal(gruppi[2], gruppi[3]);
    }

    [Fact]
    public void Scatti_oltre_la_soglia_restano_separati()
    {
        var t0 = new DateTime(2026, 8, 5, 15, 22, 0);
        var input = new[]
        {
            new BurstGroupingInput(1, t0),
            new BurstGroupingInput(2, t0.AddSeconds(10)),
        };

        var gruppi = BurstGrouper.Group(input, TimeSpan.FromSeconds(3));

        Assert.Empty(gruppi);
    }

    [Fact]
    public void Foto_senza_data_scatto_non_entra_mai_in_un_gruppo()
    {
        var t0 = new DateTime(2026, 8, 5, 15, 22, 0);
        var input = new[]
        {
            new BurstGroupingInput(1, t0),
            new BurstGroupingInput(2, t0.AddSeconds(1)),
            new BurstGroupingInput(3, null),
        };

        var gruppi = BurstGrouper.Group(input, TimeSpan.FromSeconds(3));

        Assert.True(gruppi.ContainsKey(1));
        Assert.True(gruppi.ContainsKey(2));
        Assert.False(gruppi.ContainsKey(3));
    }

    [Fact]
    public void Due_raffiche_separate_da_un_gap_grande_ricevono_gruppi_diversi()
    {
        var t0 = new DateTime(2026, 8, 5, 15, 22, 0);
        var input = new[]
        {
            new BurstGroupingInput(1, t0),
            new BurstGroupingInput(2, t0.AddSeconds(1)),
            new BurstGroupingInput(3, t0.AddMinutes(5)),
            new BurstGroupingInput(4, t0.AddMinutes(5).AddSeconds(1)),
        };

        var gruppi = BurstGrouper.Group(input, TimeSpan.FromSeconds(3));

        Assert.Equal(gruppi[1], gruppi[2]);
        Assert.Equal(gruppi[3], gruppi[4]);
        Assert.NotEqual(gruppi[1], gruppi[3]);
    }
}
