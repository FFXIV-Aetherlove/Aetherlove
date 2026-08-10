using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AetherLove.Services.Echo;

/// <summary>Owns the on-disk layout of the Echo playback host and resolves which build to run:
/// an explicit override first (dev builds), otherwise the newest installed version that carries its
/// completion marker. A version folder without the marker is a torn install and is never run.</summary>
public sealed class EchoHostLocator
{
    public const string HostExeName = "AetherOS.WatchHost.exe";

    /// <summary>Written last by the installer, so a half-extracted folder can never resolve.</summary>
    public const string CompleteMarkerName = ".complete";

    private const string EchoAppId = "echo";

    public EchoHostLocator()
        : this(Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "apps", EchoAppId, "host"))
    {
    }

    public EchoHostLocator(string root)
    {
        Root = root;
    }

    /// <summary>Parent of every installed version folder.</summary>
    public string Root { get; }

    /// <summary>Full path to a host executable that overrides whatever is installed; for local development.</summary>
    public string? OverrideExePath { get; set; }

    /// <summary>Newest fully installed version, or null when nothing is installed. An override does not
    /// count: the installer compares this against the manifest.</summary>
    public string? InstalledVersion => CompleteVersions().FirstOrDefault();

    /// <summary>The host executable to launch, or null when nothing runnable is present.</summary>
    public string? HostExePath
    {
        get
        {
            if (OverrideExePath is { Length: > 0 } over && File.Exists(over))
            {
                return over;
            }
            foreach (var version in CompleteVersions())
            {
                var exe = Path.Combine(VersionDir(version), HostExeName);
                if (File.Exists(exe))
                {
                    return exe;
                }
            }
            return null;
        }
    }

    public string VersionDir(string version) => Path.Combine(Root, version);

    public string MarkerPath(string version) => Path.Combine(VersionDir(version), CompleteMarkerName);

    public bool IsComplete(string version) =>
        File.Exists(MarkerPath(version)) && File.Exists(Path.Combine(VersionDir(version), HostExeName));

    /// <summary>Deletes every installed version except the one given; failures are logged and ignored so a
    /// locked leftover can never fail an otherwise good install.</summary>
    public void PruneOtherVersions(string keep)
    {
        foreach (var dir in EnumerateVersionDirs())
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, keep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, $"[Echo] Could not remove the old host build {name}.");
            }
        }
    }

    /// <summary>Complete versions, newest first; folder names that do not parse as a version sort last.</summary>
    private IEnumerable<string> CompleteVersions() =>
        EnumerateVersionDirs()
            .Select(dir => Path.GetFileName(dir) ?? string.Empty)
            .Where(name => name.Length > 0 && IsComplete(name))
            .OrderByDescending(name => Version.TryParse(name, out var parsed) ? parsed : null)
            .ThenByDescending(name => name, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<string> EnumerateVersionDirs()
    {
        try
        {
            return Directory.Exists(Root) ? Directory.GetDirectories(Root) : [];
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Echo] Could not enumerate installed host builds.");
            return [];
        }
    }
}
