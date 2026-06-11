// Attribution: Derived from XIVInstantMessenger's SegmentEmoji
// Source: https://github.com/NightmareXIV/XIVInstantMessenger

using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Emoji.Segments;

/// <summary>An inline emoji segment rendered at sizeMult times the text line height.</summary>
public class SegmentEmoji : ISegment
{
    public readonly string EmojiName;

    public SegmentEmoji(string name)
    {
        EmojiName = name ?? throw new ArgumentNullException(nameof(name));
    }

    public virtual void Draw() => Draw(1f);

    protected void Draw(float sizeMult)
    {
        var lineH = ImGui.GetTextLineHeight();
        var size = new Vector2(MathF.Floor(lineH * sizeMult));

        // No leading SameLine: the preceding text/space segment owns the gap before us. Adding one here
        // would clobber it (two consecutive SameLine calls — last wins), eating a space typed before the emoji.
        if (ImGui.GetContentRegionAvail().X < size.X)
        {
            ImGui.NewLine();
        }

        // Texture loads async, so the wrap can be null for the first frame(s). Reserve the glyph
        // box regardless, so the measured height stays correct (else a 2x emoji clips once loaded).
        var tex = Plugin.EmojiService.GetEmoji(EmojiName)?.GetWrapOrDefault();
        if (tex == null)
        {
            ImGui.Dummy(size);
        }
        else
        {
            ImGui.Image(tex.Handle, size);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($":{EmojiName}:");
            }
        }

        ImGui.SameLine(0, sizeMult > 1f ? 0 : 2);
    }
}
