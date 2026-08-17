using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Apps.Timers.Schedule;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Timers;

/// <summary>The countdown-ring hero and the server-resets card.</summary>
public sealed partial class TimersApp
{
    private static readonly TimeSpan DailyCycle = TimeSpan.FromHours(24);

    private string _heroRemaining = "";
    private string _heroClock = "";
    private string _heroCaption = "";
    private float _heroFrac;
    private TimerRow[] _resetRows = [];

    private void BuildHeroView(DateTime utcNow)
    {
        var next = EorzeaSchedule.NextDailyReset(utcNow);
        var remaining = next - utcNow;
        _heroFrac = 1f - Math.Clamp((float)(remaining.TotalSeconds / DailyCycle.TotalSeconds), 0f, 1f);
        _heroRemaining = $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        _heroClock = DateTime.Now.ToString("HH:mm", _culture);
        _heroCaption = Loc.T("os.timers_hero_caption", ToLocal(next).ToString("HH:mm", _culture));
    }

    private void BuildResetRows(DateTime utcNow)
    {
        var daily = EorzeaSchedule.NextDailyReset(utcNow);
        var gc = EorzeaSchedule.NextGrandCompanyReset(utcNow);
        var weekly = EorzeaSchedule.NextWeeklyReset(utcNow);
        _resetRows =
        [
            ResetRow(FontAwesomeIcon.Sun, "os.timers_reset_daily", daily, utcNow, "##calrstDaily",
                ToLocal(daily).ToString("HH:mm", _culture)),
            ResetRow(FontAwesomeIcon.Flag, "os.timers_reset_gc", gc, utcNow, "##calrstGc",
                ToLocal(gc).ToString("HH:mm", _culture)),
            ResetRow(FontAwesomeIcon.CalendarWeek, "os.timers_reset_weekly", weekly, utcNow, "##calrstWeekly",
                ToLocal(weekly).ToString("ddd HH:mm", _culture)),
        ];
    }

    private TimerRow ResetRow(FontAwesomeIcon icon, string titleKey, DateTime whenUtc, DateTime utcNow,
        string calId, string sub)
    {
        var title = Loc.T(titleKey);
        return new TimerRow(icon, new Vector4(1f, 1f, 1f, 0.9f), title, sub,
            FormatCountdown(whenUtc - utcNow), UiColors.Body, calId, title, ToUnix(whenUtc));
    }

    private void DrawHero(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var winW = ImGui.GetWindowSize().X;
        var cardW = winW - Px(PadX) * 2f;
        var ringR = Px(64f);
        var thickness = Px(11f);
        var lineH = ImGui.GetTextLineHeight();
        var cardH = Px(18f) + ringR * 2f + Px(14f) + lineH + Px(14f);

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), OsDrawShared.White(0.05f), Px(CardRounding));

        var center = new Vector2(tl.X + cardW * 0.5f, tl.Y + Px(18f) + ringR);
        dl.AddCircle(center, ringR, OsDrawShared.White(0.08f), 96, thickness);
        var reveal = Reveal(ctx);
        StrokeArc(dl, center, ringR, thickness, -MathF.PI / 2f,
            -MathF.PI / 2f + MathF.Tau * _heroFrac * reveal, ImGui.GetColorU32(t.Accent));

        Vector2 bigSz;
        using (UiFonts.H1?.Push())
        {
            bigSz = ImGui.CalcTextSize(_heroRemaining);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                center - new Vector2(bigSz.X * 0.5f, bigSz.Y * 0.72f), ImGui.GetColorU32(UiColors.Body),
                _heroRemaining);
        }
        var clockSz = ImGui.CalcTextSize(_heroClock);
        dl.AddText(new Vector2(center.X - clockSz.X * 0.5f, center.Y + bigSz.Y * 0.34f),
            ImGui.GetColorU32(UiColors.Hint), _heroClock);

        var captionSz = ImGui.CalcTextSize(_heroCaption);
        dl.AddText(new Vector2(tl.X + (cardW - captionSz.X) * 0.5f, center.Y + ringR + Px(12f)),
            ImGui.ColorConvertFloat4ToU32(t.AccentLight), _heroCaption);

        ImGui.Dummy(new Vector2(0f, cardH + Px(10f)));
    }

    private void DrawResetsCard(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var winW = ImGui.GetWindowSize().X;
        var rowH = Px(RowHeight);
        var cardTL = BeginCard(dl, winW, _resetRows.Length * rowH, Loc.T("os.timers_resets_title"),
            out var cardW, out var cardH, out var y);

        for (var i = 0; i < _resetRows.Length; i++)
        {
            if (i > 0)
            {
                DrawHairline(dl, cardTL.X, y, cardW);
            }
            DrawTimerRow(dl, new Vector2(cardTL.X, y), cardW, rowH, in _resetRows[i]);
            y += rowH;
        }

        EndCard(cardTL, cardW, cardH);
    }

    private static void StrokeArc(ImDrawListPtr dl, Vector2 center, float radius, float thickness,
        float a0, float a1, uint color)
    {
        if (a1 - a0 < 0.001f)
        {
            return;
        }
        var segments = Math.Max(3, (int)(96 * (a1 - a0) / MathF.Tau));
        dl.PathClear();
        for (var i = 0; i <= segments; i++)
        {
            var a = a0 + (a1 - a0) * (i / (float)segments);
            dl.PathLineTo(new Vector2(center.X + MathF.Cos(a) * radius, center.Y + MathF.Sin(a) * radius));
        }
        dl.PathStroke(color, ImDrawFlags.None, thickness);
    }
}
