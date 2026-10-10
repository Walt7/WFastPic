using System.Text.Json;

namespace WFastPic.App;

public sealed record Impostazioni(int DimensioneCellaGriglia = 160)
{
    /// <summary>Nomi dei luoghi chiesti a OpenStreetMap (le coordinate escono dal PC); spento = solo cache.</summary>
    public bool NomiLuoghiOnline { get; init; } = true;
}

