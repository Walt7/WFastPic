namespace WFastPic.Core.Processing;

/// <summary>
/// Ricetta di editing non distruttiva: mai scritta sull'originale, applicata al volo in
/// preview/export. Crop, raddrizzamento e regolazioni tonali stile Picasa (Basic Fixes + Tuning).
/// Crop espresso in coordinate normalizzate (0..1) relative alla tela raddrizzata (stessa
/// dimensione dell'immagine), cosi la ricetta resta valida a qualunque risoluzione di decode.
/// </summary>
public sealed record EditRecipe
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public double CropX { get; init; } = 0;
    public double CropY { get; init; } = 0;
    public double CropWidth { get; init; } = 1;
    public double CropHeight { get; init; } = 1;

    public double StraightenAngleDegrees { get; init; } = 0;

    /// <summary>Legacy schema v1 ("auto-correzione"): migrato in AutoContrast+AutoColor da <see cref="EditRecipeSerializer"/>.</summary>
    public bool AutoCorrect { get; init; } = false;

    /// <summary>Stretch della luminanza (tutti i canali con lo stesso guadagno, niente viraggi).</summary>
    public bool AutoContrast { get; init; } = false;

    /// <summary>Bilanciamento dei canali (grigio medio), guadagni limitati.</summary>
    public bool AutoColor { get; init; } = false;

    /// <summary>0..1: schiarisce le ombre e i mezzitoni scuri, bianchi e neri restano fermi.</summary>
    public double FillLight { get; init; } = 0;

    /// <summary>0..1: scurisce le alte luci.</summary>
    public double Highlights { get; init; } = 0;

    /// <summary>0..1: scurisce le ombre.</summary>
    public double Shadows { get; init; } = 0;

    /// <summary>-1 (fredda) .. +1 (calda).</summary>
    public double Temperature { get; init; } = 0;

    public static EditRecipe Default => new();

    public bool HasTone =>
        AutoCorrect || AutoContrast || AutoColor || FillLight != 0 || Highlights != 0 || Shadows != 0 || Temperature != 0;

    public bool HasGeometry =>
        StraightenAngleDegrees != 0 || CropX != 0 || CropY != 0 || CropWidth != 1 || CropHeight != 1;

    /// <summary>Per i simboli sulle miniature: ritaglio vero e proprio (non il solo raddrizzamento).</summary>
    public bool HasCrop => CropX != 0 || CropY != 0 || CropWidth != 1 || CropHeight != 1;

    public bool HasStraighten => StraightenAngleDegrees != 0;
}
