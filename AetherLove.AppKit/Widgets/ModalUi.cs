using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Shared building blocks for modal bodies: the standard icon/title header and a themed
/// full-width button.</summary>
internal static class ModalUi
{
    /// <summary>Centering is cursor-relative so it lines up inside padded panels.</summary>
    internal static void Header(float availW, string title, Vector4 accent)
    {
        // Taken before the title, because drawing text ends the line and puts the cursor back at the window's
        // content left. For a panel that is only a rect painted on a bigger window, that is nowhere near it.
        var left = ImGui.GetCursorScreenPos().X;

        using (UiFonts.H3?.Push())
        {
            var titleSz = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availW - titleSz.X) * 0.5f);
            ImGui.TextColored(accent, title);
        }
        ImGui.Spacing();

        // Not ImGui.Separator: that spans the WINDOW, so the store's sheets got a rule running the whole
        // screen behind the card. The width and the left edge are both the panel's.
        var thickness = System.MathF.Max(1f, Px(1f));
        var y = ImGui.GetCursorScreenPos().Y;
        ImGui.GetWindowDrawList().AddLine(new Vector2(left, y), new Vector2(left + availW, y),
            ImGui.GetColorU32(accent with { W = 0.35f }), thickness);
        ImGui.Dummy(new Vector2(availW, thickness));
        ImGui.Spacing();
    }

    internal static void Header(float availW, FontAwesomeIcon icon, string title, Vector4 accent)
    {
        var iconPx = Px(40f);
        var iconSz = IconDraw.Measure(icon, iconPx);
        var origin = ImGui.GetCursorScreenPos();
        IconDraw.Add(ImGui.GetWindowDrawList(), icon, iconPx,
            new Vector2(origin.X + (availW - iconSz.X) * 0.5f, origin.Y), ImGui.GetColorU32(accent));
        ImGui.Dummy(new Vector2(availW, iconSz.Y));
        ImGui.Spacing();

        Header(availW, title, accent);
    }

    internal static bool Button(string label, float width)
    {
        var t = ThemeService.Current;
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var clicked = ImGui.Button(label, new Vector2(width, Px(32f)));
        SharedUiHelpers.HandOnHover();
        ImGui.PopStyleVar();
        PopThemeButton();
        return clicked;
    }

    /// <summary>The circular top-right close/back button used by overlay panels: a translucent dark circle with a
    /// Times (or, when <paramref name="back"/>, an ArrowLeft) glyph that brightens on hover, with the pointer-hand
    /// cursor and an optional resolved <paramref name="tooltip"/>. Drawn at the right edge of the current content
    /// region (<paramref name="width"/>); returns true on click and leaves the cursor where it started.</summary>
    internal static bool CloseButton(string id, float width, bool back = false, string? tooltip = null)
    {
        var btnSize = Px(32f);
        var saveCursor = ImGui.GetCursorScreenPos();
        var btnTL = new Vector2(saveCursor.X + width - btnSize, saveCursor.Y);

        ImGui.SetCursorScreenPos(btnTL);
        ImGui.InvisibleButton(id, new Vector2(btnSize, btnSize));
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
            if (tooltip is not null)
            {
                ImGui.SetTooltip(tooltip);
            }
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddCircleFilled(btnTL + new Vector2(btnSize * 0.5f), btnSize * 0.5f, hovered ? 0x99000000u : 0x66000000u);
        var iconColor = hovered ? 0xFFFFFFFFu : 0xFFB4B4B4u;
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var icon = (back ? FontAwesomeIcon.ArrowLeft : FontAwesomeIcon.Times).ToIconString();
        var iconSz = ImGui.CalcTextSize(icon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), btnTL + (new Vector2(btnSize) - iconSz) * 0.5f, iconColor, icon);
        ImGui.PopFont();

        ImGui.SetCursorScreenPos(saveCursor);
        return clicked;
    }
}
