using System;
using System.IO;
using System.Numerics;
using AetherLove.UI;
using AetherOS.PetKit.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

internal enum RacerTextSize
{
    Small,
    Caption,
    Body,
    Button,
    Heading,
}

internal sealed class GrandstandFrame(IRacerHost host, Action home, Action bonus, Action stats, Action help, Func<bool> muted, Action toggleMute, Func<float> volume, Action<float> setVolume)
{
    /// <summary>Draws the grandstand around <paramref name="content"/>. <paramref name="overlay"/> is drawn last, over
    /// the whole grandstand rather than inside the content box, so a popup centres on the app and its scrim covers
    /// the header and the footer too. <paramref name="overlayOpen"/> says one is up this frame, which stands the
    /// hand-hit-tested speaker chip down so a tap on the scrim cannot also reach it.</summary>
    public void Draw(OsAppContext ctx, int tab, string? title, Action content, bool paper = false, bool contentPage = false,
        Action? overlay = null, bool overlayOpen = false, string? backgroundName = null, uint? contentInk = null,
        float contentInset = 8f)
    {
        var avail = ImGui.GetContentRegionAvail();
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var body = ImRaii.Child("##grandstand", avail, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!body)
        {
            return;
        }

        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var defaultBackground = contentPage ? "grandstand-content-bg" : "grandstand-home-bg";
        Art(ctx, host, backgroundName ?? defaultBackground, origin, size, fallbackName: defaultBackground);
        DrawHeader(ctx, origin, size, contentPage, overlayOpen);
        var footerH = MathF.Min(Px(68), size.Y * .10f);
        var footer = origin + new Vector2(Px(8), size.Y - footerH - Px(10));
        var footerW = (size.X - Px(16)) / 3;
        Action[] actions = [home, bonus, stats];
        for (var i = 0; i < 3; i++)
        {
            var at = footer + new Vector2(i * footerW, 0);
            ImGui.SetCursorScreenPos(at);
            var clicked = ImGui.InvisibleButton($"##grandstandTab{i}", new Vector2(footerW, footerH));
            HandOnHover();
            HomeArtwork.Footer(ctx, host, i, tab == i, ImGui.IsItemHovered(), at, new Vector2(footerW, footerH));
            if (clicked)
            {
                actions[i]();
            }
        }

        var top = origin.Y + size.Y * (contentPage ? .15f : .38f);
        var horizontalInset = Px(MathF.Max(0f, contentInset));
        var boxAt = new Vector2(origin.X + horizontalInset, top);
        var boxSize = new Vector2(size.X - (horizontalInset * 2f), footer.Y - top - Px(9));
        var ink = contentInk ?? (paper ? Ink : Cream);
        if (paper)
        {
            Panel(ctx, host, "paper", boxAt, boxSize);
        }

        if (title is not null)
        {
            using var headingFont = RacerFonts.Get(RacerTextSize.Heading)?.Push();
            var label = ctx.Localize(title);
            var height = MathF.Max(Px(26), ImGui.CalcTextSize(label, false, boxSize.X - Px(28)).Y);
            Label(ctx, label, boxAt + new Vector2(Px(14), Px(12)), new Vector2(boxSize.X - Px(28), height), ink, RacerTextSize.Heading);
            boxAt.Y += height + Px(20);
            boxSize.Y -= height + Px(30);
        }

        if (paper)
        {
            boxAt.X += Px(12);
            boxSize.X -= Px(24);
        }

        ImGui.SetCursorScreenPos(boxAt);
        using var text = ImRaii.PushColor(ImGuiCol.Text, ink);
        using (var child = ImRaii.Child("##grandstandContent", boxSize, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar))
        {
            if (child)
            {
                content();
            }
        }

        overlay?.Invoke();
    }

