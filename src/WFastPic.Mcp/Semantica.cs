using System.Diagnostics;
using System.Text.Json;

namespace WFastPic.Mcp;

/// <summary>
/// Ricerca per significato in foto e video («piazza con fontana», «ballerine di flamenco»): usa lo script Python
/// <c>ricerca_semantica.py</c> (CLIP, tutto in locale; i video a fotogrammi con ffmpeg). Gli indici stanno in
/// <c>LocalAppData\WFastPic\clip</c>; le foto non vengono mai toccate né inviate altrove.
/// </summary>
public static class Semantica
{
    private static readonly Lock Lucchetto = new();
    private static Process? _indicizzazione;

    public static string CartellaIndici => Path.Combine(Ricerca.DatiApp, "clip");

    private static string Python => Environment.GetEnvironmentVariable("WFASTPIC_PYTHON") ?? "python";

    private static string Script
    {
        get
        {
            var accanto = Path.Combine(AppContext.BaseDirectory, "ricerca_semantica.py");
            if (File.Exists(accanto)) return accanto;
            throw new InvalidOperationException("Manca ricerca_semantica.py accanto a WFastPic.Mcp.exe.");
        }
    }

    private static ProcessStartInfo Avvio(IEnumerable<string> argomenti)
    {
        var psi = new ProcessStartInfo(Python)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = new System.Text.UTF8Encoding(false), StandardErrorEncoding = new System.Text.UTF8Encoding(false),
        };
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["TOKENIZERS_PARALLELISM"] = "false";
        psi.ArgumentList.Add(Script);
        foreach (var a in argomenti) psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>Avvia in background l'indice di una cartella (foto e video, sottocartelle comprese); riprende da dove era.</summary>
    public static void Indicizza(string cartella, int ogniSecondi)
    {
        if (!Directory.Exists(cartella)) throw new InvalidOperationException($"Cartella non trovata: {cartella}");
        lock (Lucchetto)
        {
            if (_indicizzazione is { HasExited: false }) throw new InvalidOperationException("Un'indicizzazione è già in corso: controlla con stato_significato.");
            Directory.CreateDirectory(CartellaIndici);
            var p = Process.Start(Avvio(["indicizza", Path.GetFullPath(cartella), CartellaIndici, $"--ogni={Math.Max(1, ogniSecondi)}"]))
                ?? throw new InvalidOperationException("Impossibile avviare Python.");
            p.OutputDataReceived += (_, _) => { };
            p.ErrorDataReceived += (_, _) => { };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _indicizzazione = p;
        }
    }

    public static object Stato()
    {
        var file = Path.Combine(CartellaIndici, "stato_clip.json");
        if (!File.Exists(file)) return new { nota = "Nessuna indicizzazione avviata." };
        return JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file));
    }

    /// <summary>Le migliori corrispondenze per <paramref name="testo"/>; <paramref name="solo"/> limita a certi percorsi (foto già filtrate per persone, date, luogo…).</summary>
    public static JsonElement Cerca(string testo, string? cartella, IReadOnlyCollection<string>? solo, int limite)
    {
        if (!Directory.Exists(CartellaIndici) || !Directory.EnumerateFiles(CartellaIndici, "clip_*.npz").Any())
            throw new InvalidOperationException("Nessun indice: lancia prima indicizza_significato su una cartella.");
        var argomenti = new List<string> { "cerca", testo, CartellaIndici, $"--limite={limite}" };
        if (!string.IsNullOrWhiteSpace(cartella)) argomenti.Add($"--cartella={cartella}");
        string? lista = null;
        try
        {
            if (solo is not null)
            {
                lista = Path.GetTempFileName();
                File.WriteAllLines(lista, solo);
                argomenti.Add($"--solo={lista}");
            }
            using var p = Process.Start(Avvio(argomenti)) ?? throw new InvalidOperationException("Impossibile avviare Python.");
            var errori = p.StandardError.ReadToEndAsync();
            var uscita = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException("Ricerca per significato non riuscita: " + Ultime(errori.Result));
            return JsonSerializer.Deserialize<JsonElement>(uscita.Split('\n').Last(r => r.TrimStart().StartsWith('[')));
        }
        finally { if (lista is not null) File.Delete(lista); }
    }

    private static string Ultime(string s) => string.Join(" | ", s.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(3));
}
