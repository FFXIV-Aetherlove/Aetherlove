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
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Os;

/// <summary>The gate an app passes through before it is allowed onto somebody's home screen. An update that
/// ships new apps used to scatter them across the grid and leave the player to clear up after it; now they
/// are offered, one row each with a switch, and only the accepted ones are placed.
///
/// <para>This is the general rule, not a per-release screen: anything the registry knows about that the
/// player's layout has never seen is a candidate, so a new app needs no code here at all. A fresh install
/// never sees it, because the home screen seeds every app before there is anything left over to offer.</para>
/// </summary>
public sealed class NewAppOffer(OsShell shell)
{
    private readonly List<string> _pending = [];
    private readonly HashSet<string> _declined = new(StringComparer.Ordinal);
    private float _in;

    /// <summary>Whether the offer owns the screen. Held off while the boot intro, a transition or the guided
    /// tour is running, so it never lands on top of one of those.</summary>
    public bool Active => _pending.Count > 0;

    /// <summary>Ids being offered right now, which the home screen must not place until the player has said
    /// yes to them.</summary>
    public bool IsPending(string appId) => _pending.Contains(appId);

    /// <summary>Recomputed at the top of the home layout, from the saved layout rather than from the
    /// frame's own lists: an app this config has never placed anywhere has never been offered. It runs
    /// BEFORE the folder seeds, or Notes, Calculator and Timers would be swallowed into a seeded Utilities
    /// folder and counted as placed without anybody having been asked.</summary>
    public void Refresh()
    {
        var os = UiHost.Configuration.Os;
        foreach (var app in shell.Apps)
        {
            var id = app.Id;
            if (_pending.Contains(id) || os.OfferedApps.Contains(id) || os.RemovedApps.Contains(id))
            {
                continue;
            }
            if (!app.Available || id.StartsWith(ExternalApp.IdPrefix, StringComparison.Ordinal))
            {
                continue;
            }
            if (OsFolders.IsPlaced(os, id))
            {
                continue;
            }
            _pending.Add(id);
        }
    }

    /// <summary>Everything the registry currently holds counts as already offered. The home screen calls this
    /// when it seeds a brand-new phone, so a first launch is welcomed by the tour rather than by a list of
    /// switches for apps it has just placed anyway.</summary>
    public void MarkAllOffered()
    {
        var offered = UiHost.Configuration.Os.OfferedApps;
        foreach (var app in shell.Apps)
        {
            if (!offered.Contains(app.Id))
            {
                offered.Add(app.Id);
            }
        }
        _pending.Clear();
    }

