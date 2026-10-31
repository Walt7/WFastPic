namespace WFastPic.Core.Localizzazione;

internal static partial class TestiEn
{
    /// <summary>«Importa i nomi da Picasa» nella sezione Persone.</summary>
    private static void Picasa(Dictionary<string, string> d)
    {
        d["Importa i nomi da Picasa"] = "Import names from Picasa";
        d["Nessun volto marcato da Picasa in questa cartella."] = "No faces tagged by Picasa in this folder.";
        d["Picasa ha {0} volti con nome qui, ma WFastPic non ha ancora cercato le persone in quelle foto: riprova a ricerca finita."] =
            "Picasa has {0} named faces here, but WFastPic has not looked for people in those photos yet: try again when the search is done.";
        d["Nessun volto di Picasa corrisponde ai volti trovati da WFastPic."] = "No Picasa face matches the faces found by WFastPic.";
        d["Picasa ha marcato {0} volti in questa cartella: {1} corrispondono ai volti trovati da WFastPic e prendono il nome di {2} persone: {3}."] =
            "Picasa tagged {0} faces in this folder: {1} match the faces found by WFastPic and take the names of {2} people: {3}.";
        d["I volti che hai già confermato come un'altra persona, o ignorato, restano come sono. Puoi annullare subito dopo."] =
            "Faces you already confirmed as someone else, or ignored, stay as they are. You can undo right after.";
        d["{0} volti di Picasa restano fuori: foto non ancora analizzate o volti che WFastPic non ha trovato."] =
            "{0} Picasa faces are left out: photos not analyzed yet or faces WFastPic did not find.";
        d["{0} volti hanno come nome un indirizzo email e non vengono importati."] =
            "{0} faces have an email address as their name and are not imported.";
        d["Importare i nomi da Picasa?"] = "Import names from Picasa?";
        d["Importa"] = "Import";
        d["Nomi da Picasa dati a {0} volti"] = "Picasa names given to {0} faces";
        d["Nomi da Picasa dati a {0} volti ({1} persone)."] = "Picasa names given to {0} faces ({1} people).";
    }
}
