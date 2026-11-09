namespace WFastPic.Core.Decoding;

/// <summary>
/// Controlla (e ripara) le intestazioni di un JPEG senza decodificarlo. Segnala i file «sequenziali» (SOF0/SOF1) con un
/// blocco di scansione (SOS) dai parametri non standard — devono essere Ss=0, Se=63, Ah/Al=0 —: libjpeg li legge lo
/// stesso ma stampa «Invalid SOS parameters for sequential JPEG», segno di un file scritto male da qualche programma.
/// La riparazione riscrive solo quei tre byte, ai valori che la specifica prevede: la parte compressa non si tocca.
/// </summary>
public static class IntestazioneJpeg
{
    /// <summary>Null se l'intestazione è regolare, altrimenti il motivo in parole.</summary>
    public static string? Problema(ReadOnlySpan<byte> d) => Analizza(d, null);

    /// <summary>Copia con i parametri SOS riportati allo standard, o null se non c'è nulla da riparare.</summary>
    public static byte[]? Ripara(ReadOnlySpan<byte> d)
    {
        var posizioni = new List<int>();
        if (Analizza(d, posizioni) is null) return null;

        var copia = d.ToArray();
        foreach (var p in posizioni) { copia[p] = 0; copia[p + 1] = 63; copia[p + 2] = 0; }
        return copia;
    }

    // Percorre i segmenti; per ogni SOS anomalo registra la posizione dei tre byte Ss, Se, AhAl.
    private static string? Analizza(ReadOnlySpan<byte> d, List<int>? posizioni)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return null; // non è un JPEG: non è questo il problema
        string? motivo = null;
        var i = 2;
        var sof = -1;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF) break; // dati fuori posto: lo vedrà il decodificatore
            var m = d[i + 1];
            if (m == 0xFF) { i++; continue; }
            if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            if (m == 0xD9) break; // EOI
            var lunghezza = (d[i + 2] << 8) | d[i + 3];
            if (m is 0xC0 or 0xC1 or 0xC2) sof = m;
            if (m == 0xDA)
            {
                var p = i + 5 + 2 * d[i + 4];
                if (p + 3 > d.Length) break;
                if (sof is 0xC0 or 0xC1 && (d[p] != 0 || d[p + 1] != 63 || d[p + 2] != 0))
                {
                    motivo ??= $"SOS sequenziale con Ss={d[p]} Se={d[p + 1]} AhAl=0x{d[p + 2]:X2}";
                    if (posizioni is null) return motivo;
                    posizioni.Add(p);
                }

                // Salta i dati compressi fino al prossimo marcatore vero (non 00 e non RSTn).
                i += 2 + lunghezza;
                while (i + 1 < d.Length && !(d[i] == 0xFF && d[i + 1] != 0 && !(d[i + 1] >= 0xD0 && d[i + 1] <= 0xD7))) i++;
                continue;
            }
            i += 2 + lunghezza;
        }
        return motivo;
    }
}
