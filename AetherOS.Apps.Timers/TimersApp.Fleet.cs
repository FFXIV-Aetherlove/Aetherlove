using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Timers;

/// <summary>The Retainers and Fleet cards: one collapsible group per character, rows sorted ready-first,
/// views rebuilt at most once per second and on <see cref="ITimersRetainers.Version"/> bumps.</summary>
public sealed partial class TimersApp
{
    private const float GroupHeaderHeight = 44f;
    private const string CollapsedGroupsKey = "collapsedGroups";

    private sealed class GroupView
    {
        public string Key = "";
        public string StorageKey = "";
        public string Title = "";
        public string Sub = "";
        public string Summary = "";
        public bool HasReady;
        public bool IsCurrent;
        public TimerRow[] Rows = [];
    }

    private GroupView[] _retainerGroups = [];
    private GroupView[] _fleetGroups = [];
    private HashSet<string> _collapsed = new();
    private bool _collapsedLoaded;
    private bool _collapsedSeeded;

    private string? _readyWorkName;
    private string? _soonestWorkName;
    private DateTime _soonestWorkUtc = DateTime.MaxValue;

    private void EnsureCollapsedLoaded()
    {
        if (_collapsedLoaded)
        {
            return;
        }
        _collapsedLoaded = true;
        if (_storage.Get<List<string>>(CollapsedGroupsKey) is { } stored)
        {
            _collapsed = new HashSet<string>(stored);
            _collapsedSeeded = true;
        }
    }

    private void BuildGroupViews(DateTime utcNow)
    {
        EnsureCollapsedLoaded();
        _readyWorkName = null;
        _soonestWorkName = null;
        _soonestWorkUtc = DateTime.MaxValue;

        var characters = _retainers.Characters;
        var retainerGroups = new List<GroupView>(characters.Count);
        var fleetGroups = new List<GroupView>();
        for (var i = 0; i < characters.Count; i++)
        {
            var character = characters[i];
            if (character.Retainers.Count > 0)
            {
                retainerGroups.Add(BuildRetainerGroup(character, i == 0, utcNow));
            }
            if (character.Fleet.Count > 0)
            {
                fleetGroups.Add(BuildFleetGroup(character, i == 0, utcNow));
            }
        }
        SeedCollapsedDefaults(retainerGroups, fleetGroups);
        _retainerGroups = retainerGroups.ToArray();
        _fleetGroups = fleetGroups.ToArray();
    }

    /// <summary>First run only: every non-current character starts collapsed, the current one open. From
    /// then on the persisted list is the whole truth.</summary>
    private void SeedCollapsedDefaults(List<GroupView> retainerGroups, List<GroupView> fleetGroups)
    {
        if (_collapsedSeeded || (retainerGroups.Count == 0 && fleetGroups.Count == 0))
        {
            return;
        }
        _collapsedSeeded = true;
        foreach (var group in retainerGroups.Concat(fleetGroups))
        {
            if (!group.IsCurrent)
            {
                _collapsed.Add(group.StorageKey);
            }
        }
        PersistCollapsed();
    }

    private void PersistCollapsed()
    {
        _storage.Set(CollapsedGroupsKey, _collapsed.ToList());
    }

    /// <summary>Collapsing is the player's call and nothing overrides it. Forcing a group open because
    /// something in it is ready made the header un-collapsible for anyone with enough retainers that one is
    /// always ready, and it buys nothing: the header still says how many are waiting while collapsed.</summary>
    private bool IsExpanded(GroupView group) => !_collapsed.Contains(group.StorageKey);

    private void ToggleGroup(GroupView group)
    {
        if (!_collapsed.Remove(group.StorageKey))
        {
            _collapsed.Add(group.StorageKey);
        }
        PersistCollapsed();
    }

