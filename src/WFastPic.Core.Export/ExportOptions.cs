namespace WFastPic.Core.Export;

public enum PosizioneWatermark { AltoSinistra, AltoDestra, BassoSinistra, BassoDestra, Centro }

/// <summary>
/// Opzioni di un export batch, sul modello del dialog "Esporta" di Picasa: cartella di output
/// (default: sottocartella "Export" dentro la cartella foto), dimensioni originali o lato lungo
/// massimo a scelta, qualità JPEG, filigrana testuale opzionale. In più: tag scritti come Keywords
/// EXIF e la scelta se tenere la posizione GPS nei file condivisi.
/// </summary>
public sealed record ExportOptions
{
    public const int LatoLungoMinimo = 200;
    public const int LatoLungoMassimoConsentito = 20000;

    public required string CartellaOutput { get; init; }

    /// <summary>Lato lungo massimo in pixel; null = dimensioni originali. Mai ingrandita.</summary>
    public int? LatoLungoMassimo { get; init; } = 2048;

    public int QualitaJpeg { get; init; } = 90;

    public string? TestoWatermark { get; init; }
    public double OpacitaWatermark { get; init; } = 0.5;
    public PosizioneWatermark PosizioneWatermark { get; init; } = PosizioneWatermark.BassoDestra;

    public IReadOnlyList<string> Tag { get; init; } = [];

    /// <summary>true = solo le selezionate; false = tutte tranne gli scarti (mai gli scarti).</summary>
    public bool SoloSelezionate { get; init; } = true;

    /// <summary>Le foto esportate si condividono: la posizione (anche di casa) si può togliere.</summary>
    public bool RimuoviGps { get; init; }

    /// <summary>
    /// Riesportando dopo una correzione il JPEG vecchio viene sostituito invece di affiancare
    /// "_2". Mai nella cartella delle foto stessa: li si numera sempre, per non toccare originali.
    /// </summary>
    public bool SostituisciEsistenti { get; init; } = true;

    /// <summary>Sottocartella di default proposta all'utente, dentro la cartella foto corrente.</summary>
    public static string CartellaOutputDefault(string cartellaFoto) => Path.Combine(cartellaFoto, "Export");
}
