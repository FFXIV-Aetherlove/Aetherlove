using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper;

/// <summary>The in-phone "where from?" chooser shown before any image is added: take a selfie, pick from
/// the Photos app, or browse a file from disk. Drawn last by the app so it layers over the current screen.</summary>
internal sealed class ImageSourceSheet
{
    private bool _open;
    private Action? _onSelfie;
    private Action? _onPhotos;
    private Action? _onFile;

    public void Open(Action onSelfie, Action onPhotos, Action onFile)
    {
        _onSelfie = onSelfie;
        _onPhotos = onPhotos;
        _onFile = onFile;
        _open = true;
    }

    public void Draw(OsAppContext ctx)
    {
        if (!_open)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _open = false;
            return;
        }

        var origin = ImGui.GetWindowPos();
        var avail = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(origin);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var layer = ImRaii.Child("##yapImgSrcOverlay", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.72f)));

        var pad = Px(14f);
        var rowH = Px(44f);
        var panelW = MathF.Min(avail.X - Px(28f), Px(280f));
        var panelH = pad * 2f + Px(24f) + rowH * 3f;
        var panelPos = origin + (avail - new Vector2(panelW, panelH)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
        using (var panel = ImRaii.Child("##yapImgSrcPanel", new Vector2(panelW, panelH), true,
                   ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (panel)
            {
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.55f), Loc.T("os.yapper_src_title"));
                ImGui.Dummy(new Vector2(0f, Px(4f)));
                var innerW = panelW - pad * 2f;
                DrawOption(ctx, FontAwesomeIcon.Camera, Loc.T("os.yapper_src_selfie"), innerW, rowH, _onSelfie);
                DrawOption(ctx, FontAwesomeIcon.Images, Loc.T("os.yapper_src_photos"), innerW, rowH, _onPhotos);
                DrawOption(ctx, FontAwesomeIcon.FolderOpen, Loc.T("os.yapper_src_file"), innerW, rowH, _onFile);
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        // Scrim click closes; submitted last so the panel wins input.
        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##yapImgSrcScrim", avail)
            && !ImGui.IsMouseHoveringRect(panelPos, panelPos + new Vector2(panelW, panelH)))
        {
            _open = false;
        }
    }

    private void DrawOption(OsAppContext ctx, FontAwesomeIcon icon, string label, float width, float rowH, Action? onPick)
    {
        var tl = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton($"##yapImgSrc{icon}", new Vector2(width, rowH)))
        {
            _open = false;
            onPick?.Invoke();
        }
        HandOnHover();
        var dl = ImGui.GetWindowDrawList();
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(tl, tl + new Vector2(width, rowH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(8f));
        }
        IconDraw.AddCentered(dl, icon, Px(15f), tl + new Vector2(Px(16f), rowH * 0.5f),
            ImGui.GetColorU32(ctx.Theme.Accent));
        dl.AddText(tl + new Vector2(Px(36f), (rowH - ImGui.GetTextLineHeight()) * 0.5f), 0xFFFFFFFFu, label);
        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + rowH));
    }
}
