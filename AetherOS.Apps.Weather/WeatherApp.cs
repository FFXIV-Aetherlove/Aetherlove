using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Weather;

public sealed class WeatherApp : IAetherApp
{
    private const int MinutesPerDay = 1440;

    private static readonly Vector4 TileTopColor = new(0.35f, 0.68f, 0.98f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.10f, 0.28f, 0.62f, 1f);
    private static readonly Vector4 MutedText = new(1f, 1f, 1f, 0.55f);
    private static readonly Vector4 WhiteText = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 WarningText = new(1f, 0.72f, 0.35f, 1f);

    // Deliberately theme-neutral: the app colors itself by weather tints over a dark base.
    private static readonly Vector4 BaseCard = new(0.09f, 0.10f, 0.14f, 0.92f);
    private static readonly Vector4 GhostFill = new(1f, 1f, 1f, 0.08f);
    private static readonly Vector4 HoverFill = new(1f, 1f, 1f, 0.14f);
    private static readonly Vector4 CardBorder = new(1f, 1f, 1f, 0.10f);
    private static readonly Vector4 TimeAccent = new(0.45f, 0.62f, 0.95f, 1f);

    private readonly Func<string> name;
    private readonly IWeatherStation station;

    public WeatherApp(Func<string> name, IWeatherStation station)
    {
        this.name = name;
        this.station = station;
    }

    public string Id => "weather";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.CloudSun;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

    public bool HasSurface => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => AppStrings.Packs;

    public void Open()
    {
    }

