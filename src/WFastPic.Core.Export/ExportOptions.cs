namespace WFastPic.Core.Export;

public enum PresetLatoLungo { Px1600, Px2048, Px3200, Originale }

public enum PosizioneWatermark { AltoSinistra, AltoDestra, BassoSinistra, BassoDestra, Centro }

public static class PresetLatoLungoEstensioni
{
    /// <summary>Lato lungo in pixel per il preset, o null per "originale" (nessuna riduzione).</summary>
    public static int? LatoLungoPixel(this PresetLatoLungo preset) => preset switch
    {
        PresetLatoLungo.Px1600 => 1600,
        PresetLatoLungo.Px2048 => 2048,
        PresetLatoLungo.Px3200 => 3200,
        PresetLatoLungo.Originale => null,
        _ => 2048,
    };
}

/// <summary>
/// Opzioni di un export batch. Cartella di output personalizzabile (default: sottocartella
/// "Export" dentro la cartella foto già aperta), watermark testuale opzionale e tag
/// personalizzabili scritti come Keywords EXIF/IPTC nel JPEG risultante.
/// </summary>
public sealed record ExportOptions
{
    public required string CartellaOutput { get; init; }
    public PresetLatoLungo LatoLungo { get; init; } = PresetLatoLungo.Px2048;
    public int QualitaJpeg { get; init; } = 90;

    public string? TestoWatermark { get; init; }
    public double OpacitaWatermark { get; init; } = 0.5;
    public PosizioneWatermark PosizioneWatermark { get; init; } = PosizioneWatermark.BassoDestra;

    public IReadOnlyList<string> Tag { get; init; } = [];

    public bool SoloSelezionate { get; init; } = true;

    /// <summary>Sottocartella di default proposta all'utente, dentro la cartella foto corrente.</summary>
    public static string CartellaOutputDefault(string cartellaFoto) => Path.Combine(cartellaFoto, "Export");
}
