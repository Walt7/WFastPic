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

    // ---- Gira/specchia della ricetta: la miniatura viene girata (RenderTransform), niente simbolo.

    private double _rotazioneMiniatura;
    public double RotazioneMiniatura => _rotazioneMiniatura;

    private double _scalaXMiniatura = 1;
    public double ScalaXMiniatura => _scalaXMiniatura;

    /// <summary>Allinea i simboli alla ricetta (all'apertura della cartella e mentre si modifica nell'editor).</summary>
    public void AggiornaModifiche(WFastPic.Core.Processing.EditRecipe ricetta)
    {
        var (rotazione, scalaX) = (ricetta.RotationDegrees % 360, ricetta.FlipHorizontal ? -1.0 : 1.0);
        if (rotazione != _rotazioneMiniatura || scalaX != _scalaXMiniatura)
        {
            (_rotazioneMiniatura, _scalaXMiniatura) = (rotazione, scalaX);
            OnPropertyChanged(nameof(RotazioneMiniatura));
            OnPropertyChanged(nameof(ScalaXMiniatura));
        }

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

    // ---- Doppioni: la stessa foto più volte nella cartella (identica o ricompressa/ridimensionata)

    /// <summary>Impronta percettiva della miniatura; null = non ancora calcolata.</summary>
    public ulong? Impronta { get; set; }

    /// <summary>Larghezza/altezza della miniatura su cui è calcolata l'impronta.</summary>
    public double? RapportoMiniatura { get; set; }

    private int? _doppioneId;
    public int? DoppioneId
    {
        get => _doppioneId;
        set
        {
            if (_doppioneId == value) return;
            _doppioneId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDoppione));
            OnPropertyChanged(nameof(DoppioneDescrizione));
        }
    }

    public bool HasDoppione => DoppioneId is not null;

    private bool _doppioneIdentico;
    public bool DoppioneIdentico
    {
        get => _doppioneIdentico;
        set
        {
            if (_doppioneIdentico == value) return;
            _doppioneIdentico = value;
            OnPropertyChanged(nameof(DoppioneDescrizione));
        }
    }

    private bool _miglioreDoppione;
    /// <summary>La copia suggerita da tenere (le scelte dell'utente contano più di tutto).</summary>
    public bool MiglioreDoppione
    {
        get => _miglioreDoppione;
        set
        {
            if (_miglioreDoppione == value) return;
            _miglioreDoppione = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DoppioneDescrizione));
        }
    }

    private string? _doppioneAltrove;
    /// <summary>Dove sono le copie in altre cartelle ("«Puglia» (2), «Roma»"), null se solo qui.</summary>
    public string? DoppioneAltrove
    {
        get => _doppioneAltrove;
        set
        {
            if (_doppioneAltrove == value) return;
            _doppioneAltrove = value;
            OnPropertyChanged(nameof(DoppioneDescrizione));
        }
    }

    private string? _miglioreAltrove;
    /// <summary>La copia da tenere è in un'altra cartella: quale («Puglia»).</summary>
    public string? MiglioreAltrove
    {
        get => _miglioreAltrove;
        set
        {
            if (_miglioreAltrove == value) return;
            _miglioreAltrove = value;
            OnPropertyChanged(nameof(DoppioneDescrizione));
        }
    }

    public string DoppioneDescrizione => _doppioneId is null
        ? ""
        : EtichetteDoppioni.Descrivi(_doppioneIdentico, _miglioreDoppione, _doppioneAltrove, _miglioreAltrove);

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

    /// <summary>Miniatura più alta che larga (foto non girata nella ricetta).</summary>
    public bool MiniaturaVerticale => _verticale;

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

    // ---- Voto automatico: sempre separato dal voto dell'utente (Rating), che non tocca mai.

    /// <summary>Misure tecniche dalla ricerca in background; null = foto non ancora analizzata.</summary>
    public WFastPic.Core.Processing.MisureQualita? Qualita { get; set; }

    private WFastPic.Core.Processing.VotoSuggerito? _votoAuto;
    public WFastPic.Core.Processing.VotoSuggerito? VotoAuto
    {
        get => _votoAuto;
        set
        {
            if (_votoAuto == value) return;
            _votoAuto = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVotoAuto));
            OnPropertyChanged(nameof(VotoAutoTesto));
            OnPropertyChanged(nameof(VotoAutoDescrizione));
            OnPropertyChanged(nameof(DaScartareSuggerita));
            OnPropertyChanged(nameof(MiglioreRaffica));
            OnPropertyChanged(nameof(CiSeiTu));
        }
    }

    public bool HasVotoAuto => _votoAuto is not null;

    /// <summary>Stelline vuote (☆), non piene come quelle date dall'utente.</summary>
    public string VotoAutoTesto => _votoAuto is null ? "" : new string('☆', _votoAuto.Stelle);

    /// <summary>"Voto automatico: 4 stelle · nitida · la migliore della raffica" (tooltip e viewer).</summary>
    public string VotoAutoDescrizione => _votoAuto is null ? "" : EtichetteVoto.Descrivi(_votoAuto);

    /// <summary>Mossa, sfocata o esposta malissimo: compare in "Da scartare?". Lo scarto lo decide l'utente.</summary>
    public bool DaScartareSuggerita => _votoAuto?.DaScartare == true;

    public bool MiglioreRaffica => _votoAuto?.MiglioreRaffica == true;

    /// <summary>Ci sei tu (la persona "Io"): la foto è messa in evidenza.</summary>
    public bool CiSeiTu => _votoAuto?.CiSeiTu == true;

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
            OnPropertyChanged(nameof(OpacitaRaffica));
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

    /// <summary>Striscia della raffica nel viewer: le altre foto del gruppo restano ben leggibili per confrontarle.</summary>
    public double OpacitaRaffica => IsCorrenteViewer ? 1.0 : 0.8;

    private void OnPropertyChanged([CallerMemberName] string? nome = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}
