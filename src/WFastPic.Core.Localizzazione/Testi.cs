using System.Globalization;

namespace WFastPic.Core.Localizzazione;

public enum Lingua { Italiano, Inglese }

/// <summary>
/// Testi dell'interfaccia in italiano e inglese. La chiave è il testo italiano (la lingua originale
/// dell'app): <c>T("Apri cartella…")</c> restituisce l'originale in italiano e la traduzione in inglese,
/// così il codice resta leggibile e un testo mancante in inglese si vede subito come italiano invece di
/// sparire. Per i testi con valori: <c>T("{0} di {1}", a, b)</c> (formato composto, non stringhe
/// interpolate). La lingua si sceglie all'avvio (preferenza "auto" = lingua di Windows) e non cambia
/// mentre l'app gira. Logica pura, senza dipendenze da WinUI.
/// </summary>
public static class Testi
{
    private static Lingua _corrente = Lingua.Italiano;
    private static CultureInfo _cultura = CultureInfo.GetCultureInfo("it-IT");

    public static Lingua Corrente => _corrente;

    /// <summary>Cultura per date e numeri nella lingua scelta (it-IT, en-GB: giorno prima del mese come in italiano).</summary>
    public static CultureInfo Cultura => _cultura;

    public static void Imposta(Lingua lingua)
    {
        _corrente = lingua;
        _cultura = CultureInfo.GetCultureInfo(lingua == Lingua.Inglese ? "en-GB" : "it-IT");
    }

    /// <summary>
    /// "auto" (o vuoto): italiano se Windows è in italiano, inglese altrimenti; "it" / "en" forzano la lingua.
    /// </summary>
    public static Lingua Risolvi(string? preferenza, CultureInfo? sistema)
    {
        switch (preferenza?.Trim().ToLowerInvariant())
        {
            case "it": return Lingua.Italiano;
            case "en": return Lingua.Inglese;
            default:
                var codice = (sistema ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
                return string.Equals(codice, "it", StringComparison.OrdinalIgnoreCase) ? Lingua.Italiano : Lingua.Inglese;
        }
    }

    /// <summary>Il testo nella lingua corrente; senza traduzione resta quello italiano.</summary>
    public static string T(string italiano) =>
        _corrente == Lingua.Inglese && TestiEn.Voci.TryGetValue(italiano, out var inglese) ? inglese : italiano;

    /// <summary>Come <see cref="T(string)"/> per un formato composto ("{0} di {1}"), con la cultura della lingua.</summary>
    public static string T(string formatoItaliano, params object?[] argomenti) =>
        string.Format(_cultura, T(formatoItaliano), argomenti);

    /// <summary>"1 foto" / "340 foto": il plurale inglese ("1 photo" / "340 photos") sta nelle due voci.</summary>
    public static string Foto(int numero) =>
        numero == 1 ? T("1 foto") : T("{0:N0} foto", numero);

    /// <summary>
    /// Pagine HTML fisse (mappe) con testi tra <c>@@…@@</c>: <c>'@@Mappa@@'</c> diventa il testo nella lingua corrente.
    /// </summary>
    public static string Segnaposti(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "@@(.+?)@@", m => T(m.Groups[1].Value));

    /// <summary>Tutte le traduzioni inglesi (italiano → inglese): serve ai controlli di completezza.</summary>
    public static IReadOnlyDictionary<string, string> TraduzioniInglesi => TestiEn.Voci;
}
