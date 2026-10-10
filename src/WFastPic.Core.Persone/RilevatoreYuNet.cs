using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace WFastPic.Core.Persone;

/// <summary>
/// Rilevamento volti con YuNet (OpenCV Zoo, licenza MIT): rispetto al rilevatore di Windows dà anche i
/// 5 punti del volto, indispensabili per allinearlo prima del riconoscimento. Il modello ha ingresso
/// fisso 640×640: la foto viene ridotta mantenendo le proporzioni e completata con nero in basso/a
/// destra; le coordinate tornano poi in pixel della foto. Decodifica delle uscite come in OpenCV
/// (FaceDetectorYN): per ogni passo 8/16/32, punteggio = √(classe·oggetto), riquadro e punti
/// relativi alla cella, poi soppressione dei duplicati.
/// </summary>
public sealed class RilevatoreYuNet : IDisposable
{
    private const int Lato = 640;
    private static readonly int[] Passi = [8, 16, 32];

    private readonly InferenceSession _sessione;

    public float SogliaPunteggio { get; init; } = 0.8f;
    public float SogliaSovrapposizione { get; init; } = 0.3f;

    public RilevatoreYuNet(string percorsoModello)
    {
        _sessione = new InferenceSession(percorsoModello, OpzioniSessione.Crea());
    }

    public IReadOnlyList<VoltoRilevato> Rileva(byte[] bgra, int larghezza, int altezza)
    {
        var scala = Math.Min((float)Lato / larghezza, (float)Lato / altezza);
        var ingresso = PreparaIngresso(bgra, larghezza, altezza, scala);

        using var risultati = _sessione.Run([NamedOnnxValue.CreateFromTensor("input", ingresso)]);
        var uscite = risultati.ToDictionary(r => r.Name, r => r.AsTensor<float>());

        var candidati = new List<VoltoRilevato>();
        foreach (var passo in Passi)
        {
            var classe = uscite[$"cls_{passo}"];
            var oggetto = uscite[$"obj_{passo}"];
            var riquadri = uscite[$"bbox_{passo}"];
            var punti = uscite[$"kps_{passo}"];
            var colonne = Lato / passo;
            var righe = Lato / passo;

            for (var r = 0; r < righe; r++)
            {
                for (var c = 0; c < colonne; c++)
                {
                    var i = r * colonne + c;
                    var cls = Math.Clamp(classe[0, i, 0], 0f, 1f);
                    var obj = Math.Clamp(oggetto[0, i, 0], 0f, 1f);
                    var punteggio = MathF.Sqrt(cls * obj);
                    if (punteggio < SogliaPunteggio) continue;

                    var cx = (c + riquadri[0, i, 0]) * passo;
                    var cy = (r + riquadri[0, i, 1]) * passo;
                    var w = MathF.Exp(riquadri[0, i, 2]) * passo;
                    var h = MathF.Exp(riquadri[0, i, 3]) * passo;

                    var kp = new (float, float)[5];
                    for (var n = 0; n < 5; n++)
                        kp[n] = ((punti[0, i, 2 * n] + c) * passo / scala, (punti[0, i, 2 * n + 1] + r) * passo / scala);

                    candidati.Add(new VoltoRilevato(
                        Math.Max(0, (cx - w / 2) / scala), Math.Max(0, (cy - h / 2) / scala),
                        Math.Min(larghezza, (cx + w / 2) / scala), Math.Min(altezza, (cy + h / 2) / scala),
                        punteggio, kp));
                }
            }
        }

        return SopprimiDuplicati(candidati, SogliaSovrapposizione);
    }

    /// <summary>Riduzione con media a blocchi/bilineare verso 640×640, BGR float 0..255 in formato CHW.</summary>
    private static DenseTensor<float> PreparaIngresso(byte[] bgra, int larghezza, int altezza, float scala)
    {
        var tensore = new DenseTensor<float>([1, 3, Lato, Lato]);
        var wRidotta = Math.Min(Lato, (int)MathF.Round(larghezza * scala));
        var hRidotta = Math.Min(Lato, (int)MathF.Round(altezza * scala));
        var passo = 1 / scala;

        for (var y = 0; y < hRidotta; y++)
        {
            var sy0 = (int)(y * passo);
            var sy1 = Math.Max(sy0 + 1, Math.Min(altezza, (int)((y + 1) * passo)));
            for (var x = 0; x < wRidotta; x++)
            {
                var sx0 = (int)(x * passo);
                var sx1 = Math.Max(sx0 + 1, Math.Min(larghezza, (int)((x + 1) * passo)));

                // Con riduzioni forti si campiona al massimo una griglia 4×4 del blocco: abbastanza
                // per un rilevatore, molto più veloce della media su tutti i pixel.
                var stepY = Math.Max(1, (sy1 - sy0) / 4);
                var stepX = Math.Max(1, (sx1 - sx0) / 4);
                float b = 0, g = 0, r = 0; var n = 0;
                for (var sy = sy0; sy < sy1; sy += stepY)
                {
                    for (var sx = sx0; sx < sx1; sx += stepX)
                    {
                        var o = (sy * larghezza + sx) * 4;
                        b += bgra[o]; g += bgra[o + 1]; r += bgra[o + 2]; n++;
                    }
                }

                tensore[0, 0, y, x] = b / n;
                tensore[0, 1, y, x] = g / n;
                tensore[0, 2, y, x] = r / n;
            }
        }

        return tensore;
    }

    internal static List<VoltoRilevato> SopprimiDuplicati(List<VoltoRilevato> candidati, float soglia)
    {
        var tenuti = new List<VoltoRilevato>();
        foreach (var v in candidati.OrderByDescending(c => c.Punteggio))
        {
            if (tenuti.All(t => Sovrapposizione(t, v) < soglia)) tenuti.Add(v);
        }
        return tenuti;
    }

    private static float Sovrapposizione(VoltoRilevato a, VoltoRilevato b)
    {
        var ix = MathF.Max(0, MathF.Min(a.X1, b.X1) - MathF.Max(a.X0, b.X0));
        var iy = MathF.Max(0, MathF.Min(a.Y1, b.Y1) - MathF.Max(a.Y0, b.Y0));
        var inter = ix * iy;
        var unione = a.Larghezza * a.Altezza + b.Larghezza * b.Altezza - inter;
        return unione <= 0 ? 0 : inter / unione;
    }

    public void Dispose() => _sessione.Dispose();
}

internal static class OpzioniSessione
{
    // I modelli OpenCV hanno gli inizializzatori tra gli ingressi: ONNX Runtime lo segnala con decine
    // di avvisi innocui a ogni caricamento. Solo errori nel log.
    public static SessionOptions Crea() => new()
    {
        LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
    };
}
