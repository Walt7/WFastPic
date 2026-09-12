using WFastPic.Core.Persistence;

namespace WFastPic.Core.Selection;

/// <summary>
/// Unico punto dove uno scarto (stato Reject) può diventare una cancellazione fisica reale.
/// Mai invocato dal ciclo di selezione principale (Delete = solo stato Reject in DB).
/// </summary>
public sealed class RejectManager
{
    private readonly LibrarySidecarDatabase _db;

    public RejectManager(LibrarySidecarDatabase db)
    {
        _db = db;
    }

    public IReadOnlyList<PhotoRecord> GetScarti() => _db.GetByStato(StatoSelezione.Reject);

    public void EliminaDefinitivamente(PhotoRecord foto)
    {
        var percorso = Path.Combine(_db.CartellaFoto, foto.PathRelativo);
        if (File.Exists(percorso))
        {
            File.Delete(percorso);
        }

        _db.RemovePhoto(foto.Id);
    }

    public void SpostaInScarti(PhotoRecord foto)
    {
        var origine = Path.Combine(_db.CartellaFoto, foto.PathRelativo);
        var cartellaScarti = Path.Combine(_db.CartellaFoto, "Scarti");
        Directory.CreateDirectory(cartellaScarti);
        var destinazione = Path.Combine(cartellaScarti, foto.PathRelativo);

        if (File.Exists(origine))
        {
            File.Move(origine, destinazione, overwrite: false);
        }

        _db.RemovePhoto(foto.Id);
    }

    public void Lascia(PhotoRecord foto) => _db.UpdateStato(foto.Id, StatoSelezione.None);
}
