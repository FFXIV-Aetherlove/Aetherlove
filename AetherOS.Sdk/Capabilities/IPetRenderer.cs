using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Sdk;

/// <summary>Draws an Aetherling for a surface that is not the Aetherling app. Apps never reference each
/// other, so the pet app parks one of these on its host at startup and another app reaches it through its
/// own host bridge. A creature is named by a growth rung, a palette and a list of worn refs, never by an
/// asset name; the renderer keeps one runtime per <c>key</c> so the same creature animates across frames.</summary>
public interface IPetRenderer
{
    /// <summary>Draws a creature that is not the player's own, feet at <paramref name="bottomCentre"/>.
    /// <paramref name="shell"/> is the worn shell ref off the wire, empty for the trueform.</summary>
    void Draw(ImDrawListPtr dl, Guid key, Vector2 bottomCentre, float size,
        short stage, string palette, IReadOnlyList<string> accessories, bool reduceMotion,
        string shell = "");

    /// <summary>Draws the player's own creature as it is right now. False when there is none to draw.</summary>
    bool DrawOwn(ImDrawListPtr dl, Vector2 bottomCentre, float size, bool reduceMotion);

    /// <summary>Drops the runtimes behind keys a surface no longer draws.</summary>
    void Forget(IReadOnlyCollection<Guid> keep);
}
