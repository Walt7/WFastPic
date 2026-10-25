using Microsoft.UI.Xaml.Controls;
using WFastPic.Core.Library;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// Le selezioni salvate di questa cartella nel menu dei filtri (sotto una voce "Selezioni salvate"): scegliendone una
/// la griglia mostra solo quelle foto. Il menu è una ComboBox, che non ha sottomenu: le selezioni stanno in coda
/// alla lista, sotto un'intestazione. Compaiono solo le selezioni con almeno una foto in questa cartella.
/// </summary>
public partial class MainWindow
{
    private string? _selezioneAttiva;
    private HashSet<long>? _idSelezioneAttiva;

    private bool CorrispondeAllaSelezione(PhotoGridItem foto) => _idSelezioneAttiva is not { } ids || ids.Contains(foto.Id);

    /// <summary>Id delle foto di questa cartella che fanno parte della selezione salvata.</summary>
    private HashSet<long> FotoDellaSelezione(SelezioneSalvata selezione)
    {
        var percorsi = selezione.Percorsi.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _tutte.Where(i => percorsi.Contains(Path.GetFullPath(i.PercorsoCompleto))).Select(i => i.Id).ToHashSet();
    }

    /// <summary>Ricostruisce le voci in coda alla lista dei filtri (a cartella caricata e dopo ogni salvataggio).</summary>
    private void RicaricaVociSelezioni()
    {
        var qui = Selezioni.Elenco()
            .Select(s => (Selezione: s, Foto: FotoDellaSelezione(s)))
            .Where(s => s.Foto.Count > 0)
            .ToList();

        _aggiornamentoFiltroInCorso = true;
        try
        {
            while (FiltroComboBox.Items.Count > Filtri.Length) FiltroComboBox.Items.RemoveAt(FiltroComboBox.Items.Count - 1);

            if (qui.Count > 0)
            {
                FiltroComboBox.Items.Add(new ComboBoxItem { Content = T("Selezioni salvate"), IsEnabled = false });
                foreach (var (selezione, foto) in qui)
                    FiltroComboBox.Items.Add(new ComboBoxItem { Content = $"{selezione.Nome} ({foto.Count})", Tag = selezione.Nome });
            }

            // La selezione attiva può essere sparita (cartella cambiata) o aver cambiato foto (risalvata).
            if (_selezioneAttiva is { } attiva)
            {
                var ancora = qui.FirstOrDefault(s => string.Equals(s.Selezione.Nome, attiva, StringComparison.CurrentCultureIgnoreCase));
                _idSelezioneAttiva = ancora.Selezione is null ? null : ancora.Foto;
                if (ancora.Selezione is null) _selezioneAttiva = null;
            }
        }
        finally
        {
            _aggiornamentoFiltroInCorso = false;
        }
        AggiornaFiltroUI();
    }

    /// <summary>Posizione da mostrare nella lista: la selezione attiva, altrimenti il filtro normale.</summary>
    private int IndiceFiltroNellaLista()
    {
        if (_selezioneAttiva is null) return _filtroIndice;
        for (var i = Filtri.Length; i < FiltroComboBox.Items.Count; i++)
            if (FiltroComboBox.Items[i] is ComboBoxItem { Tag: string nome } && string.Equals(nome, _selezioneAttiva, StringComparison.CurrentCultureIgnoreCase))
                return i;
        return _filtroIndice;
    }

    private void ApplicaSelezioneScelta(string nome)
    {
        var selezione = Selezioni.Trova(nome);
        if (selezione is null) return;
        _selezioneAttiva = selezione.Nome;
        _idSelezioneAttiva = FotoDellaSelezione(selezione);
        _filtroIndice = 0;
        ApplicaFiltro();
    }

    private void TogliSelezioneAttiva()
    {
        _selezioneAttiva = null;
        _idSelezioneAttiva = null;
    }
}
