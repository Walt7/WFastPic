using System.Globalization;

namespace WFastPic.Core.Metadata;

public enum TipoDispositivo { Sconosciuto, Telefono, Drone, Fotocamera }

/// <summary>
/// Come, con cosa e da dove è stata scattata la foto. Tutti i campi sono opzionali: un telefono di
/// solito ha altitudine e direzione, un drone DJI anche altezza dal decollo e inclinazione del gimbal.
/// </summary>
/// <param name="AltitudineMetri">Sul livello del mare (EXIF GPSAltitude, o AbsoluteAltitude DJI).</param>
/// <param name="AltezzaDalDecolloMetri">Drone: quota relativa al punto di decollo (DJI RelativeAltitude).</param>
/// <param name="DirezioneGradi">Verso dove guarda la camera, 0 = nord, in senso orario.</param>
/// <param name="InclinazioneGradi">Gimbal: 0 = orizzontale, -90 = camera rivolta in basso (nadir).</param>
/// <param name="Dispositivo">Marca e modello leggibili, es. "Apple iPhone 15 Pro", "DJI FC8482".</param>
public sealed record DatiRipresa(
    double? AltitudineMetri,
    double? AltezzaDalDecolloMetri,
    double? DirezioneGradi,
    double? InclinazioneGradi,
    string? Dispositivo = null,
    TipoDispositivo Tipo = TipoDispositivo.Sconosciuto)
{
    public static readonly DatiRipresa Vuoti = new(null, null, null, null);

    private static readonly string[] MarcheDroni = ["DJI", "Autel", "Parrot", "Skydio", "Yuneec", "Hubsan", "Potensic"];

    private static readonly string[] MarcheTelefoni =
    [
        "Apple", "samsung", "Google", "Xiaomi", "Redmi", "POCO", "HUAWEI", "HONOR", "OnePlus", "OPPO", "vivo",
        "realme", "motorola", "Nothing", "Sony Mobile", "Fairphone", "ASUS", "LGE", "HMD Global", "Nokia",
    ];

    /// <summary>EXIF + proprietà XMP (chiave "prefisso:Nome"). L'XMP DJI ha la precedenza dove più preciso.</summary>
    public static DatiRipresa Componi(
        double? altitudineExif, double? direzioneExif, IReadOnlyDictionary<string, string> xmp,
        string? marca = null, string? modello = null)
    {
        double? Xmp(string nome) =>
            xmp.TryGetValue("drone-dji:" + nome, out var testo)
            && double.TryParse(testo.Trim().TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out var valore)
                ? valore
                : null;

        var direzione = Xmp("GimbalYawDegree") ?? direzioneExif ?? Xmp("FlightYawDegree");
        var haXmpDrone = xmp.Keys.Any(k => k.StartsWith("drone-dji:", StringComparison.Ordinal));

        return new DatiRipresa(
            altitudineExif ?? Xmp("AbsoluteAltitude"),
            Xmp("RelativeAltitude"),
            direzione is double d ? NormalizzaDirezione(d) : null,
            Xmp("GimbalPitchDegree"),
            NomeDispositivo(marca, modello),
            ClassificaDispositivo(marca, modello, haXmpDrone));
    }

    /// <summary>"Apple" + "iPhone 15 Pro" → "Apple iPhone 15 Pro"; niente marca ripetuta se il modello la contiene già.</summary>
    public static string? NomeDispositivo(string? marca, string? modello)
    {
        marca = marca?.Trim().TrimEnd('\0');
        modello = modello?.Trim().TrimEnd('\0');
        if (string.IsNullOrEmpty(modello)) return string.IsNullOrEmpty(marca) ? null : marca;
        if (string.IsNullOrEmpty(marca) || modello.StartsWith(marca, StringComparison.OrdinalIgnoreCase)) return modello;
        return $"{marca} {modello}";
    }

    public static TipoDispositivo ClassificaDispositivo(string? marca, string? modello, bool haXmpDrone)
    {
        bool Inizia(string[] elenco) => elenco.Any(m =>
            (marca?.Trim().StartsWith(m, StringComparison.OrdinalIgnoreCase) ?? false)
            || (modello?.Trim().StartsWith(m, StringComparison.OrdinalIgnoreCase) ?? false));

        if (haXmpDrone || Inizia(MarcheDroni)) return TipoDispositivo.Drone;
        if (Inizia(MarcheTelefoni)) return TipoDispositivo.Telefono;
        return string.IsNullOrWhiteSpace(marca) && string.IsNullOrWhiteSpace(modello)
            ? TipoDispositivo.Sconosciuto
            : TipoDispositivo.Fotocamera;
    }

    // I droni DJI usano -180..180: si riporta a 0..360 come una bussola.
    private static double NormalizzaDirezione(double gradi) => ((gradi % 360) + 360) % 360;

    /// <summary>"NE", "SO"…: punto cardinale a 8 settori, come si legge su una bussola.</summary>
    public static string PuntoCardinale(double gradi)
    {
        string[] settori = ["N", "NE", "E", "SE", "S", "SO", "O", "NO"];
        return settori[(int)Math.Round(NormalizzaDirezione(gradi) / 45) % 8];
    }
}
