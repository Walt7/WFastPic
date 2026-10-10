using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WFastPic.Core.Decoding;
using WFastPic.Core.Processing;

namespace WFastPic.App;

/// <summary>Voce cliccabile dei ritagli suggeriti.</summary>
public sealed class SuggerimentoRitaglioUi
{
    public required ImageSource Anteprima { get; init; }
    public required string Etichetta { get; init; }
    public required RitaglioSuggerito Ritaglio { get; init; }
}

/// <summary>
/// Ritagli suggeriti stile Picasa nel pannello di ritaglio. Calcolati in background sulla tela come
/// la si vede (raddrizzata, con le regolazioni di tono), con i volti rilevati da Windows che non vengono
/// mai tagliati. Un clic posiziona il riquadro, che resta modificabile a mano; Invio applica.
/// </summary>
public sealed partial class ViewerView
{
    private const int LatoMiniaturaSuggerimento = 104;
    private int _richiestaSuggerimenti;
    private DispatcherTimer? _timerSuggerimenti;

    private async void CalcolaSuggerimentiRitaglio()
    {
        var richiesta = ++_richiestaSuggerimenti;
        var originale = _viewerPixelOriginali;
        if (originale is null || !_geometriaAttiva) return;

        var (wo, ho) = (_viewerPixelWidth, _viewerPixelHeight);
        var angolo = Math.Round(ViewerStraightenSlider.Value, 1);
        var tono = _ricettaCorrente with { CropX = 0, CropY = 0, CropWidth = 1, CropHeight = 1, StraightenAngleDegrees = 0 };
        var foto = _fotoRicetta;
        var db = Host.Database;
        var manager = Host.SelectionManager;

        ViewerSuggerimentiAnello.IsActive = true;
        try
        {
            var risultato = await Task.Run(async () =>
            {
                // Ritagli proposti sulla foto già girata/specchiata, come quelli fatti a mano.
                var (orientata, w, h) = Orientamento.Applica(originale, wo, ho, tono);
                var tela = angolo != 0 ? PixelOps.Rotate(orientata, w, h, angolo) : orientata;
                if (tono.HasTone)
                {
                    if (ReferenceEquals(tela, originale)) tela = (byte[])originale.Clone();
                    ToneProcessor.ApplyInPlace(tela, w, h, tono);
                }

                // Volti già trovati dalla ricerca in background (sulla foto non raddrizzata); se la foto
                // non è ancora stata analizzata si cercano adesso e si salvano per le prossime volte.
                IReadOnlyList<(double X0, double Y0, double X1, double Y1)>? volti = null;
                if (foto is not null && db is not null)
                {
                    try { volti = db.GetVolti(foto.Id); } catch (ObjectDisposedException) { }
                }
                if (volti is null)
                {
                    // Sull'originale: i volti salvati nel sidecar sono sempre in coordinate del file.
                    volti = await RilevatoreVolti.RilevaAsync(originale, wo, ho);
                    if (foto is not null) manager?.SalvaVolti(foto.Id, volti);
                }

                var zone = volti
                    .Select(v => Orientamento.TrasformaRettangolo(v.X0, v.Y0, v.X1, v.Y1, tono))
                    .Select(v => StraightenGeometry.RuotaRettangolo(w, h, angolo, v.X0 * w, v.Y0 * h, v.X1 * w, v.Y1 * h))
                    .Select(r => new ZonaImportante(r.X0 / w, r.Y0 / h, r.X1 / w, r.Y1 / h))
                    .ToList();
                var proposte = SuggerimentiRitaglio.Calcola(tela, w, h, angolo, zone);
                var miniature = proposte
                    .Select(p => (Proposta: p, Miniatura: SuggerimentiRitaglio.Miniatura(tela, w, h, p, LatoMiniaturaSuggerimento)))
                    .ToList();
                return (Miniature: miniature, Volti: zone.Count);
            });

            if (foto is not null) foto.NumeroVolti = risultato.Volti;

            // Foto cambiata, ritaglio chiuso o angolo cambiato nel frattempo: risultato vecchio.
            if (richiesta != _richiestaSuggerimenti || !_geometriaAttiva || !ReferenceEquals(originale, _viewerPixelOriginali)) return;

            ViewerSuggerimentiList.ItemsSource = risultato.Miniature.Select(m => new SuggerimentoRitaglioUi
            {
                Anteprima = CreaBitmap(m.Miniatura.Bgra, m.Miniatura.Larghezza, m.Miniatura.Altezza),
                Etichetta = m.Proposta.Etichetta,
                Ritaglio = m.Proposta,
            }).ToList();

            ViewerSuggerimentiVolti.Text = risultato.Volti switch
            {
                0 => "",
                1 => "Tiene conto di 1 volto.",
                var n => $"Tiene conto di {n} volti.",
            };
            ViewerSuggerimentiVolti.Visibility = risultato.Volti > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception)
        {
            // Senza suggerimenti il ritaglio manuale funziona come sempre.
            if (richiesta == _richiestaSuggerimenti) ViewerSuggerimentiList.ItemsSource = null;
        }
        finally
        {
            if (richiesta == _richiestaSuggerimenti) ViewerSuggerimentiAnello.IsActive = false;
        }
    }

    /// <summary>Raddrizzando cambia la parte utilizzabile della foto: si ricalcola a slider fermo.</summary>
    private void ProgrammaSuggerimentiRitaglio()
    {
        if (_timerSuggerimenti is null)
        {
            _timerSuggerimenti = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _timerSuggerimenti.Tick += (_, _) =>
            {
                _timerSuggerimenti.Stop();
                CalcolaSuggerimentiRitaglio();
            };
        }

        _timerSuggerimenti.Stop();
        _timerSuggerimenti.Start();
    }

    private void SvuotaSuggerimentiRitaglio()
    {
        _richiestaSuggerimenti++;
        _timerSuggerimenti?.Stop();
        ViewerSuggerimentiList.ItemsSource = null;
        ViewerSuggerimentiVolti.Visibility = Visibility.Collapsed;
        ViewerSuggerimentiAnello.IsActive = false;
    }

    private void ViewerSuggerimento_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (!_geometriaAttiva || e.ClickedItem is not SuggerimentoRitaglioUi { Ritaglio: var r }) return;

        _cropN0X = r.X0; _cropN0Y = r.Y0;
        _cropN1X = r.X1; _cropN1Y = r.Y1;
        _cropAutoPieno = false;
        _cropVoluto = (r.X0, r.Y0, r.X1, r.Y1);

        // Le maniglie continuano con la stessa proporzione (se è tra quelle disponibili).
        var orizzontale = Math.Max(r.Rapporto, 1 / r.Rapporto);
        // "Come la foto" diventa "originale"; le altre la proporzione uguale, altrimenti "libera".
        var indice = Enumerable.Range(0, ProporzioniDisponibili.Length)
            .FirstOrDefault(i => RapportoProporzione(i) is double v && Math.Abs(v - orizzontale) < 0.01, 0);
        _proporzioneIndice = indice;
        ViewerProporzioneToggle.Content = $"Proporzioni: {ProporzioniDisponibili[_proporzioneIndice].Etichetta}";

        AggiornaOverlayCrop();
        ViewerOverlay.Focus(FocusState.Programmatic);
    }

    private static WriteableBitmap CreaBitmap(byte[] bgra, int larghezza, int altezza)
    {
        var bitmap = new WriteableBitmap(larghezza, altezza);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(bgra, 0, larghezza * altezza * 4);
        }
        bitmap.Invalidate();
        return bitmap;
    }
}
