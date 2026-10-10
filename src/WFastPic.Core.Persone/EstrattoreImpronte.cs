using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace WFastPic.Core.Persone;

/// <summary>
/// "Impronta" di un volto con SFace (OpenCV Zoo, licenza Apache 2.0): 128 numeri, normalizzati a
/// lunghezza 1. Due volti della stessa persona hanno impronte vicine: si confrontano con la
/// somiglianza coseno (vedi <see cref="Impronte"/>).
/// </summary>
public sealed class EstrattoreImpronte : IDisposable
{
    private readonly InferenceSession _sessione;

    public EstrattoreImpronte(string percorsoModello)
    {
        _sessione = new InferenceSession(percorsoModello, OpzioniSessione.Crea());
    }

    public float[] Calcola(float[] voltoAllineatoChw)
    {
        var lato = AllineamentoVolto.Lato;
        var ingresso = new DenseTensor<float>(voltoAllineatoChw, [1, 3, lato, lato]);
        using var risultati = _sessione.Run([NamedOnnxValue.CreateFromTensor("data", ingresso)]);
        var impronta = risultati.First().AsTensor<float>().ToArray();
        return Impronte.Normalizza(impronta);
    }

    public void Dispose() => _sessione.Dispose();
}

/// <summary>Operazioni sulle impronte: pure, testabili senza modello.</summary>
public static class Impronte
{
    public const int Dimensione = 128;

    /// <summary>Soglia OpenCV per SFace: sopra questa somiglianza coseno è la stessa persona.</summary>
    public const float SogliaStessaPersona = 0.363f;

    public static float[] Normalizza(float[] v)
    {
        var norma = MathF.Sqrt(v.Sum(x => x * x));
        return norma > 0 ? v.Select(x => x / norma).ToArray() : v;
    }

    /// <summary>Somiglianza coseno tra impronte normalizzate: 1 = identiche, ~0 = estranei.</summary>
    public static float Somiglianza(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float s = 0;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    public static byte[] InByte(float[] impronta)
    {
        var dati = new byte[impronta.Length * sizeof(float)];
        Buffer.BlockCopy(impronta, 0, dati, 0, dati.Length);
        return dati;
    }

    public static float[] DaByte(byte[] dati)
    {
        var impronta = new float[dati.Length / sizeof(float)];
        Buffer.BlockCopy(dati, 0, impronta, 0, impronta.Length * sizeof(float));
        return impronta;
    }
}
