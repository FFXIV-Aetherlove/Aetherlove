using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MessagePack;

namespace AetherLove.Shared.Assets;

/// <summary>A pack is either a folder of files the server zipped itself, or an opaque bundle somebody
/// uploaded whole (the Echo playback host), which the client hands to an installer instead of unpacking
/// into the asset store.</summary>
public enum AssetPackKind
{
    Files = 0,
    Bundle = 1,
}

/// <summary>One file in the collection: its root-relative path with forward slashes, the SHA-256 of its
/// bytes and its size.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AssetFileDto(string Path, string Sha256, long Size);

/// <summary>One pack. <see cref="PackHash"/> is derived from the files by <see cref="AssetManifest.PackHash"/>;
/// <see cref="ZipSha256"/> and <see cref="ZipSize"/> describe the zip the pack route serves, which for a
/// bundle is the uploaded file itself. <see cref="Version"/> is set for bundles only.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AssetPackDto(
    string Name,
    AssetPackKind Kind,
    string PackHash,
    string ZipSha256,
    long ZipSize,
    string? Version,
    AssetFileDto[] Files);

/// <summary>The whole collection as the server holds it, or as the client last landed it. Equal
/// <see cref="CollectionHash"/> values mean nothing to do.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AssetManifestDto(string CollectionHash, AssetPackDto[] Packs);

public enum AssetPlanMode
{
    Skip = 0,
    Pack = 1,
    Files = 2,
}

/// <summary>What one sync does for one pack: nothing, fetch the zip, or fetch the listed files one by one.</summary>
public sealed record AssetPackPlan(AssetPackDto Remote, AssetPlanMode Mode, AssetFileDto[] Download);

/// <summary>What one sync does for the collection. <see cref="Delete"/> holds local paths the server no
/// longer lists in any pack.</summary>
public sealed record AssetSyncPlan(AssetPackPlan[] Packs, string[] Delete)
{
    public bool IsEmpty => Delete.Length == 0 && Packs.All(p => p.Mode == AssetPlanMode.Skip);
}

/// <summary>When a changed pack is fetched file by file rather than as a zip: at most this many files
/// and at most this share of the pack. One changed emoji is one small request; a redrawn set is one zip.</summary>
public sealed record AssetSyncPolicy(int MaxFilesForSingle = 32, double MaxFractionForSingle = 0.25)
{
    public static readonly AssetSyncPolicy Default = new();
}

/// <summary>The naming and hashing rules the server and the plugin share for the asset collection. Both
/// sides derive every hash from the same sorted lines, so an up-to-date client answers with one string
/// compare, a changed pack with one more, and only then walks files.</summary>
public static class AssetManifest
{
    public const int MaxPathLength = 200;
    public const int MaxSegments = 6;
    public const int MaxPackNameLength = 40;

