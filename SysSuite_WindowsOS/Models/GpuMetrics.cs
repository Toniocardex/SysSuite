namespace SysSuite.Models;

/// <summary>
/// VRAM da DXGI <c>QueryVideoMemoryInfo</c> + (se disponibile) picco carico motori da contatori Windows <c>GPU Engine</c>
/// (3D, Compute, copy, decode, …) per la stessa scheda (filtro LUID DXGI).
/// </summary>
public readonly record struct GpuMetrics(
    string Name,
    long UsedVramBytes,
    long TotalVramBytes,
    /// <summary>Percentuale allocazione VRAM rispetto al budget DXGI (non equivale al carico compute).</summary>
    double VramUsagePercent,
    /// <summary>Massimo utilizzo fra i motori riportati dai contatori; null se non disponibile.</summary>
    double? EngineUtilizationPercent);
