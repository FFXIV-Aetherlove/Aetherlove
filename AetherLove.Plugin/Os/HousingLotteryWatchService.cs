using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AetherOS.Apps.Realtor;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AetherLove.Os;

/// <summary>Reads the player's housing-lottery entry out of the game's Timers window.
///
/// The client exposes no struct for this: the entry only exists as rendered text in ContentsInfoDetail, so
/// this scrapes that pane whenever it opens and remembers what it saw. The consequence is that the entry is
/// only known once per cycle after the player has opened Timers at least once, which is why the app treats
/// "nothing captured" as unknown rather than as "no entry". The scrape walks the node tree looking for the
/// shape of the lines rather than fixed node indices, so a UI reshuffle degrades to no capture, not to
/// wrong data. English clients only for now; other languages simply never match.</summary>
public sealed class HousingLotteryWatchService : IHousingLotteryWatch, IDisposable
{
    private const string DetailAddon = "ContentsInfoDetail";
    private const string StorageKey = "lotteryEntry";

    private static readonly Regex PlotLine =
        new(@"Plot\s+(\d+)\s*,\s*(\d+)\D*\s+Ward\s*,\s*(.+?)\s*\((.+?)\)", RegexOptions.Compiled);

    private static readonly Regex NumberLine = new(@"Lottery Number\s*:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex TypeLine = new(@"Type of Entry\s*:\s*(.+)", RegexOptions.Compiled);

    private readonly AppStorageService _storage;
    private HousingLotteryEntry? _current;
    private bool _loaded;

    public HousingLotteryWatchService(AppStorageService storage)
    {
        _storage = storage;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, DetailAddon, OnDetail);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, DetailAddon, OnDetail);
    }

    public HousingLotteryEntry? Current
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _current = _storage.For("realtor").Get<HousingLotteryEntry>(StorageKey);
            }
            return _current;
        }
    }

    public void Clear()
    {
        _loaded = true;
        _current = null;
        _storage.For("realtor").Set<HousingLotteryEntry?>(StorageKey, null);
    }

    public void Dispose() => Plugin.AddonLifecycle.UnregisterListener(OnDetail);

    private void OnDetail(AddonEvent type, AddonArgs args)
    {
        try
        {
            var lines = ReadLines(args.Addon);
            if (Parse(lines) is not { } entry)
            {
                return;
            }
            _loaded = true;
            _current = entry;
            _storage.For("realtor").Set(StorageKey, entry);
            Plugin.Log.Debug($"[Realtor] Captured lottery entry: plot {entry.Plot}, ward {entry.Ward}, " +
                $"{entry.District}, number {entry.Number}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Realtor] Lottery capture failed.");
        }
    }

    private static HousingLotteryEntry? Parse(List<string> lines)
    {
        var plot = 0;
        var ward = 0;
        var district = string.Empty;
        var size = string.Empty;
        var number = 0;
        var entryType = string.Empty;

        foreach (var line in lines)
        {
            if (PlotLine.Match(line) is { Success: true } m)
            {
                plot = int.Parse(m.Groups[1].Value);
                ward = int.Parse(m.Groups[2].Value);
                district = m.Groups[3].Value.Trim();
                size = m.Groups[4].Value.Trim();
            }
            else if (NumberLine.Match(line) is { Success: true } n)
            {
                number = int.Parse(n.Groups[1].Value);
            }
            else if (TypeLine.Match(line) is { Success: true } t)
            {
                entryType = t.Groups[1].Value.Trim();
            }
        }

        return plot > 0 && ward > 0 && district.Length > 0
            ? new HousingLotteryEntry(plot, ward, district, size, number, entryType, DateTimeOffset.UtcNow)
            : null;
    }

    private static unsafe List<string> ReadLines(nint addonPtr)
    {
        var lines = new List<string>();
        var addon = (AtkUnitBase*)addonPtr;
        if (addon == null || addon->RootNode == null)
        {
            return lines;
        }
        Collect(addon->RootNode, lines, 0);
        return lines;
    }

    /// <summary>Walks the whole node tree, not just the top-level list: the lines sit inside component
    /// nodes. Depth-capped so a cyclic or absurdly deep tree can never hang a frame.</summary>
    private static unsafe void Collect(AtkResNode* node, List<string> lines, int depth)
    {
        for (var n = node; n != null; n = n->PrevSiblingNode)
        {
            if (n->Type == NodeType.Text)
            {
                var text = ((AtkTextNode*)n)->NodeText.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text);
                }
            }
            else if ((ushort)n->Type >= 1000 && depth < 12)
            {
                var component = ((AtkComponentNode*)n)->Component;
                if (component != null && component->UldManager.RootNode != null)
                {
                    Collect(component->UldManager.RootNode, lines, depth + 1);
                }
            }
            if (n->ChildNode != null && depth < 12)
            {
                Collect(n->ChildNode, lines, depth + 1);
            }
        }
    }
}
