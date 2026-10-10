namespace WFastPic.Core.Localizzazione;

/// <summary>
/// Traduzioni inglesi, per area: chiave = testo italiano esatto usato nel codice o nello XAML.
/// Il test <c>TraduzioniTests</c> controlla che ogni testo dell'interfaccia abbia la sua voce e che i
/// segnaposto ({0}, {1}…) coincidano tra italiano e inglese.
/// </summary>
internal static partial class TestiEn
{
    internal static readonly Dictionary<string, string> Voci = Crea();

    private static Dictionary<string, string> Crea()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        FinestraXaml(d);
        ViewerXaml(d);
        FinestraCodice(d);
        ViewerCodice(d);
        PersoneCodice(d);
        Libreria(d);
        VotoAutomatico(d);
        return d;
    }
}
