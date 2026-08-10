using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>Shared draw helpers for the match-effect screens; animated effects take a time-based phase.</summary>
internal static class MatchFx
{
    public static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public static Vector4 Rgba(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    /// <summary>Recolours text vertices added since <paramref name="vtxStart"/> with a horizontal
    /// gradient across [minX, maxX], scrolled by <paramref name="phase"/>.</summary>
    public static void GradientText(ImDrawListPtr dl, int vtxStart, float minX, float maxX,
        Vector4 a, Vector4 b, float phase)
    {
        var span = MathF.Max(1f, maxX - minX);
        for (int v = vtxStart; v < dl.VtxBuffer.Size; v++)
        {
            var vert = dl.VtxBuffer[v];
            var blend = 0.5f + 0.5f * MathF.Sin((vert.Pos.X - minX) / span * MathF.Tau - phase);
            vert.Col = U32(Vector4.Lerp(a, b, blend));
            dl.VtxBuffer[v] = vert;
        }
    }

    /// <summary>Strokes a circular ring whose colour sweeps <paramref name="a"/>→<paramref name="b"/>
    /// around the circumference and rotates by <paramref name="phase"/>.</summary>
    public static void GradientRing(ImDrawListPtr dl, Vector2 center, float radius, float thickness,
        Vector4 a, Vector4 b, float phase, int segments = 96)
    {
        var prev = center + new Vector2(radius, 0f);
        for (int i = 1; i <= segments; i++)
        {
            var ang = i / (float)segments * MathF.Tau;
            var pt = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * radius;
            var blend = 0.5f + 0.5f * MathF.Sin((i - 0.5f) / segments * MathF.Tau - phase);
            dl.AddLine(prev, pt, U32(Vector4.Lerp(a, b, blend)), thickness);
            prev = pt;
        }
    }

    /// <summary>Draws the logo_mini avatar (or a fallback circle) at <paramref name="center"/>, with an
    /// optional solid rim and the wearer's equipped ring on top.</summary>
    public static void Avatar(ImDrawListPtr dl, Vector2 center, float radius, ISharedImmediateTexture? tex,
        uint rim, float rimThickness, string? frameRef = null)
    {
        var wrap = tex?.GetWrapOrDefault();
        var tl = center - new Vector2(radius, radius);
        var br = center + new Vector2(radius, radius);
        if (wrap != null)
        {
            dl.AddImageRounded(wrap.Handle, tl, br, Vector2.Zero, Vector2.One, 0xFFFFFFFF,
                radius, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(center, radius, UiColors.AvatarFallback);
        }
        if (rimThickness > 0f)
        {
            dl.AddCircle(center, radius, rim, 64, rimThickness);
        }
        // Effects pass MatchContent.OwnAvatar/PeerAvatar (directly or via a local), so texture identity
        // resolves whose ring this is without threading a ref through all 21 call sites.
        if (frameRef is null && wrap != null)
        {
            if (ReferenceEquals(tex, MatchContent.OwnAvatar))
            {
                frameRef = MatchContent.OwnFrameRef;
            }
            else if (ReferenceEquals(tex, MatchContent.PeerAvatar))
            {
                frameRef = MatchContent.PeerFrameRef;
            }
        }
        AvatarRings.Draw(dl, center, radius, frameRef);
    }

    /// <summary>Draws <paramref name="text"/> horizontally centred on <paramref name="cx"/> in the
    /// current font.</summary>
    public static void CenterText(ImDrawListPtr dl, float cx, float y, string text, uint col)
    {
        var w = ImGui.CalcTextSize(text).X;
        dl.AddText(new Vector2(cx - w * 0.5f, y), col, text);
    }

    /// <summary>Shared match actions pinned near the bottom; every match effect calls this so the actions
    /// stay consistent.</summary>
    public static void DrawActionButtons(LoveRouter router, Vector2 pos, Vector2 size, float alpha = 1f)
    {
        var t = ThemeService.Current;
        var btnW = Px(150f);
        var btnH = Px(38f);
        var gap = Px(12f);
        var cx = pos.X + size.X * 0.5f;
        var btnY = pos.Y + size.Y - Px(56f);

        var accent = WithA(t.ButtonNormal, alpha);
        var accentHov = WithA(t.ButtonHovered, alpha);
        var neutral = WithA(new Vector4(0.22f, 0.22f, 0.22f, 1f), alpha);
        var neutralHov = WithA(new Vector4(0.34f, 0.34f, 0.34f, 1f), alpha);

        if (IconButton("##matchBackSwipe", Loc.T("deck.match_keep_swiping"), FontAwesomeIcon.ArrowLeft,
                neutral, neutralHov, new Vector2(cx - btnW - gap * 0.5f, btnY), new Vector2(btnW, btnH)))
        {
            router.Navigate(LoveView.Deck);
        }
        if (IconButton("##matchStartChat", Loc.T("deck.match_start_chatting"), FontAwesomeIcon.Comments,
                accent, accentHov, new Vector2(cx + gap * 0.5f, btnY), new Vector2(btnW, btnH)))
        {
            router.Navigate(LoveView.ChatList);
        }
    }

    private static Vector4 WithA(Vector4 c, float a) => new(c.X, c.Y, c.Z, c.W * a);

    private static bool IconButton(string id, string label, FontAwesomeIcon icon, Vector4 bg, Vector4 bgHover,
        Vector2 buttonPos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(buttonPos);
        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bgHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, bg);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        var clicked = ImGui.Button(id, size);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var fontSize = ImGui.GetFontSize();
        var textFont = ImGui.GetFont();

        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var iconFont = ImGui.GetFont();
        var iconStr = icon.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        ImGui.PopFont();

        var textSz = ImGui.CalcTextSize(label);
        var g = Px(6f);
        var totalW = iconSz.X + g + textSz.X;
        var startX = min.X + (size.X - totalW) * 0.5f;
        var cy = min.Y + size.Y * 0.5f;
        dl.AddText(iconFont, fontSize, new Vector2(startX, cy - iconSz.Y * 0.5f), 0xFFFFFFFFu, iconStr);
        dl.AddText(textFont, fontSize, new Vector2(startX + iconSz.X + g, cy - textSz.Y * 0.5f), 0xFFFFFFFFu, label);
        return clicked;
    }
}
