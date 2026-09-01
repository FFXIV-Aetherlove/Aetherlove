using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Pieces every boost surface shares: the "Boosted" pill a listing wears, the remaining-time
/// wording, and the style picker. Places, Levemetes and the Store all draw these, so they live here rather
/// than three times over.</summary>
internal static class BoostUi
{
    /// <summary>The pill on a boosted listing. Returns the width it drew, so callers can lay out beside it.</summary>
    public static float DrawBoostedPill(ImDrawListPtr dl, Vector2 topLeft, BoostStyle style)
    {
        var label = Loc.T("os.boost_boosted");
        var textSize = ImGui.CalcTextSize(label);
        var iconPx = ImGui.GetTextLineHeight() * 0.82f;
        var padX = Px(8f);
        var gap = Px(5f);
        var h = textSize.Y + Px(6f);
        var w = padX + iconPx + gap + textSize.X + padX;
        var br = topLeft + new Vector2(w, h);
        var key = BoostFx.KeyColor(style);

        dl.AddRectFilled(topLeft, br, ImGui.GetColorU32(key with { W = 0.20f }), h * 0.5f);
        dl.AddRect(topLeft, br, ImGui.GetColorU32(key with { W = 0.62f }), h * 0.5f, ImDrawFlags.None, Px(1f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, iconPx,
            new Vector2(topLeft.X + padX + (iconPx * 0.5f), topLeft.Y + (h * 0.5f)), ImGui.GetColorU32(key));
        dl.AddText(new Vector2(topLeft.X + padX + iconPx + gap, topLeft.Y + ((h - textSize.Y) * 0.5f)),
            ImGui.GetColorU32(key), label);
        return w;
    }

    /// <summary>"3d" / "7h", the same shape the Levemetes expiry line uses.</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        return remaining.TotalDays >= 1.0
            ? $"{(int)Math.Ceiling(remaining.TotalDays)}d"
            : $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalHours))}h";
    }

    /// <summary>"Boosted for 3d more", or null when the window has run out.</summary>
    public static string? RemainingLabel(DateTimeOffset? boostedUntilUtc)
    {
        var now = DateTimeOffset.UtcNow;
        return BoostRules.IsActive(boostedUntilUtc, now)
            ? Loc.T("os.boost_left", FormatRemaining(boostedUntilUtc!.Value - now))
            : null;
    }

    /// <summary>A 2x2 grid of style tiles, each drawing the effect it is offering. Returns true when the
    /// selection changed this frame.</summary>
    public static bool DrawStylePicker(float innerW, ref BoostStyle selected)
    {
        var changed = false;
        var gap = Px(8f);
        var tileW = (innerW - gap) * 0.5f;
        var tileH = Px(46f);
        for (short i = 0; i < BoostRules.StyleCount; i++)
        {
            var style = (BoostStyle)i;
            if (i % 2 == 1)
            {
                ImGui.SameLine(0f, gap);
            }
            if (DrawStyleTile(style, new Vector2(tileW, tileH), style == selected))
            {
                selected = style;
                changed = true;
            }
        }
        return changed;
    }

    private static bool DrawStyleTile(BoostStyle style, Vector2 size, bool active)
    {
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton($"##boostStyle{(short)style}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        var br = tl + size;
        var rounding = Px(9f);
        var key = BoostFx.KeyColor(style);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(key with { W = active ? 0.20f : (hovered ? 0.12f : 0.07f) }), rounding);
        BoostFx.Draw(dl, tl, br, rounding, style, active ? 1f : 0.55f);

        var label = Loc.T(BoostFx.NameKey(style));
        var textSize = ImGui.CalcTextSize(label);
        dl.AddText(tl + ((size - textSize) * 0.5f),
            ImGui.GetColorU32(active ? Vector4.One : UiColors.Body with { W = 0.88f }), label);
        return pressed;
    }
}

/// <summary>The in-page confirm a boost is always spent through: what it will do, the four effects to pick
/// from, and how many are left after. One instance per screen that can spend one.</summary>
internal sealed class BoostConfirmOverlay
{
    private BoostTarget _target;
    private string _name = string.Empty;
    private DateTimeOffset? _activeUntil;
    private int _owned;
    private float _panelH;

    public bool IsOpen { get; private set; }

    public BoostStyle Style { get; private set; }

    public Guid TargetId { get; private set; }

    public BoostTarget Target => _target;

    /// <summary>Set by the caller while its hub call is in flight; the buttons go quiet.</summary>
    public bool Busy { get; set; }

    /// <summary>A resolved message to show under the buttons; the caller clears it.</summary>
    public string? Error { get; set; }

    public void Open(BoostTarget target, Guid targetId, string name, DateTimeOffset? activeUntilUtc, int owned)
    {
        _target = target;
        TargetId = targetId;
        _name = name;
        _activeUntil = activeUntilUtc;
        _owned = owned;
        _panelH = 0f;
        Style = BoostRules.IsActive(activeUntilUtc, DateTimeOffset.UtcNow) ? Style : BoostStyle.Aurora;
        Busy = false;
        Error = null;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        Busy = false;
        Error = null;
    }

    /// <summary>Returns true on the frame the user confirmed. Draw it after the page body.</summary>
    public bool Draw(Vector2 winPos, Vector2 winSize)
    {
        if (!IsOpen)
        {
            return false;
        }
        if (!Busy && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            Close();
            return false;
        }

        var confirmed = false;
        var canSpend = _owned > 0 && !Busy;
        var dismissed = SharedUiHelpers.DrawPageOverlayPanel(
            "boostConfirm", winPos, winSize, ref _panelH, Px(330f), innerW =>
            {
                ModalUi.Header(innerW, FontAwesomeIcon.Bolt,
                    Loc.T(_target == BoostTarget.Levemete ? "os.boost_confirm_ad" : "os.boost_confirm_venue"),
                    BoostFx.KeyColor(Style));

                ImGui.PushTextWrapPos(innerW);
                ImGui.TextColored(UiColors.Body, _name);
                ImGui.Spacing();
                ImGui.TextColored(UiColors.Subtle, BoostRules.IsActive(_activeUntil, DateTimeOffset.UtcNow)
                    ? Loc.T("os.boost_confirm_extend", BoostUi.FormatRemaining(_activeUntil!.Value - DateTimeOffset.UtcNow))
                    : Loc.T("os.boost_confirm_body"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();

                var style = Style;
                if (BoostUi.DrawStylePicker(innerW, ref style))
                {
                    Style = style;
                }
                ImGui.Spacing();

                ImGui.TextColored(_owned > 0 ? UiColors.Subtle : UiColors.Muted,
                    _owned > 0 ? Loc.T("os.boost_left_count", _owned) : Loc.T("os.boost_none"));
                ImGui.Spacing();

                var gap = Px(8f);
                var half = (innerW - gap) * 0.5f;
                if (ModalUi.Button($"{Loc.T("common.cancel")}##boostCancel", half) && !Busy)
                {
                    Close();
                }
                ImGui.SameLine(0f, gap);
                using (Dalamud.Interface.Utility.Raii.ImRaii.Disabled(!canSpend))
                {
                    if (ModalUi.Button($"{Loc.T("os.boost_go")}##boostGo", half) && canSpend)
                    {
                        confirmed = true;
                    }
                }

                if (Error is { Length: > 0 } error)
                {
                    ImGui.Spacing();
                    ImGui.PushTextWrapPos(innerW);
                    ImGui.TextColored(UiColors.Danger, error);
                    ImGui.PopTextWrapPos();
                }
            });
        if (dismissed && !Busy)
        {
            Close();
        }
        return confirmed;
    }
}
