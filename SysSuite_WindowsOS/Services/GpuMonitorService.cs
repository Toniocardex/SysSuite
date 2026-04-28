// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Reflection;
using SysSuite.Models;
using Vortice.DXGI;

namespace SysSuite.Services;

/// <summary>
/// VRAM DXGI 1.4 (<c>QueryVideoMemoryInfo</c>) +
/// carico motori da <c>\GPU Engine\Utilization Percentage</c> (3D, Compute, copy, decode, …) filtrato sul LUID della scheda DXGI scelta.
/// </summary>
public sealed class GpuMonitorService : IDisposable
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);

    private IDXGIFactory4? _factory;
    private IDXGIAdapter3? _adapter3;
    private string _cachedName;
    private bool _disposed;

    /// <summary>Substring nei nomi istanza contatori (es. <c>luid_0x........_0x........</c>).</summary>
    private string? _adapterLuidFilter;

    private string? _gpuEngineCategoryName;
    private readonly List<PerformanceCounter> _engineUtilCounters = [];

    private DateTimeOffset _nextEngineCounterRebuild = DateTimeOffset.MinValue;

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
            if (!TrySelectAdapter(factory, out var adapter3, out var chosenName, out var luidFilter))
                return;

            _factory = factory;
            _adapter3 = adapter3;
            _cachedName = chosenName;
            _adapterLuidFilter = luidFilter;
            factory = null;

            TryInitializeGpuEngineCounters();
        }
        finally
        {
            factory?.Dispose();
        }
    }

    public GpuMetrics GetGpuMetrics()
    {
        if (_adapter3 == null)
            return new GpuMetrics(_cachedName, 0, 0, 0, null);

        MaybeRefreshEngineCounters();

        var info = _adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
        var used = (long)info.CurrentUsage;
        var total = (long)info.Budget;
        var vramPct = total > 0 ? 100.0 * used / total : 0;
        if (vramPct < 0)
            vramPct = 0;
        if (vramPct > 100)
            vramPct = 100;

        double? enginePct = TrySampleEngineUtilizationMax();

        return new GpuMetrics(_cachedName, used, total, vramPct, enginePct);
    }

    private void MaybeRefreshEngineCounters()
    {
        if (string.IsNullOrEmpty(_adapterLuidFilter))
            return;
        if (DateTimeOffset.UtcNow < _nextEngineCounterRebuild)
            return;
        _nextEngineCounterRebuild = DateTimeOffset.UtcNow.AddMinutes(2);

        if (_engineUtilCounters.Count > 0)
            return;

        TryInitializeGpuEngineCounters();
    }

    private double? TrySampleEngineUtilizationMax()
    {
        if (_engineUtilCounters.Count == 0)
            return null;

        double max = 0;
        foreach (var c in _engineUtilCounters)
        {
            try
            {
                max = Math.Max(max, c.NextValue());
            }
            catch
            {
                // istanza obsoleta
            }
        }

        if (max < 0)
            return 0;
        if (max > 100)
            return 100;
        return max;
    }

    private void TryInitializeGpuEngineCounters()
    {
        if (string.IsNullOrEmpty(_adapterLuidFilter))
            return;

        foreach (var c in _engineUtilCounters)
        {
            try { c.Dispose(); } catch { /* ignore */ }
        }
        _engineUtilCounters.Clear();

        _gpuEngineCategoryName ??= TryResolveGpuEngineCategoryName();
        if (_gpuEngineCategoryName == null)
            return;

        try
        {
            var category = new PerformanceCounterCategory(_gpuEngineCategoryName);
            foreach (var inst in category.GetInstanceNames())
            {
                if (inst.IndexOf(_adapterLuidFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                foreach (var c in category.GetCounters(inst))
                {
                    if (!c.CounterName.Contains("Utilization", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (c.CounterName.Contains("Running", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        _ = c.NextValue();
                        _engineUtilCounters.Add(c);
                    }
                    catch
                    {
                        c.Dispose();
                    }
                }
            }
        }
        catch
        {
            // Categoria assente o permessi
        }
    }

    /// <summary>
    /// Nome categoria: in genere <c>GPU Engine</c> (inglese). Evita <see cref="PerformanceCounterCategory.GetCategories"/> (lento).
    /// </summary>
    private static string? TryResolveGpuEngineCategoryName()
    {
        foreach (var name in new[]
                 {
                     "GPU Engine",
                     "Motore GPU",
                     "Motore Gpu",
                     "GPU-Engine",
                     "Moteur GPU",
                 })
        {
            try
            {
                var cat = new PerformanceCounterCategory(name);
                var inst = cat.GetInstanceNames();
                if (inst.Length > 0)
                    return cat.CategoryName;
            }
            catch
            {
                /* try next */
            }
        }

        return null;
    }

    private static bool TryGetAdapterLuidFilterFromDescription1(IDXGIAdapter1 adapter, out string filter)
    {
        filter = "";
        object desc;
        try
        {
            desc = adapter.Description1;
        }
        catch
        {
            return false;
        }

        Type dt = desc.GetType();
        object? luidObj = GetPropOrFieldValue(desc, dt, "AdapterLuid") ?? GetPropOrFieldValue(desc, dt, "Luid");
        if (luidObj == null)
            return false;

        Type lt = luidObj.GetType();
        object? lowObj = GetPropOrFieldValue(luidObj, lt, "LowPart") ?? GetPropOrFieldValue(luidObj, lt, "Low");
        object? highObj = GetPropOrFieldValue(luidObj, lt, "HighPart") ?? GetPropOrFieldValue(luidObj, lt, "High");
        if (!TryCoerceUInt(lowObj, out uint low))
            return false;
        if (!TryCoerceInt(highObj, out int highSigned))
            return false;

        filter = $"luid_0x{low:X8}_0x{(uint)highSigned:X8}";
        return filter.Length >= 22;
    }

    private static object? GetPropOrFieldValue(object target, Type t, string name)
    {
        PropertyInfo? p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (p != null)
            return p.GetValue(target);
        FieldInfo? f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public);
        return f?.GetValue(target);
    }

    private static bool TryCoerceUInt(object? o, out uint v)
    {
        v = 0;
        if (o is uint u)
        {
            v = u;
            return true;
        }
        if (o is int i && i >= 0)
        {
            v = (uint)i;
            return true;
        }
        return false;
    }

    private static bool TryCoerceInt(object? o, out int v)
    {
        v = 0;
        if (o is int i)
        {
            v = i;
            return true;
        }
        if (o is uint u)
        {
            v = unchecked((int)u);
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var c in _engineUtilCounters)
        {
            try { c.Dispose(); } catch { /* ignore */ }
        }
        _engineUtilCounters.Clear();

        _adapter3?.Dispose();
        _adapter3 = null;
        _factory?.Dispose();
        _factory = null;
    }

    private static bool TrySelectAdapter(
        IDXGIFactory4 factory,
        out IDXGIAdapter3? adapter3,
        out string name,
        out string? luidFilter)
    {
        adapter3 = null;
        name = "GPU non disponibile";
        luidFilter = null;

        nuint bestDedicated = 0;
        IDXGIAdapter3? bestAdapter = null;
        string bestName = name;
        string? bestLuid = null;

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
                        bestLuid = TryGetAdapterLuidFilterFromDescription1(adapter1, out var luidTag) ? luidTag : null;
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
        luidFilter = bestLuid;
        return true;
    }
}
