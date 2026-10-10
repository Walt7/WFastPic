using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media.Imaging;
using WFastPic.Core.Selection;

namespace WFastPic.App;

/// <summary>
/// Voce della griglia. Codice minimale (no CommunityToolkit.Mvvm) per questo primo checkpoint:
/// refactor verso MVVM completo rimandato a quando la Grid è stabile.
/// </summary>
public sealed class PhotoGridItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required long Id { get; init; }
    public required string PathRelativo { get; init; }
    public required string PercorsoCompleto { get; init; }
    public required long FileSize { get; init; }
    public required DateTime LastWriteUtc { get; init; }
    public DateTime? CaptureDate { get; init; }
    public bool HasVideo { get; init; }
    private double? _latitude;
    public double? Latitude
    {
        get => _latitude;
        set
        {
            if (_latitude == value) return;
            _latitude = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGps));
        }
    }

    private double? _longitude;
    public double? Longitude
    {
        get => _longitude;
        set
        {
            if (_longitude == value) return;
            _longitude = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGps));
        }
    }

    public bool HasGps => Latitude.HasValue && Longitude.HasValue;

    /// <summary>Metri sul livello del mare, se il file la riporta (non per posizioni corrette a mano).</summary>
    public double? Altitude { get; set; }

    /// <summary>Verso dove guardava la camera (gradi da nord, orario): la freccia sulla mappa.</summary>
    public double? Direction { get; set; }

    /// <summary>Volti trovati dalla ricerca in background; null = foto non ancora analizzata.</summary>
    public int? NumeroVolti { get; set; }

    /// <summary>Volti già analizzati per riconoscere le persone (non solo rilevati).</summary>
    public bool PersoneAnalizzate { get; set; }

    /// <summary>Posizione scelta/corretta dall'utente nell'editor: la riscansione GPS non la tocca.</summary>
    public bool GpsManuale { get; set; }

    // ---- Simboli delle modifiche sulla miniatura (ritaglio, raddrizzamento, colore/tono)

    private bool _haRitaglio;
    public bool HaRitaglio => _haRitaglio;

    private bool _haRaddrizzamento;
    public bool HaRaddrizzamento => _haRaddrizzamento;

    private bool _haTono;
    public bool HaTono => _haTono;

    public bool HaModifiche => _haRitaglio || _haRaddrizzamento || _haTono;

    /// <summary>Allinea i simboli alla ricetta (all'apertura della cartella e mentre si modifica nell'editor).</summary>
    public void AggiornaModifiche(WFastPic.Core.Processing.EditRecipe ricetta)
    {
        var (ritaglio, raddrizzamento, tono) = (ricetta.HasCrop, ricetta.HasStraighten, ricetta.HasTone);
        if (ritaglio == _haRitaglio && raddrizzamento == _haRaddrizzamento && tono == _haTono) return;

        (_haRitaglio, _haRaddrizzamento, _haTono) = (ritaglio, raddrizzamento, tono);
        OnPropertyChanged(nameof(HaRitaglio));
        OnPropertyChanged(nameof(HaRaddrizzamento));
        OnPropertyChanged(nameof(HaTono));
        OnPropertyChanged(nameof(HaModifiche));
    }

    private int? _groupId;
    public int? GroupId
    {
        get => _groupId;
        set { _groupId = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasGroup)); }
    }

    private int _posizioneNelGruppo;
    public int PosizioneNelGruppo
    {
        get => _posizioneNelGruppo;
        set { _posizioneNelGruppo = value; OnPropertyChanged(); OnPropertyChanged(nameof(GruppoTesto)); }
    }

    private int _dimensioneGruppo;
    public int DimensioneGruppo
    {
        get => _dimensioneGruppo;
        set { _dimensioneGruppo = value; OnPropertyChanged(); OnPropertyChanged(nameof(GruppoTesto)); }
    }

    public bool HasGroup => GroupId is not null;
    public string GruppoTesto => $"{PosizioneNelGruppo}/{DimensioneGruppo}";

    private BitmapImage? _thumbnail;
    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            OnPropertyChanged();

            var verticale = value is not null && value.PixelHeight > value.PixelWidth;
            if (verticale == _verticale) return;
            _verticale = verticale;
            OnPropertyChanged(nameof(OrientamentoBadge));
        }
    }

    private bool _verticale;

    /// <summary>
    /// Badge in griglia (video, GPS, modifiche) fuori dalla foto: in colonna nella banda vuota a sinistra
    /// di una foto verticale, in riga nella banda in alto di una orizzontale.
    /// </summary>
    public Microsoft.UI.Xaml.Controls.Orientation OrientamentoBadge =>
        _verticale ? Microsoft.UI.Xaml.Controls.Orientation.Vertical : Microsoft.UI.Xaml.Controls.Orientation.Horizontal;

    private string _stato = StatoSelezione.None;
    public string Stato
    {
        get => _stato;
        set
        {
            if (_stato == value) return;
            _stato = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(IsReject));
        }
    }

    private int _rating;
    public int Rating
    {
        get => _rating;
        set
        {
            if (_rating == value) return;
            _rating = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRating));
            OnPropertyChanged(nameof(RatingTesto));
        }
    }

    public bool IsSelected => Stato == StatoSelezione.Selected;
    public bool IsReject => Stato == StatoSelezione.Reject;
    public bool HasRating => Rating > 0;
    public string RatingTesto => new string('★', Rating);

    private bool _isCorrenteGriglia;
    public bool IsCorrenteGriglia
    {
        get => _isCorrenteGriglia;
        set
        {
            if (_isCorrenteGriglia == value) return;
            _isCorrenteGriglia = value;
            OnPropertyChanged();
        }
    }

    // Solo per il filmstrip del Viewer: quale foto e' quella grande, e quali fanno parte della
    // stessa raffica cosi' da evidenziarle senza toccare l'aspetto nella griglia principale.
    private bool _isCorrenteViewer;
    public bool IsCorrenteViewer
    {
        get => _isCorrenteViewer;
        set
        {
            if (_isCorrenteViewer == value) return;
            _isCorrenteViewer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DimensioneFilmstrip));
            OnPropertyChanged(nameof(OpacitaFilmstrip));
        }
    }

    private bool _evidenziaGruppoViewer;
    public bool EvidenziaGruppoViewer
    {
        get => _evidenziaGruppoViewer;
        set
        {
            if (_evidenziaGruppoViewer == value) return;
            _evidenziaGruppoViewer = value;
            OnPropertyChanged();
        }
    }

    // Misure del filmstrip: usate anche da AggiornaFilmstrip per centrare la foto corrente.
    // Solo due larghezze (corrente / altre): la raffica si distingue col colore, non con la misura.
    public const double FilmstripLato = 80;
    public const double FilmstripLatoCorrente = 108;
    public const double FilmstripMargine = 4;

    public double DimensioneFilmstrip => IsCorrenteViewer ? FilmstripLatoCorrente : FilmstripLato;

    // Le altre foto attenuate: l'occhio va subito a quella in osservazione.
    public double OpacitaFilmstrip => IsCorrenteViewer ? 1.0 : 0.55;

    private void OnPropertyChanged([CallerMemberName] string? nome = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}
