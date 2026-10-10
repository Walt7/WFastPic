using System.Diagnostics;
using Microsoft.UI.Xaml;
using WFastPic.Core.Decoding;
using WFastPic.Core.Library;

namespace WFastPic.App;

/// <summary>
/// Ricerca dei volti in background, come la scansione volti di Picasa: parte solo quando le miniature
/// della griglia sono finite, analizza una foto alla volta (preview da ~1024 px, rilevatore di Windows
/// offline) e salva il risultato nel sidecar, così alla riapertura non si ripete. Con il viewer aperto
/// si mette in pausa: il ciclo ←/→ non deve mai aspettare un decode in più.
/// </summary>
public partial class MainWindow
{
    private CancellationTokenSource? _ctsVolti;
    private bool _ricercaVoltiAvviata;

    private void FermaRicercaVolti()
    {
        _ctsVolti?.Cancel();
        _ctsVolti = null;
        _ricercaVoltiAvviata = false;
    }

    private void AvviaRicercaVolti()
    {
        if (_ricercaVoltiAvviata || !RilevatoreVolti.Disponibile) return;
        _ricercaVoltiAvviata = true;

        var daAnalizzare = _tutte.Where(f => f.NumeroVolti is null).ToList();
        var manager = _selectionManager;
        if (daAnalizzare.Count == 0 || manager is null) return;

        var cts = new CancellationTokenSource();
        _ctsVolti = cts;
        var token = cts.Token;
        var cronometro = Stopwatch.StartNew();

        _ = Task.Run(async () =>
        {
            for (var i = 0; i < daAnalizzare.Count && !token.IsCancellationRequested; i++)
            {
                while (_viewerAperto && !token.IsCancellationRequested) await Task.Delay(500);
                if (token.IsCancellationRequested) break;

                var foto = daAnalizzare[i];
                try
                {
                    var (bgra, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(foto.PercorsoCompleto, RilevatoreVolti.LatoAnalisi);
                    var volti = await RilevatoreVolti.RilevaAsync(bgra, w, h, token);
                    if (token.IsCancellationRequested) break;

                    manager.SalvaVolti(foto.Id, volti);
                    var completate = i + 1;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        foto.NumeroVolti = volti.Count;
                        MostraAvanzamentoVolti(completate, daAnalizzare.Count, cronometro.Elapsed);
                    });
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    // File illeggibile: si salta, la ricerca continua con la foto dopo.
                }
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!token.IsCancellationRequested) MiniaturePanel.Visibility = Visibility.Collapsed;
            });
        }, token);
    }

    /// <summary>Stessa barra in basso delle miniature: "Volti 120 di 1300 · circa 2 min rimanenti".</summary>
    private void MostraAvanzamentoVolti(int completate, int totale, TimeSpan trascorso)
    {
        // Le miniature hanno la precedenza sulla barra (es. riapertura veloce della stessa cartella).
        if (_miniatureCompletate < _miniatureTotali) return;

        if (completate >= totale)
        {
            MiniaturePanel.Visibility = Visibility.Collapsed;
            return;
        }

        MiniaturePanel.Visibility = Visibility.Visible;
        MiniatureProgressBar.Maximum = totale;
        MiniatureProgressBar.Value = completate;
        MiniatureText.Text = "Volti " + StimaAvanzamento.Descrivi(completate, totale, trascorso);
    }
}
