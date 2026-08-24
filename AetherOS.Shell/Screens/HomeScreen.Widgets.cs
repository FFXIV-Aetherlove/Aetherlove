using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Config;
using AetherLove.Os;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

/// <summary>Which widgets the page shows, in what order, and how they are taken off it. A card is held for a
/// beat and then dragged to move it up or down; the order is kept by id, and so is a removed one, so a newly
/// installed app brings its card along without anybody having to add it.</summary>
public sealed partial class HomeScreen
{
    internal const string ClockWidgetId = "clock";
    internal const string StatusWidgetId = "status";
    internal const string NotificationsWidgetId = "notifications";
    internal const string PartyWidgetId = "party";

    private static readonly (string Id, string NameKey, FontAwesomeIcon Icon)[] BuiltInWidgets =
    [
        (ClockWidgetId, "os.widget_clock", FontAwesomeIcon.Clock),
        (StatusWidgetId, "os.widget_status", FontAwesomeIcon.Signal),
        (NotificationsWidgetId, "os.widget_notifications", FontAwesomeIcon.Bell),
        (PartyWidgetId, "os.widget_party", FontAwesomeIcon.UserFriends),
    ];

    /// <summary>Whether the cursor was over a widget card this frame. The page-wide swipe button is
    /// submitted after the cards and fires its own right-click, so without this the background menu opened
    /// on top of the card's a moment later and the card's menu was never the one you saw.</summary>
    private bool _widgetHovered;

    private static OsSettingsConfig Settings => UiHost.Configuration.OsSettings;

    /// <summary>True while the context menu owns the page's input. The menu's rows are submitted at the
    /// END of the page so their pixels sit over every card, which under first-submitted-wins would hand
    /// their clicks to the card buttons beneath; while this is set the cards submit no buttons at all, so
    /// "Remove this widget" can never fall through and open the app it was about to remove.</summary>
    private bool WidgetMenuBlocking => _menuOpen && _menuDelay <= 0f;

    private static bool WidgetHidden(string id) => Settings.HiddenWidgets.Contains(id);

    private static void HideWidget(string id)
    {
        if (Settings.HiddenWidgets.Contains(id))
        {
            return;
        }
        Settings.HiddenWidgets.Add(id);
        UiHost.Configuration.Save();
    }

    private void ShowWidget(string id)
    {
        if (Settings.HiddenWidgets.Remove(id))
        {
            // The page it comes back to may be long and it may come back below the fold, so the page goes to
            // it. Done by id and settled during the next layout, because nothing here knows where it lands.
            _widgetRevealId = id;
            UiHost.Configuration.Save();
        }
    }

    /// <summary>Every widget the page draws, top to bottom. The player's order comes first, and anything that
    /// order has never heard of follows in the order the phone itself would have used, so an app installed
    /// after the last drag still turns up.</summary>
    private List<string> OrderedWidgetIds()
    {
        var natural = new List<string>();
        foreach (var (id, _, _) in BuiltInWidgets)
        {
            if (!WidgetHidden(id))
            {
                natural.Add(id);
            }
        }
        foreach (var app in _shell.Apps)
        {
            if (!app.Available || _shell.IsAppRemoved(app.Id) || WidgetHidden(app.Id) || app.WidgetItems.Count == 0)
            {
                continue;
            }
            natural.Add(app.Id);
        }

        var saved = Settings.WidgetOrder;
        if (saved.Count == 0)
        {
            return natural;
        }
        var ordered = new List<string>(natural.Count);
        foreach (var id in saved)
        {
            if (natural.Contains(id))
            {
                ordered.Add(id);
            }
        }
        foreach (var id in natural)
        {
            if (!ordered.Contains(id))
            {
                ordered.Add(id);
            }
        }
        return ordered;
    }

    /// <summary>How long a card is held before it lifts. The phone's long press: long enough that a slow
    /// press on a button inside a card is still that button's, short enough not to feel like waiting.
    /// </summary>
    private const float WidgetHoldSeconds = 0.40f;

    /// <summary>How far the cursor may wander during the hold. Past this the press is a swipe between pages
    /// or a mis-click, and the hold is dropped.</summary>
    private const float WidgetHoldSlack = 9f;

    private string? _widgetPressId;
    private Vector2 _widgetPressPos;
    private float _widgetPressT;
    private float _widgetPressTop;
    private float _widgetPressSpan;

    private string? _widgetDragId;
    private float _widgetDragGrab;
    private float _widgetDragSpan;
    private float _widgetDragY;
    private float _widgetDragLift;
    private float _widgetSlotTop;
    private string? _widgetRevealId;

    /// <summary>Where each card that actually drew ended up this frame, in the order they were drawn, with
    /// the lifted card's open row taken back out of the numbers. Counting the ones above the cursor then
    /// gives the row it would land on, and gives the same answer wherever the open row happens to be.
    /// </summary>
    private readonly List<(string Id, float Top, float Bottom)> _widgetRects = [];

