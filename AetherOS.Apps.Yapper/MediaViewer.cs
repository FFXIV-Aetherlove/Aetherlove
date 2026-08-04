using System;
using System.Numerics;
using AetherLove.Shared.Yapper;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Yapper;

/// <summary>The breakout image viewer: a separate resizable window outside the phone (the sanctioned
/// exception to in-phone overlays) showing one yap's gallery full size with left/right paging.</summary>
internal sealed class MediaViewer(YapperMediaCache mediaCache)
{
    private YapMediaMetaDto[] _media = [];
    private int _index;
    private bool _open;
    private Guid _sizedFor;

    public void Open(YapDto dto, int index)
    {
        _media = dto.Media;
        _index = Math.Clamp(index, 0, Math.Max(0, dto.Media.Length - 1));
        _open = _media.Length > 0;
        _sizedFor = Guid.Empty;
    }

    public void Draw()
    {
        if (!_open)
        {
            return;
        }

        // Native 1:1 pixels, capped at 80% of the game screen; sized once per image (the user can
        // still resize freely afterwards) and centered so a big screenshot doesn't open off-screen.
        var currentMeta = _media[Math.Clamp(_index, 0, _media.Length - 1)];
        var currentWrap = mediaCache.Get(currentMeta.ImageId)?.Tex?.GetWrapOrDefault();
        if (currentWrap is not null && _sizedFor != currentMeta.ImageId)
        {
            _sizedFor = currentMeta.ImageId;
            var viewport = ImGui.GetMainViewport().Size;
            var fit = Math.Min(1f, Math.Min(viewport.X * 0.8f / currentWrap.Width, viewport.Y * 0.8f / currentWrap.Height));
            var style = ImGui.GetStyle();
            var target = new Vector2(currentWrap.Width, currentWrap.Height) * fit
                + style.WindowPadding * 2f + new Vector2(0f, ImGui.GetFrameHeight());
            ImGui.SetNextWindowSize(target, ImGuiCond.Always);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos + (viewport - target) * 0.5f, ImGuiCond.Always);
        }
        else
        {
            ImGui.SetNextWindowSize(new Vector2(Px(680f), Px(560f)), ImGuiCond.Appearing);
        }
        if (ImGui.Begin("Yapper###yapMediaViewer", ref _open,
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var avail = ImGui.GetContentRegionAvail();
            var meta = _media[Math.Clamp(_index, 0, _media.Length - 1)];
            var wrap = mediaCache.Get(meta.ImageId)?.Tex?.GetWrapOrDefault();
            if (wrap is not null && avail.X > 1f && avail.Y > 1f)
            {
                var scale = Math.Min(avail.X / wrap.Width, avail.Y / wrap.Height);
                var size = new Vector2(wrap.Width * scale, wrap.Height * scale);
                var origin = ImGui.GetCursorScreenPos() + (avail - size) * 0.5f;
                ImGui.GetWindowDrawList().AddImage(wrap.Handle, origin, origin + size);
            }

            if (_media.Length > 1)
            {
                var dl = ImGui.GetWindowDrawList();
                var winPos = ImGui.GetWindowPos();
                var winSize = ImGui.GetWindowSize();
                var midY = winPos.Y + winSize.Y * 0.5f;
                DrawPager(dl, new Vector2(winPos.X + Px(10f), midY), FontAwesomeIcon.ChevronLeft,
                    "##yapViewerPrev", () => _index = (_index - 1 + _media.Length) % _media.Length);
                DrawPager(dl, new Vector2(winPos.X + winSize.X - Px(38f), midY), FontAwesomeIcon.ChevronRight,
                    "##yapViewerNext", () => _index = (_index + 1) % _media.Length);
                var counter = $"{_index + 1}/{_media.Length}";
                var sz = ImGui.CalcTextSize(counter);
                dl.AddText(new Vector2(winPos.X + (winSize.X - sz.X) * 0.5f, winPos.Y + winSize.Y - sz.Y - Px(8f)),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.7f)), counter);
            }
        }
        ImGui.End();
    }

    private static void DrawPager(ImDrawListPtr dl, Vector2 tl, FontAwesomeIcon icon, string id, Action page)
    {
        var size = new Vector2(Px(28f), Px(44f));
        ImGui.SetCursorScreenPos(tl - new Vector2(0f, size.Y * 0.5f));
        if (ImGui.InvisibleButton(id, size))
        {
            page();
        }
        HandOnHover();
        var center = tl + new Vector2(size.X * 0.5f, 0f);
        dl.AddCircleFilled(center, Px(14f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)));
        AetherLove.UI.IconDraw.AddCentered(dl, icon, Px(13f), center,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, ImGui.IsItemHovered() ? 1f : 0.8f)));
    }
}
