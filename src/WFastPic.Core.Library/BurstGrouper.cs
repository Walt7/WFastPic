namespace WFastPic.Core.Library;

public sealed record BurstGroupingInput(long Id, DateTime? CaptureDate);

/// <summary>
/// Raggruppa foto scattate a pochi secondi di distanza (raffiche/quasi-duplicati) in base alla sola
/// data scatto EXIF: ordina per data e chiude un gruppo quando il gap tra due scatti consecutivi
/// supera la soglia. Nessun confronto pixel/hash percettivo — quello resta fuori scope per ora.
/// Foto senza data scatto (es. JPG senza EXIF) non entrano mai in un gruppo.
/// </summary>
public static class BurstGrouper
{
    public static readonly TimeSpan SogliaDefault = TimeSpan.FromSeconds(3);

    public static IReadOnlyDictionary<long, int> Group(IReadOnlyList<BurstGroupingInput> foto, TimeSpan? soglia = null)
    {
        var s = soglia ?? SogliaDefault;
        var risultato = new Dictionary<long, int>();

        var conData = foto
            .Where(f => f.CaptureDate.HasValue)
            .OrderBy(f => f.CaptureDate!.Value)
            .ToList();

        var prossimoGroupId = 0;
        DateTime? precedente = null;
        var gruppoCorrente = new List<long>();

        void ChiudiGruppo()
        {
            if (gruppoCorrente.Count > 1)
            {
                prossimoGroupId++;
                foreach (var id in gruppoCorrente)
                {
                    risultato[id] = prossimoGroupId;
                }
            }
            gruppoCorrente.Clear();
        }

        foreach (var f in conData)
        {
            if (precedente is not null && f.CaptureDate!.Value - precedente.Value > s)
            {
                ChiudiGruppo();
            }
            gruppoCorrente.Add(f.Id);
            precedente = f.CaptureDate;
        }
        ChiudiGruppo();

        return risultato;
    }
}
