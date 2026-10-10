using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WFastPic.Core.Decoding;
using WFastPic.Core.Persistence;
using WFastPic.Core.Processing;
using WFastPic.Core.Selection;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// Voto automatico e suggerimenti di scarto, tutto sul PC: dopo i volti, in background, ogni foto viene
/// misurata una volta (nitidezza, anche dei volti, ed esposizione) e le misure finiscono nel sidecar. Le
/// stelline automatiche si ricalcolano dalle misure di tutta la cartella, dalle raffiche e da chi c'è
/// nella foto (tu, preferite e famiglia, amici…). Restano sempre separate dal voto dell'utente: stelline
/// vuote viola contro piene gialle, e nessuna foto viene mai scartata da sola.
/// </summary>
public partial class MainWindow
{
    /// <summary>Per ogni foto, la persona più importante che compare (dal pannello Persone).</summary>
    private Dictionary<long, ImportanzaPersone> _importanzaFoto = [];

    private Task AnalizzaQualitaAsync(SelectionManager manager, CancellationToken token)
    {
        var daMisurare = _tutte.Where(f => f.Qualita is null).ToList();
        var db = _db;
        if (daMisurare.Count == 0 || db is null) return Task.CompletedTask;

        var cronometro = Stopwatch.StartNew();
        return Task.Run(async () =>
        {
            // I volti appena trovati sono ancora in coda di scrittura: servono per la nitidezza dei volti.
            await manager.SvuotaAsync();

            for (var i = 0; i < daMisurare.Count && !token.IsCancellationRequested; i++)
            {
                while (Viewer.IsAperto && !token.IsCancellationRequested) await Task.Delay(500);
                if (token.IsCancellationRequested) break;

                var foto = daMisurare[i];
                try
                {
                    var (bgra, w, h) = await PreviewDecoder.DecodePreviewBgraAsync(foto.PercorsoCompleto, MisuraQualita.LatoAnalisi);
                    var misure = MisuraQualita.Misura(bgra, w, h, db.GetVolti(foto.Id));
                    if (token.IsCancellationRequested) break;

                    manager.SalvaQualita(foto.Id, MisuraQualita.Versione, new QualitaSalvata(
                        misure.Nitidezza, misure.NitidezzaVolti, misure.Bruciati, misure.Chiusi, misure.Luminosita));

                    var completate = i + 1;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        foto.Qualita = misure;
                        MostraAvanzamentoVolti(completate, daMisurare.Count, cronometro.Elapsed, T("Qualità") + " ");
                        // Le stelline compaiono mentre l'analisi va avanti, non solo alla fine.
                        if (completate % 50 == 0) RicalcolaVotiAutomatici();
                    });
                }
                catch (Exception)
                {
                    // File illeggibile: si salta. Cartella cambiata (sidecar chiuso): il ciclo esce.
                }
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (token.IsCancellationRequested) return;
                MiniaturePanel.Visibility = Visibility.Collapsed;
                RicalcolaVotiAutomatici();
                if (Filtri[_filtroIndice] == FiltroDaScartare) ApplicaFiltro();
            });
        }, token);
    }

    /// <summary>Misure salvate in passato (riapertura): il voto c'è subito, senza rianalizzare.</summary>
    private static void AssegnaQualitaSalvate(IEnumerable<PhotoGridItem> foto, IReadOnlyDictionary<long, QualitaSalvata> salvate)
    {
        foreach (var item in foto)
        {
            if (salvate.TryGetValue(item.Id, out var q))
                item.Qualita = new MisureQualita(q.Nitidezza, q.NitidezzaVolti, q.Bruciati, q.Chiusi, q.Luminosita);
        }
    }

    /// <summary>Le stelline dipendono da tutta la cartella (percentili, raffiche, persone): si ricalcolano insieme.</summary>
    private void RicalcolaVotiAutomatici()
    {
        var voti = VotoAutomatico.Calcola(_tutte
            .Select(f => new FotoDaValutare(f.Id, f.Qualita, f.GroupId, _importanzaFoto.GetValueOrDefault(f.Id)))
            .ToList());

        foreach (var item in _tutte) item.VotoAuto = voti.GetValueOrDefault(item.Id);
        AggiornaScartaSuggerite();
    }

    /// <summary>Dal pannello Persone (dopo ogni nome, unione, cerchia o stella): chi conta di più in ogni foto.</summary>
    private void AggiornaImportanzaPersone(IEnumerable<PersonaUi> persone)
    {
        var importanza = new Dictionary<long, ImportanzaPersone>();
        foreach (var persona in persone.Where(p => !p.SenzaNome))
        {
            var livello = persona.Importanza;
            foreach (var foto in persona.Foto)
            {
                if (importanza.GetValueOrDefault(foto) < livello) importanza[foto] = livello;
            }
        }

        _importanzaFoto = importanza;
        RicalcolaVotiAutomatici();
    }

    // ---------------------------------------------------------------- "Da scartare?"

    private const string FiltroDaScartare = "Da scartare?";

    private void AggiornaScartaSuggerite()
    {
        var quante = _tutte.Count(f => f.DaScartareSuggerita && f.Stato != StatoSelezione.Reject);
        var visibile = Filtri[_filtroIndice] == FiltroDaScartare && quante > 0;
        ScartaSuggeriteButton.Visibility = visibile ? Visibility.Visible : Visibility.Collapsed;
        ScartaSuggeriteText.Text = T("Scarta tutte ({0})", quante);
    }

    /// <summary>
    /// Scarta in un colpo le foto suggerite. Lo scarto è solo un segno (Tab → Scarti per rivederle, il
    /// Cestino passa sempre da "Gestisci scarti"), ma si chiede comunque conferma.
    /// </summary>
    private async void ScartaSuggeriteButton_Click(object sender, RoutedEventArgs e)
    {
        var daScartare = _tutte.Where(f => f.DaScartareSuggerita && f.Stato != StatoSelezione.Reject).ToList();
        if (daScartare.Count == 0) return;

        var dialogo = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = T("Scartare {0}?", Foto(daScartare.Count)),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Text = T("Sono le foto mosse, sfocate, quasi nere o quasi tutte bianche. Vengono solo segnate come scarti: " +
                         "puoi rivederle nel filtro «Scarti» e niente viene cancellato senza passare da «Gestisci scarti»."),
            },
            PrimaryButtonText = T("Scarta"),
            CloseButtonText = T("Annulla"),
            DefaultButton = ContentDialogButton.Primary,
        };

        try
        {
            if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return;
        }
        catch (Exception)
        {
            return; // un altro dialogo è già aperto
        }

        SetStati(daScartare.Select(f => (f, StatoSelezione.Reject)).ToList());
        AggiornaScartaSuggerite();
    }
}

