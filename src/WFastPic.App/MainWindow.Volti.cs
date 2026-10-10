using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WFastPic.Core.Decoding;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;
using WFastPic.Core.Selection;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// Ricerca dei volti in background, come la scansione volti di Picasa: parte solo quando le miniature
/// della griglia sono finite, analizza una foto alla volta e salva il risultato nel sidecar, così alla
/// riapertura non si ripete. Con il viewer aperto si mette in pausa: il ciclo ←/→ non deve mai
/// aspettare un decode in più.
/// Due livelli: con il riconoscimento persone attivo (opzione, chiesta al primo uso) YuNet + SFace
/// danno anche l'impronta per riconoscere chi è; altrimenti solo il rilevatore di Windows, leggero,
/// che basta ai ritagli suggeriti e al filtro "Con volti".
/// </summary>
public partial class MainWindow
{
    private const int LatoAnalisiPersone = 1600;
    private static readonly HttpClient HttpModelli = CreaClientModelli();

    private CancellationTokenSource? _ctsVolti;
    private bool _ricercaVoltiAvviata;
    private bool? _riconoscimentoPersone;

    private static HttpClient CreaClientModelli()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WFastPic/1.0");
        return http;
    }

    private void FermaRicercaVolti()
    {
        _ctsVolti?.Cancel();
        _ctsVolti = null;
        _ricercaVoltiAvviata = false;
    }

    private async void AvviaRicercaVolti()
    {
        if (_ricercaVoltiAvviata) return;
        _ricercaVoltiAvviata = true;

        var cts = new CancellationTokenSource();
        _ctsVolti = cts;
        var token = cts.Token;
        var manager = _selectionManager;
        if (manager is null) return;

        if (_riconoscimentoPersone is null) await ChiediRiconoscimentoPersoneAsync();
        if (token.IsCancellationRequested) return;

        try
        {
            if (_riconoscimentoPersone == true && await AssicuraModelliPersoneAsync(token))
            {
                await AvviaAnalisiPersone(manager, token);
            }
            else
            {
                if (token.IsCancellationRequested) return;
                if (_riconoscimentoPersone == true) StatusText.Text = T("Riconoscimento persone non disponibile (modelli non scaricati): solo rilevamento dei volti.");
                await AvviaRilevamentoVoltiWindows(manager, token);
            }

            // Dopo i volti (servono per la nitidezza dei visi): misure per il voto automatico.
            if (!token.IsCancellationRequested) await AnalizzaQualitaAsync(manager, token);
        }
        catch (OperationCanceledException)
        {
            // Cartella cambiata o app chiusa: la ricerca riparte alla prossima apertura.
        }
    }

    // ---------------------------------------------------------------- domanda al primo uso

    private async Task ChiediRiconoscimentoPersoneAsync()
    {
        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = T("Riconoscere le persone?"),
            PrimaryButtonText = T("Attiva"),
            CloseButtonText = T("Non ora"),
            DefaultButton = ContentDialogButton.Primary,
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Text = T(
                    "WFastPic può riconoscere le persone nelle foto, come Picasa: raggruppa i volti simili, " +
                    "tu dai un nome e la ritrova da solo anche nelle altre cartelle.\n\n" +
                    "• Funziona tutto sul PC: nessuna foto esce.\n" +
                    "• Al primo uso scarica due modelli (38 MB) da Hugging Face (OpenCV).\n" +
                    "• Analizza le foto in background, dopo le miniature: con tante foto può richiedere " +
                    "qualche minuto e usare il processore. Si ferma mentre guardi le foto nel viewer.\n\n" +
                    "Puoi cambiare idea quando vuoi dall'interruttore nella sezione Persone a sinistra."),
            },
        };

        try
        {
            _riconoscimentoPersone = await dialogo.ShowAsync() == ContentDialogResult.Primary;
            SalvaImpostazioni();
            RiconoscimentoPersoneSwitch.IsOn = _riconoscimentoPersone == true;
        }
        catch (Exception)
        {
            // Un altro dialogo è già aperto (es. export): si chiederà alla prossima cartella.
        }
    }

    // ---------------------------------------------------------------- modelli

    private async Task<bool> AssicuraModelliPersoneAsync(CancellationToken token)
    {
        var cartella = ModelliPersone.CartellaPredefinita;
        if (ModelliPersone.Presenti(cartella)) return true;

        var progresso = new Progress<(long Scaricati, long Totale)>(p =>
        {
            if (token.IsCancellationRequested || p.Totale == 0) return;
            MiniaturePanel.Visibility = Visibility.Visible;
            MiniatureProgressBar.Maximum = p.Totale;
            MiniatureProgressBar.Value = p.Scaricati;
            MiniatureText.Text = T("Scarico i modelli per le persone · {0} di {1} MB", p.Scaricati / 1_048_576, p.Totale / 1_048_576);
        });

        try
        {
            await Task.Run(() => ModelliPersone.AssicuraAsync(HttpModelli, cartella, progresso, token), token);
            return ModelliPersone.Presenti(cartella);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return false; // offline o download non riuscito: si riproverà alla prossima apertura
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (!token.IsCancellationRequested) MiniaturePanel.Visibility = Visibility.Collapsed;
        }
    }

    // ---------------------------------------------------------------- riconoscimento persone

    private Task AvviaAnalisiPersone(SelectionManager manager, CancellationToken token)
    {
        var daAnalizzare = _tutte.Where(f => !f.PersoneAnalizzate).ToList();
        var persone = PersoneNote();
        var dbAnalisi = _db;
        AggiornaPannelloPersone();
        if (daAnalizzare.Count == 0)
        {
            // Tutto già analizzato: i volti ancora senza nome vengono comunque confrontati con le persone note.
            if (dbAnalisi is null) return Task.CompletedTask;
            return Task.Run(() => RiproponiPersone(dbAnalisi, persone)).ContinueWith(
                _ => DispatcherQueue.TryEnqueue(AggiornaPannelloPersone), TaskScheduler.Default);
        }

        var cronometro = Stopwatch.StartNew();
        return Task.Run(async () =>
        {
            try
            {
                using var analizzatore = new AnalizzatorePersone(ModelliPersone.CartellaPredefinita);
                for (var i = 0; i < daAnalizzare.Count && !token.IsCancellationRequested; i++)
                {
                    while (Viewer.IsAperto && !token.IsCancellationRequested) await Task.Delay(500);
                    if (token.IsCancellationRequested) break;

                    var foto = daAnalizzare[i];
                    try
                    {
                        // Nomi dati mentre l'analisi è in corso valgono subito anche per le foto che restano.
                        if (i % 5 == 0) persone = PersoneNote();
                        var (bgra, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(foto.PercorsoCompleto, LatoAnalisiPersone);
                        var volti = analizzatore.Analizza(bgra, w, h);

                        var daSalvare = new List<VoltoDaSalvare>();
                        foreach (var v in volti)
                        {
                            var proposta = RiconoscimentoPersone.Riconosci(v.Impronta, persone);
                            daSalvare.Add(new VoltoDaSalvare(
                                v.X0, v.Y0, v.X1, v.Y1, v.Punteggio,
                                Impronte.InByte(v.Impronta),
                                await CodificaJpegAsync(v.MiniaturaBgra, AllineamentoVolto.Lato, AllineamentoVolto.Lato),
                                proposta?.Persona.Id,
                                proposta is null ? StatoPersona.Nessuno : StatoPersona.Proposto));
                        }

                        if (token.IsCancellationRequested) break;
                        manager.SalvaVoltiPersone(foto.Id, daSalvare);

                        var completate = i + 1;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (token.IsCancellationRequested) return;
                            foto.NumeroVolti = daSalvare.Count;
                            foto.PersoneAnalizzate = true;
                            MostraAvanzamentoVolti(completate, daAnalizzare.Count, cronometro.Elapsed, T("Persone") + " ");
                            // Il pannello si riempie mentre l'analisi va avanti, non solo alla fine.
                            if (completate % 25 == 0) AggiornaPannelloPersone();
                        });
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        // File illeggibile: si salta, l'analisi continua con la foto dopo.
                    }
                }
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                // Modelli non caricabili (file danneggiato, runtime mancante): resta il rilevamento di base.
                DispatcherQueue.TryEnqueue(() => StatusText.Text = T("Riconoscimento persone non disponibile su questo PC."));
            }

            // Ultimo giro: volti analizzati prima che un nome fosse dato o prima dell'ultimo aggiornamento.
            if (!token.IsCancellationRequested && dbAnalisi is not null)
            {
                try { await Task.Run(() => RiproponiPersone(dbAnalisi, PersoneNote())); }
                catch (Exception) when (!token.IsCancellationRequested) { }
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (token.IsCancellationRequested) return;
                MiniaturePanel.Visibility = Visibility.Collapsed;
                AggiornaPannelloPersone();
            });
        }, token);
    }

    private static async Task<byte[]> CodificaJpegAsync(byte[] bgra, int larghezza, int altezza)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)larghezza, (uint)altezza, 96, 96, bgra);
        await encoder.FlushAsync();

        var dati = new byte[stream.Size];
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(dati);
        return dati;
    }

    // ---------------------------------------------------------------- solo rilevamento (Windows)

    private Task AvviaRilevamentoVoltiWindows(SelectionManager manager, CancellationToken token)
    {
        if (!RilevatoreVolti.Disponibile) return Task.CompletedTask;
        var daAnalizzare = _tutte.Where(f => f.NumeroVolti is null).ToList();
        if (daAnalizzare.Count == 0) return Task.CompletedTask;

        var cronometro = Stopwatch.StartNew();
        return Task.Run(async () =>
        {
            for (var i = 0; i < daAnalizzare.Count && !token.IsCancellationRequested; i++)
            {
                while (Viewer.IsAperto && !token.IsCancellationRequested) await Task.Delay(500);
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
                        MostraAvanzamentoVolti(completate, daAnalizzare.Count, cronometro.Elapsed, T("Volti") + " ");
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

    /// <summary>Stessa barra in basso delle miniature: "Persone 120 di 1300 · circa 2 min rimanenti".</summary>
    private void MostraAvanzamentoVolti(int completate, int totale, TimeSpan trascorso, string etichetta)
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
        MiniatureText.Text = etichetta + StimaAvanzamento.Descrivi(completate, totale, trascorso);
    }
}