    private void DrawHeader(OsAppContext ctx, Vector2 at, Vector2 size, bool compact, bool overlayOpen)
    {
        var width = size.X * (compact ? .37f : .59f);
        var logoSize = new Vector2(width, width / 1.5f);
        Art(ctx, host, "grandstand-logo", at + new Vector2(Px(10), Px(4)), logoSize);
        var dl = ImGui.GetWindowDrawList();
        var chipCenter = at + new Vector2(size.X - Px(25), Px(25));
        dl.AddCircleFilled(chipCenter, Px(18), 0xFF173357, 40);
        dl.AddCircle(chipCenter, Px(18), Gold, 40, Px(2));
        RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume, beside: true, interactive: !overlayOpen);
        var helpCenter = at + new Vector2(size.X - Px(25), Px(76));
        var radius = Px(15);
        ImGui.SetCursorScreenPos(helpCenter - new Vector2(radius));
        var pressed = ImGui.InvisibleButton("##racerHelp", new Vector2(radius * 2));
        var hovered = ImGui.IsItemHovered();
        HandOnHover();
        dl.AddCircleFilled(helpCenter, radius, hovered ? 0xFF63391C : 0xFF173357, 40);
        dl.AddCircle(helpCenter, radius, Gold, 40, Px(1.5f));
        Label(ctx, "?", helpCenter - new Vector2(radius), new Vector2(radius * 2), Cream, RacerTextSize.Button);
        if (hovered)
        {
            Cards.CardChrome.Tooltip(ctx.Localize("os.racer_intro_again"));
        }
        if (pressed)
        {
            help();
        }
    }

    private const float FigureStroke = 1f;
    public const uint Ink = 0xFF48220C;
    public const uint Cream = 0xFFE5F5FF;
    public const uint Gold = 0xFF79CEF7;
    public static void Art(OsAppContext ctx, IRacerHost host, string name, Vector2 at, Vector2 size, uint tint = 0xFFFFFFFF,
        string? fallbackName = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var art = ctx.Capabilities.Textures.Get(Path.Combine(host.PetAssetRoot, "racer", name + ".png"));
        if (art is null && fallbackName is not null)
        {
            art = ctx.Capabilities.Textures.Get(Path.Combine(host.PetAssetRoot, "racer", fallbackName + ".png"));
        }

        if (art is { } found)
        {
            dl.AddImage(found, at, at + size, Vector2.Zero, Vector2.One, tint);
        }
        else
        {
            dl.AddRectFilled(at, at + size, 0xFF3F2517, Px(8));
        }
    }

    public static void Panel(OsAppContext ctx, IRacerHost host, string color, Vector2 at, Vector2 size, uint tint = 0xFFFFFFFF)
    {
        var dl = ImGui.GetWindowDrawList();
        var path = Path.Combine(host.PetAssetRoot, "racer", "grandstand-" + color + ".png");
        if (ctx.Capabilities.Textures.Get(path)is not { } art)
        {
            dl.AddRectFilled(at, at + size, color == "paper" ? Cream : color == "red" ? 0xFF2728A7u : 0xFF562614u, Px(9));
            dl.AddRect(at, at + size, Gold, Px(9), ImDrawFlags.None, Px(2));
            return;
        }

        var texSize = ctx.Capabilities.Textures.GetSize(path) ?? new Vector2(1536, 1024);
        var edge = MathF.Min(Px(23), MathF.Min(size.X, size.Y) * .22f);
        float[] xs = [0, edge, size.X - edge, size.X];
        float[] ys = [0, edge, size.Y - edge, size.Y];
        float[] us = [0, .12f, .88f, 1];
        var v = .12f * texSize.X / texSize.Y;
        float[] vs = [0, v, 1 - v, 1];
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                dl.AddImage(art, at + new Vector2(xs[x], ys[y]), at + new Vector2(xs[x + 1], ys[y + 1]), new Vector2(us[x], vs[y]), new Vector2(us[x + 1], vs[y + 1]), tint);
            }
        }
    }

    public static bool Ticket(OsAppContext ctx, IRacerHost host, string id, Vector2 at, Vector2 size, string color, string label, FontAwesomeIcon icon, bool enabled = true, string? status = null)
    {
        ImGui.SetCursorScreenPos(at);
        var pressed = ImGui.InvisibleButton(id, size) && enabled;
        var hovered = ImGui.IsItemHovered();
        if (hovered && enabled)
        {
            HandOnHover();
        }

        Art(ctx, host, "grandstand-" + color, at, size, enabled ? 0xFFFFFFFF : 0xAAFFFFFF);

        var ink = color == "paper" ? Ink : Cream;
        var dl = ImGui.GetWindowDrawList();
        if (hovered && enabled)
        {
            dl.AddRect(at + new Vector2(Px(3)), at + size - new Vector2(Px(3)), Cream, Px(8), ImDrawFlags.None, Px(1.5f));
        }

        IconDraw.AddCentered(dl, icon, MathF.Min(Px(string.IsNullOrEmpty(status) ? 40 : 28), size.Y * .30f), at + new Vector2(size.X / 2, size.Y * (string.IsNullOrEmpty(status) ? .31f : .16f)), color == "paper" ? Ink : Gold);
        var waiting = !string.IsNullOrEmpty(status);
        WrappedLabel(ctx, label, at + new Vector2(Px(18), size.Y * (waiting ? .30f : .48f)),
            new Vector2(size.X - Px(36), size.Y * (waiting ? .30f : .40f)), ink, RacerTextSize.Button);
        if (waiting)
        {
            var statusAt = at + new Vector2(Px(12), size.Y * .62f);
            var statusSize = new Vector2(size.X - Px(24), size.Y * .26f);
            WrappedLabel(ctx, status!, statusAt + new Vector2(Px(5), 0), statusSize - new Vector2(Px(10), 0), Gold, RacerTextSize.Body);
        }
        return pressed;
    }

    public static bool ActionButton(OsAppContext ctx, IRacerHost host, string id, string label, bool enabled = true, string? reason = null, bool secondary = false, FontAwesomeIcon secondaryIcon = FontAwesomeIcon.ArrowLeft)
    {
        var width = ImGui.GetContentRegionAvail().X - Px(24);
        using var font = RacerFonts.Get(secondary ? RacerTextSize.Caption : RacerTextSize.Button)?.Push();
        var measured = ImGui.CalcTextSize(label, false, width - Px(56));
        var height = MathF.Max(Px(secondary ? 36 : 50), measured.Y + Px(20));
        ImGui.SetCursorPosX(Px(12));
        var at = ImGui.GetCursorScreenPos();
        var live = enabled && reason is null;
        var pressed = ImGui.InvisibleButton(id, new Vector2(width, height)) && live;
        var hovered = ImGui.IsItemHovered() && live;
        if (hovered)
        {
            HandOnHover();
        }
        var dl = ImGui.GetWindowDrawList();
        if (secondary)
        {
            dl.AddRectFilled(at, at + new Vector2(width, height), 0xE6422517, Px(6));
            dl.AddRect(at, at + new Vector2(width, height), hovered ? Gold : 0x6679CEF7, Px(6), ImDrawFlags.None, Px(1));
            IconDraw.AddCentered(dl, secondaryIcon, Px(12), at + new Vector2(Px(18), height / 2), Gold);
        }
        else
        {
            Panel(ctx, host, "red", at, new Vector2(width, height), live ? 0xFFFFFFFF : 0x99FFFFFF);
        }
        WrappedLabel(ctx, label, at + new Vector2(Px(28), Px(8)), new Vector2(width - Px(56), height - Px(16)), Cream,
            secondary ? RacerTextSize.Caption : RacerTextSize.Button);
        if (!string.IsNullOrEmpty(reason))
        {
            ImGui.Dummy(new Vector2(1, Px(5)));
            using var bodyFont = RacerFonts.Get(RacerTextSize.Body)?.Push();
            RacerChrome.CenteredWrapped(reason);
        }
        return pressed;
    }

    public static void Label(OsAppContext ctx, string text, Vector2 at, Vector2 size, uint ink, RacerTextSize textSize = RacerTextSize.Body, bool centered = true)
    {
        using var font = RacerFonts.Get(textSize)?.Push();
        var measured = ImGui.CalcTextSize(text);
        if (measured.X > size.X)
        {
            WrappedLabel(ctx, text, at, size, ink, textSize);
            return;
        }
        var pos = at + new Vector2(centered ? (size.X - measured.X) / 2 : 0, (size.Y - measured.Y) / 2);
        pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
        ImGui.GetWindowDrawList().AddText(pos, ink, text);
    }

    /// <summary>A headline count printed into a slot the artwork leaves blank: centred on <paramref name="centreX"/>,
    /// standing on <paramref name="baseline"/>, in the baked heading size and struck twice one pixel apart so it
    /// carries the weight of the heavy lettering printed beside it.</summary>
    public static void Figure(string text, float centreX, float baseline, uint ink)
    {
        using var font = RacerFonts.Get(RacerTextSize.Heading)?.Push();
        var stroke = MathF.Max(1f, MathF.Round(Px(FigureStroke)));
        var width = ImGui.CalcTextSize(text).X + stroke;
        var at = new Vector2(MathF.Round(centreX - (width * 0.5f)), MathF.Round(baseline - ImGui.GetFont().Ascent));
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(at, ink, text);
        dl.AddText(at + new Vector2(stroke, 0f), ink, text);
    }

    public static void WrappedLabel(OsAppContext ctx, string text, Vector2 at, Vector2 room, uint ink, RacerTextSize textSize = RacerTextSize.Body)
    {
        using var font = RacerFonts.Get(textSize)?.Push();
        var line = string.Empty;
        var lines = new System.Collections.Generic.List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (ImGui.CalcTextSize(next).X <= room.X)
                {
                    line = next;
                    continue;
                }
                if (line.Length > 0)
                {
                    lines.Add(line);
                    line = string.Empty;
                }
                var remainder = word;
                var hyphen = remainder.LastIndexOf('-');
                if (hyphen > 0 && ImGui.CalcTextSize(remainder[..(hyphen + 1)]).X <= room.X)
                {
                    lines.Add(remainder[..(hyphen + 1)]);
                    remainder = remainder[(hyphen + 1)..];
                }
                var pieces = System.Globalization.StringInfo.GetTextElementEnumerator(remainder);
                while (pieces.MoveNext())
                {
                    var character = pieces.GetTextElement();
                    if (line.Length > 0 && ImGui.CalcTextSize(line + character).X > room.X)
                    {
                        lines.Add(line);
                        line = string.Empty;
                    }
                    line += character;
                }
            }
            if (line.Length > 0)
            {
                lines.Add(line);
                line = string.Empty;
            }
        }
        if (lines.Count == 0)
        {
            return;
        }

        var h = ImGui.GetTextLineHeight();
        for (var i = 0; i < lines.Count; i++)
        {
            var width = ImGui.CalcTextSize(lines[i]).X;
            var pos = at + new Vector2((room.X - width) / 2, (room.Y - lines.Count * h) / 2 + i * h);
            pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
            ImGui.GetWindowDrawList().AddText(pos, ink, lines[i]);
        }
    }

    public static void Sparkles(OsAppContext ctx, Vector2 at, Vector2 size, int count = 28)
    {
        var dl = ImGui.GetWindowDrawList();
        var time = ctx.ReduceMotion ? 0f : (float)ImGui.GetTime();
        dl.PushClipRect(at, at + size, true);
        for (var i = 0; i < count; i++)
        {
            var phase = (i * .618034f + time * .16f) % 1;
            var x = (i * .754877f) % 1;
            var p = at + new Vector2(x * size.X, (1 - phase) * size.Y);
            var alpha = ctx.ReduceMotion ? .7f : MathF.Sin(phase * MathF.PI);
            var ink = ImGui.ColorConvertFloat4ToU32(new Vector4(1, .84f, .43f, alpha));
            var r = Px(1.5f + i % 3);
            dl.AddQuadFilled(p - new Vector2(0, r), p + new Vector2(r * .3f, 0), p + new Vector2(0, r), p - new Vector2(r * .3f, 0), ink);
            dl.AddQuadFilled(p - new Vector2(r, 0), p + new Vector2(0, r * .3f), p + new Vector2(r, 0), p - new Vector2(0, r * .3f), ink);
        }

        dl.PopClipRect();
    }
}
