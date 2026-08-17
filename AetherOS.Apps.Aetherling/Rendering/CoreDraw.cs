using System;
using System.Numerics;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Rendering;

/// <summary>Draws the ceremony crystal into an ImGui draw list: one layered atlas quad per sheet, tinted per
/// layer role, optionally with the code-side gloss passes. Layers still decoding simply skip a frame.</summary>
/// <summary>A colour per tinted layer role. The crystal is one substance so it wears a single tint; a
/// creature with eyes is not, and one colour over all three layers turns the eyes into more of the body.</summary>
public readonly record struct CoreTints(Vector4 Body, Vector4 Accent, Vector4 Eye)
{
    /// <summary>Every role the same colour: what the crystal has always used.</summary>
    public static CoreTints Uniform(Vector4 tint) => new(tint, tint, tint);

    public Vector4 For(TintRole role) => role switch
    {
        TintRole.Accent => Accent,
        TintRole.Eye => Eye,
        _ => Body,
    };
}

public sealed class CoreDraw
{
    private readonly CoreAssets _assets;
    private readonly ImTextureID?[] _layerTextures;

    public CoreDraw(CoreAssets assets)
    {
        _assets = assets;
        _layerTextures = new ImTextureID?[assets.Manifest.Layers.Count];
    }

    /// <summary>Draws the crystal so that its cell's bottom-centre sits at <paramref name="bottomCentre"/>
    /// in screen coordinates, at <paramref name="displaySize"/> pixels per cell.
    /// <paramref name="tint"/> colours the tinted layers and its alpha carries every layer, so
    /// fading the crystal fades its gloss with it. <paramref name="fx"/> adds the enhanced look;
    /// null or disabled draws the plain quad.</summary>
    public void Draw(
        ImDrawListPtr drawList,
        ITextureCache textures,
        Vector2 bottomCentre,
        float displaySize,
        int cellIndex,
        Vector4 tint,
        Vector2 scale,
        Vector2 offset,
        ShadingFx? fx = null) =>
        Draw(drawList, textures, bottomCentre, displaySize, cellIndex, CoreTints.Uniform(tint), scale, offset,
            fx, flipX: false);

    /// <summary>The layered draw with a colour per role, and the option to face the other way.</summary>
    public void Draw(
        ImDrawListPtr drawList,
        ITextureCache textures,
        Vector2 bottomCentre,
        float displaySize,
        int cellIndex,
        CoreTints tints,
        Vector2 scale,
        Vector2 offset,
        ShadingFx? fx = null,
        bool flipX = false)
    {
        var manifest = _assets.Manifest;
        var ds = displaySize / manifest.Cell;

        // Offsets are authored in 256-cell space, so they scale by display size, not ds.
        var local = flipX ? offset with { X = -offset.X } : offset;
        var anchorBase = bottomCentre + (local * (displaySize / 256f));

        var (u0, v0, u1, v1) = manifest.UvForCell(cellIndex);
        var uv0 = new Vector2(flipX ? u1 : u0, v0);
        var uv1 = new Vector2(flipX ? u0 : u1, v1);

        var width = manifest.Cell * ds * scale.X;
        var height = manifest.Cell * ds * scale.Y;
        var min = anchorBase - new Vector2(width / 2f, height);
        var max = anchorBase + new Vector2(width / 2f, 0f);

        for (var i = 0; i < _layerTextures.Length; i++)
        {
            var texture = textures.Get(_assets.LayerPaths[i]);
            _layerTextures[i] = texture;
            if (texture is not { } handle)
            {
                continue;
            }

            var layer = manifest.Layers[i];
            var roleTint = tints.For(layer.Role);
            var colour = layer.Role == TintRole.None ? new Vector4(1f, 1f, 1f, roleTint.W) : roleTint;
            colour.W *= layer.Alpha;
            drawList.AddImage(handle, min, max, uv0, uv1, ImGui.ColorConvertFloat4ToU32(colour));
        }

        if (fx is { Enabled: true, SheenSweep: true, SweepT: { } sweepT })
        {
            DrawSheen(drawList, min, max, uv0, uv1, sweepT, tints.Body.W);
        }

        if (fx is { Enabled: true, Specular: true })
        {
            DrawSpecular(drawList, manifest, cellIndex, scale, local, anchorBase, ds, tints.Body.W);
        }
    }

