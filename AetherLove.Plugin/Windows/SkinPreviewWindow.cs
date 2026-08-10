using System;
using System.Numerics;
using System.Threading;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;

namespace AetherLove.Windows;

/// <summary>A second phone that sits beside the real one showing a skin the user does not own yet. The
/// frame is the product art itself, so the preview is shaped by that skin rather than by the current
/// theme. The watermark is baked into the bytes by the server, which is the whole point: nothing the
/// client receives is a usable frame, so no client-side drawing has to be trusted.</summary>
public sealed class SkinPreviewWindow : Window, IDisposable
{
    private const float GapFromPhone = 12f;

    private IDalamudTextureWrap? _texture;
    private string _title = "";
    private bool _failed;
    private Vector2 _anchorPos;
    private Vector2 _anchorSize;
    private int _generation;

    public SkinPreviewWindow() : base(
        "AetherLove##SkinPreview",
        ImGuiWindowFlags.NoResize
      | ImGuiWindowFlags.NoScrollbar
      | ImGuiWindowFlags.NoScrollWithMouse
      | ImGuiWindowFlags.NoTitleBar
      | ImGuiWindowFlags.NoDocking
      | ImGuiWindowFlags.NoBackground)
    {
        Size = UiScale.Design;
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Always;
    }

    /// <summary>Opens the frame straight away on a placeholder, so the click feels answered while the
    /// watermarked image is still on the wire.</summary>
    public void BeginLoading(string title)
    {
        Interlocked.Increment(ref _generation);
        Retire();
        _title = title;
        _failed = false;
        IsOpen = true;
    }

    /// <summary>Hands over the server's watermarked bytes (or null when the fetch failed). A theme's
    /// wallpaper arrives already composed into the frame by the server, which owns the geometry.</summary>
    public void Deliver(string title, byte[]? bytes)
    {
        var generation = Volatile.Read(ref _generation);
        if (bytes is not { Length: > 0 })
        {
            _failed = true;
            return;
        }
        try
        {
            var wrap = UiHost.TextureProvider.CreateFromImageAsync(bytes).GetAwaiter().GetResult();
            if (Volatile.Read(ref _generation) != generation)
            {
                wrap.Dispose();
                return;
            }
            Retire();
            _texture = wrap;
            _title = title;
            _failed = false;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[SkinPreview] Could not decode the preview image.");
            _failed = true;
        }
    }

    /// <summary>The phone's live rect, pushed every frame it draws, so the preview tracks it.</summary>
    public void SetAnchor(Vector2 pos, Vector2 size)
    {
        _anchorPos = pos;
        _anchorSize = size;
    }

    public override void PreDraw()
    {
        // Height is taken from the phone's live rect rather than the design constant, so the two windows
        // match whatever scaling Dalamud applied; the ratio back to Size units is derived from the same
        // pair. The skin's own aspect sets the width: every frame is shaped a little differently.
        var designHeight = Px(UiScale.Design.Y);
        var screenHeight = _anchorSize.Y > 1f ? _anchorSize.Y : designHeight;
        var unitsPerPixel = designHeight / screenHeight;
        var aspect = _texture is { Height: > 0 } tex ? tex.Width / (float)tex.Height : 0.5569f;
        var screenWidth = MathF.Max(60f, screenHeight * aspect);

        Size = new Vector2(screenWidth, screenHeight) * unitsPerPixel;
        Position = NextTo(screenWidth);
    }

    /// <summary>Right of the phone by default, flipping to its left when the viewport has no room.</summary>
    private Vector2 NextTo(float width)
    {
        var gap = Px(GapFromPhone);
        var viewport = ImGui.GetMainViewport();
        var right = _anchorPos.X + _anchorSize.X + gap;
        if (right + width > viewport.Pos.X + viewport.Size.X)
        {
            var left = _anchorPos.X - gap - width;
            if (left >= viewport.Pos.X)
            {
                return new Vector2(left, _anchorPos.Y);
            }
        }
        return new Vector2(right, _anchorPos.Y);
    }

    public override void Draw()
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var t = ThemeService.Current;

        if (_texture is { } tex)
        {
            dl.AddImage(tex.Handle, pos, pos + size);
        }
        else
        {
            OsDrawShared.RoundedGradient(dl, pos, pos + size, Px(28f), t.Accent, t.AccentDark);
            var message = _failed ? Loc.T("os.store_preview_failed") : Loc.T("os.store_preview_loading");
            OsDrawShared.CenteredText(dl, message, pos.X + size.X * 0.5f,
                pos.Y + size.Y * 0.5f, OsDrawShared.White(0.85f), 1f, size.X - Px(48f));
        }

        DrawCaption(dl, pos, size);
        DrawCloseButton(dl, pos, size);
    }

    private void DrawCaption(ImDrawListPtr dl, Vector2 pos, Vector2 size)
    {
        var label = _title;
        var sub = Loc.T("os.store_preview_caption");
        var labelSz = ImGui.CalcTextSize(label);
        var subSz = ImGui.CalcTextSize(sub);
        var boxW = MathF.Min(size.X - Px(40f), MathF.Max(labelSz.X, subSz.X) + Px(28f));
        var boxH = labelSz.Y + subSz.Y + Px(16f);
        var tl = new Vector2(pos.X + (size.X - boxW) * 0.5f, pos.Y + size.Y - boxH - Px(64f));

        dl.AddRectFilled(tl, tl + new Vector2(boxW, boxH), OsDrawShared.Black(0.62f), boxH * 0.28f);
        dl.AddText(new Vector2(tl.X + (boxW - labelSz.X) * 0.5f, tl.Y + Px(6f)), 0xFFFFFFFFu, label);
        dl.AddText(new Vector2(tl.X + (boxW - subSz.X) * 0.5f, tl.Y + Px(6f) + labelSz.Y),
            ImGui.GetColorU32(UiColors.Hint), sub);
    }

    private void DrawCloseButton(ImDrawListPtr dl, Vector2 pos, Vector2 size)
    {
        var side = Px(28f);
        var tl = new Vector2(pos.X + size.X - side - Px(16f), pos.Y + Px(16f));
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##skinPreviewClose", new Vector2(side, side)))
        {
            IsOpen = false;
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T("os.store_preview_close"));
        }
        var center = tl + new Vector2(side * 0.5f, side * 0.5f);
        dl.AddCircleFilled(center, side * 0.5f, OsDrawShared.Black(hovered ? 0.8f : 0.55f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(13f), center, 0xFFFFFFFFu);
    }

    public override void OnClose() => Retire();

    private void Retire()
    {
        _texture?.Dispose();
        _texture = null;
    }

    public void Dispose() => Retire();
}