    private GroupView BuildRetainerGroup(TimersCharacter character, bool isCurrent, DateTime utcNow)
    {
        var sorted = character.Retainers
            .OrderBy(r => RetainerSortClass(r, utcNow))
            .ThenBy(r => r.CompleteUtc)
            .ToArray();
        var rows = new TimerRow[sorted.Length];
        var readyCount = 0;
        var nextUtc = DateTime.MaxValue;
        for (var i = 0; i < sorted.Length; i++)
        {
            var retainer = sorted[i];
            var cls = RetainerSortClass(retainer, utcNow);
            if (cls == 0)
            {
                readyCount++;
                _readyWorkName ??= retainer.Name;
                rows[i] = new TimerRow(FontAwesomeIcon.None, Vector4.Zero, retainer.Name, retainer.VentureName,
                    Loc.T("os.timers_ready"), UiColors.Success, "", "", 0L);
            }
            else if (cls == 1)
            {
                if (retainer.CompleteUtc < nextUtc)
                {
                    nextUtc = retainer.CompleteUtc;
                }
                if (retainer.CompleteUtc < _soonestWorkUtc)
                {
                    _soonestWorkUtc = retainer.CompleteUtc;
                    _soonestWorkName = retainer.Name;
                }
                rows[i] = new TimerRow(FontAwesomeIcon.None, Vector4.Zero, retainer.Name, retainer.VentureName,
                    FormatCountdown(retainer.CompleteUtc - utcNow), UiColors.Body,
                    $"##calrt{retainer.RetainerId}", retainer.Name, ToUnix(retainer.CompleteUtc));
            }
            else
            {
                rows[i] = new TimerRow(FontAwesomeIcon.None, Vector4.Zero, retainer.Name,
                    Loc.T("os.timers_no_venture"), "", UiColors.Muted, "", "", 0L);
            }
        }
        return new GroupView
        {
            Key = $"##grpR{character.ContentId}",
            StorageKey = $"r:{character.ContentId}",
            Title = character.Name,
            Sub = character.World,
            Summary = GroupSummary(readyCount, nextUtc, utcNow),
            HasReady = readyCount > 0,
            IsCurrent = isCurrent,
            Rows = rows,
        };
    }

    private GroupView BuildFleetGroup(TimersCharacter character, bool isCurrent, DateTime utcNow)
    {
        var sorted = character.Fleet
            .OrderBy(v => VesselSortClass(v, utcNow))
            .ThenBy(v => v.ReturnUtc)
            .ToArray();
        var rows = new TimerRow[sorted.Length];
        var readyCount = 0;
        var nextUtc = DateTime.MaxValue;
        for (var i = 0; i < sorted.Length; i++)
        {
            var vessel = sorted[i];
            var icon = vessel.Kind == VesselKind.Airship ? FontAwesomeIcon.Plane : FontAwesomeIcon.Water;
            var kindLabel = Loc.T(vessel.Kind == VesselKind.Airship ? "os.timers_airship" : "os.timers_submersible");
            var cls = VesselSortClass(vessel, utcNow);
            if (cls == 0)
            {
                readyCount++;
                _readyWorkName ??= vessel.Name;
                rows[i] = new TimerRow(icon, new Vector4(1f, 1f, 1f, 0.9f), vessel.Name, kindLabel,
                    Loc.T("os.timers_ready"), UiColors.Success, "", "", 0L);
            }
            else if (cls == 1)
            {
                if (vessel.ReturnUtc < nextUtc)
                {
                    nextUtc = vessel.ReturnUtc;
                }
                if (vessel.ReturnUtc < _soonestWorkUtc)
                {
                    _soonestWorkUtc = vessel.ReturnUtc;
                    _soonestWorkName = vessel.Name;
                }
                rows[i] = new TimerRow(icon, new Vector4(1f, 1f, 1f, 0.9f), vessel.Name, kindLabel,
                    FormatCountdown(vessel.ReturnUtc - utcNow), UiColors.Body,
                    $"##calfv{character.ContentId}x{i}", vessel.Name, ToUnix(vessel.ReturnUtc));
            }
            else
            {
                rows[i] = new TimerRow(icon, new Vector4(1f, 1f, 1f, 0.9f), vessel.Name, kindLabel,
                    Loc.T("os.timers_docked"), UiColors.Muted, "", "", 0L);
            }
        }
        return new GroupView
        {
            Key = $"##grpF{character.ContentId}",
            StorageKey = $"f:{character.ContentId}",
            Title = character.Name,
            Sub = character.FreeCompany.Length > 0
                ? $"{character.FreeCompany} · {character.World}"
                : character.World,
            Summary = GroupSummary(readyCount, nextUtc, utcNow),
            HasReady = readyCount > 0,
            IsCurrent = isCurrent,
            Rows = rows,
        };
    }

    private static int RetainerSortClass(RetainerRow retainer, DateTime utcNow)
    {
        if (retainer.VentureId == 0)
        {
            return 2;
        }
        return retainer.CompleteUtc <= utcNow ? 0 : 1;
    }

    private static int VesselSortClass(FleetVessel vessel, DateTime utcNow)
    {
        if (vessel.ReturnUtc == DateTime.MinValue)
        {
            return 2;
        }
        return vessel.ReturnUtc <= utcNow ? 0 : 1;
    }

