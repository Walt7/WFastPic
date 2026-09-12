using System.Diagnostics;
using WFastPic.Core.Library;
using WFastPic.Core.Persistence;

var cartella = args.Length > 0 ? args[0] : @"E:\Fotocamera\2026\Puglia";

if (!Directory.Exists(cartella))
{
    Console.WriteLine($"Cartella non trovata: {cartella}");
    return;
}

Console.WriteLine($"Scansione: {cartella}\n");

var sw = Stopwatch.StartNew();
var foto = FolderScanner.Scan(cartella);
sw.Stop();
Console.WriteLine($"Scan (file-stat + EXIF, no hash): {foto.Count} foto in {sw.ElapsedMilliseconds} ms");

using var db = LibrarySidecarDatabase.OpenOrCreate(cartella);

var swSync = Stopwatch.StartNew();
var risultato = db.SyncScan(foto);
swSync.Stop();
Console.WriteLine($"Sync DB: nuove={risultato.Nuove} aggiornate={risultato.Aggiornate} invariate={risultato.Invariate} in {swSync.ElapsedMilliseconds} ms");

var tutte = db.GetAllPhotos();
Console.WriteLine($"\nTotale foto in DB: {tutte.Count}");
Console.WriteLine($"Sidecar: {Path.Combine(cartella, LibrarySidecarDatabase.CartellaSidecar, LibrarySidecarDatabase.NomeFileDatabase)}");

Console.WriteLine("\nPrime 10 righe:");
foreach (var p in tutte.Take(10))
{
    Console.WriteLine($"  [{p.Id}] {p.PathRelativo}  {p.Size / 1024.0:F0}KB  scatto={p.CaptureDate:yyyy-MM-dd HH:mm}  stato={p.Stato} rating={p.Rating}");
}

// Seconda esecuzione a freddo nello stesso processo: verifica che una riscansione senza modifiche
// non tocchi il DB (tutte invariate) e sia comunque rapida.
Console.WriteLine("\n--- Riscansione (nessuna modifica attesa) ---");
var sw2 = Stopwatch.StartNew();
var foto2 = FolderScanner.Scan(cartella);
var risultato2 = db.SyncScan(foto2);
sw2.Stop();
Console.WriteLine($"Riscansione+sync: nuove={risultato2.Nuove} aggiornate={risultato2.Aggiornate} invariate={risultato2.Invariate} in {sw2.ElapsedMilliseconds} ms");
