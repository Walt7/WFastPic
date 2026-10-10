using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace WFastPic.Core.Export;

/// <summary>
/// Disegna una filigrana testuale sopra un buffer BGRA8 già decodificato. Usa System.Drawing
/// (GDI+) solo per il rendering del testo su un layer trasparente grande quanto il testo (non
/// quanto la foto: a 48 MP sarebbero centinaia di MB), poi lo fonde a mano nel buffer principale.
/// </summary>
public static class WatermarkRenderer
{
    private const float FrazioneLarghezzaMassima = 0.9f;

    public static void Applica(byte[] bgra, int larghezza, int altezza, string testo, double opacita, PosizioneWatermark posizione)
    {
        if (string.IsNullOrWhiteSpace(testo) || larghezza <= 0 || altezza <= 0) return;
        opacita = Math.Clamp(opacita, 0.05, 1.0);

        // Proporzionata al lato corto: stessa resa su foto orizzontali e verticali.
        var lato = Math.Min(larghezza, altezza);
        var dimensioneFont = Math.Max(10f, lato / 20f);
        var margine = Math.Max(8f, lato * 0.025f);

        SizeF misura;
        using (var misuratore = new Bitmap(1, 1))
        using (var gm = Graphics.FromImage(misuratore))
        {
            misura = Misura(gm, testo, dimensioneFont);
            // Testo lungo: si rimpicciolisce finché sta nella foto invece di uscire dai bordi.
            if (misura.Width > larghezza * FrazioneLarghezzaMassima)
            {
                dimensioneFont = Math.Max(6f, dimensioneFont * larghezza * FrazioneLarghezzaMassima / misura.Width);
                misura = Misura(gm, testo, dimensioneFont);
            }
        }

        // +2 per il contorno scuro di 1 px attorno alle lettere.
        var layerW = Math.Min(larghezza, (int)Math.Ceiling(misura.Width) + 2);
        var layerH = Math.Min(altezza, (int)Math.Ceiling(misura.Height) + 2);
        var (px, py) = CalcolaPosizione(posizione, larghezza, altezza, layerW, layerH, margine);

        using var overlay = new Bitmap(layerW, layerH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(overlay))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var font = CreaFont(dimensioneFont);

            using var pennelloBordo = new SolidBrush(Color.FromArgb((int)(180 * opacita), 0, 0, 0));
            using var pennelloTesto = new SolidBrush(Color.FromArgb((int)(255 * opacita), 255, 255, 255));

            foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (0, -1), (0, 1), (-1, 0), (1, 0) })
                g.DrawString(testo, font, pennelloBordo, 1 + dx, 1 + dy);

            g.DrawString(testo, font, pennelloTesto, 1, 1);
        }

        FondiOverlay(overlay, bgra, larghezza, px, py);
    }

    /// <summary>Angolo in alto a sinistra del layer, sempre interamente dentro la foto.</summary>
    public static (int X, int Y) CalcolaPosizione(
        PosizioneWatermark posizione, int larghezza, int altezza, int testoW, int testoH, float margine)
    {
        var (x, y) = posizione switch
        {
            PosizioneWatermark.AltoSinistra => (margine, margine),
            PosizioneWatermark.AltoDestra => (larghezza - testoW - margine, margine),
            PosizioneWatermark.BassoSinistra => (margine, altezza - testoH - margine),
            PosizioneWatermark.Centro => ((larghezza - testoW) / 2f, (altezza - testoH) / 2f),
            _ => (larghezza - testoW - margine, altezza - testoH - margine), // BassoDestra
        };

        return (
            Math.Clamp((int)Math.Round(x), 0, Math.Max(0, larghezza - testoW)),
            Math.Clamp((int)Math.Round(y), 0, Math.Max(0, altezza - testoH)));
    }

    private static Font CreaFont(float dimensione) =>
        new("Segoe UI", dimensione, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);

    private static SizeF Misura(Graphics g, string testo, float dimensione)
    {
        using var font = CreaFont(dimensione);
        return g.MeasureString(testo, font);
    }

    private static void FondiOverlay(Bitmap overlay, byte[] bgra, int larghezza, int px, int py)
    {
        var rect = new Rectangle(0, 0, overlay.Width, overlay.Height);
        var dati = overlay.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var overlayBuffer = new byte[dati.Stride * overlay.Height];
            System.Runtime.InteropServices.Marshal.Copy(dati.Scan0, overlayBuffer, 0, overlayBuffer.Length);

            for (var y = 0; y < overlay.Height; y++)
            {
                var rigaOverlay = y * dati.Stride;
                var rigaBase = ((py + y) * larghezza + px) * 4;

                for (var x = 0; x < overlay.Width; x++)
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
