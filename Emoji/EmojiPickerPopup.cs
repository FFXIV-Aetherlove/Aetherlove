// Attribution: Derived from XIVInstantMessenger's PseudoMultilineInput.DrawEmojiPopup
// Source: https://github.com/NightmareXIV/XIVInstantMessenger

using System;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Emoji;

/// <summary>Emoji-picker popup widget.</summary>
public sealed class EmojiPickerPopup
{
    private const string PopupId = "##ALEmojiPicker";
    private static float GridSize => Px(30f);
    private static float PopupW => Px(260f);
    private static float PopupH => Px(240f);

    private string _search = "";
    private Action<string>? _onInsert;

    /// <summary><paramref name="onInsert"/> receives the shortcode name without colons.</summary>
    public void Open(Action<string> onInsert)
    {
        _onInsert = onInsert;
        _search = "";
        ImGui.OpenPopup(PopupId);
    }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(PopupW, PopupH), ImGuiCond.Always);
        using var popup = ImRaii.Popup(PopupId, ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoResize);
        if (!popup.Success)
        {
            return;
        }

        var t = ThemeService.Current;

        ImGui.SetNextItemWidth(PopupW - ImGui.GetStyle().WindowPadding.X * 2f - Px(2f));
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }
        ImGui.InputTextWithHint("##emjsearch", Loc.T("common.emoji_search_hint"), ref _search, 64);
        ImGui.Spacing();

        var scrollH = PopupH - ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y - Px(4f);
        using var scroll = ImRaii.Child("##emjgrid", new Vector2(0f, scrollH), false,
            ImGuiWindowFlags.NoScrollbar);
        if (!scroll.Success)
        {
            return;
        }

        var avail = ImGui.GetContentRegionAvail().X;
        var filter = _search;

        var any = false;
        foreach (var (name, tex) in Plugin.EmojiService.All)
        {
            if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var wrap = tex.GetWrapOrDefault();

            if (wrap != null)
            {
                ImGui.Image(wrap.Handle, new Vector2(GridSize));
            }
            else
            {
                ImGui.Dummy(new Vector2(GridSize));
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip($":{name}:");

                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    _onInsert?.Invoke(name);
                    ImGui.CloseCurrentPopup();
                    break;
                }
            }

            any = true;

            ImGui.SameLine();
            if (ImGui.GetContentRegionAvail().X < GridSize)
            {
                ImGui.NewLine();
            }
        }

        if (!any)
        {
            ImGui.TextColored(UiColors.Muted, Loc.T("common.emoji_none_found"));
        }
    }
}