    /// <summary>The gloss band: N horizontal strips whose clip bands step sideways give the band
    /// a diagonal lean. Only tinted layers are redrawn (the mass); the untinted overlay is left
    /// alone so the sweep never whites out the crystal's inner detail.</summary>
    private void DrawSheen(
        ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector2 uv0, Vector2 uv1, float sweepT, float bodyAlpha)
    {
        const int Strips = 5;
        var width = max.X - min.X;
        var height = max.Y - min.Y;
        if (width <= 0f || height <= 0f || bodyAlpha <= 0f)
        {
            return;
        }

        var bandW = width * 0.22f;
        var shear = width * 0.14f;
        var cx = min.X - bandW + (sweepT * (width + (bandW * 2f)));

        var strength = MathF.Sin(MathF.PI * sweepT);
        var tint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.30f * strength * bodyAlpha));

        var layers = _assets.Manifest.Layers;
        for (var i = 0; i < Strips; i++)
        {
            var stripTop = min.Y + (height * i / Strips);
            var stripBottom = min.Y + (height * (i + 1) / Strips);
            var lean = ((i / (float)(Strips - 1)) - 0.5f) * shear;
            var clipMin = new Vector2(MathF.Max(min.X, cx + lean - (bandW * 0.5f)), stripTop);
            var clipMax = new Vector2(MathF.Min(max.X, cx + lean + (bandW * 0.5f)), stripBottom);
            if (clipMax.X <= clipMin.X)
            {
                continue;
            }

            drawList.PushClipRect(clipMin, clipMax, true);
            for (var l = 0; l < layers.Count; l++)
            {
                if (layers[l].Role is TintRole.Body or TintRole.Accent && _layerTextures[l] is { } handle)
                {
                    drawList.AddImage(handle, min, max, uv0, uv1, tint);
                }
            }

            drawList.PopClipRect();
        }
    }

    /// <summary>The sliding highlight, placed from the manifest's <c>spec</c> anchor when the
    /// sheet ships one and derived from body/head otherwise. Squash pushes it down and widens
    /// it, motion makes it lag the body, which is the cue that reads as gloss.</summary>
    private static void DrawSpecular(
        ImDrawListPtr drawList,
        AtlasManifest manifest,
        int cellIndex,
        Vector2 scale,
        Vector2 offset,
        Vector2 anchorBase,
        float ds,
        float bodyAlpha)
    {
        if (bodyAlpha <= 0f)
        {
            return;
        }

        var cell = (float)manifest.Cell;
        Vector2 local;
        if (manifest.Anchors.ContainsKey("spec"))
        {
            local = manifest.AnchorForCell("spec", cellIndex);
        }
        else
        {
            var body = manifest.AnchorForCell("body", cellIndex);
            var head = manifest.AnchorForCell("head", cellIndex);
            local = Vector2.Lerp(body, head, 0.48f);
            local.X -= cell * 0.105f;
        }

        var toCell = cell / 256f;
        var squash = scale.X - scale.Y;
        local.Y += MathF.Max(0f, squash) * cell * 0.16f;
        local.X -= offset.X * 0.10f * toCell;
        local.Y += -offset.Y * 0.06f * toCell;

        var at = LocalToScreen(manifest, local, scale, anchorBase, ds);
        var (coreAlpha, haloAlpha, sizeMul) = ShadingFx.SpecFor(manifest.Style);
        var r = cell * ds * 0.075f * sizeMul;
        var widen = 1f + (MathF.Max(0f, squash) * 0.5f);

        // Never tinted: the highlight is the light's colour, not the crystal's.
        AddSoftEllipse(
            drawList, at, new Vector2(r * 2.1f * widen, r * 1.7f), new Vector4(1f, 1f, 1f, haloAlpha * bodyAlpha));
        AddSoftEllipse(
            drawList, at, new Vector2(r * widen, r * 0.8f), new Vector4(1f, 1f, 1f, coreAlpha * bodyAlpha));
    }

    /// <summary>Draws one worn accessory riding its per-cell anchor pin, so it follows hops and
    /// squashes with the body. Authored in 256-space: the sprite scales with display size and the
    /// slot's fit multiplier, never with the sheet's cell resolution. Head and face items squash
    /// with the pose; everything else stays rigid.</summary>
    public void DrawAccessory(
        ImDrawListPtr drawList,
        ITextureCache textures,
        string imagePath,
        AccessoryDef def,
        Vector2 bottomCentre,
        float displaySize,
        int cellIndex,
        Vector2 scale,
        Vector2 offset,
        bool flipX,
        float alpha = 1f)
    {
        if (textures.Get(imagePath) is not { } handle)
        {
            return;
        }

        var manifest = _assets.Manifest;
        var ds = displaySize / manifest.Cell;

        // A still piece is furniture rather than clothing: it drops the body's whole animation, the hop and
        // bob (offset), the squash (scale) and the drift of the anchor itself across the frames, and reads
        // its place off the resting pose. Keeping the flip is deliberate, so a piece the creature turns
        // around inside still turns with it.
        var still = def.StaysStill;
        var poseScale = still ? Vector2.One : scale;
        var poseOffset = still ? Vector2.Zero : offset;
        var poseCell = still ? manifest.RestCell ?? cellIndex : cellIndex;

        var local = flipX ? poseOffset with { X = -poseOffset.X } : poseOffset;
        var anchorBase = bottomCentre + (local * (displaySize / 256f));

        var anchor = manifest.AnchorForCell(def.Anchor, poseCell);
        if (flipX)
        {
            anchor.X = manifest.Cell - anchor.X;
        }
        var screenAnchor = LocalToScreen(manifest, anchor, poseScale, anchorBase, ds);

        var accessoryScale = (displaySize / 256f) * manifest.SlotScaleFor(def.Slot);
        var quadScale = def.Anchor is "head" or "face"
            ? new Vector2(accessoryScale) * poseScale
            : new Vector2(accessoryScale);

        var origin = def.OriginPoint;
        if (flipX)
        {
            origin.X = def.Width - origin.X;
        }
        var min = screenAnchor - (origin * quadScale);
        var max = min + (new Vector2(def.Width, def.Height) * quadScale);
        // Half a texel in from each edge: the sampler wraps, so sampling exactly at 0 or 1 bleeds the
        // opposite edge back in as a phantom line along an accessory that reaches its own border.
        var inset = new Vector2(0.5f / MathF.Max(1, def.Width), 0.5f / MathF.Max(1, def.Height));
        var uv0 = new Vector2(flipX ? 1f - inset.X : inset.X, inset.Y);
        var uv1 = new Vector2(flipX ? inset.X : 1f - inset.X, 1f - inset.Y);
        drawList.AddImage(
            handle, min, max, uv0, uv1, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha)));
    }

    /// <summary>Inks the dynamic mouth at the manifest's mouth anchor. A sheet that declares no
    /// mouth anchor kept its baked face and this is a no-op.</summary>
    public void DrawMouth(
        ImDrawListPtr drawList,
        Vector2 bottomCentre,
        float displaySize,
        int cellIndex,
        Vector2 scale,
        Vector2 offset,
        bool flipX,
        in MouthShape shape,
        float alpha = 1f)
    {
        var manifest = _assets.Manifest;
        if (!manifest.HasDynamicMouth)
        {
            return;
        }

        var ds = displaySize / manifest.Cell;
        var local = flipX ? offset with { X = -offset.X } : offset;
        var anchorBase = bottomCentre + (local * (displaySize / 256f));

        var anchor = manifest.AnchorForCell("mouth", cellIndex);
        if (flipX)
        {
            anchor.X = manifest.Cell - anchor.X;
        }
        var screenAnchor = LocalToScreen(manifest, anchor, scale, anchorBase, ds);

        var ink = manifest.LineColor.Length > 0 ? Palette.ParseHex(manifest.LineColor) : Vector4.Zero;
        MouthDraw.Draw(
            drawList, screenAnchor, displaySize / 256f, scale, flipX, shape, manifest.MouthScale, ink, alpha);
    }

    /// <summary>A soft-edged ellipse built from concentric filled rings.</summary>
    private static void AddSoftEllipse(
        ImDrawListPtr drawList, Vector2 centre, Vector2 radii, Vector4 colour, int rings = 5)
    {
        const int Segments = 24;
        var ringColour = ImGui.ColorConvertFloat4ToU32(colour with { W = colour.W * 0.36f });
        for (var ring = 0; ring < rings; ring++)
        {
            var scale = 1f - (ring / (float)rings * 0.62f);
            var rx = radii.X * scale;
            var ry = radii.Y * scale;
            if (rx < 0.5f || ry < 0.5f)
            {
                break;
            }

            for (var s = 0; s < Segments; s++)
            {
                var a = MathF.Tau * s / Segments;
                drawList.PathLineTo(new Vector2(centre.X + (MathF.Cos(a) * rx), centre.Y + (MathF.Sin(a) * ry)));
            }

            drawList.PathFillConvex(ringColour);
        }
    }

    /// <summary>Any cell-local point to screen, applying the bottom-anchored squash.</summary>
    private static Vector2 LocalToScreen(
        AtlasManifest manifest, Vector2 local, Vector2 scale, Vector2 anchorBase, float ds)
    {
        var relative = local - new Vector2(manifest.Cell / 2f, manifest.Cell);
        return anchorBase + (relative * ds * scale);
    }
}
