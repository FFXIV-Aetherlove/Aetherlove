using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>The shared look of an app's settings page: an accent section label and a card carrying a label,
/// a hint and a checkbox. The design comes from Photos, which keeps its own copy because it is a pure-SDK
/// app and cannot reference AppKit; every AppKit-linked app's settings page should use this one.</summary>
internal static class AppSettingsUi
{
    private static readonly Vector4 GhostFill = new(1f, 1f, 1f, 0.08f);
    private static readonly Vector4 CardBorder = new(1f, 1f, 1f, 0.10f);
    private static readonly Vector4 MutedText = new(1f, 1f, 1f, 0.62f);

    public static void SectionLabel(OsAppContext ctx, float x, string text)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, ImGui.GetCursorScreenPos().Y + ctx.Px(4f)));
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.AccentLight);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
    }

    /// <summary>A card with a wrapped label plus hint on the left and a checkbox on the right; the whole card
    /// toggles. Returns true on the frame the value changed.</summary>
    public static bool SettingToggle(OsAppContext ctx, string id, string label, string hint, float x, float width,
        ref bool value)
    {
        var dl = ImGui.GetWindowDrawList();
        var padIn = ctx.Px(12f);
        var boxSide = ImGui.GetFrameHeight();
        var textW = width - padIn * 2f - boxSide - ctx.Px(12f);
        var labelH = ImGui.CalcTextSize(label, false, textW).Y;
        var hintH = ImGui.CalcTextSize(hint, false, textW).Y;
        var tl = new Vector2(x, ImGui.GetCursorScreenPos().Y);
        var size = new Vector2(width, padIn + labelH + ctx.Px(4f) + hintH + padIn);
        var br = tl + size;

        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(GhostFill), ctx.Px(12f));
        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(CardBorder), ctx.Px(12f), ImDrawFlags.RoundCornersAll, 1f);

        ImGui.SetCursorScreenPos(new Vector2(br.X - padIn - boxSide, tl.Y + (size.Y - boxSide) * 0.5f));
        var changed = ImGui.Checkbox($"##{id}", ref value);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        // Submitted after the checkbox, so the box keeps its own clicks and this only catches the rest of the card.
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##{id}Row", size))
        {
            value = !value;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        ImGui.SetCursorScreenPos(tl + new Vector2(padIn, padIn));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        ImGui.TextUnformatted(label);
        ImGui.SetCursorScreenPos(new Vector2(tl.X + padIn, tl.Y + padIn + labelH + ctx.Px(4f)));
        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.TextUnformatted(hint);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();

        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y));
        ImGui.Dummy(new Vector2(width, ctx.Px(10f)));
        return changed;
    }
}
