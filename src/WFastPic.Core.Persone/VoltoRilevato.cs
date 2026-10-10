namespace WFastPic.Core.Persone;

/// <summary>
/// Un volto trovato da YuNet, in pixel dell'immagine analizzata: riquadro, affidabilità e i 5 punti
/// nell'ordine del modello (occhio a sinistra nell'immagine, occhio a destra, naso, angolo sinistro e
/// destro della bocca) che servono ad allinearlo per il riconoscimento.
/// </summary>
public sealed record VoltoRilevato(
    float X0, float Y0, float X1, float Y1,
    float Punteggio,
    (float X, float Y)[] Punti)
{
    public float Larghezza => X1 - X0;
    public float Altezza => Y1 - Y0;
}