    /// <summary>A path is root-relative, forward-slashed, one to six segments, each segment letters, digits,
    /// underscores and hyphens with dots only between such runs. Case is kept: emoji file names carry it
    /// and are the emoji keys. Nothing here can name a parent folder or an absolute location.</summary>
    public static bool IsValidPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength)
        {
            return false;
        }
        var segments = path.Split('/');
        if (segments.Length > MaxSegments)
        {
            return false;
        }
        foreach (var segment in segments)
        {
            if (!IsValidSegment(segment))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>A pack name is a lower-case folder-derived token: letters, digits and hyphens, starting
    /// with a letter or digit.</summary>
    public static bool IsValidPackName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxPackNameLength)
        {
            return false;
        }
        var first = name[0];
        if (!((first >= 'a' && first <= 'z') || (first >= '0' && first <= '9')))
        {
            return false;
        }
        foreach (var c in name)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
            if (!ok)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsValidSegment(string segment)
    {
        if (segment.Length == 0 || segment[0] == '.' || segment[^1] == '.')
        {
            return false;
        }
        var previousDot = false;
        foreach (var c in segment)
        {
            if (c == '.')
            {
                if (previousDot)
                {
                    return false;
                }
                previousDot = true;
                continue;
            }
            previousDot = false;
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
            if (!ok)
            {
                return false;
            }
        }
        return true;
    }

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Hash(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    /// <summary>SHA-256 over the files as sorted <c>path:sha256</c> lines: order-independent and blind to
    /// size, so two folders holding the same bytes under the same names agree.</summary>
    public static string PackHash(IEnumerable<AssetFileDto> files) =>
        HashLines(files.Select(f => $"{f.Path}:{f.Sha256}"));

    /// <summary>SHA-256 over the packs as sorted <c>name:packhash</c> lines.</summary>
    public static string CollectionHash(IEnumerable<AssetPackDto> packs) =>
        HashLines(packs.Select(p => $"{p.Name}:{p.PackHash}"));

    private static string HashLines(IEnumerable<string> lines)
    {
        var text = string.Join('\n', lines.OrderBy(l => l, StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public static AssetPackDto BuildPack(
        string name, AssetPackKind kind, IEnumerable<AssetFileDto> files, string zipSha256, long zipSize, string? version = null)
    {
        var entries = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        return new AssetPackDto(name, kind, PackHash(entries), zipSha256, zipSize, version, entries);
    }

    public static AssetManifestDto Build(IEnumerable<AssetPackDto> packs)
    {
        var entries = packs.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        return new AssetManifestDto(CollectionHash(entries), entries);
    }

    /// <summary>Which files of <paramref name="remote"/> the client is missing or holds under another
    /// hash, and whether to fetch them one by one or as the pack's zip. A bundle is all or nothing.</summary>
    public static AssetPackPlan PlanPack(AssetPackDto? local, AssetPackDto remote, AssetSyncPolicy policy)
    {
        if (local is not null && string.Equals(local.PackHash, remote.PackHash, StringComparison.Ordinal))
        {
            return new AssetPackPlan(remote, AssetPlanMode.Skip, []);
        }
        if (remote.Kind == AssetPackKind.Bundle || local is null || local.Files.Length == 0)
        {
            return new AssetPackPlan(remote, AssetPlanMode.Pack, remote.Files);
        }
        var have = local.Files.ToDictionary(f => f.Path, f => f.Sha256, StringComparer.Ordinal);
        var changed = remote.Files
            .Where(f => !have.TryGetValue(f.Path, out var hash) || !string.Equals(hash, f.Sha256, StringComparison.Ordinal))
            .ToArray();
        if (changed.Length == 0)
        {
            return new AssetPackPlan(remote, AssetPlanMode.Skip, []);
        }
        var single = changed.Length <= policy.MaxFilesForSingle
            && changed.Length <= policy.MaxFractionForSingle * remote.Files.Length;
        return new AssetPackPlan(remote, single ? AssetPlanMode.Files : AssetPlanMode.Pack, changed);
    }

    /// <summary>The plan for the whole collection. A null local manifest means nothing is known and every
    /// pack is fetched whole.</summary>
    public static AssetSyncPlan PlanCollection(AssetManifestDto? local, AssetManifestDto remote, AssetSyncPolicy policy)
    {
        if (local is not null && string.Equals(local.CollectionHash, remote.CollectionHash, StringComparison.Ordinal))
        {
            return new AssetSyncPlan(remote.Packs.Select(p => new AssetPackPlan(p, AssetPlanMode.Skip, [])).ToArray(), []);
        }
        var localPacks = (local?.Packs ?? []).ToDictionary(p => p.Name, StringComparer.Ordinal);
        var packs = remote.Packs
            .Select(p => PlanPack(localPacks.TryGetValue(p.Name, out var mine) ? mine : null, p, policy))
            .ToArray();
        var wanted = remote.Packs.SelectMany(p => p.Files).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var delete = (local?.Packs ?? [])
            .Where(p => p.Kind == AssetPackKind.Files)
            .SelectMany(p => p.Files)
            .Select(f => f.Path)
            .Where(path => !wanted.Contains(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        return new AssetSyncPlan(packs, delete);
    }

    /// <summary>Groups files found on disk by the remote pack that lists their path, for a client whose
    /// local manifest is lost: what is present and correct is then skipped, and only the rest is fetched.
    /// Files no remote pack lists are left out, so the plan deletes them.</summary>
    public static AssetManifestDto FromScan(IEnumerable<AssetFileDto> present, AssetManifestDto remote)
    {
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pack in remote.Packs.Where(p => p.Kind == AssetPackKind.Files))
        {
            foreach (var file in pack.Files)
            {
                owner[file.Path] = pack.Name;
            }
        }
        var grouped = present
            .Where(f => owner.ContainsKey(f.Path))
            .GroupBy(f => owner[f.Path], StringComparer.Ordinal);
        var packs = new List<AssetPackDto>();
        foreach (var group in grouped)
        {
            var remotePack = remote.Packs.First(p => p.Name == group.Key);
            packs.Add(BuildPack(group.Key, AssetPackKind.Files, group, remotePack.ZipSha256, remotePack.ZipSize));
        }
        return Build(packs);
    }
}
