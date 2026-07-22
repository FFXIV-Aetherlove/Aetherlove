// Attribution: Derived from XIVInstantMessenger's SegmentDoubleEmoji
// Source: https://github.com/NightmareXIV/XIVInstantMessenger

namespace AetherLove.Emoji.Segments;

/// <summary>An emoji that fills the entire message, rendered at 2x text-line-height.</summary>
public sealed class SegmentDoubleEmoji(string name) : SegmentEmoji(name)
{
    public override void Draw() => base.Draw(2f);
}
