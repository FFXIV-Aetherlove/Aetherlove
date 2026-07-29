using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Realtor;
using AetherLove.UI;
using AetherOS.Sdk;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>Full-screen world picker: the PaissaDB world list grouped by data center, with search.</summary>
internal sealed class WorldPickScreen
{
    private const float PadX = 16f;

    private readonly RealtorDataService _data;
    private readonly Action _back;
    private readonly Action<string> _pick;
    private readonly EntranceAnimation _entrance = new();

    private volatile List<PaissaWorldSummary>? _worlds;
    private volatile bool _loading;
    private string _query = "";
    private string _current = "";

    public WorldPickScreen(RealtorDataService data, Action back, Action<string> pick)
    {
        _data = data;
        _back = back;
        _pick = pick;
    }

    public void OnShow(string currentWorld)
    {
        _entrance.Arm();
        _current = currentWorld;
        _query = "";
        StartFetch();
    }

    private void StartFetch()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _worlds = await _data.GetWorldsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Realtor] World list fetch failed: {ex.Message}");
            }
            finally
            {
                _loading = false;
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        if (RealtorHeader.Draw(Loc.T("os.realtor_pick_world")))
        {
            _back();
            _entrance.EndFrame();
            return;
        }

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        ImGui.InputTextWithHint("##realtorWorldSearch", Loc.T("os.realtor_search_world"), ref _query, 32);
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        var worlds = _worlds;
        if (worlds is null)
        {
            if (_loading)
            {
                ImGui.Dummy(new Vector2(0f, Px(30f)));
                var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(20f));
                LoadingSpinner.Draw(center, Px(14f), Px(3f), ImGui.GetColorU32(t.Accent));
                ImGui.Dummy(new Vector2(0f, Px(50f)));
            }
            else
            {
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushTextWrapPos(winW - Px(PadX));
                ImGui.TextColored(UiColors.Hint, Loc.T("os.realtor_offline"));
                ImGui.PopTextWrapPos();
            }
            _entrance.EndFrame();
            return;
        }

        var query = _query.Trim();
        var filtered = query.Length == 0
            ? worlds
            : worlds.Where(w => w.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var group in filtered.GroupBy(w => w.DatacenterName).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(UiColors.Hint, group.Key);
            foreach (var world in group.OrderBy(w => w.Name, StringComparer.Ordinal))
            {
                DrawWorldRow(world, winW);
            }
            ImGui.Dummy(new Vector2(0f, Px(8f)));
        }
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        _entrance.EndFrame();
    }

    private void DrawWorldRow(PaissaWorldSummary world, float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(36f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##realtorWorld{world.Id}", new Vector2(cardW, cardH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var isCurrent = string.Equals(world.Name, _current, StringComparison.OrdinalIgnoreCase);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(hovered ? t.Accent with { W = 0.18f } : new Vector4(1f, 1f, 1f, 0.04f)), Px(10f));
        dl.AddText(new Vector2(tl.X + Px(12f), tl.Y + (cardH - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(isCurrent ? t.AccentLight : UiColors.Body), world.Name);
        if (isCurrent)
        {
            var iconPx = ImGui.GetFontSize() * 0.8f;
            IconDraw.Add(dl, FontAwesomeIcon.Check, iconPx,
                new Vector2(tl.X + cardW - Px(24f), tl.Y + (cardH - iconPx) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        if (clicked)
        {
            _pick(world.Name);
        }
    }
}
