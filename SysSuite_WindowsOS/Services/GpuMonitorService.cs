using SysSuite.Models;
using Vortice.DXGI;

namespace SysSuite.Services;

/// <summary>
/// Metriche GPU via DXGI 1.4 (<c>QueryVideoMemoryInfo</c>), senza WMI.
/// Interop DXGI tramite <see cref="Vortice.DXGI"/> (SharpGen) per evitare RCW <c>ComImport</c> manuali che causavano <c>AccessViolationException</c> in avvio.
/// </summary>
public sealed class GpuMonitorService : IDisposable
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);

    private IDXGIFactory4? _factory;
    private IDXGIAdapter3? _adapter3;
    private string _cachedName;
    private bool _disposed;

    public GpuMonitorService()
    {
        _cachedName = "GPU non disponibile";

        IDXGIFactory4? factory;
        try
        {
            factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>();
        }
        catch
        {
            return;
        }

        try
        {
            if (!TrySelectAdapter(factory, out var adapter3, out var chosenName))
                return;

            _factory = factory;
            _adapter3 = adapter3;
            _cachedName = chosenName;
            factory = null;
        }
        finally
        {
            factory?.Dispose();
        }
    }

    /// <summary>
    /// Legge solo <c>QueryVideoMemoryInfo</c> (segmento locale). Totale = Budget DXGI, usato = CurrentUsage.
    /// </summary>
    public GpuMetrics GetGpuMetrics()
    {
        if (_adapter3 == null)
            return new GpuMetrics(_cachedName, 0, 0, 0);

        var info = _adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
        var used = (long)info.CurrentUsage;
        var total = (long)info.Budget;
        var pct = total > 0 ? 100.0 * used / total : 0;
        if (pct < 0)
            pct = 0;
        if (pct > 100)
            pct = 100;

        return new GpuMetrics(_cachedName, used, total, pct);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _adapter3?.Dispose();
        _adapter3 = null;
        _factory?.Dispose();
        _factory = null;
    }

    private static bool TrySelectAdapter(
        IDXGIFactory4 factory,
        out IDXGIAdapter3? adapter3,
        out string name)
    {
        adapter3 = null;
        name = "GPU non disponibile";

        nuint bestDedicated = 0;
        IDXGIAdapter3? bestAdapter = null;
        var bestName = name;

        for (uint i = 0; ; i++)
        {
            var r = factory.EnumAdapters1(i, out var adapter1);
            if (r.Code == DxgiErrorNotFound)
                break;
            if (r.Failure || adapter1 == null)
                continue;

            using (adapter1)
            {
                if (adapter1.Description1.Flags.HasFlag(AdapterFlags.Software))
                    continue;

                var cand = adapter1.QueryInterface<IDXGIAdapter3>();
                if (cand == null)
                    continue;

                try
                {
                    var dedicated = adapter1.Description1.DedicatedVideoMemory;
                    if (bestAdapter == null || dedicated > bestDedicated)
                    {
                        bestAdapter?.Dispose();
                        bestAdapter = cand;
                        bestDedicated = dedicated;
                        bestName = adapter1.Description1.Description;
                        cand = null;
                    }
                }
                finally
                {
                    cand?.Dispose();
                }
            }
        }

        if (bestAdapter == null)
            return false;

        adapter3 = bestAdapter;
        name = string.IsNullOrWhiteSpace(bestName) ? "GPU" : bestName.Trim();
        return true;
    }
}
