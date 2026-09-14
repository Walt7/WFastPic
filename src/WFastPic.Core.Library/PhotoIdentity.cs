using WFastPic.Core.Metadata;

namespace WFastPic.Core.Library;

/// <summary>
/// Identità leggera di un file per rilevare modifiche senza calcolare un hash del contenuto.
/// Un hash completo va calcolato solo su richiesta esplicita (es. rilevare un rinominato con stesso
/// contenuto), mai in scansione di massa su centinaia/migliaia di file.
/// </summary>
public sealed record PhotoIdentity(string PathRelativo, long Size, DateTime LastWriteUtc);

public sealed record ScannedPhoto(PhotoIdentity Identity, DateTime? CaptureDate, GeoCoordinate? Gps = null);
