using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>The flat-battery takeover. Covers the whole phone screen whatever the user was doing, so the status
/// bar and home pill are suppressed while it is up and the app underneath keeps its state: tapping the grass
/// recharges and drops the user straight back where they were.</summary>
public static class FlatBatteryOverlay
{
    private const float FadeSeconds = 0.9f;

    private static float _fade;

    public static bool Active => BatteryService.IsEmpty;

    /// <summary>Resets the fade so the next flat battery plays the animation from the top.</summary>
    public static void Rearm()
    {
        _fade = 0f;
    }

    public static void Draw(Vector2 contentTL, Vector2 contentBR)
    {
        BatteryService.MarkEmptySeen();

        var dl = ImGui.GetWindowDrawList();
        var avail = contentBR - contentTL;
        var dt = ImGui.GetIO().DeltaTime;
        _fade = AccessibilityService.ReduceMotion ? 1f : MathF.Min(1f, _fade + dt / FadeSeconds);

        dl.PushClipRect(contentTL, contentBR, true);
        dl.AddRectFilled(contentTL, contentBR, OsDraw.Black(0.96f * _fade));
        if (_fade < 0.35f)
        {
            dl.PopClipRect();
            return;
        }
        var alpha = (_fade - 0.35f) / 0.65f;

        DrawBrokenBattery(dl, new Vector2(contentTL.X + avail.X * 0.5f, contentTL.Y + avail.Y * 0.32f), alpha);

        var grass = CustomIcons.Get("grass")?.GetWrapOrDefault();
        var grassH = Px(58f);
        var grassTop = contentBR.Y - grassH;

        var wrapW = avail.X - Px(48f);
        var textX = contentTL.X + Px(24f);

        var hint = Loc.T("os.battery_grass_hint");
        var hintSz = ImGui.CalcTextSize(hint, false, wrapW);
        var body = Loc.T("os.battery_grass_body");
        var bodySz = ImGui.CalcTextSize(body, false, wrapW);
        var note = Loc.T("os.battery_grass_settings_hint");
        var noteFontSz = ImGui.GetFontSize() * 0.85f;
        var noteSz = ImGui.CalcTextSize(note, false, wrapW) * 0.85f;

        var noteY = grassTop - noteSz.Y - Px(12f);
        var hintY = noteY - hintSz.Y - Px(8f);
        var bodyY = hintY - bodySz.Y - Px(10f);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textX, bodyY),
            OsDraw.White(0.92f * alpha), body, wrapWidth: wrapW);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textX, hintY),
            ImGui.ColorConvertFloat4ToU32(ThemeService.Current.AccentLight with { W = 0.95f * alpha }),
            hint, wrapWidth: wrapW);
        dl.AddText(ImGui.GetFont(), noteFontSz, new Vector2(textX, noteY),
            OsDraw.White(0.55f * alpha), note, wrapWidth: wrapW);

        // The grass strip is the only control on the screen, so it is also the way out.
        ImGui.SetCursorScreenPos(new Vector2(contentTL.X, grassTop - Px(8f)));
        var tapped = ImGui.InvisibleButton("##batteryGrass", new Vector2(avail.X, grassH + Px(22f)));
        SharedUiHelpers.HandOnHover();
        var lift = ImGui.IsItemHovered() ? Px(3f) : 0f;

        if (grass is { Width: > 0, Height: > 0 })
        {
            // The step is floored: a texture that reports a zero width would otherwise never advance x and hang
            // the render thread outright.
            var tileW = MathF.Max(Px(8f), grassH * (grass.Width / (float)grass.Height));
            for (var x = contentTL.X; x < contentBR.X; x += tileW)
            {
                var w = MathF.Min(tileW, contentBR.X - x);
                dl.AddImage(grass.Handle, new Vector2(x, grassTop - lift),
                    new Vector2(x + w, grassTop + grassH - lift), Vector2.Zero, new Vector2(w / tileW, 1f),
                    OsDraw.White(alpha));
            }
        }
        else
        {
            dl.AddRectFilled(new Vector2(contentTL.X, grassTop - lift + grassH * 0.4f),
                new Vector2(contentBR.X, grassTop + grassH - lift),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.62f, 0.28f, alpha)));
        }
        dl.PopClipRect();

        if (tapped)
        {
            BatteryService.Reset();
            _fade = 0f;
        }
    }

    /// <summary>A battery outline with a jagged split through it, in the same accent glow disc the onboarding
    /// hero badge uses. Drawn rather than shipped as art so it picks up the scale and the critical tint.</summary>
    private static void DrawBrokenBattery(ImDrawListPtr dl, Vector2 center, float alpha)
    {
        const float BadgeR = 38f;
        var glow = ImGui.ColorConvertFloat4ToU32(ThemeService.Current.Accent with { W = 0.13f * alpha });
        dl.AddCircleFilled(center, Px(BadgeR + 9f), glow, 48);

        var bodyW = Px(42f);
        var bodyH = Px(23f);
        var tl = center - new Vector2(bodyW * 0.5f, bodyH * 0.5f);
        var br = center + new Vector2(bodyW * 0.5f, bodyH * 0.5f);
        var shell = ImGui.ColorConvertFloat4ToU32(BatteryService.CriticalTint with { W = 0.92f * alpha });
        dl.AddRect(tl, br, shell, Px(5f), ImDrawFlags.RoundCornersAll, Px(2.2f));
        dl.AddRectFilled(new Vector2(br.X + Px(2f), center.Y - Px(5f)),
            new Vector2(br.X + Px(5f), center.Y + Px(5f)), shell, Px(1.5f));

        var x = center.X + Px(2f);
        dl.AddLine(new Vector2(x - Px(5f), tl.Y - Px(3f)), new Vector2(x + Px(2f), center.Y - Px(2f)), shell, Px(2.2f));
        dl.AddLine(new Vector2(x + Px(2f), center.Y - Px(2f)), new Vector2(x - Px(4f), center.Y + Px(3f)), shell, Px(2.2f));
        dl.AddLine(new Vector2(x - Px(4f), center.Y + Px(3f)), new Vector2(x + Px(3f), br.Y + Px(3f)), shell, Px(2.2f));
    }
}
