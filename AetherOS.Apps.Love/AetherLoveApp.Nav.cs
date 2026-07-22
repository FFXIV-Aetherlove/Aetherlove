using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Screens;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Love;

/// <summary>The dating app's own bottom tab bar (Profile / Swipe / Matches / Settings), moved out of the phone
/// window now that dating renders as a surface app inside its own content rect.</summary>
public sealed partial class AetherLoveApp
{
    private const float NavBarHeight = 70f;

    private static bool IsNavActive(LoveView target, LoveView current) => target switch
    {
        LoveView.ChatList => current is LoveView.ChatList or LoveView.ChatCategory or LoveView.Chat or LoveView.Blocked,
        _ => current == target,
    };

    private void DrawBottomNav(Vector2 origin, Vector2 avail)
    {
        var barTop = origin.Y + avail.Y - Px(NavBarHeight) + Px(16f);
        var barLeft = origin.X;
        var barWidth = avail.X;

        var drawList = ImGui.GetWindowDrawList();

        // Profile draws the avatar, so its label is unused. The Switch slot goes back to the profile
        // selection screen (which also hosts the create slot, so it earns its place on one profile too).
        var items = new (FontAwesomeIcon icon, string label, LoveView target)[]
        {
            (FontAwesomeIcon.User, string.Empty, LoveView.MyProfile),
            (FontAwesomeIcon.LayerGroup, Loc.T("common.nav_swipe"), LoveView.Deck),
            (FontAwesomeIcon.Comment, Loc.T("common.nav_matches"), LoveView.ChatList),
            (FontAwesomeIcon.Cog, Loc.T("common.nav_settings"), LoveView.Settings),
            (FontAwesomeIcon.Sync, Loc.T("common.nav_switch"), LoveView.ProfilePicker),
        };

        var slotWidth = barWidth / items.Length;
        var iconFont = UiHost.PluginInterface.UiBuilder.FontIcon;
        var fontSize = ImGui.GetFontSize();
        var accentCol = ThemeService.Current.AccentU32;
        var gradA = ThemeService.Current.SecondaryStart;
        var gradB = ThemeService.Current.SecondaryEnd;
        var gradPhase = (float)ImGui.GetTime() * 1.6f;

        for (int i = 0; i < items.Length; i++)
        {
            var (icon, label, target) = items[i];
            var isActive = IsNavActive(target, _router.Current);
            var color = isActive ? accentCol : UiColors.TextMuted;

            var slotCenterX = barLeft + slotWidth * i + slotWidth * 0.5f;
            var iconY = barTop + Px(10f);
            var labelY = iconY + fontSize + Px(4f);

            if (target == LoveView.MyProfile)
            {
                DrawProfileNavButton(drawList, slotCenterX, iconY, isActive, icon, color);
            }
            else
            {
                ImGui.PushFont(iconFont);
                var iconStr = icon.ToIconString();
                var iconSize = ImGui.CalcTextSize(iconStr);
                var iconVtx = drawList.VtxBuffer.Size;
                drawList.AddText(ImGui.GetFont(), fontSize,
                    new Vector2(slotCenterX - iconSize.X * 0.5f, iconY),
                    color, iconStr);
                if (isActive && !AccessibilityService.ReduceMotion)
                {
                    GradientSweepVertices(drawList, iconVtx, gradA, gradB, gradPhase);
                }
                ImGui.PopFont();

                if (target == LoveView.ChatList)
                {
                    var total = _notifications.TotalBadge;
                    if (total > 0)
                    {
                        var badgeR = Px(7f);
                        var badgeCenter = new Vector2(slotCenterX + iconSize.X * 0.5f + Px(1f), iconY - Px(1f));
                        drawList.AddCircleFilled(badgeCenter, badgeR, UiColors.UnreadBadge);
                        var badgeLabel = total > 9 ? "9+" : total.ToString();
                        var badgeFsz = fontSize * 0.68f;
                        var badgeTsz = ImGui.CalcTextSize(badgeLabel) * (badgeFsz / fontSize);
                        drawList.AddText(ImGui.GetFont(), badgeFsz,
                            badgeCenter - badgeTsz * 0.5f, 0xFFFFFFFF, badgeLabel);
                    }
                }

                // Switch slot: badge the OTHER profiles' pending activity, so a user working in one profile sees
                // when a sibling has new matches/messages without leaving.
                if (target == LoveView.ProfilePicker)
                {
                    var (sm, su) = _siblingBadges.TotalsExcluding(UiHost.Configuration.Auth.ActiveProfileId ?? Guid.Empty);
                    var others = sm + su;
                    if (others > 0)
                    {
                        var badgeR = Px(7f);
                        var badgeCenter = new Vector2(slotCenterX + iconSize.X * 0.5f + Px(1f), iconY - Px(1f));
                        drawList.AddCircleFilled(badgeCenter, badgeR, UiColors.UnreadBadge);
                        var badgeLabel = others > 9 ? "9+" : others.ToString();
                        var badgeFsz = fontSize * 0.68f;
                        var badgeTsz = ImGui.CalcTextSize(badgeLabel) * (badgeFsz / fontSize);
                        drawList.AddText(ImGui.GetFont(), badgeFsz,
                            badgeCenter - badgeTsz * 0.5f, 0xFFFFFFFF, badgeLabel);
                    }
                }

                var labelSize = ImGui.CalcTextSize(label);
                var labelVtx = drawList.VtxBuffer.Size;
                drawList.AddText(new Vector2(slotCenterX - labelSize.X * 0.5f, labelY), color, label);

                if (isActive)
                {
                    if (!AccessibilityService.ReduceMotion)
                    {
                        GradientSweepVertices(drawList, labelVtx, gradA, gradB, gradPhase);
                    }
                    var dotY = labelY + fontSize + Px(3f);
                    drawList.AddCircleFilled(new Vector2(slotCenterX, dotY), Px(2.5f), accentCol);
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(barLeft + slotWidth * i, barTop));
            var navClicked = ImGui.InvisibleButton($"##nav_{i}", new Vector2(slotWidth, Px(NavBarHeight)));
            SharedUiHelpers.HandOnHover();
            if (navClicked)
            {
                if (target == LoveView.ProfilePicker)
                {
                    // Entered from the nav, not from Settings: no settings back pill.
                    _profilePicker.OpenedFromSettings = false;
                }
                if (target != _router.Current)
                {
                    _router.Navigate(target);
                }
            }
        }
    }

    private void DrawProfileNavButton(ImDrawListPtr drawList, float centerX, float iconY,
                                      bool isActive, FontAwesomeIcon fallbackIcon, uint iconColor)
    {
        var fontSize = ImGui.GetFontSize();
        var baseRadius = fontSize + Px(2f);
        // Grows up from a fixed bottom edge.
        const float avatarScale = 1.44f;
        var radius = baseRadius * avatarScale;
        var center = new Vector2(centerX, iconY + baseRadius * 2f - radius);

        var wrap = _ownAvatar.Texture?.GetWrapOrDefault();
        if (wrap != null)
        {
            var tl = center - new Vector2(radius, radius);
            var br = center + new Vector2(radius, radius);
            drawList.AddImageRounded(wrap.Handle, tl, br,
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, radius, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
            var iconStr = fallbackIcon.ToIconString();
            var iconSize = ImGui.CalcTextSize(iconStr);
            drawList.AddText(ImGui.GetFont(), fontSize, center - iconSize * 0.5f, iconColor, iconStr);
            ImGui.PopFont();
        }

        drawList.AddCircle(center, radius, UiColors.AvatarRing, 64, Px(1f));

        if (isActive)
        {
            var th = ThemeService.Current;
            if (AccessibilityService.ReduceMotion)
            {
                drawList.AddCircle(center, radius, th.AccentU32, 64, Px(2f));
            }
            else
            {
                DrawGradientRing(drawList, center, radius, Px(2.5f), th.SecondaryStart, th.SecondaryEnd);
            }
        }

        if (_bootstrap.LastConnection is { IsSupporter: true })
        {
            var badgeCenter = center + new Vector2(radius * 0.74f, -radius * 0.74f);
            var badgeR = Px(9f);
            drawList.AddCircleFilled(badgeCenter, badgeR, 0xFF1E1E24u, 24);
            drawList.AddCircle(badgeCenter, badgeR, UiColors.FavoriteStar, 24, Px(1.2f));
            // The star glyph's empty descent makes box-centring sit visually high; nudge down to compensate.
            IconDraw.AddCentered(drawList, FontAwesomeIcon.Star, badgeR * 1.2f,
                badgeCenter + new Vector2(0f, Px(0.5f)), UiColors.FavoriteStar);
        }
    }

    private static void DrawGradientRing(ImDrawListPtr dl, Vector2 center, float radius, float thickness,
        Vector4 colorA, Vector4 colorB)
    {
        const int segments = 96;
        var phase = (float)ImGui.GetTime() * 1.6f;
        var prev = center + new Vector2(radius, 0f);
        for (int i = 1; i <= segments; i++)
        {
            var ang = i / (float)segments * MathF.Tau;
            var pt = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * radius;
            var blend = 0.5f + 0.5f * MathF.Sin((i - 0.5f) / segments * MathF.Tau - phase);
            var col = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(colorA, colorB, blend));
            dl.AddLine(prev, pt, col, thickness);
            prev = pt;
        }
    }

    private void MarkChatListSeen()
    {
        _notifications.NewMatches = 0;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.MarkMatchListSeenAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[AetherLoveApp] MarkMatchListSeenAsync failed.");
            }
        });
    }
}
