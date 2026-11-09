using System.Runtime.InteropServices;

namespace WFastPic.Mcp;

/// <summary>
/// Tiene sveglio Windows finché dura un lavoro lungo (l'analisi di ore dell'archivio): senza, il PC va in
/// sospensione e il lavoro si ferma. Impedisce solo il sonno automatico, non la chiusura del coperchio.
/// SetThreadExecutionState vale per il thread che lo chiama: ne serve uno dedicato, vivo fino al Dispose.
/// </summary>
internal sealed class NonDormire : IDisposable
{
    private const uint Continuo = 0x80000000, SistemaRichiesto = 0x00000001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint stato);

    private readonly ManualResetEventSlim _fine = new(false);
    private readonly Thread _thread;

    private NonDormire()
    {
        _thread = new Thread(() =>
        {
            SetThreadExecutionState(Continuo | SistemaRichiesto);
            _fine.Wait();
            SetThreadExecutionState(Continuo);
        }) { IsBackground = true, Name = "NonDormire" };
        _thread.Start();
    }

    public static NonDormire Attiva() => new();

    public void Dispose()
    {
        _fine.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        _fine.Dispose();
    }
}
