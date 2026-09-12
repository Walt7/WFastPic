namespace WFastPic.Core.Processing;

/// <summary>
/// Ricetta di editing non distruttiva: mai scritta sull'originale, applicata al volo in
/// preview/export. Solo le tre operazioni del primo giro (crop, raddrizzamento, auto-correzione) —
/// esposizione/contrasto/WB/saturazione/nitidezza restano fuori scope per ora.
/// Crop espresso in coordinate normalizzate (0..1) relative all'immagine intera, cosi la ricetta
/// resta valida indipendentemente dalla risoluzione con cui viene poi decodificata (thumbnail,
/// preview o full-res).
/// </summary>
public sealed record EditRecipe
{
    public int SchemaVersion { get; init; } = 1;

    public double CropX { get; init; } = 0;
    public double CropY { get; init; } = 0;
    public double CropWidth { get; init; } = 1;
    public double CropHeight { get; init; } = 1;

    public double StraightenAngleDegrees { get; init; } = 0;

    public bool AutoCorrect { get; init; } = false;

    public static EditRecipe Default => new();
}
