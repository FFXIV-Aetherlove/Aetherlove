using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Market;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Widgets = AetherLove.Widgets;

namespace AetherOS.Apps.Market;

/// <summary>Full-index item search: prefix-ranked results with game icons and rarity-tinted names.</summary>
internal sealed class SearchScreen
{
    private const float PadX = 16f;

    private readonly MarketItemIndex _index;
    private readonly MarketUserStore _store;
    private readonly Action _back;
    private readonly Action<uint> _openItem;
    private readonly EntranceAnimation _entrance = new();
    private string _query = "";
    private bool _focus;
    private bool _stale;
    private IReadOnlyList<MarketItemIndex.Entry> _results = [];

    public SearchScreen(MarketItemIndex index, MarketUserStore store, Action back, Action<uint> openItem)
    {
        _index = index;
        _store = store;
        _back = back;
        _openItem = openItem;
    }

    public void OnShow()
    {
        _focus = true;
        _stale = true;
        _entrance.Arm();
        _index.EnsureBuildStarted();
    }

    internal static Vector4 RarityColor(byte rarity) => rarity switch
    {
        2 => new Vector4(0.55f, 0.83f, 0.51f, 1f),
        3 => new Vector4(0.45f, 0.66f, 0.95f, 1f),
        4 => new Vector4(0.72f, 0.55f, 0.92f, 1f),
        7 => new Vector4(0.92f, 0.55f, 0.75f, 1f),
        _ => UiColors.Body,
    };

    public void Draw(OsAppContext ctx)
    {
        var winW = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), FontAwesomeIcon.Bell))
        {
            _back();
            return;
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(winW - ImGui.GetCursorPosX() - Px(PadX));
        if (_focus)
        {
            ImGui.SetKeyboardFocusHere();
            _focus = false;
        }
        if (ImGui.InputTextWithHint("##marketSearchInput", Loc.T("os.market_search_hint"), ref _query, 64))
        {
            _stale = true;
        }
        ImGui.Spacing();

        if (!_index.Ready)
        {
            Widgets.LoadingIndicator.Draw(Loc.T("os.market_index_building"));
            return;
        }
        if (_stale)
        {
            _results = _index.Search(_query);
            _stale = false;
        }

        if (_query.Trim().Length == 0)
        {
            DrawRecents(winW);
            return;
        }
        if (_results.Count == 0)
        {
            DrawCenteredHint(Loc.T("os.market_no_results"), winW);
            return;
        }

        _entrance.BeginFrame();
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##marketSearchResults", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                foreach (var entry in _results)
                {
                    DrawResultRow(entry, ImGui.GetWindowSize().X);
                }
                ImGui.Dummy(new Vector2(0f, Px(10f)));
            }
        }
        PopScrollbarStyle();
        _entrance.EndFrame();
    }

    private void DrawRecents(float winW)
    {
        var recents = _store.Recents;
        var entries = new List<MarketItemIndex.Entry>(recents.Count);
        foreach (var id in recents)
        {
            if (_index.TryGet(id, out var entry))
            {
                entries.Add(entry);
            }
        }
        if (entries.Count == 0)
        {
            DrawCenteredHint(Loc.T("os.market_search_empty"), winW);
            return;
        }

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.market_recents"));
        ImGui.Spacing();
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##marketSearchRecents", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                foreach (var entry in entries)
                {
                    DrawResultRow(entry, ImGui.GetWindowSize().X);
                }
                ImGui.Dummy(new Vector2(0f, Px(10f)));
            }
        }
        PopScrollbarStyle();
    }

    private void DrawResultRow(in MarketItemIndex.Entry entry, float winW)
    {
        var rowH = Px(44f);
        ImGui.SetCursorPosX(Px(6f));
        var clicked = ImGui.InvisibleButton($"##marketResult{entry.Id}", new Vector2(winW - Px(12f), rowH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetItemRectMin();
        var br = ImGui.GetItemRectMax();
        if (hovered)
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(10f));
        }

        var iconSize = Px(30f);
        var iconTl = new Vector2(tl.X + Px(10f), tl.Y + (rowH - iconSize) * 0.5f);
        if (MarketItemIcons.Get(entry.Icon) is { } handle)
        {
            dl.AddImageRounded(handle, iconTl, iconTl + new Vector2(iconSize, iconSize),
                Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), Px(6f));
        }
        else
        {
            dl.AddRectFilled(iconTl, iconTl + new Vector2(iconSize, iconSize),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), Px(6f));
        }

        var name = TruncateToWidth(entry.Name, winW - Px(70f));
        var nameSz = ImGui.CalcTextSize(name);
        dl.AddText(new Vector2(iconTl.X + iconSize + Px(10f), tl.Y + (rowH - nameSz.Y) * 0.5f),
            ImGui.GetColorU32(RarityColor(entry.Rarity)), name);

        if (clicked)
        {
            _openItem(entry.Id);
        }
    }

    private static void DrawCenteredHint(string text, float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(30f)));
        var textW = MathF.Min(ImGui.CalcTextSize(text).X, winW - Px(PadX) * 2f);
        ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - textW) * 0.5f));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Hint, text);
        ImGui.PopTextWrapPos();
    }
}
