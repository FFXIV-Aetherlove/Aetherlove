using System;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Market;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Market;

/// <summary>Every price alert in one list: condition, last seen price, an enable toggle, and
/// right-click to delete. Rows open the item page.</summary>
internal sealed class AlertsScreen
{
    private const float PadX = 16f;

    private readonly MarketAlertStore _store;
    private readonly MarketItemIndex _index;
    private readonly Action _back;
    private readonly Action<uint> _openItem;
    private readonly EntranceAnimation _entrance = new();

    public AlertsScreen(MarketAlertStore store, MarketItemIndex index, Action back, Action<uint> openItem)
    {
        _store = store;
        _index = index;
        _back = back;
        _openItem = openItem;
    }

    public void OnShow()
    {
        _entrance.Arm();
        _index.EnsureBuildStarted();
    }

    public void Draw(OsAppContext ctx)
    {
        var winW = ImGui.GetContentRegionAvail().X;

        if (MarketHeader.Draw(Loc.T("os.market_menu_alerts"), out _, out _))
        {
            _back();
            return;
        }
        ImGui.Spacing();

        var alerts = _store.Alerts.OrderBy(a => a.Acknowledged).ThenBy(a => a.ItemName).ToList();
        if (alerts.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, Px(24f)));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Hint, Loc.T("os.market_alerts_empty"));
            ImGui.PopTextWrapPos();
            return;
        }

        _entrance.BeginFrame();
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##marketAlertsScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.TextColored(UiColors.Hint, Loc.T("os.market_remove_hint"));
                ImGui.Spacing();
                foreach (var alert in alerts)
                {
                    DrawRow(alert, ImGui.GetWindowSize().X);
                }
                ImGui.Dummy(new Vector2(0f, Px(12f)));
            }
        }
        PopScrollbarStyle();
        _entrance.EndFrame();
    }

    private void DrawRow(MarketAlert alert, float winW)
    {
        var rowH = Px(52f);
        var rowW = winW - Px(12f);
        ImGui.SetCursorPosX(Px(6f));
        var rowTl = ImGui.GetCursorScreenPos();

        var toggleW = Px(38f);
        ImGui.SetCursorScreenPos(new Vector2(rowTl.X + rowW - toggleW - Px(8f), rowTl.Y + (rowH - Px(20f)) * 0.5f));
        if (DrawToggleSwitch($"##marketAlertToggle{alert.Id:N}", "", alert.Enabled))
        {
            _store.SetEnabled(alert.Id, !alert.Enabled);
        }

        ImGui.SetCursorScreenPos(rowTl);
        var clicked = ImGui.InvisibleButton($"##marketAlertRow{alert.Id:N}", new Vector2(rowW - toggleW - Px(12f), rowH));
        HandOnHover();
        var rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        var dl = ImGui.GetWindowDrawList();
        if (ImGui.IsMouseHoveringRect(rowTl, rowTl + new Vector2(rowW, rowH)))
        {
            dl.AddRectFilled(rowTl, rowTl + new Vector2(rowW, rowH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(10f));
        }

        var iconSize = Px(30f);
        var iconTl = new Vector2(rowTl.X + Px(10f), rowTl.Y + (rowH - iconSize) * 0.5f);
        _index.TryGet(alert.ItemId, out var entry);
        if (MarketItemIcons.Get(entry.Icon) is { } handle)
        {
            dl.AddImageRounded(handle, iconTl, iconTl + new Vector2(iconSize, iconSize),
                Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), Px(6f));
        }

        var textX = iconTl.X + iconSize + Px(10f);
        var name = alert.ItemName.Length > 0 ? alert.ItemName : $"#{alert.ItemId}";
        var nameColor = alert.Acknowledged ? UiColors.Body : ThemeService.Current.AccentLight;
        dl.AddText(new Vector2(textX, rowTl.Y + Px(8f)), ImGui.GetColorU32(nameColor),
            TruncateToWidth(name, rowW - toggleW - Px(160f) - (textX - rowTl.X)));

        var threshold = alert.IsPercent
            ? $"{alert.Threshold}%"
            : $"{MarketFormat.Gil(alert.Threshold)} gil";
        var condition = Loc.T(alert.TriggerAbove ? "os.market_alert_cond_above" : "os.market_alert_cond_below", threshold);
        dl.AddText(new Vector2(textX, rowTl.Y + Px(8f) + ImGui.GetTextLineHeight() + Px(2f)),
            ImGui.GetColorU32(UiColors.Hint), TruncateToWidth($"{condition} · {alert.ScopeName}", rowW - toggleW - Px(120f)));

        if (alert.LastSeenPrice > 0)
        {
            var seen = MarketFormat.Gil(alert.LastSeenPrice);
            var seenSz = ImGui.CalcTextSize(seen);
            dl.AddText(new Vector2(rowTl.X + rowW - toggleW - Px(16f) - seenSz.X, rowTl.Y + (rowH - seenSz.Y) * 0.5f),
                ImGui.GetColorU32(new Vector4(0.98f, 0.80f, 0.36f, 1f)), seen);
        }

        if (rightClicked)
        {
            _store.Remove(alert.Id);
            return;
        }
        if (clicked)
        {
            _openItem(alert.ItemId);
        }
    }
}
