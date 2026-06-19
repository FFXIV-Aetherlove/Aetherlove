using System;
using System.Runtime.InteropServices;

namespace AetherLove.Services;

/// <summary>Best-effort, fully-managed system facts for the debug screen. Every probe is guarded and returns
/// "Unavailable" rather than throwing, so a value the runtime (or Wine) won't surface is reported honestly.
/// No P/Invoke or registry — only cross-platform framework APIs, so it stays safe under Wine.</summary>
public static class SystemInfo
{
    public static string PluginVersion()
    {
        try
        {
            return typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "Unavailable";
        }
        catch
        {
            return "Unavailable";
        }
    }

    public static string DalamudVersion()
    {
        try
        {
            return typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly.GetName().Version?.ToString() ?? "Unavailable";
        }
        catch
        {
            return "Unavailable";
        }
    }

    public static string Runtime()
    {
        try
        {
            return RuntimeInformation.FrameworkDescription;
        }
        catch
        {
            return "Unavailable";
        }
    }

    public static string Os()
    {
        try
        {
            return $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
        }
        catch
        {
            return "Unavailable";
        }
    }

    public static string Cpu()
    {
        try
        {
            var id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            var cores = Environment.ProcessorCount;
            var arch = RuntimeInformation.ProcessArchitecture;
            return string.IsNullOrWhiteSpace(id)
                ? $"{cores} logical cores ({arch})"
                : $"{id.Trim()} - {cores} logical cores ({arch})";
        }
        catch
        {
            return "Unavailable";
        }
    }

    public static string Memory()
    {
        try
        {
            var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var procMb = Environment.WorkingSet / 1024.0 / 1024.0;
            if (total > 0)
            {
                var totalGb = total / 1024.0 / 1024.0 / 1024.0;
                return $"~{totalGb:F1} GB available to the runtime, plugin using {procMb:F0} MB";
            }
            return $"plugin using {procMb:F0} MB (total unavailable)";
        }
        catch
        {
            return "Unavailable";
        }
    }
}
