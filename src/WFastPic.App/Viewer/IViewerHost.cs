using Microsoft.UI.Xaml;
using WFastPic.Core.Persistence;
using WFastPic.Core.Selection;

namespace WFastPic.App;

/// <summary>
/// Quello che il viewer chiede alla finestra: l'elenco su cui naviga, la cartella aperta (sidecar,
/// selezione, miniature) e le azioni che toccano anche la griglia. Il resto è tutto del viewer.
/// </summary>
internal interface IViewerHost
{
    /// <summary>Foto visibili nella griglia (filtro attivo): ←/→, "N di M" e filmstrip seguono questo elenco.</summary>
    IList<PhotoGridItem> Visibili { get; }

    /// <summary>Tutte le foto della cartella, anche fuori dal filtro.</summary>
    IReadOnlyList<PhotoGridItem> Tutte { get; }

    LibrarySidecarDatabase? Database { get; }

    SelectionManager? SelectionManager { get; }

    ThumbnailCacheLoader? ThumbnailLoader { get; }

    /// <summary>Per schermo intero (AppWindow) e perdita del focus della finestra.</summary>
    Window Finestra { get; }

    /// <summary>Cambio di stato con suono, conteggi e filtro: la foto può sparire da <see cref="Visibili"/>.</summary>
    void SetStato(PhotoGridItem foto, string nuovoStato);

    void SetRating(PhotoGridItem foto, int rating);

    Task<string?> NomeLuogoAsync(double latitudine, double longitudine, CancellationToken ct);

    /// <summary>Volti della foto con il nome della persona (sezione Volti dei dettagli); null se non ancora analizzata.</summary>
    Task<IReadOnlyList<VoltoDellaFoto>?> VoltiDellaFotoAsync(PhotoGridItem foto);

    /// <summary>"Chi è?" su un volto: dà o corregge il nome, conferma una proposta.</summary>
    Task DaiNomeAlVoltoAsync(VoltoDellaFoto volto);
}

/// <summary>Un volto nei dettagli del viewer: miniatura, persona (anche solo proposta) e chi è per l'utente.</summary>
internal sealed record VoltoDellaFoto(
    long Id, long PhotoId, byte[]? Miniatura, float[] Impronta,
    string? Nome, bool Proposto, int Cerchia, bool Preferita);
