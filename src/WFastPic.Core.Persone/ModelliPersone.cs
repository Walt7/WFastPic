using System.Security.Cryptography;

namespace WFastPic.Core.Persone;

/// <summary>Un modello ONNX da scaricare: fonte ufficiale e impronta SHA-256 attesa.</summary>
public sealed record ModelloOnnx(string NomeFile, string Url, string Sha256, long Byte);

/// <summary>
/// Modelli per riconoscere le persone (OpenCV Zoo su Hugging Face): YuNet trova i volti (MIT), SFace
/// ne calcola l'impronta (Apache 2.0). Non sono nel repository: si scaricano una volta al primo uso
/// in LocalAppData e se ne verifica l'SHA-256, così un file corrotto o diverso non viene mai usato.
/// </summary>
public static class ModelliPersone
{
    public static readonly ModelloOnnx Rilevatore = new(
        "face_detection_yunet_2023mar.onnx",
        "https://huggingface.co/opencv/face_detection_yunet/resolve/main/face_detection_yunet_2023mar.onnx",
        "8F2383E4DD3CFBB4553EA8718107FC0423210DC964F9F4280604804ED2552FA4",
        232_589);

    public static readonly ModelloOnnx Riconoscimento = new(
        "face_recognition_sface_2021dec.onnx",
        "https://huggingface.co/opencv/face_recognition_sface/resolve/main/face_recognition_sface_2021dec.onnx",
        "0BA9FBFA01B5270C96627C4EF784DA859931E02F04419C829E83484087C34E79",
        38_696_353);

    public static IReadOnlyList<ModelloOnnx> Tutti => [Rilevatore, Riconoscimento];

    public static string CartellaPredefinita => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WFastPic", "modelli");

    public static string Percorso(string cartella, ModelloOnnx modello) => Path.Combine(cartella, modello.NomeFile);

    /// <summary>Presenti con la dimensione giusta (lo SHA-256 è verificato al download).</summary>
    public static bool Presenti(string cartella) =>
        Tutti.All(m => new FileInfo(Percorso(cartella, m)) is { Exists: true } f && f.Length == m.Byte);

    /// <summary>
    /// Scarica i modelli mancanti. Progresso in byte su tutti i modelli da scaricare. Scrive su un file
    /// temporaneo e lo rinomina solo se l'SHA-256 coincide: mai un modello a metà o alterato in uso.
    /// </summary>
    public static async Task AssicuraAsync(HttpClient http, string cartella, IProgress<(long Scaricati, long Totale)>? progresso = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cartella);
        var mancanti = Tutti.Where(m => !(new FileInfo(Percorso(cartella, m)) is { Exists: true } f && f.Length == m.Byte)).ToList();
        var totale = mancanti.Sum(m => m.Byte);
        long scaricati = 0;

        foreach (var modello in mancanti)
        {
            var temporaneo = Percorso(cartella, modello) + ".part";
            try
            {
                using (var risposta = await http.GetAsync(modello.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    risposta.EnsureSuccessStatusCode();
                    await using var sorgente = await risposta.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using var file = File.Create(temporaneo);
                    var buffer = new byte[81920];
                    int letti;
                    while ((letti = await sorgente.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, letti), ct).ConfigureAwait(false);
                        scaricati += letti;
                        progresso?.Report((scaricati, totale));
                    }
                }

                var hash = Convert.ToHexString(await Sha256Async(temporaneo, ct).ConfigureAwait(false));
                if (!string.Equals(hash, modello.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{modello.NomeFile}: impronta SHA-256 diversa da quella attesa, file scartato.");

                File.Move(temporaneo, Percorso(cartella, modello), overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temporaneo)) File.Delete(temporaneo); } catch { /* conserva l'errore originale */ }
            }
        }
    }

    private static async Task<byte[]> Sha256Async(string percorso, CancellationToken ct)
    {
        await using var file = File.OpenRead(percorso);
        return await SHA256.HashDataAsync(file, ct).ConfigureAwait(false);
    }
}
