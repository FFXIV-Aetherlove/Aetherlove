// Attribution: Derived from XIVInstantMessenger's ISegment
// Source: https://github.com/NightmareXIV/XIVInstantMessenger

namespace AetherLove.Emoji.Segments;

/// <summary>A single renderable unit inside a <see cref="ParsedMessage"/>.</summary>
public interface ISegment
{
    void Draw();
}