/// <summary>Testi del voto automatico (tooltip in griglia e dettagli nel viewer).</summary>
internal static class EtichetteVoto
{
    public static string Descrivi(VotoSuggerito voto)
    {
        var parti = new List<string> { T("Voto automatico: {0} su 5", voto.Stelle) };
        parti.AddRange(Motivi(voto.Motivi));
        return string.Join(" · ", parti);
    }

    public static IEnumerable<string> Motivi(MotiviVoto motivi)
    {
        if (motivi.HasFlag(MotiviVoto.CiSeiTu)) yield return T("ci sei tu");
        if (motivi.HasFlag(MotiviVoto.PersoneImportanti)) yield return T("persone importanti");
        if (motivi.HasFlag(MotiviVoto.MossaOSfocata)) yield return T("mossa o sfocata");
        if (motivi.HasFlag(MotiviVoto.Morbida)) yield return T("poco nitida");
        if (motivi.HasFlag(MotiviVoto.Nitida)) yield return T("nitida");
        if (motivi.HasFlag(MotiviVoto.VoltoSfocato)) yield return T("volto sfocato");
        if (motivi.HasFlag(MotiviVoto.TroppoScura)) yield return T("quasi nera");
        if (motivi.HasFlag(MotiviVoto.Bruciata)) yield return T("quasi tutta bianca");
        if (motivi.HasFlag(MotiviVoto.MiglioreRaffica)) yield return T("la più nitida della raffica");
        if (motivi.HasFlag(MotiviVoto.RafficaPiuNitida)) yield return T("nella raffica ce n'è una più nitida");
    }
}
