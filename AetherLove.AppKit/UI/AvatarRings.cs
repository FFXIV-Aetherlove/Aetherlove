using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.UI;

/// <summary>Draws the equipped avatar ring around a circular avatar. The plugin installs the resolver at
/// boot (the <c>UiHost.SetEmojiService</c> pattern), so every app, AppKit itself and the Shell can call
/// <see cref="Draw"/> without referencing Core. Call it after the avatar image and any plain rim, BEFORE
/// badges (supporter star, away dot, unread), so status markers stay on top of the fashion.</summary>
public static class AvatarRings
{
    /// <summary>Ring art is a square drawn at this multiple of the avatar diameter; the transparent hole
    /// in the asset is sized so the avatar exactly fills it.</summary>
    public const float Overhang = 1.3f;

    private static Func<string, ISharedImmediateTexture?>? _resolve;

    public static void Install(Func<string, ISharedImmediateTexture?> resolve)
    {
        _resolve = resolve;
    }

    /// <summary>No-op while the ref is null/empty, the resolver is not installed, or the art is still
    /// loading, so call sites never need a guard.</summary>
    public static void Draw(ImDrawListPtr dl, Vector2 center, float radius, string? frameRef)
    {
        if (string.IsNullOrEmpty(frameRef) || _resolve is null)
        {
            return;
        }
        var wrap = _resolve(frameRef)?.GetWrapOrDefault();
        if (wrap is null)
        {
            return;
        }
        var half = new Vector2(radius * Overhang);
        dl.AddImage(wrap.Handle, center - half, center + half);
    }
}
