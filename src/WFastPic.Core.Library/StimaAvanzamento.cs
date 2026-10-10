namespace WFastPic.Core.Library;

/// <summary>
/// Stima del tempo rimanente per le barre di caricamento (scansione cartella, miniature),
/// a velocità media costante dall'inizio dell'operazione.
/// </summary>
public static class StimaAvanzamento
{
    private const int CampioniMinimi = 5;
    private static readonly TimeSpan TrascorsoMinimo = TimeSpan.FromSeconds(1);

    /// <summary>Null finché non ci sono abbastanza dati per una stima credibile, o a lavoro finito.</summary>
    public static TimeSpan? TempoRimanente(int completate, int totale, TimeSpan trascorso)
    {
        if (completate < CampioniMinimi || completate >= totale || trascorso < TrascorsoMinimo) return null;
        return trascorso / completate * (totale - completate);
    }

    /// <summary>"340 di 1300 · circa 12 s rimanenti" (la stima compare solo quando è disponibile).</summary>
    public static string Descrivi(int completate, int totale, TimeSpan trascorso)
    {
        var testo = $"{completate} di {totale}";
        var rimanente = TempoRimanente(completate, totale, trascorso);
        if (rimanente is null) return testo;

        return $"{testo} · circa {Durata(rimanente.Value)} rimanenti";
    }

    /// <summary>"12 s" sotto il minuto, "3 min" oltre (arrotondata per eccesso).</summary>
    public static string Durata(TimeSpan durata) => durata.TotalSeconds < 60
        ? $"{Math.Max(1, (int)Math.Ceiling(durata.TotalSeconds))} s"
        : $"{(int)Math.Ceiling(durata.TotalMinutes)} min";
}
