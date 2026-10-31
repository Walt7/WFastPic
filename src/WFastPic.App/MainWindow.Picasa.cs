using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;
using WFastPic.Core.Persone;
using static WFastPic.Core.Localizzazione.Testi;

namespace WFastPic.App;

/// <summary>
/// «Importa i nomi da Picasa»: i volti che Picasa aveva marcato e nominato (file Picasa.ini nelle cartelle) danno il nome
/// ai volti trovati qui nelle stesse foto, come se l'utente li avesse confermati. Così i nomi già dati in Picasa diventano
/// campioni e il riconoscimento li propone anche nelle altre foto. I Picasa.ini e le foto non vengono toccati; si può
/// annullare subito dopo, come gli altri nomi.
/// </summary>
public partial class MainWindow
{
    private async void ImportaPicasa_Click(object sender, RoutedEventArgs e)
    {
        var db = _db;
        if (db is null) return;
        if (_selectionManager is not null) await _selectionManager.SvuotaAsync();

        PianoPicasa<long> piano;
        try
        {
            piano = await Task.Run(() =>
            {
                var percorsi = db.GetAllPhotos().ToDictionary(f => f.Id, f => Path.Combine(db.CartellaFoto, f.PathRelativo));
                var volti = db.GetVoltiPersone()
                    .Where(v => percorsi.ContainsKey(v.PhotoId))
                    .Select(v => (v.Id, percorsi[v.PhotoId], new Riquadro(v.X0, v.Y0, v.X1, v.Y1)))
                    .ToList();
                return VoltiPicasa.Pianifica(db.CartellaFoto, volti, VoltiPicasa.LeggiContattiXml(VoltiPicasa.ContattiXmlPredefinito));
            });
        }
        catch (ObjectDisposedException)
        {
            return; // cartella cambiata nel frattempo
        }
        if (db != _db) return;

        if (piano.VoltiPicasa == 0)
        {
            StatusText.Text = T("Nessun volto marcato da Picasa in questa cartella.");
            return;
        }
        // Un indirizzo email al posto del nome (contatto Google senza nome): meglio non inventare una persona.
        var daImportare = piano.Abbinati.Where(a => !VoltiPicasa.EEmail(a.Nome)).ToList();
        if (daImportare.Count == 0)
        {
            StatusText.Text = piano.InFotoNonAnalizzate > 0
                ? T("Picasa ha {0} volti con nome qui, ma WFastPic non ha ancora cercato le persone in quelle foto: riprova a ricerca finita.", piano.InFotoNonAnalizzate)
                : T("Nessun volto di Picasa corrisponde ai volti trovati da WFastPic.");
            return;
        }

        var nomi = daImportare.GroupBy(a => a.Nome).OrderByDescending(g => g.Count()).ToList();
        var elenco = string.Join(", ", nomi.Take(8).Select(g => $"{g.Key} ({g.Count()})")) + (nomi.Count > 8 ? ", …" : "");
        var testo = T("Picasa ha marcato {0} volti in questa cartella: {1} corrispondono ai volti trovati da WFastPic e prendono il nome di {2} persone: {3}.",
                        piano.VoltiPicasa, daImportare.Count, nomi.Count, elenco)
                    + "\n\n" + T("I volti che hai già confermato come un'altra persona, o ignorato, restano come sono. Puoi annullare subito dopo.");
        if (piano.InFotoNonAnalizzate + piano.NonTrovati > 0)
            testo += "\n\n" + T("{0} volti di Picasa restano fuori: foto non ancora analizzate o volti che WFastPic non ha trovato.", piano.InFotoNonAnalizzate + piano.NonTrovati);
        if (piano.Abbinati.Count > daImportare.Count)
            testo += "\n\n" + T("{0} volti hanno come nome un indirizzo email e non vengono importati.", piano.Abbinati.Count - daImportare.Count);
        if (!await ChiediConfermaAsync(T("Importare i nomi da Picasa?"), testo, T("Importa"))) return;
        if (db != _db) return;

        var archivio = Archivio;
        var personePrima = archivio.Elenco().ToDictionary(p => p.Id);
        var voltiPrima = await Task.Run(() => FotografaVolti(db));
        var esito = await Task.Run(() =>
        {
            var risultato = GestionePersone.AssegnaNomi(db, archivio, daImportare);
            var toccate = risultato.Persone.ToHashSet();
            GestionePersone.Riproponi(db, GestionePersone.PersoneNote(archivio).Where(p => toccate.Contains(p.Id)).ToList());
            return risultato;
        });
        RegistroAttivita.Scrivi("persona.picasa", $"{db.CartellaFoto}: {esito.Assegnati} volti da Picasa, {esito.Persone.Count} persone ({esito.Create.Count} nuove), {esito.InConflitto} lasciati");

        ImpostaAnnulla(db, T("Nomi da Picasa dati a {0} volti", esito.Assegnati), async () =>
        {
            await Task.Run(() => RipristinaVolti(db, voltiPrima));
            foreach (var id in esito.Create) Archivio.Elimina(id);
            foreach (var id in esito.Persone.Except(esito.Create))
                if (personePrima.TryGetValue(id, out var p)) Archivio.ImpostaCampioni(id, p.Campioni);
        });
        StatusText.Text = T("Nomi da Picasa dati a {0} volti ({1} persone).", esito.Assegnati, esito.Persone.Count);
        AggiornaPannelloPersone();
    }
}