    public void Draw(Vector2 contentTL, Vector2 contentBR)
    {
        if (!Active)
        {
            return;
        }

        var size = contentBR - contentTL;
        ImGui.SetCursorScreenPos(contentTL);
        using var layer = ImRaii.Child("##newAppOffer", size, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        _in = AccessibilityService.ReduceMotion
            ? 1f
            : MathF.Min(1f, _in + (ImGui.GetIO().DeltaTime * 3.2f));
        var ease = 1f - MathF.Pow(1f - _in, 3f);

        // Solid, not a scrim: the grid underneath is exactly what the reader must not be comparing this
        // list against, and at 90% the tiles still read straight through the text.
        dl.AddRectFilled(contentTL, contentBR, OsDraw.Black(ease));
        OsDraw.RoundedGradient(dl, contentTL, contentBR, 0f,
            t.SecondaryStart with { W = 0.40f }, t.SecondaryEnd with { W = 0.14f }, ease);

        var pad = Px(18f);
        var innerW = size.X - (pad * 2f);
        var top = contentTL.Y + Px(26f) + ((1f - ease) * Px(14f));

        var glyphC = new Vector2(contentTL.X + (size.X * 0.5f), top + Px(14f));
        dl.AddCircleFilled(glyphC, Px(20f), ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.18f * ease }), 32);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Gift, Px(19f), glyphC,
            ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.95f * ease }));

        var title = Loc.T("os.new_apps_title");
        using (UiFonts.H2?.Push())
        {
            var titleSz = ImGui.CalcTextSize(title);
            dl.AddText(new Vector2(contentTL.X + ((size.X - titleSz.X) * 0.5f), top + Px(42f)),
                OsDraw.White(0.98f * ease), title);
        }

        var body = Loc.T("os.new_apps_body");
        var bodyTL = new Vector2(contentTL.X + pad, top + Px(78f));
        var bodyH = ImGui.CalcTextSize(body, false, innerW).Y;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.88f, bodyTL,
            OsDraw.White(0.62f * ease), body, innerW);

        var buttonH = Px(42f);
        var listTop = bodyTL.Y + bodyH + Px(14f);
        var listH = contentBR.Y - listTop - buttonH - Px(26f);

        ImGui.SetCursorScreenPos(new Vector2(contentTL.X + pad, listTop));
        using (ImRaii.Child("##newAppOfferList", new Vector2(innerW, listH), false,
            ImGuiWindowFlags.NoBackground))
        {
            foreach (var id in _pending.ToList())
            {
                DrawRow(id, innerW - Px(4f), ease);
            }
        }

        var buttonTL = new Vector2(contentTL.X + pad, contentBR.Y - buttonH - Px(16f));
        ImGui.SetCursorScreenPos(buttonTL);
        if (DrawContinue(dl, buttonTL, new Vector2(innerW, buttonH), ease))
        {
            Commit();
        }
    }

    /// <summary>One app: its tile, its name, what it is for, and the switch that decides whether it lands.</summary>
    private void DrawRow(string appId, float width, float ease)
    {
        if (shell.Find(appId) is not { } app)
        {
            _pending.Remove(appId);
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var tile = Px(44f);
        var accepted = !_declined.Contains(appId);
        var about = Description(appId);
        // Measured from the tile's right EDGE rather than from the row's, or the tile's own inset eats
        // the gap and the name sits against the icon.
        var tileInset = Px(10f);
        var textX = tl.X + tileInset + tile + Px(14f);
        // The switch sits in its own column on the right; the text stops short of it, or a two-line
        // description runs straight under the knob.
        var switchCol = Px(72f);
        var textW = MathF.Max(Px(40f), width - (textX - tl.X) - switchCol);
        var aboutH = about.Length == 0 ? 0f : ImGui.CalcTextSize(about, false, textW).Y;
        var rowH = MathF.Max(tile + Px(18f), Px(30f) + aboutH + Px(14f));
        var br = new Vector2(tl.X + width, tl.Y + rowH);

        ImGui.InvisibleButton($"##offerRow{appId}", new Vector2(width, rowH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        if (ImGui.IsItemClicked())
        {
            accepted = !accepted;
            Set(appId, accepted);
        }

        dl.AddRectFilled(tl, br, OsDraw.White((hovered ? 0.10f : 0.06f) * ease), Px(14f));
        OsDraw.AppTile(dl, app, new Vector2(tl.X + tileInset, tl.Y + ((rowH - tile) * 0.5f)),
            new Vector2(tl.X + tileInset + tile, tl.Y + ((rowH - tile) * 0.5f) + tile),
            accepted ? ease : 0.35f * ease);

        var nameY = about.Length == 0 ? tl.Y + ((rowH - ImGui.GetTextLineHeight()) * 0.5f) : tl.Y + Px(11f);
        dl.AddText(new Vector2(textX, nameY), OsDraw.White((accepted ? 0.97f : 0.55f) * ease), app.Name);
        if (about.Length > 0)
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.84f,
                new Vector2(textX, nameY + Px(19f)), OsDraw.White((accepted ? 0.60f : 0.35f) * ease),
                about, textW);
        }

        DrawSwitch(dl, new Vector2(br.X - Px(34f), tl.Y + (rowH * 0.5f)), accepted, ease);
        ImGui.Dummy(new Vector2(0f, Px(8f)));
    }

    /// <summary>The row's own switch. Hand-drawn rather than the shared toggle because the whole row is the
    /// hit target here, and the shared one owns its own.</summary>
    private static void DrawSwitch(ImDrawListPtr dl, Vector2 centre, bool on, float ease)
    {
        var t = ThemeService.Current;
        var w = Px(42f);
        var h = Px(23f);
        var tl = centre - new Vector2(w * 0.5f, h * 0.5f);
        var br = tl + new Vector2(w, h);
        dl.AddRectFilled(tl, br,
            on ? ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.85f * ease }) : OsDraw.White(0.16f * ease),
            h * 0.5f);
        var knob = new Vector2(on ? br.X - (h * 0.5f) : tl.X + (h * 0.5f), centre.Y);
        dl.AddCircleFilled(knob, (h * 0.5f) - Px(2.5f), OsDraw.White(0.96f * ease), 24);
    }

    private bool DrawContinue(ImDrawListPtr dl, Vector2 tl, Vector2 size, float ease)
    {
        var t = ThemeService.Current;
        var pressed = ImGui.InvisibleButton("##newAppOfferGo", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        var br = tl + size;
        dl.AddRectFilled(tl, br,
            ImGui.ColorConvertFloat4ToU32(t.Accent with { W = (hovered ? 0.95f : 0.82f) * ease }),
            size.Y * 0.5f);
        var label = Loc.T("os.new_apps_continue");
        var sz = ImGui.CalcTextSize(label);
        dl.AddText(tl + ((size - sz) * 0.5f), OsDraw.White(0.98f * ease), label);
        return pressed;
    }

    private void Set(string appId, bool accepted)
    {
        if (accepted)
        {
            _declined.Remove(appId);
        }
        else
        {
            _declined.Add(appId);
        }
    }

    /// <summary>Records the answer for every offered app: accepted ones are simply released, so the home
    /// screen places them in the next free cell with their "new" pill on, and declined ones are marked
    /// removed, which is the same state as an app taken off the grid by hand and reachable again from the
    /// home screen's add-apps sheet.</summary>
    private void Commit()
    {
        var os = UiHost.Configuration.Os;
        foreach (var id in _pending)
        {
            if (!os.OfferedApps.Contains(id))
            {
                os.OfferedApps.Add(id);
            }
            if (_declined.Contains(id) && !os.RemovedApps.Contains(id))
            {
                os.RemovedApps.Add(id);
            }
        }
        UiHost.Configuration.Save();
        _pending.Clear();
        _declined.Clear();
        _in = 0f;
    }

    /// <summary>An app's one line, from the host tables beside its name key. A missing key resolves to
    /// itself, and the row simply drops the line rather than printing it.</summary>
    private static string Description(string appId)
    {
        var key = $"os.app_{appId}_about";
        var text = Loc.T(key);
        return text == key ? string.Empty : text;
    }
}