    public void Draw(OsAppContext ctx)
    {
        ImGui.BeginChild("##weatherScroll", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.None);
        if (!this.station.InGame)
        {
            DrawNotInGame(ctx);
            ImGui.EndChild();
            return;
        }

        var pad = ctx.Px(14f);
        ImGui.Indent(pad);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ctx.Px(10f));
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));

        this.DrawHero(ctx, pad);
        this.DrawForecast(ctx, pad);
        this.DrawWeatherControl(ctx, pad);
        this.DrawTimeControl(ctx, pad);

        ImGui.Dummy(new Vector2(0f, ctx.Px(14f)));
        ImGui.PopStyleVar();
        ImGui.Unindent(pad);
        ImGui.EndChild();
    }

    private static void DrawNotInGame(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var center = origin + ImGui.GetContentRegionAvail() * 0.5f;
        AddIconCentered(dl, FontAwesomeIcon.CloudSun, ctx.Px(46f), center - new Vector2(0f, ctx.Px(26f)), ImGui.ColorConvertFloat4ToU32(MutedText));
        var text = ctx.Localize("os.weather_not_ingame");
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorScreenPos(new Vector2(center.X - size.X * 0.5f, center.Y + ctx.Px(8f)));
        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private void DrawHero(OsAppContext ctx, float pad)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X - pad;
        var height = ctx.Px(146f);
        var current = this.station.CurrentWeather;
        var tint = HeroTint(current?.IconKey ?? "clouds");
        var fill = Blend(BaseCard, tint, 0.38f);
        dl.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(fill), ctx.Px(18f));
        dl.AddRect(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(tint with { W = 0.55f }),
            ctx.Px(18f), ImDrawFlags.RoundCornersAll, ctx.Px(1.2f));

        var inset = ctx.Px(14f);
        ImGui.SetCursorScreenPos(pos + new Vector2(inset, ctx.Px(12f)));
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextUnformatted(this.station.ZoneName);
        }

        var iconSide = ctx.Px(64f);
        var iconTl = pos + new Vector2(inset, ctx.Px(44f));
        var iconCenter = iconTl + new Vector2(iconSide * 0.5f, iconSide * 0.5f);
        dl.AddCircleFilled(iconCenter, iconSide * 0.64f, ImGui.ColorConvertFloat4ToU32(tint with { W = 0.28f }), 40);
        DrawWeatherIcon(dl, current, iconTl, iconSide);

        using (ctx.TitleFont?.Push())
        {
            var label = current?.Name ?? string.Empty;
            var size = ImGui.CalcTextSize(label);
            ImGui.SetCursorScreenPos(new Vector2(iconTl.X + iconSide + ctx.Px(12f), iconTl.Y + (iconSide - size.Y) * 0.5f));
            ImGui.TextUnformatted(label);
        }

        var et = this.station.EorzeaTime;
        var etLine = string.Create(CultureInfo.InvariantCulture, $"{ctx.Localize("os.weather_eorzea_time")}  {et.Hours:00}:{et.Minutes:00}");
        ImGui.SetCursorScreenPos(new Vector2(pos.X + inset, pos.Y + height - ctx.Px(30f)));
        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.TextUnformatted(etLine);
        ImGui.PopStyleColor();

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
    }

    private void DrawForecast(OsAppContext ctx, float pad)
    {
        DrawSectionHeading(ctx, ctx.Localize("os.weather_forecast"));
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X - pad;
        var height = ctx.Px(42f);
        var iconSide = ctx.Px(28f);
        var inset = ctx.Px(12f);
        var entries = this.station.Forecast(7);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var pos = ImGui.GetCursorScreenPos();
            var tint = HeroTint(entry.Weather.IconKey);
            var fill = entry.Active ? Blend(BaseCard, tint, 0.34f) : GhostFill;
            dl.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(fill), ctx.Px(12f));
            if (entry.Active)
            {
                dl.AddRect(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(tint with { W = 0.75f }), ctx.Px(12f), ImDrawFlags.RoundCornersAll, ctx.Px(1.2f));
            }

            var label = i == 0 ? ctx.Localize("os.weather_now") : entry.StartLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            var labelSize = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(pos.X + inset, pos.Y + (height - labelSize.Y) * 0.5f), ImGui.GetColorU32(ImGuiCol.Text), label);

            var iconTl = new Vector2(pos.X + width * 0.44f - iconSide * 0.5f, pos.Y + (height - iconSide) * 0.5f);
            DrawWeatherIcon(dl, entry.Weather, iconTl, iconSide);

            var nameSize = ImGui.CalcTextSize(entry.Weather.Name);
            dl.AddText(new Vector2(pos.X + width - inset - nameSize.X, pos.Y + (height - nameSize.Y) * 0.5f), ImGui.GetColorU32(ImGuiCol.Text), entry.Weather.Name);

            ImGui.Dummy(new Vector2(width, height));
            ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        }
    }

    private void DrawWeatherControl(OsAppContext ctx, float pad)
    {
        DrawSectionHeading(ctx, ctx.Localize("os.weather_control"));
        var width = ImGui.GetContentRegionAvail().X - pad;
        if (!this.station.CanMutate)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, WarningText);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextUnformatted(ctx.Localize("os.weather_combat"));
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            return;
        }

        var gap = ctx.Px(8f);
        var chipSize = new Vector2((width - gap) * 0.5f, ctx.Px(38f));
        var weathers = this.station.ZoneWeathers;
        var overrideId = this.station.WeatherOverride;
        for (var i = 0; i < weathers.Count; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(0f, gap);
            }
            this.DrawWeatherChip(ctx, weathers[i], overrideId == weathers[i].Id, chipSize);
        }

        if (overrideId != null)
        {
            ImGui.Dummy(new Vector2(0f, ctx.Px(2f)));
            if (ThemedButton(ctx, $"{ctx.Localize("os.weather_reset")}###weatherReset", new Vector2(width, ctx.Px(34f))))
            {
                this.station.ClearWeatherOverride();
            }
        }
    }

    private void DrawWeatherChip(OsAppContext ctx, WeatherInfo weather, bool selected, Vector2 size)
    {
        var tint = HeroTint(weather.IconKey);
        var normal = selected ? Blend(BaseCard, tint, 0.42f) : GhostFill;
        var hovered = selected ? Blend(BaseCard, tint, 0.52f) : HoverFill;
        ImGui.PushStyleColor(ImGuiCol.Button, normal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, hovered);
        var clicked = ImGui.Button($"###weatherChip{weather.Id}", size);
        ImGui.PopStyleColor(3);

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(selected ? tint with { W = 0.85f } : CardBorder),
            ctx.Px(10f), ImDrawFlags.RoundCornersAll, selected ? ctx.Px(1.6f) : 1f);

        var iconSide = ctx.Px(24f);
        var inset = ctx.Px(8f);
        var iconTl = new Vector2(min.X + inset, min.Y + (size.Y - iconSide) * 0.5f);
        var badgeC = iconTl + new Vector2(iconSide * 0.5f, iconSide * 0.5f);
        dl.AddCircleFilled(badgeC, iconSide * 0.62f, ImGui.ColorConvertFloat4ToU32(tint with { W = 0.30f }), 32);
        DrawWeatherIcon(dl, weather, iconTl, iconSide);

        dl.PushClipRect(min, max, true);
        var nameSize = ImGui.CalcTextSize(weather.Name);
        dl.AddText(new Vector2(iconTl.X + iconSide + inset, min.Y + (size.Y - nameSize.Y) * 0.5f),
            ImGui.ColorConvertFloat4ToU32(selected ? WhiteText : WhiteText with { W = 0.85f }), weather.Name);
        dl.PopClipRect();

        if (clicked)
        {
            this.station.SetWeatherOverride(weather.Id);
        }
    }

    private void DrawTimeControl(OsAppContext ctx, float pad)
    {
        if (!this.station.CanMutate)
        {
            return;
        }
        DrawSectionHeading(ctx, ctx.Localize("os.weather_time"));
        var width = ImGui.GetContentRegionAvail().X - pad;

        var minutes = this.station.TimeOverrideMinutes ?? (int)this.station.EorzeaTime.TotalMinutes % MinutesPerDay;
        // ImGui prints a format string with no % tokens literally, so the precomputed HH:mm is the slider text
        var sliderText = string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}");
        ImGui.PushStyleColor(ImGuiCol.FrameBg, GhostFill);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, HoverFill);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, HoverFill);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, TimeAccent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, TimeAccent with { X = 0.58f, Y = 0.72f });
        ImGui.SetNextItemWidth(width);
        var edited = minutes;
        if (ImGui.SliderInt("##weatherTimeSlider", ref edited, 0, MinutesPerDay - 1, sliderText))
        {
            this.station.SetTimeOverride(edited);
        }
        ImGui.PopStyleColor(5);

        var gap = ctx.Px(8f);
        var presetSize = new Vector2((width - gap * 3f) / 4f, ctx.Px(30f));
        var presets = new (string Key, int Minutes)[]
        {
            ("os.weather_sunrise", 360),
            ("os.weather_noon", 720),
            ("os.weather_sunset", 1080),
            ("os.weather_midnight", 0),
        };
        for (var i = 0; i < presets.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0f, gap);
            }
            if (ThemedButton(ctx, $"{ctx.Localize(presets[i].Key)}###timePreset{i}", presetSize))
            {
                this.station.SetTimeOverride(presets[i].Minutes);
            }
        }

        if (this.station.TimeOverrideMinutes != null)
        {
            ImGui.Dummy(new Vector2(0f, ctx.Px(2f)));
            if (ThemedButton(ctx, $"{ctx.Localize("os.weather_time_reset")}###timeReset", new Vector2(width, ctx.Px(34f))))
            {
                this.station.ClearTimeOverride();
            }
        }
    }

    /// <summary>Signature color per icon family; the hero card blends it over the theme chip fill.</summary>
    private static Vector4 HeroTint(string iconKey) => iconKey switch
    {
        "clear" => new Vector4(0.95f, 0.68f, 0.25f, 1f),
        "fair" => new Vector4(0.42f, 0.68f, 0.95f, 1f),
        "fog" => new Vector4(0.55f, 0.57f, 0.62f, 1f),
        "wind" => new Vector4(0.35f, 0.75f, 0.70f, 1f),
        "gales" => new Vector4(0.20f, 0.62f, 0.60f, 1f),
        "rain" => new Vector4(0.30f, 0.50f, 0.85f, 1f),
        "showers" => new Vector4(0.22f, 0.40f, 0.78f, 1f),
        "thunder" => new Vector4(0.42f, 0.38f, 0.85f, 1f),
        "thunderstorm" => new Vector4(0.32f, 0.28f, 0.72f, 1f),
        "levin" => new Vector4(0.85f, 0.25f, 0.40f, 1f),
        "dust" => new Vector4(0.72f, 0.60f, 0.38f, 1f),
        "sandstorm" => new Vector4(0.80f, 0.58f, 0.25f, 1f),
        "heat" => new Vector4(0.92f, 0.42f, 0.20f, 1f),
        "snow" => new Vector4(0.68f, 0.80f, 0.92f, 1f),
        "blizzard" => new Vector4(0.52f, 0.70f, 0.88f, 1f),
        "gloom" => new Vector4(0.38f, 0.35f, 0.48f, 1f),
        "darkness" => new Vector4(0.25f, 0.20f, 0.38f, 1f),
        "ominous" => new Vector4(0.48f, 0.28f, 0.32f, 1f),
        "aurora" => new Vector4(0.35f, 0.80f, 0.60f, 1f),
        "eruption" => new Vector4(0.88f, 0.30f, 0.12f, 1f),
        "radiation" => new Vector4(0.72f, 0.82f, 0.22f, 1f),
        "quake" => new Vector4(0.60f, 0.45f, 0.28f, 1f),
        "waves" => new Vector4(0.18f, 0.48f, 0.72f, 1f),
        "arcane" => new Vector4(0.62f, 0.35f, 0.85f, 1f),
        _ => new Vector4(0.45f, 0.52f, 0.62f, 1f),
    };

    private static Vector4 Blend(Vector4 a, Vector4 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W);

    private static void DrawWeatherIcon(ImDrawListPtr dl, WeatherInfo? weather, Vector2 tl, float side)
    {
        if (weather?.Icon is { } tex)
        {
            dl.AddImage(tex, tl, tl + new Vector2(side, side));
            return;
        }
        AddIconCentered(dl, FontAwesomeIcon.Cloud, side * 0.78f, tl + new Vector2(side * 0.5f, side * 0.5f), ImGui.GetColorU32(ImGuiCol.Text));
    }

    private static void AddIconCentered(ImDrawListPtr dl, FontAwesomeIcon icon, float px, Vector2 center, uint col)
    {
        var glyph = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(glyph) * (px / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), px, center - size * 0.5f, col, glyph);
        ImGui.PopFont();
    }

    private static void DrawSectionHeading(OsAppContext ctx, string text)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextUnformatted(text);
        }
        ImGui.Dummy(new Vector2(0f, ctx.Px(2f)));
    }

    private static bool ThemedButton(OsAppContext ctx, string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, GhostFill);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, HoverFill);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, GhostFill);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.ColorConvertFloat4ToU32(CardBorder),
            ctx.Px(10f), ImDrawFlags.RoundCornersAll, 1f);
        return clicked;
    }

    public void OnIntent(OsIntent intent)
    {
    }
}
