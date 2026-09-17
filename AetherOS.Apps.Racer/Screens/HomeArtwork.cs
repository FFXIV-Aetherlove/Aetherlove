using System.IO;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens;

internal enum HomePanel
{
    Race,
    Cup,
    Stamps,
    Packs,
}

internal static class HomeArtwork
{
    public static void Draw(OsAppContext ctx, IRacerHost host, HomePanel panel, Vector2 at, Vector2 size, bool hover, bool enabled = true)
    {
        var language = Language(ctx);
        var root = Path.Combine(host.PetAssetRoot, "racer");
        var normal = ctx.Capabilities.Textures.Get(Path.Combine(root, $"home-controls-{language}.png"));
        var highlight = ctx.Capabilities.Textures.Get(Path.Combine(root, $"home-controls-{language}-hover.png"));
        var art = hover && enabled ? highlight ?? normal : normal;
        art ??= ctx.Capabilities.Textures.Get(Path.Combine(root, "home-controls-en.png"));
        if (art is null)
        {
            return;
        }
        var bounds = panel switch
        {
            HomePanel.Race => new Vector4(8, 40, 627, 517),
            HomePanel.Cup => new Vector4(627, 40, 1246, 517),
            HomePanel.Stamps => new Vector4(8, 519, 1246, 866),
            _ => new Vector4(8, 866, 1246, 1224),
        };
        const float atlasSize = 1254;
        ImGui.GetWindowDrawList().AddImage(art.Value, at, at + size,
            new Vector2(bounds.X, bounds.Y) / atlasSize,
            new Vector2(bounds.Z, bounds.W) / atlasSize,
            enabled ? 0xFFFFFFFF : 0xFFAAAAAA);
    }

    private static string Language(OsAppContext ctx)
    {
        var language = ctx.Culture.TwoLetterISOLanguageName;
        return language is "de" or "es" or "fr" or "pt" or "ru" ? language : "en";
    }

    public static void Footer(OsAppContext ctx, IRacerHost host, int tab, bool selected, bool hovered, Vector2 at, Vector2 size)
    {
        var root = Path.Combine(host.PetAssetRoot, "racer");
        var art = ctx.Capabilities.Textures.Get(Path.Combine(root, $"footer-controls-{Language(ctx)}.png"))
            ?? ctx.Capabilities.Textures.Get(Path.Combine(root, "footer-controls-en.png"));
        if (art is null)
        {
            return;
        }
        var row = selected ? 1 : hovered ? 2 : 0;
        var uvMin = new Vector2(tab / 3f, row / 3f);
        var uvMax = new Vector2((tab + 1) / 3f, (row + 1) / 3f);
        ImGui.GetWindowDrawList().AddImage(art.Value, at, at + size, uvMin, uvMax);
    }

    /// <summary>A home ticket with an optional status plate along its foot. <paramref name="statusOnHover"/> shows the
    /// plate only while the pointer is on the ticket.</summary>
    public static bool Ticket(OsAppContext ctx, IRacerHost host, string id, HomePanel panel, Vector2 at, Vector2 size, bool enabled, string? status,
        bool statusOnHover = false)
    {
        ImGui.SetCursorScreenPos(at);
        var pressed = ImGui.InvisibleButton(id, size) && enabled;
        var hovered = ImGui.IsItemHovered();
        if (enabled)
        {
            HandOnHover();
        }
        Draw(ctx, host, panel, at, size, hovered, enabled);
        if (!string.IsNullOrEmpty(status) && (hovered || !statusOnHover))
        {
            var badgeAt = at + new Vector2(size.X * .08f, size.Y * .79f);
            var badgeSize = new Vector2(size.X * .84f, size.Y * .21f);
            ImGui.GetWindowDrawList().AddRectFilled(badgeAt, badgeAt + badgeSize, 0xF52E1C10, Px(8));
            GrandstandFrame.Label(ctx, status, badgeAt, badgeSize, GrandstandFrame.Cream, RacerTextSize.Caption);
        }
        return pressed;
    }
}
