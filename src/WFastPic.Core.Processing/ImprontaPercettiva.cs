using System.Numerics;

namespace WFastPic.Core.Processing;

/// <summary>
/// Impronta percettiva da 64 bit (pHash) per riconoscere la stessa foto salvata due volte, anche
/// ridimensionata o ricompressa (copia WhatsApp, esportata): grigio 32×32 a media d'area, DCT, segno
/// delle 8×8 frequenze più basse rispetto alla mediana. Calcolata sulla miniatura già in cache, mai
/// sull'originale. Due foto con distanza piccola (bit diversi) sono quasi certamente la stessa immagine.
/// </summary>
public static class ImprontaPercettiva
{
    /// <summary>Cambiando il calcolo si alza: le impronte salvate con un'altra versione si ricalcolano.</summary>
    public const int Versione = 1;

    /// <summary>Lato lungo a cui decodificare la miniatura: basta e avanza per il 32×32.</summary>
    public const int LatoAnalisi = 128;

    private const int Lato = 32;
    private const int Frequenze = 8;

    private static readonly double[,] Coseni = CalcolaCoseni();

    /// <summary>Impronta di un'immagine BGRA (4 byte per pixel, righe senza padding).</summary>
    public static ulong Calcola(byte[] bgra, int larghezza, int altezza)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(larghezza, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(altezza, 1);
        if (bgra.Length < 4L * larghezza * altezza) throw new ArgumentException("Buffer più piccolo dell'immagine.", nameof(bgra));

        var grigio = RiduciInGrigio(bgra, larghezza, altezza);

        // DCT 2D solo delle 8×8 frequenze basse: separabile, prima le righe poi le colonne.
        var righe = new double[Lato, Frequenze];
        for (var y = 0; y < Lato; y++)
        {
            for (var u = 0; u < Frequenze; u++)
            {
                var somma = 0.0;
                for (var x = 0; x < Lato; x++) somma += grigio[y, x] * Coseni[u, x];
                righe[y, u] = somma;
            }
        }

        var coefficienti = new double[Frequenze * Frequenze];
        for (var v = 0; v < Frequenze; v++)
        {
            for (var u = 0; u < Frequenze; u++)
            {
                var somma = 0.0;
                for (var y = 0; y < Lato; y++) somma += righe[y, u] * Coseni[v, y];
                coefficienti[v * Frequenze + u] = somma;
            }
        }

        // La componente continua (luminosità media) non entra nella mediana: dipende dall'esposizione.
        var mediana = coefficienti.Skip(1).Order().ElementAt((coefficienti.Length - 1) / 2);
        ulong impronta = 0;
        for (var i = 1; i < coefficienti.Length; i++)
        {
            if (coefficienti[i] > mediana) impronta |= 1UL << i;
        }
        return impronta;
    }

    /// <summary>Quanti bit diversi: 0 = stessa immagine, oltre ~20 = immagini diverse.</summary>
    public static int Distanza(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>Media d'area su una griglia 32×32 (resiste a ridimensionamento e rumore di compressione).</summary>
    private static double[,] RiduciInGrigio(byte[] bgra, int larghezza, int altezza)
    {
        var somme = new double[Lato, Lato];
        var conteggi = new int[Lato, Lato];
        for (var y = 0; y < altezza; y++)
        {
            var cy = (int)((long)y * Lato / altezza);
            var riga = y * larghezza * 4;
            for (var x = 0; x < larghezza; x++)
            {
                var cx = (int)((long)x * Lato / larghezza);
                var p = riga + x * 4;
                somme[cy, cx] += 0.114 * bgra[p] + 0.587 * bgra[p + 1] + 0.299 * bgra[p + 2];
                conteggi[cy, cx]++;
            }
        }

        // Immagini più piccole di 32 px: le celle vuote prendono il pixel più vicino.
        var grigio = new double[Lato, Lato];
        for (var cy = 0; cy < Lato; cy++)
        {
            for (var cx = 0; cx < Lato; cx++)
            {
                if (conteggi[cy, cx] > 0)
                {
                    grigio[cy, cx] = somme[cy, cx] / conteggi[cy, cx];
                    continue;
                }
                var x = Math.Min(larghezza - 1, cx * larghezza / Lato);
                var y = Math.Min(altezza - 1, cy * altezza / Lato);
                var p = (y * larghezza + x) * 4;
                grigio[cy, cx] = 0.114 * bgra[p] + 0.587 * bgra[p + 1] + 0.299 * bgra[p + 2];
            }
        }
        return grigio;
    }

    private static double[,] CalcolaCoseni()
    {
        var coseni = new double[Frequenze, Lato];
        for (var u = 0; u < Frequenze; u++)
        {
            for (var x = 0; x < Lato; x++)
            {
                coseni[u, x] = Math.Cos((2 * x + 1) * u * Math.PI / (2 * Lato));
            }
        }
        return coseni;
    }
}
