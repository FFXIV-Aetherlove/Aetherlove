using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Matching;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class DeckScreen
{
    private static readonly Vector4 SuperTop = new(1.00f, 0.80f, 0.28f, 1f);
    private static readonly Vector4 SuperBottom = new(0.82f, 0.55f, 0.05f, 1f);

    private volatile int _superlikesRemaining;
    private volatile int _superlikesPerDay;
    private bool _throwSuperlike;
    private float _superHover;
    private float _superViewHover;

    // Deferred superliked cards; cleared when a fresh deck applies, since the server re-injects un-actioned superlikers on every pull.
    private readonly List<DeckCardDto> _deferredSuperlikes = [];

    // Superlike intro overlay; panel height is re-measured each frame.
    private bool _showSuperlikeIntro;
    private float _superlikeIntroHeight;

    /// <summary>Middle star button; a spent non-supporter button opens the supporter pitch instead of superliking.</summary>
    private void DrawSuperlikeButton(Vector2 center, float radius)
    {
        var dl = ImGui.GetWindowDrawList();
        var isSupporter = _superlikesPerDay > 0;
        var enabled = _superlikesRemaining > 0;

        if (enabled)
        {
            // Idle pulse so the button reads as special even without hover.
            if (!AccessibilityService.ReduceMotion)
            {
                var pulse = (MathF.Sin((float)ImGui.GetTime() * 3.2f) + 1f) * 0.5f;
                dl.AddCircleFilled(center, radius + Px(3f) + Px(8f) * pulse, ToU32(SuperTop, 0.26f * (1f - pulse) + 0.06f), 56);
            }
            if (DrawActionButton("##deckSuper", center, radius, FontAwesomeIcon.Star, SuperTop, SuperBottom, ref _superHover))
            {
                OnSuperlikeClicked();
            }
        }
        else
        {
            ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
            var clicked = ImGui.InvisibleButton("##deckSuper", new Vector2(radius * 2f, radius * 2f));
            dl.AddCircleFilled(center + new Vector2(0f, Px(3f)), radius, 0x44000000u, 48);
            dl.AddCircleFilled(center, radius, 0xFF2A2A2Au, 56);
            dl.AddCircle(center, radius, 0x33FFFFFFu, 56, Px(1.2f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Star, radius * 0.9f, center, UiColors.TextMuted);
            if (!isSupporter && clicked)
            {
                Widgets.SupporterInfoPopup.Open("deck.superlike_supporter_pitch");
            }
        }

        if (ImGui.IsItemHovered())
        {
            if (enabled && isSupporter)
            {
                ImGui.SetTooltip(Loc.T("deck.superlike_tooltip", _superlikesRemaining, _superlikesPerDay));
            }
            else if (enabled)
            {
                ImGui.SetTooltip(Loc.T("deck.superlike_free_tooltip"));
            }
            else if (isSupporter)
            {
                var remaining = DailyResetRemaining();
                ImGui.SetTooltip(Loc.T("deck.superlike_cooldown", (int)remaining.TotalHours, remaining.Minutes));
            }
            else
            {
                var remaining = WeeklyResetRemaining();
                ImGui.SetTooltip(Loc.T("deck.superlike_weekly_cooldown", (int)remaining.TotalDays, remaining.Hours));
            }
        }
    }

    /// <summary>Star button while deferred superlikes wait; re-deals them on top of the deck.</summary>
    private void DrawViewSuperlikesButton(Vector2 center, float radius)
    {
        var dl = ImGui.GetWindowDrawList();
        if (!AccessibilityService.ReduceMotion)
        {
            var pulse = (MathF.Sin((float)ImGui.GetTime() * 3.2f) + 1f) * 0.5f;
            dl.AddCircleFilled(center, radius + Px(3f) + Px(8f) * pulse, ToU32(SuperTop, 0.26f * (1f - pulse) + 0.06f), 56);
        }
        if (DrawActionButton("##deckSuperView", center, radius, FontAwesomeIcon.Star, SuperTop, SuperBottom, ref _superViewHover))
        {
            RedealDeferredSuperlikes();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("deck.superlikes_waiting", _deferredSuperlikes.Count));
        }

        var badgeCenter = center + new Vector2(radius * 0.78f, -radius * 0.78f);
        dl.AddCircleFilled(badgeCenter, Px(8f), 0xFF3535E0u, 20);
        var label = _deferredSuperlikes.Count > 9 ? "9+" : _deferredSuperlikes.Count.ToString();
        var sz = ImGui.CalcTextSize(label);
        dl.AddText(badgeCenter - sz * 0.5f, 0xFFFFFFFFu, label);
    }

    private void RedealDeferredSuperlikes()
    {
        if (_deferredSuperlikes.Count == 0 || _isThrowingCard || _isSnappingBack || _isUndoing || _isDeferring)
        {
            return;
        }
        foreach (var card in _deferredSuperlikes)
        {
            EnsurePortraitCached(card);
        }
        _cards.InsertRange(0, _deferredSuperlikes);
        _deferredSuperlikes.Clear();
    }

    /// <summary>The first tap shows a one-time intro without spending a superlike; after that the current
    /// card is thrown right as a superlike.</summary>
    private void OnSuperlikeClicked()
    {
        if (_isThrowingCard || _isSnappingBack || _isUndoing || _isDeferring || _cards.Count == 0)
        {
            return;
        }
        if (!UiHost.Configuration.SeenSuperlikeIntro)
        {
            UiHost.Configuration.SeenSuperlikeIntro = true;
            UiHost.Configuration.Save();
            _showSuperlikeIntro = true;
            return;
        }
        _throwSuperlike = true;
        StartThrow(true);
    }

    /// <summary>One-time overlay explaining the superlike; dims only the phone content.</summary>
    private void DrawSuperlikeIntroOverlay(Vector2 windowPos, Vector2 windowSize)
    {
        if (!_showSuperlikeIntro)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(windowPos, windowPos + windowSize,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

        ImGui.SetCursorScreenPos(windowPos);
        if (ImGui.InvisibleButton("##superlikeIntroScrim", windowSize))
        {
            _showSuperlikeIntro = false;
        }

        var w = Px(272f);
        var pad = Px(16f, 16f);
        var h = _superlikeIntroHeight > 0f ? _superlikeIntroHeight : Px(200f);
        var panelPos = windowPos + (windowSize - new Vector2(w, h)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
        using (var child = ImRaii.Child("##superlikeIntroPanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                ModalUi.Header(innerW, FontAwesomeIcon.Star, Loc.T("deck.superlike_intro_title"), SuperTop);
                var allowance = _superlikesPerDay > 0
                    ? Loc.T("deck.superlike_intro_daily", _superlikesPerDay)
                    : Loc.T("deck.superlike_intro_weekly");
                ImGui.TextColored(UiColors.Body, Loc.T("deck.superlike_intro", allowance));
                ImGui.Spacing();
                ImGui.Spacing();
                if (ModalUi.Button($"{Loc.T("common.ok")}##superlikeIntroOk", innerW))
                {
                    _showSuperlikeIntro = false;
                }
                ImGui.PopTextWrapPos();
                _superlikeIntroHeight = ImGui.GetCursorPosY() + pad.Y;
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    // The superlike reveal shrinks the card into a glowing frame below a header strip.
    private const float RevealCardScale = 0.84f;
    private const float RevealHeaderH = 84f;

    private float RevealCardCenterY(Vector2 windowPos) =>
        windowPos.Y + Px(RevealHeaderH) + Px(CardHeight) * RevealCardScale * 0.5f;

    /// <summary>Backdrop for a superlike reveal; drawn before the card, reduce-motion freezes all movement.</summary>
    private static void DrawSuperlikeScene(Vector2 windowPos, Vector2 windowSize)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var time = AccessibilityService.ReduceMotion ? 0f : (float)ImGui.GetTime();

        var cardW = windowSize.X * RevealCardScale;
        var cardH = Px(CardHeight) * RevealCardScale;
        var cardCenter = new Vector2(windowPos.X + windowSize.X * 0.5f,
            windowPos.Y + Px(RevealHeaderH) + cardH * 0.5f);

        dl.PushClipRect(windowPos, windowPos + windowSize, true);

        // Vertical wash from near-black into the theme accent.
        var washTop = ImGui.GetColorU32(Darken(t.Accent, 0.88f));
        var washBottom = ImGui.GetColorU32(Darken(t.Accent, 0.55f));
        dl.AddRectFilledMultiColor(windowPos, windowPos + windowSize, washTop, washTop, washBottom, washBottom);

        // Slow-turning rays in the secondary gradient colours, radiating from behind the card.
        const int RayCount = 12;
        const float Slice = MathF.Tau / RayCount;
        var baseAngle = time * 0.15f;
        var reach = windowSize.Length();
        for (var i = 0; i < RayCount; i++)
        {
            var a0 = baseAngle + i * Slice;
            var a1 = a0 + Slice * 0.42f;
            var rayCol = (i & 1) == 0 ? t.SecondaryStart : t.SecondaryEnd;
            dl.AddTriangleFilled(
                cardCenter,
                cardCenter + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * reach,
                cardCenter + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * reach,
                ImGui.GetColorU32(rayCol with { W = 0.10f }));
        }

        // Radial glow pooling behind the card.
        dl.AddCircleFilled(cardCenter, windowSize.X * 0.55f, ImGui.GetColorU32(t.AccentLight with { W = 0.10f }), 48);
        dl.AddCircleFilled(cardCenter, windowSize.X * 0.34f, ImGui.GetColorU32(t.AccentLight with { W = 0.12f }), 48);

        // Glowing frame marking the card's slot (a soft halo under a crisp stroke).
        var frameTL = cardCenter - new Vector2(cardW * 0.5f, cardH * 0.5f) - Px(4f, 4f);
        var frameBR = cardCenter + new Vector2(cardW * 0.5f, cardH * 0.5f) + Px(4f, 4f);
        dl.AddRect(frameTL - Px(3f, 3f), frameBR + Px(3f, 3f),
            ImGui.GetColorU32(t.AccentLight with { W = 0.22f }), Px(14f), ImDrawFlags.None, Px(6f));
        dl.AddRect(frameTL, frameBR,
            ImGui.GetColorU32(t.AccentLight with { W = 0.75f }), Px(12f), ImDrawFlags.None, Px(2f));

        DrawRevealSparkles(dl, windowPos, windowSize, time);

        // Header strip: big gold-starred title carrying the sender's name, then the subtitle.
        var titleFontPtr = ImGui.GetFont();
        var titleSize = ImGui.GetFontSize();
        using (UiFonts.H1?.Push())
        {
            titleFontPtr = ImGui.GetFont();
            titleSize = ImGui.GetFontSize();
        }
        var title = Loc.T("deck.superliked_you").ToUpperInvariant();
        var titleW = ImGui.CalcTextSize(title).X * (titleSize / ImGui.GetFontSize());
        // Long translations shrink the line to fit between the flanking stars.
        var starPx = titleSize * 0.5f;
        var maxTitleW = windowSize.X - Px(24f) * 2f - (Px(8f) + starPx) * 2f;
        if (titleW > maxTitleW)
        {
            titleSize *= maxTitleW / titleW;
            titleW = maxTitleW;
            starPx = titleSize * 0.5f;
        }
        var titleY = windowPos.Y + Px(14f);
        var titleX = windowPos.X + (windowSize.X - titleW) * 0.5f;
        dl.AddText(titleFontPtr, titleSize, new Vector2(titleX + Px(1.5f), titleY + Px(1.5f)), 0xAA000000u, title);
        dl.AddText(titleFontPtr, titleSize, new Vector2(titleX, titleY), 0xFFFFFFFFu, title);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, starPx,
            new Vector2(titleX - Px(8f) - starPx * 0.5f, titleY + titleSize * 0.5f), UiColors.FavoriteStar);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, starPx,
            new Vector2(titleX + titleW + Px(8f) + starPx * 0.5f, titleY + titleSize * 0.5f), UiColors.FavoriteStar);

        var subFontPtr = ImGui.GetFont();
        var subSize = ImGui.GetFontSize();
        using (UiFonts.H3?.Push())
        {
            subFontPtr = ImGui.GetFont();
            subSize = ImGui.GetFontSize();
        }
        var sub = Loc.T("deck.superliked_you_sub");
        var subW = ImGui.CalcTextSize(sub).X * (subSize / ImGui.GetFontSize());
        dl.AddText(subFontPtr, subSize,
            new Vector2(windowPos.X + (windowSize.X - subW) * 0.5f, titleY + titleSize + Px(8f)),
            0xD8FFFFFFu, sub);

        dl.PopClipRect();
    }

    /// <summary>Star sparkles on deterministic per-index tracks; a time of 0 (reduce-motion) leaves them static.</summary>
    private static void DrawRevealSparkles(ImDrawListPtr dl, Vector2 windowPos, Vector2 windowSize, float time)
    {
        const int Count = 14;
        var t = ThemeService.Current;
        for (var i = 0; i < Count; i++)
        {
            var fx = Frac(i * 0.6180339887f + 0.13f);
            var speed = 0.012f + Frac(i * 0.377f) * 0.02f;
            var fy = Frac(i * 0.7548776662f + 0.41f - time * speed);
            var pos = windowPos + new Vector2(fx * windowSize.X, fy * windowSize.Y);
            var twinkle = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin(time * 1.7f + i * 2.1f));
            var size = Px(5f) + Px(4f) * Frac(i * 0.271f);
            var col = (i % 3) switch
            {
                0 => UiColors.FavoriteStar,
                1 => ImGui.GetColorU32(t.AccentLight),
                _ => ImGui.GetColorU32(t.SecondaryEnd),
            };
            col = (col & 0x00FFFFFFu) | ((uint)(twinkle * 255f) << 24);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Star, size, pos, col);
        }
    }

    private static float Frac(float v) => v - MathF.Floor(v);

    private static Vector4 Darken(Vector4 c, float amount) =>
        new(c.X * (1f - amount), c.Y * (1f - amount), c.Z * (1f - amount), 1f);
}
