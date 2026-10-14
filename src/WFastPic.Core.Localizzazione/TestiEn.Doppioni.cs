namespace WFastPic.Core.Localizzazione;

internal static partial class TestiEn
{
    /// <summary>Filtro «Doppioni»: gruppi, copia da tenere, "Scarta le copie".</summary>
    private static void Doppioni(Dictionary<string, string> d)
    {
        d["Doppioni"] = "Duplicates";
        d["Scarta le copie"] = "Reject the copies";
        d["Scarta le copie ({0})"] = "Reject the copies ({0})";
        d["Tiene in ogni gruppo la copia suggerita e segna come scarti le altre (Shift+Space: tieni questa)"] =
            "Keeps the suggested copy in each group and marks the others as rejects (Shift+Space: keep this one)";
        d["Foto identiche · {0}"] = "Identical files · {0}";
        d["Stessa foto salvata più volte · {0}"] = "Same photo saved more than once · {0}";
        d["In ogni gruppo resta la copia da tenere (quella che hai scelto tu, altrimenti l'originale più " +
          "grande, a volte in un'altra cartella) e le altre di questa cartella vengono solo segnate come " +
          "scarti: puoi rivederle nel filtro «Scarti» e niente viene cancellato senza passare da " +
          "«Gestisci scarti». Le altre cartelle non vengono toccate."] =
            "In each group the copy to keep stays (the one you chose, otherwise the largest original, sometimes in " +
            "another folder) and the others in this folder are only marked as rejects: you can review them in the " +
            "«Rejects» filter and nothing is deleted without going through «Manage rejects». Other folders are not touched.";
        d["Nessun doppione"] = "No duplicates";
        d["La ricerca dei doppioni parte quando le miniature sono pronte: le foto doppie compariranno qui."] =
            "The duplicate search starts when the thumbnails are ready: duplicate photos will appear here.";
        d["In questa cartella ogni foto c'è una volta sola."] = "Every photo in this folder appears only once.";
        d["Doppione: file identico a un altro"] = "Duplicate: file identical to another";
        d["Doppione: la stessa foto salvata più volte"] = "Duplicate: the same photo saved more than once";
        d["la copia da tenere"] = "the copy to keep";
        d["anche in {0}"] = "also in {0}";
        d["la copia da tenere è in {0}"] = "the copy to keep is in {0}";
    }
}
