namespace WFastPic.Core.Metadata;

/// <summary>
/// Posizione GPS. <see cref="Altitude"/> in metri sul livello del mare e <see cref="Direction"/>
/// (verso dove guardava la camera, gradi da nord in senso orario) se il file li riporta.
/// </summary>
public readonly record struct GeoCoordinate(double Latitude, double Longitude, double? Altitude = null, double? Direction = null);
