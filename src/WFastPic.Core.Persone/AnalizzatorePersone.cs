namespace WFastPic.Core.Persone;

/// <summary>Un volto pronto per il riconoscimento: riquadro normalizzato (0..1), impronta, miniatura.</summary>
public sealed record VoltoAnalizzato(
    double X0, double Y0, double X1, double Y1,
    float Punteggio,
    float[] Impronta,
    byte[] MiniaturaBgra);

/// <summary>
/// Rilevamento + allineamento + impronta per una foto, con i due modelli caricati una volta sola.
/// La miniatura (112×112 BGRA, il volto già allineato) serve da "avatar" nel pannello Persone.
/// </summary>
public sealed class AnalizzatorePersone : IDisposable
{
    /// <summary>Volti più piccoli di così (in pixel della foto analizzata) danno impronte inaffidabili.</summary>
    public const float LatoMinimoVolto = 36;

    private readonly RilevatoreYuNet _rilevatore;
    private readonly EstrattoreImpronte _estrattore;

    public AnalizzatorePersone(string cartellaModelli)
    {
        _rilevatore = new RilevatoreYuNet(ModelliPersone.Percorso(cartellaModelli, ModelliPersone.Rilevatore));
        _estrattore = new EstrattoreImpronte(ModelliPersone.Percorso(cartellaModelli, ModelliPersone.Riconoscimento));
    }

    public IReadOnlyList<VoltoAnalizzato> Analizza(byte[] bgra, int larghezza, int altezza)
    {
        var risultato = new List<VoltoAnalizzato>();
        foreach (var volto in _rilevatore.Rileva(bgra, larghezza, altezza))
        {
            if (Math.Min(volto.Larghezza, volto.Altezza) < LatoMinimoVolto) continue;

            var allineato = AllineamentoVolto.Allinea(bgra, larghezza, altezza, volto.Punti);
            risultato.Add(new VoltoAnalizzato(
                volto.X0 / larghezza, volto.Y0 / altezza, volto.X1 / larghezza, volto.Y1 / altezza,
                volto.Punteggio,
                _estrattore.Calcola(allineato),
                InBgra(allineato)));
        }
        return risultato;
    }

    private static byte[] InBgra(float[] rgbChw)
    {
        var lato = AllineamentoVolto.Lato;
        var piano = lato * lato;
        var bgra = new byte[piano * 4];
        for (var i = 0; i < piano; i++)
        {
            bgra[i * 4] = (byte)Math.Clamp(rgbChw[2 * piano + i], 0, 255);
            bgra[i * 4 + 1] = (byte)Math.Clamp(rgbChw[piano + i], 0, 255);
            bgra[i * 4 + 2] = (byte)Math.Clamp(rgbChw[i], 0, 255);
            bgra[i * 4 + 3] = 255;
        }
        return bgra;
    }

    public void Dispose()
    {
        _rilevatore.Dispose();
        _estrattore.Dispose();
    }
}
