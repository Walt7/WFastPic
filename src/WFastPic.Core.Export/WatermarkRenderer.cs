using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace WFastPic.Core.Export;

/// <summary>
/// Disegna un watermark testuale sopra un buffer BGRA8 già decodificato. Usa System.Drawing
/// (GDI+) solo per il rendering del testo su un layer trasparente separato, poi lo fonde a mano
/// nel buffer principale — evita complicazioni di alpha premoltiplicato tra le due API.
/// </summary>
public static class WatermarkRenderer
{
    public static void Applica(byte[] bgra, int larghezza, int altezza, string testo, double opacita, PosizioneWatermark posizione)
    {
        if (string.IsNullOrWhiteSpace(testo)) return;
        opacita = Math.Clamp(opacita, 0.05, 1.0);

        using var overlay = new Bitmap(larghezza, altezza, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(overlay))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var dimensioneFont = Math.Max(12f, altezza / 28f);
            using var font = new Font("Segoe UI", dimensioneFont, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);

            var misura = g.MeasureString(testo, font);
            var (px, py) = CalcolaPosizione(posizione, larghezza, altezza, misura.Width, misura.Height);

            var alphaBordo = (int)(180 * opacita);
            var alphaTesto = (int)(255 * opacita);

            using var pennelloBordo = new SolidBrush(Color.FromArgb(alphaBordo, 0, 0, 0));
            using var pennelloTesto = new SolidBrush(Color.FromArgb(alphaTesto, 255, 255, 255));

            foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (0, -1), (0, 1), (-1, 0), (1, 0) })
                g.DrawString(testo, font, pennelloBordo, px + dx, py + dy);

            g.DrawString(testo, font, pennelloTesto, px, py);
        }

        FondiOverlaySuBuffer(overlay, bgra, larghezza, altezza);
    }

    private static (float X, float Y) CalcolaPosizione(PosizioneWatermark posizione, int larghezza, int altezza, float testoW, float testoH)
    {
        const float margine = 16f;
        return posizione switch
        {
            PosizioneWatermark.AltoSinistra => (margine, margine),
            PosizioneWatermark.AltoDestra => (larghezza - testoW - margine, margine),
            PosizioneWatermark.BassoSinistra => (margine, altezza - testoH - margine),
            PosizioneWatermark.Centro => ((larghezza - testoW) / 2, (altezza - testoH) / 2),
            _ => (larghezza - testoW - margine, altezza - testoH - margine), // BassoDestra
        };
    }

    private static void FondiOverlaySuBuffer(Bitmap overlay, byte[] bgra, int larghezza, int altezza)
    {
        var rect = new Rectangle(0, 0, larghezza, altezza);
        var dati = overlay.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var overlayBuffer = new byte[dati.Stride * altezza];
            System.Runtime.InteropServices.Marshal.Copy(dati.Scan0, overlayBuffer, 0, overlayBuffer.Length);

            for (var y = 0; y < altezza; y++)
            {
                var rigaOverlay = y * dati.Stride;
                var rigaBase = y * larghezza * 4;

                for (var x = 0; x < larghezza; x++)
                {
                    var oOff = rigaOverlay + x * 4;
                    var alpha = overlayBuffer[oOff + 3];
                    if (alpha == 0) continue;

                    var bOff = rigaBase + x * 4;
                    var a = alpha / 255.0;

                    bgra[bOff + 0] = (byte)(overlayBuffer[oOff + 0] * a + bgra[bOff + 0] * (1 - a));
                    bgra[bOff + 1] = (byte)(overlayBuffer[oOff + 1] * a + bgra[bOff + 1] * (1 - a));
                    bgra[bOff + 2] = (byte)(overlayBuffer[oOff + 2] * a + bgra[bOff + 2] * (1 - a));
                }
            }
        }
        finally
        {
            overlay.UnlockBits(dati);
        }
    }
}
