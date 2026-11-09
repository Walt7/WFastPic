namespace WFastPic.Mcp;

/// <summary>Un posto fotografato spesso: dove, quante foto, in quali anni e in quanti giorni diversi.</summary>
public sealed record LuogoFrequente(
    double Latitudine, double Longitudine, int Foto, IReadOnlyList<int> Anni, int Giorni,
    DateTime Primo, DateTime Ultimo, string? Nome, string Esempio);

/// <summary>
/// I posti dove ho fatto più foto, in più occasioni: raggruppa le foto con GPS per vicinanza e li ordina per
/// numero di anni diversi e di giorni diversi (un posto fotografato in tanti anni conta più di una vacanza sola).
/// Sola lettura: usa i nomi dei luoghi già in cache nei sidecar, non chiede nulla a internet.
/// </summary>
public static class LuoghiFrequenti
{
    private sealed class Gruppo
    {
        public double SommaLat, SommaLon;
        public List<FotoTrovata> Foto = [];
        public double Lat => SommaLat / Foto.Count;
        public double Lon => SommaLon / Foto.Count;
    }

    public static double DistanzaMetri(double lat1, double lon1, double lat2, double lon2)
    {
        var dy = (lat1 - lat2) * 111_000;
        var dx = (lon1 - lon2) * 111_000 * Math.Cos(lat1 * Math.PI / 180);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static List<LuogoFrequente> Calcola(
        IEnumerable<FotoTrovata> foto, double raggioMetri, int minimoAnni, int minimoGiorni,
        (double Lat, double Lon)? casa, double escludiKm)
    {
        var gruppi = new List<Gruppo>();
        foreach (var f in foto.Where(f => f.Latitudine is not null && f.Longitudine is not null && f.Data is not null))
        {
            var (la, lo) = (f.Latitudine!.Value, f.Longitudine!.Value);
            if (casa is { } c && escludiKm > 0 && DistanzaMetri(la, lo, c.Lat, c.Lon) < escludiKm * 1000) continue;
            var g = gruppi.FirstOrDefault(g => DistanzaMetri(g.Lat, g.Lon, la, lo) <= raggioMetri);
            if (g is null) gruppi.Add(g = new Gruppo());
            g.Foto.Add(f); g.SommaLat += la; g.SommaLon += lo;
        }

        return gruppi
            .Select(g =>
            {
                var date = g.Foto.Select(f => f.Data!.Value).OrderBy(d => d).ToList();
                var nome = g.Foto.Where(f => f.Luogo is not null).GroupBy(f => f.Luogo!)
                    .OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault();
                return new LuogoFrequente(
                    Math.Round(g.Lat, 5), Math.Round(g.Lon, 5), g.Foto.Count,
                    date.Select(d => d.Year).Distinct().OrderBy(a => a).ToList(),
                    date.Select(d => d.Date).Distinct().Count(),
                    date[0], date[^1], nome, g.Foto[0].Percorso);
            })
            .Where(l => l.Anni.Count >= minimoAnni && l.Giorni >= minimoGiorni)
            .OrderByDescending(l => l.Anni.Count).ThenByDescending(l => l.Giorni).ThenByDescending(l => l.Foto)
            .ToList();
    }
}