    private static string GroupSummary(int readyCount, DateTime nextUtc, DateTime utcNow)
    {
        var hasNext = nextUtc != DateTime.MaxValue;
        if (readyCount > 0 && hasNext)
        {
            return Loc.T("os.timers_group_ready_next", readyCount, FormatCountdown(nextUtc - utcNow));
        }
        if (readyCount > 0)
        {
            return Loc.T("os.timers_group_ready", readyCount);
        }
        if (hasNext)
        {
            return Loc.T("os.timers_group_next", FormatCountdown(nextUtc - utcNow));
        }
        return Loc.T("os.timers_group_idle");
    }

    private void DrawRetainersCard(OsAppContext ctx)
    {
        DrawGroupCard(ctx, Loc.T("os.timers_retainers_title"), _retainerGroups, "os.timers_retainers_empty");
    }

    private void DrawFleetCard(OsAppContext ctx)
    {
        if (_fleetGroups.Length == 0)
        {
            return;
        }
        DrawGroupCard(ctx, Loc.T("os.timers_fleet_title"), _fleetGroups, null);
    }

    private void DrawGroupCard(OsAppContext ctx, string title, GroupView[] groups, string? emptyKey)
    {
        if (groups.Length == 0 && emptyKey is null)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var winW = ImGui.GetWindowSize().X;
        var headerH = Px(GroupHeaderHeight);
        var rowH = Px(RowHeight);
        var lineH = ImGui.GetTextLineHeight();

        // The empty line is a sentence, not a label, and the card is phone-narrow: measure it WRAPPED at the
        // width it will be drawn at, or the reserved height is one line and the text runs off the card.
        var emptyPad = Px(14f);
        var emptyTextW = winW - (Px(PadX) * 2f) - (emptyPad * 2f);
        float bodyH;
        if (groups.Length == 0)
        {
            bodyH = ImGui.CalcTextSize(Loc.T(emptyKey!), false, emptyTextW).Y + Px(10f);
        }
        else
        {
            bodyH = 0f;
            foreach (var group in groups)
            {
                bodyH += headerH;
                if (IsExpanded(group))
                {
                    bodyH += group.Rows.Length * rowH;
                }
            }
        }

        var cardTL = BeginCard(dl, winW, bodyH, title, out var cardW, out var cardH, out var y);

        if (groups.Length == 0)
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(cardTL.X + emptyPad, y + Px(2f)),
                ImGui.GetColorU32(UiColors.Hint), Loc.T(emptyKey!), emptyTextW);
            EndCard(cardTL, cardW, cardH);
            return;
        }

        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (i > 0)
            {
                DrawHairline(dl, cardTL.X, y, cardW);
            }
            var expanded = IsExpanded(group);
            if (DrawGroupHeader(dl, new Vector2(cardTL.X, y), cardW, headerH, group, expanded))
            {
                ToggleGroup(group);
            }
            y += headerH;
            if (expanded)
            {
                foreach (ref readonly var row in group.Rows.AsSpan())
                {
                    DrawTimerRow(dl, new Vector2(cardTL.X, y), cardW, rowH, in row);
                    y += rowH;
                }
            }
        }

        EndCard(cardTL, cardW, cardH);
    }

    private bool DrawGroupHeader(ImDrawListPtr dl, Vector2 tl, float w, float h, GroupView group, bool expanded)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(group.Key, new Vector2(w, h));
        HandOnHover();
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(tl, tl + new Vector2(w, h), OsDrawShared.White(0.03f));
        }

        var chevC = new Vector2(tl.X + w - Px(20f), tl.Y + h * 0.5f);
        IconDraw.AddCentered(dl, expanded ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown,
            Px(10f), chevC, OsDrawShared.White(0.55f));

        var summarySz = ImGui.CalcTextSize(group.Summary);
        var summaryX = chevC.X - Px(14f) - summarySz.X;
        dl.AddText(new Vector2(summaryX, tl.Y + (h - summarySz.Y) * 0.5f),
            ImGui.GetColorU32(group.HasReady ? UiColors.Success : UiColors.Hint), group.Summary);

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var textX = tl.X + Px(14f);
        dl.PushClipRect(new Vector2(textX, tl.Y), new Vector2(summaryX - Px(8f), tl.Y + h), true);
        dl.AddText(new Vector2(textX, tl.Y + Px(6f)), ImGui.GetColorU32(UiColors.Body), group.Title);
        dl.AddText(font, fontSize * 0.85f, new Vector2(textX, tl.Y + Px(24f)),
            ImGui.GetColorU32(UiColors.Muted), group.Sub);
        dl.PopClipRect();
        return clicked;
    }
}
