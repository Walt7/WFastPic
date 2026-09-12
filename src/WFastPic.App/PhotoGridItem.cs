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
    public DateTime? CaptureDate { get; init; }

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
        set { _thumbnail = value; OnPropertyChanged(); }
    }

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

    private void OnPropertyChanged([CallerMemberName] string? nome = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}
