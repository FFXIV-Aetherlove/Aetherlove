// Attribution: Derived from XIVInstantMessenger's PseudoMultilineInput.DrawEmojiPopup
// Source: https://github.com/NightmareXIV/XIVInstantMessenger

using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Emoji;

/// <summary>Emoji-picker popup widget. Shows emojis grouped by category; collapses to a flat
/// filtered grid when the user types in the search box.</summary>
public sealed class EmojiPickerPopup
{
    private const string PopupId = "##ALEmojiPicker";
    private static float GridSize => Px(30f);
    private static float PopupW => Px(260f);
    private static float PopupH => Px(280f);

    private string _search = "";
    private Action<string>? _onInsert;
    private string? _pendingInsert;

    private int _columns = 1;
    private float _gapX;

    /// <summary><paramref name="onInsert"/> receives the shortcode name without colons.</summary>
    public void Open(Action<string> onInsert)
    {
        _onInsert = onInsert;
        _search = "";
        ImGui.OpenPopup(PopupId);
    }

    public void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(6f), Px(6f)));
        ImGui.SetNextWindowSize(new Vector2(PopupW, PopupH), ImGuiCond.Always);
        using var popup = ImRaii.Popup(PopupId, ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoResize);
        ImGui.PopStyleVar();
        if (!popup.Success)
        {
            return;
        }

        ImGui.SetNextItemWidth(PopupW - ImGui.GetStyle().WindowPadding.X * 2f - Px(2f));
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }
        ImGui.InputTextWithHint("##emjsearch", Loc.T("common.emoji_search_hint"), ref _search, 64);
        ImGui.Spacing();

        var scrollH = PopupH - ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y - Px(4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 0f);
        using var scroll = ImRaii.Child("##emjgrid", new Vector2(0f, scrollH), false, ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();
        if (!scroll.Success)
        {
            return;
        }

        // Justify the grid: fit as many GridSize cells as the width allows, then spread the
        // leftover evenly between columns so each row fills edge-to-edge (no trailing right gap).
        var avail = ImGui.GetContentRegionAvail().X;
        var minGap = Px(3f);
        _columns = Math.Max(1, (int)MathF.Floor((avail + minGap) / (GridSize + minGap)));
        _gapX = _columns > 1 ? (avail - _columns * GridSize) / (_columns - 1) : 0f;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(_gapX, Px(3f)));

        if (_search.Length > 0)
        {
            DrawFiltered(_search);
        }
        else
        {
            DrawCategorized();
        }

        ImGui.PopStyleVar();

        if (_pendingInsert != null)
        {
            _onInsert?.Invoke(_pendingInsert);
            _pendingInsert = null;
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawCategorized()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var col = 0;

        foreach (var cat in EmojiCategories.All)
        {
            var headerPrinted = false;

            foreach (var name in cat.Names)
            {
                if (!seen.Add(name))
                {
                    continue;
                }

                var tex = Plugin.EmojiService.GetEmoji(name);
                if (tex == null)
                {
                    continue;
                }

                if (!headerPrinted)
                {
                    StartHeader(cat.Label, ref col);
                    headerPrinted = true;
                }

                DrawCell(name, tex, ref col);
            }
        }

        var otherPrinted = false;
        foreach (var (name, tex) in Plugin.EmojiService.All)
        {
            if (!seen.Add(name))
            {
                continue;
            }

            if (!otherPrinted)
            {
                StartHeader("Other", ref col);
                otherPrinted = true;
            }

            DrawCell(name, tex, ref col);
        }
    }

    private void DrawFiltered(string filter)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var col = 0;
        var any = false;

        foreach (var cat in EmojiCategories.All)
        {
            foreach (var name in cat.Names)
            {
                if (!seen.Add(name))
                {
                    continue;
                }

                if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tex = Plugin.EmojiService.GetEmoji(name);
                if (tex == null)
                {
                    continue;
                }

                DrawCell(name, tex, ref col);
                any = true;
            }
        }

        foreach (var (name, tex) in Plugin.EmojiService.All)
        {
            if (!seen.Add(name))
            {
                continue;
            }

            if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DrawCell(name, tex, ref col);
            any = true;
        }

        if (!any)
        {
            ImGui.TextColored(UiColors.Muted, Loc.T("common.emoji_none_found"));
        }
    }

    /// <summary>Drops to a fresh line (if mid-row) and prints a category label.</summary>
    private void StartHeader(string label, ref int col)
    {
        if (col != 0)
        {
            ImGui.NewLine();
            col = 0;
        }
        ImGui.TextColored(UiColors.Muted, label);
    }

    private void DrawCell(string name, ISharedImmediateTexture tex, ref int col)
    {
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
                _pendingInsert = name;
            }
        }

        col++;
        if (col >= _columns)
        {
            col = 0;
        }
        else
        {
            ImGui.SameLine();
        }
    }
}