    /// <summary>True while a card is lifted. Every card stands its own buttons down while it is, the way they
    /// do under the context menu.</summary>
    private bool WidgetDragActive => _widgetDragId != null;

    /// <summary>Lifts a card. The saved order is filled in from what is on screen first, so the first drag on
    /// a page that has never been arranged moves one card rather than shuffling all of them.</summary>
    private void BeginWidgetDrag(string id, Vector2 mouse)
    {
        var order = Settings.WidgetOrder;
        foreach (var visible in OrderedWidgetIds())
        {
            if (!order.Contains(visible))
            {
                order.Add(visible);
            }
        }

        _widgetDragId = id;
        _widgetDragGrab = mouse.Y - _widgetPressTop;
        _widgetDragSpan = _widgetPressSpan;
        _widgetDragY = mouse.Y;
        _widgetDragLift = AccessibilityService.ReduceMotion ? 1f : 0f;
        _widgetSlotTop = _widgetPressTop;
        _widgetPressId = null;

        // The press that became a lift also went to the page's swipe button, which by now has the page
        // following the cursor sideways. It is a few pixels at most, so the page is put back rather than
        // eased back.
        _draggingPages = false;
        _page = -1f;
        _targetPage = -1;
    }

    private void CancelWidgetDrag()
    {
        _widgetDragId = null;
        _widgetPressId = null;
        _widgetDragLift = 0f;
    }

