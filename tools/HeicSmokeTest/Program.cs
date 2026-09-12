using System.Diagnostics;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

var samplesDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "samples");
samplesDir = Path.GetFullPath(samplesDir);
var outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out");
outDir = Path.GetFullPath(outDir);
System.IO.Directory.CreateDirectory(outDir);

var files = System.IO.Directory.GetFiles(samplesDir, "*.heic").OrderBy(f => new FileInfo(f).Length).ToArray();
if (files.Length == 0)
{
    Console.WriteLine($"Nessun file .heic trovato in {samplesDir}");
    return;
}

Console.WriteLine($"Trovati {files.Length} file HEIC di test in {samplesDir}\n");

var rows = new List<Row>();

foreach (var path in files)
{
    var name = Path.GetFileName(path);
    var sizeKb = new FileInfo(path).Length / 1024.0;
    var row = new Row { Name = name, SizeKb = sizeKb };

    try
    {
        // --- MetadataExtractor: EXIF/GPS/data scatto, indipendente dal decoder immagine ---
        var directories = ImageMetadataReader.ReadMetadata(path);
        var exifSub = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();

        row.CaptureDate = exifSub?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal) ?? "n/d";
        row.OrientationTag = exifIfd0?.GetDescription(ExifDirectoryBase.TagOrientation) ?? "n/d";
        row.HasGps = gps?.GetGeoLocation() is not null;
    }
    catch (Exception ex)
    {
        row.MetadataError = ex.Message;
    }

    try
    {
        using var fs = File.OpenRead(path);
        using var winrtStream = fs.AsRandomAccessStream();

        var swOpen = Stopwatch.StartNew();
        var decoder = await BitmapDecoder.CreateAsync(winrtStream);
        swOpen.Stop();
        row.OpenMs = swOpen.Elapsed.TotalMilliseconds;
        row.PixelWidth = (int)decoder.PixelWidth;
        row.PixelHeight = (int)decoder.PixelHeight;
        row.OrientedWidth = (int)decoder.OrientedPixelWidth;
        row.OrientedHeight = (int)decoder.OrientedPixelHeight;

        row.ThumbMs = await DecodeScaledAsync(decoder, 256, Path.Combine(outDir, $"thumb_{name}.png"));
        row.PreviewMs = await DecodeScaledAsync(decoder, 2048, null);

        var swEmbed = Stopwatch.StartNew();
        try
        {
            var embeddedThumb = await decoder.GetThumbnailAsync();
            swEmbed.Stop();
            row.EmbeddedThumbMs = swEmbed.Elapsed.TotalMilliseconds;
            row.EmbeddedThumbSize = $"{embeddedThumb.Size}B ({embeddedThumb.ContentType})";

            using var reader = new DataReader(embeddedThumb);
            await reader.LoadAsync((uint)embeddedThumb.Size);
            var bytes = new byte[embeddedThumb.Size];
            reader.ReadBytes(bytes);
            await File.WriteAllBytesAsync(Path.Combine(outDir, $"embed_{name}.jpg"), bytes);
        }
        catch (Exception ex)
        {
            row.EmbeddedThumbMs = -1;
            row.EmbeddedThumbSize = "ERR:" + ex.Message;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var memBefore = Process.GetCurrentProcess().WorkingSet64;
        var swFull = Stopwatch.StartNew();
        using (var full = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage))
        {
            swFull.Stop();
            row.FullMs = swFull.Elapsed.TotalMilliseconds;
        }
        var memAfter = Process.GetCurrentProcess().WorkingSet64;
        row.FullDecodeDeltaMb = (memAfter - memBefore) / 1024.0 / 1024.0;
    }
    catch (Exception ex)
    {
        row.DecodeError = ex.Message;
    }

    rows.Add(row);
    Console.WriteLine(row);
}

Console.WriteLine("\n--- Riepilogo ---");
Console.WriteLine($"{"File",-28} {"KB",7} {"Px",11} {"Open ms",8} {"Thumb ms",9} {"Prev ms",8} {"Full ms",8} {"ΔMB",6} {"Orient",7} {"GPS",4} {"Errori"}");
foreach (var r in rows)
{
    Console.WriteLine($"{r.Name,-28} {r.SizeKb,7:F0} {r.PixelWidth}x{r.PixelHeight,-5} {r.OpenMs,8:F1} {r.ThumbMs,9:F1} {r.PreviewMs,8:F1} {r.FullMs,8:F1} {r.FullDecodeDeltaMb,6:F1} {r.OrientationTag,7} {(r.HasGps == true ? "si" : "no"),4} {r.DecodeError ?? r.MetadataError}");
}

var ok = rows.Count(r => r.DecodeError is null);
Console.WriteLine($"\nDecodifica riuscita: {ok}/{rows.Count}");
Console.WriteLine($"Thumbnail PNG di verifica orientamento scritte in: {outDir}");

static async Task<double> DecodeScaledAsync(BitmapDecoder decoder, int maxEdge, string? savePngPath)
{
    var ow = (double)decoder.OrientedPixelWidth;
    var oh = (double)decoder.OrientedPixelHeight;
    var scale = maxEdge / Math.Max(ow, oh);
    var transform = new BitmapTransform
    {
        InterpolationMode = BitmapInterpolationMode.Fant,
        ScaledWidth = (uint)Math.Max(1, Math.Round(ow * scale)),
        ScaledHeight = (uint)Math.Max(1, Math.Round(oh * scale)),
    };

    var sw = Stopwatch.StartNew();
    using var bmp = await decoder.GetSoftwareBitmapAsync(
        BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
        transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
    sw.Stop();

    if (savePngPath is not null)
    {
        using var outStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream);
        encoder.SetSoftwareBitmap(bmp);
        await encoder.FlushAsync();

        using var fileStream = File.Create(savePngPath);
        outStream.Seek(0);
        using var netStream = outStream.AsStreamForRead();
        await netStream.CopyToAsync(fileStream);
    }

    return sw.Elapsed.TotalMilliseconds;
}

class Row
{
    public string Name = "";
    public double SizeKb;
    public double OpenMs, ThumbMs, PreviewMs, FullMs, FullDecodeDeltaMb, EmbeddedThumbMs;
    public string EmbeddedThumbSize = "n/d";
    public int PixelWidth, PixelHeight, OrientedWidth, OrientedHeight;
    public string CaptureDate = "n/d";
    public string OrientationTag = "n/d";
    public bool? HasGps;
    public string? DecodeError;
    public string? MetadataError;

    public override string ToString() =>
        $"{Name}: {SizeKb:F0}KB px={PixelWidth}x{PixelHeight} orient={OrientationTag} data={CaptureDate} gps={(HasGps == true ? "si" : "no")}" +
        (DecodeError is null
            ? $"  open={OpenMs:F1}ms thumb256={ThumbMs:F1}ms preview2048={PreviewMs:F1}ms full={FullMs:F1}ms Δmem={FullDecodeDeltaMb:F1}MB embedThumb={EmbeddedThumbMs:F1}ms({EmbeddedThumbSize})"
            : $"  ERRORE DECODING: {DecodeError}");
}
