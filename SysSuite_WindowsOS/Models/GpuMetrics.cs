namespace SysSuite.Models;

/// <summary>Snapshot VRAM e nome scheda ( DXGI <c>QueryVideoMemoryInfo</c> + descrizione adapter ).</summary>
public readonly record struct GpuMetrics(
    string Name,
    long UsedVramBytes,
    long TotalVramBytes,
    double UsagePercentage);