    /// <summary>Moves a widget to a row, counting only the widgets on the page. Hidden ones keep their entry
    /// in the list, so putting one back later brings it out among the same neighbours rather than at the
    /// bottom of a page somebody has arranged.</summary>
    private void PlaceWidget(string id, int row)
    {
        var order = Settings.WidgetOrder;
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rect in _widgetRects)
        {
            shown.Add(rect.Id);
        }
        order.Remove(id);
        var seen = 0;
        var at = order.Count;
        for (var i = 0; i < order.Count; i++)
        {
            if (!shown.Contains(order[i]))
            {
                continue;
            }
            if (seen == row)
            {
                at = i;
                break;
            }
            seen++;
        }
        order.Insert(at, id);
    }

    /// <summary>Runs the hold, the lifted card and the drop. Called once the page has laid itself out, so the
    /// row the card would land on is read from where the other cards actually are.</summary>
    private void UpdateWidgetDrag(float bandBottom)
    {
        var dt = ImGui.GetIO().DeltaTime;
        var mouse = ImGui.GetMousePos();

        if (_widgetDragId is { } dragged)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                UiHost.Configuration.Save();
                CancelWidgetDrag();
                return;
            }

            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            _widgetDragY = mouse.Y;
            _widgetDragLift = AccessibilityService.ReduceMotion ? 1f : MathF.Min(1f, _widgetDragLift + (dt * 9f));

            var edge = Px(46f);
            if (mouse.Y > bandBottom - edge)
            {
                _widgetScroll += Px(420f) * dt;
            }
            else if (mouse.Y < _widgetBandTop + edge)
            {
                _widgetScroll -= Px(420f) * dt;
            }
            _widgetScroll = Math.Clamp(_widgetScroll, 0f, MathF.Max(0f, _widgetOverflow));

            // The card's TOP against the middle of each row, not its own middle: a card is only as far down
            // the page as its leading edge, and measuring from the middle would hand a tall card the row
            // under it the moment it lifted, purely for being tall.
            var lead = LiftedCardTop();
            var row = 0;
            foreach (var rect in _widgetRects)
            {
                if ((rect.Top + rect.Bottom) * 0.5f < lead)
                {
                    row++;
                }
            }
            PlaceWidget(dragged, row);
            return;
        }

        if (_widgetPressId is not { } pressed)
        {
            return;
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) || WidgetMenuBlocking
            || (mouse - _widgetPressPos).Length() > Px(WidgetHoldSlack))
        {
            _widgetPressId = null;
            return;
        }
        _widgetPressT += dt;
        if (_widgetPressT >= WidgetHoldSeconds)
        {
            BeginWidgetDrag(pressed, mouse);
        }
    }

    /// <summary>Where the lifted card is drawn. Clamped to the band rather than left to follow the cursor
    /// off the page, and read by the drop as well as the draw, so the row it lands on is the row it looks
    /// like it will land on.</summary>
    private float LiftedCardTop()
        => Math.Clamp(_widgetDragY - _widgetDragGrab, _widgetBandTop - Px(20f), _widgetBandBottom - Px(28f));

    private float LiftedCardHeight() => MathF.Max(Px(12f), _widgetDragSpan - Px(12f));

    /// <summary>The outline that fills in under a card being held. The lift is a gesture nothing on the
    /// page announces, so the hold has to look like it is doing something before it does it.</summary>
    private void DrawWidgetHoldHint(ImDrawListPtr dl, Vector2 tl, Vector2 br)
    {
        var t = Math.Clamp((_widgetPressT - 0.08f) / MathF.Max(0.01f, WidgetHoldSeconds - 0.08f), 0f, 1f);
        if (t <= 0f)
        {
            return;
        }
        var inset = Px(2f) * t;
        dl.AddRect(tl + new Vector2(inset, inset), br - new Vector2(inset, inset + Px(12f)),
            ThemeService.Current.AccentWithAlpha(0.45f * t), Px(18f), ImDrawFlags.RoundCornersAll,
            Px(1f) + (Px(1.2f) * t));
    }

    /// <summary>The card while it is off the page: the row it will drop into is outlined, and the card itself
    /// rides the cursor over an opaque backing, because every card is glass and two of them stacked read as
    /// neither.</summary>
    private void DrawLiftedWidget(ImDrawListPtr dl, string id, float x, float w)
    {
        var height = LiftedCardHeight();
        dl.AddRect(new Vector2(x, _widgetSlotTop), new Vector2(x + w, _widgetSlotTop + height),
            ThemeService.Current.AccentWithAlpha(0.30f), Px(18f), ImDrawFlags.RoundCornersAll, Px(1f));

        var top = LiftedCardTop();
        var tl = new Vector2(x, top);
        var br = new Vector2(x + w, top + height);
        for (var i = 4; i >= 1; i--)
        {
            var spread = Px(3f) * i * _widgetDragLift;
            dl.AddRectFilled(tl - new Vector2(spread, spread), br + new Vector2(spread, spread),
                OsDraw.Black(0.13f * (1f - ((i - 1) / 4f))), Px(18f) + spread);
        }
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(0.07f, 0.07f, 0.10f, 0.97f)), Px(18f));

        var bottom = DrawWidgetCard(dl, id, x, top, w);
        if (bottom > top)
        {
            _widgetDragSpan = bottom - top;
        }
        dl.AddRect(tl, br, ThemeService.Current.AccentWithAlpha(0.55f), Px(18f), ImDrawFlags.RoundCornersAll, Px(1.4f));
    }

    /// <summary>Right-click over a widget card raises the page's own menu for it, and a left press starts the
    /// hold that lifts it. The menu itself is drawn once, at the end of the page, so it can sit over every
    /// card rather than inside one.
    /// <para>The press is read from the mouse rather than from an item, because a card is free to cover
    /// itself in buttons of its own (an app's card is one big open target) and the hold has to work over
    /// them. Nothing is stolen by reading it: the lift stands every card's buttons down, which drops the
    /// active id, so the click those buttons were about to fire never happens.</para></summary>
    private void HandleWidgetContext(string id, Vector2 tl, Vector2 br)
    {
        if (!ImGui.IsMouseHoveringRect(tl, br))
        {
            return;
        }
        _widgetHovered = true;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            OpenWidgetMenu(id, ImGui.GetMousePos());
        }
        if (!WidgetMenuBlocking && !WidgetDragActive && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _widgetPressId = id;
            _widgetPressPos = ImGui.GetMousePos();
            _widgetPressT = 0f;
            _widgetPressTop = tl.Y;
            _widgetPressSpan = br.Y - tl.Y;
        }
    }

    /// <summary>What a widget opens when it is asked to be configured, or null when it has nothing to
    /// configure. Only the party card carries settings of its own today; the rest are what they are, and a
    /// row that dropped the player at an app's front page would be a lie about being its settings.</summary>
    private Action? WidgetSettings(string id) =>
        id == PartyWidgetId ? _partyIntro.ShowSettings : null;

    /// <summary>Whether the page has anything to offer on a right-click.</summary>
    private bool AnyHiddenWidgets => HiddenWidgetChoices().Count > 0;

    /// <summary>Everything currently off the page, built-ins first and then the apps that offer a widget.
    /// An app that was removed from the phone entirely is not offered: it has no card to come back to.</summary>
    private List<(string Id, string Name, FontAwesomeIcon Icon)> HiddenWidgetChoices()
    {
        var choices = new List<(string, string, FontAwesomeIcon)>();
        foreach (var (id, nameKey, icon) in BuiltInWidgets)
        {
            if (WidgetHidden(id))
            {
                choices.Add((id, Loc.T(nameKey), icon));
            }
        }
        foreach (var app in _shell.Apps)
        {
            if (!app.Available || _shell.IsAppRemoved(app.Id) || !WidgetHidden(app.Id) || app.WidgetItems.Count == 0)
            {
                continue;
            }
            choices.Add((app.Id, app.Name, app.Icon));
        }
        return choices;
    }
}
