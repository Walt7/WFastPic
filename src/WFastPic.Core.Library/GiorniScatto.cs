using System.Globalization;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.Core.Library;

/// <summary>
/// Divisione della griglia per giorno di scatto (opzionale, come le intestazioni per data di
/// Picasa). L'ordine resta quello in ingresso (cronologico): i giorni seguono le foto, le foto
/// senza data finiscono nel gruppo "Senza data".
/// </summary>
public static class GiorniScatto
{
    public static DateOnly? Giorno(DateTime? dataScatto) =>
        dataScatto is DateTime data ? DateOnly.FromDateTime(data) : null;

    /// <summary>Raggruppa elementi consecutivi dello stesso giorno, mantenendo l'ordine.</summary>
    public static List<(DateOnly? Giorno, List<T> Elementi)> Raggruppa<T>(IEnumerable<T> elementi, Func<T, DateTime?> dataScatto)
    {
        var gruppi = new List<(DateOnly? Giorno, List<T> Elementi)>();
        var indicePerGiorno = new Dictionary<DateOnly, int>();
        var senzaData = -1;

        foreach (var elemento in elementi)
        {
            var giorno = Giorno(dataScatto(elemento));
            int indice;
            if (giorno is DateOnly g)
            {
                if (!indicePerGiorno.TryGetValue(g, out indice))
                {
                    indice = gruppi.Count;
                    indicePerGiorno[g] = indice;
                    gruppi.Add((g, []));
                }
            }
            else
            {
                if (senzaData < 0)
                {
                    senzaData = gruppi.Count;
                    gruppi.Add((null, []));
                }
                indice = senzaData;
            }

            gruppi[indice].Elementi.Add(elemento);
        }

        return gruppi;
    }

    /// <summary>"Mercoledì 15 luglio 2026 · 37 foto", oppure "Senza data · 3 foto".</summary>
    public static string Titolo(DateOnly? giorno, int numeroFoto)
    {
        var conteggio = Foto(numeroFoto);
        if (giorno is not DateOnly g) return $"{T("Senza data")} · {conteggio}";

        var testo = g.ToString("dddd d MMMM yyyy", Cultura);
        return $"{char.ToUpper(testo[0], Cultura)}{testo[1..]} · {conteggio}";
    }
}
