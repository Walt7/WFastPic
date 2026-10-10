namespace WFastPic.Core.Localizzazione;

internal static partial class TestiEn
{
    /// <summary>Voto automatico, "Da scartare?", importanza delle persone e volti nei dettagli del viewer.</summary>
    private static void VotoAutomatico(Dictionary<string, string> d)
    {
        // Voto automatico e motivi
        d["Qualità"] = "Quality";
        d["Voto automatico: {0} su 5"] = "Auto rating: {0} of 5";
        d["ci sei tu"] = "you are in it";
        d["persone importanti"] = "important people";
        d["mossa o sfocata"] = "shaken or blurry";
        d["poco nitida"] = "slightly soft";
        d["nitida"] = "sharp";
        d["volto sfocato"] = "blurry face";
        d["quasi nera"] = "almost black";
        d["quasi tutta bianca"] = "almost all white";
        d["la più nitida della raffica"] = "the sharpest of the burst";
        d["nella raffica ce n'è una più nitida"] = "there is a sharper one in the burst";

        // "Da scartare?"
        d["Da scartare?"] = "To reject?";
        d["Scarta tutte"] = "Reject all";
        d["Scarta tutte ({0})"] = "Reject all ({0})";
        d["Segna come scarti tutte le foto suggerite (si possono rivedere nel filtro Scarti)"] =
            "Mark all suggested photos as rejects (you can review them in the Rejects filter)";
        d["Scartare {0}?"] = "Reject {0}?";
        d["Sono le foto mosse, sfocate, quasi nere o quasi tutte bianche. Vengono solo segnate come scarti: " +
          "puoi rivederle nel filtro «Scarti» e niente viene cancellato senza passare da «Gestisci scarti»."] =
            "These are the shaken, blurry, almost black or almost all white photos. They are only marked as rejects: " +
            "you can review them in the «Rejects» filter and nothing is deleted without going through «Manage rejects».";
        d["Scarta"] = "Reject";
        d["Nessuna foto da scartare"] = "No photos to reject";
        d["L'analisi delle foto è ancora in corso: quelle mosse o sfocate compariranno qui."] =
            "Photos are still being analysed: shaken or blurry ones will appear here.";
        d["Nessuna foto mossa, sfocata o esposta male tra quelle ancora da decidere."] =
            "No shaken, blurry or badly exposed photos among those still to decide.";

        // Persone: stella e cerchia
        d["Preferita"] = "Favourite";
        d["Chi è"] = "Who is it";
        d["Non specificato"] = "Not specified";
        d["Sono io"] = "It's me";
        d["Famiglia"] = "Family";
        d["Amico"] = "Friend";
        d["Conoscente"] = "Acquaintance";
        d["Il volto prende questo nome (anche di una persona già nota); WFastPic lo proporrà anche sui volti somiglianti, in tutte le cartelle."] =
            "The face takes this name (even of a person already known); WFastPic will also suggest it for similar faces, in all folders.";
        d["Nome «{0}» dato a un volto"] = "Name «{0}» given to one face";

        // Dettagli del viewer: voto e volti
        d["Voto"] = "Rating";
        d["Il tuo"] = "Yours";
        d["Automatico"] = "Automatic";
        d["nessuno (tasti 1-5)"] = "none (keys 1-5)";
        d["in analisi…"] = "analysing…";
        d["Volti non ancora analizzati (serve il riconoscimento delle persone, nel pannello a sinistra)."] =
            "Faces not analysed yet (people recognition is needed, in the left panel).";
        d["Nessun volto in questa foto."] = "No faces in this photo.";
        d["Clic su un volto per dire chi è."] = "Click a face to say who it is.";
        d["Proposta: clic per confermare o correggere"] = "Suggestion: click to confirm or correct";
    }
}
