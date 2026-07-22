using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>The OS share sheet: an in-phone overlay that lists every app accepting the shared content and hands
/// the pick back to the caller. Modeled on <see cref="NotificationShade"/> (a scrim over only the phone rect and
/// a slide-in panel), it slides up from the bottom.</summary>
public sealed class ShareSheet
{
    private const float AnimSpeed = 10f;

    private float _openT;
    private bool _targetOpen;
    private IReadOnlyList<IAetherApp> _targets = Array.Empty<IAetherApp>();
    private string _shareType = "";
    private string? _title;
    private Action<IAetherApp>? _onPick;

    public bool Visible => _openT > 0.001f || _targetOpen;

    public void Open(IReadOnlyList<IAetherApp> targets, ShareItem item, string? title, Action<IAetherApp> onPick)
    {
        _targets = targets;
        _shareType = item.Type;
        _title = string.IsNullOrWhiteSpace(title) ? null : title;
        _onPick = onPick;
        _targetOpen = true;
    }

    private void Close()
    {
        _targetOpen = false;
        _onPick = null;
    }

    public void Draw(Vector2 contentTL, Vector2 contentBR)
    {
        var dt = ImGui.GetIO().DeltaTime;
        var target = _targetOpen ? 1f : 0f;
        _openT += (target - _openT) * Math.Min(1f, dt * AnimSpeed);
        if (Math.Abs(_openT - target) < 0.004f)
        {
            _openT = target;
        }
        if (_openT <= 0.001f && !_targetOpen)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var contentSize = contentBR - contentTL;
        dl.AddRectFilled(contentTL, contentBR, OsDraw.Black(0.55f * _openT));

        var padX = Px(18f);
        var innerW = contentSize.X - padX * 2f;
        var cellW = Px(78f);
        var tileSz = Px(54f);
        var cellH = tileSz + Px(22f);
        var rowStride = cellH + Px(10f);
        var cols = Math.Clamp(_targets.Count, 1, Math.Max(1, (int)(innerW / cellW)));
        var rows = (_targets.Count + cols - 1) / cols;

        var titleH = Px(46f);
        var gridH = rows * rowStride;
        var cancelH = Px(46f);
        var panelH = titleH + gridH + cancelH + Px(30f);

        var panelBottom = contentBR.Y + panelH * (1f - _openT);
        var panelTop = panelBottom - panelH;
        var panelTL = new Vector2(contentTL.X, panelTop);
        var panelBR = new Vector2(contentBR.X, panelBottom);
        var rounding = Px(22f);

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.09f, 0.98f)),
            rounding, ImDrawFlags.RoundCornersTop);
        dl.AddRect(panelTL, panelBR, OsDraw.White(0.08f), rounding, ImDrawFlags.RoundCornersTop, Px(1f));

        var centerX = (panelTL.X + panelBR.X) * 0.5f;
        using (UiFonts.H3?.Push())
        {
            OsDraw.CenteredText(dl, _title ?? Loc.T("os.share_sheet_title"), centerX, panelTop + Px(15f), OsDraw.White(0.95f));
        }

        // Tiles first (they must win clicks over the scrim submitted last).
        IAetherApp? picked = null;
        var gridTop = panelTop + titleH;
        var rowWidth = cols * cellW;
        var startX = centerX - rowWidth * 0.5f;
        for (int i = 0; i < _targets.Count; i++)
        {
            var app = _targets[i];
            var col = i % cols;
            var row = i / cols;
            var cellX = startX + col * cellW;
            var cellY = gridTop + row * rowStride;
            var tileTL = new Vector2(cellX + (cellW - tileSz) * 0.5f, cellY);
            var tileBR = tileTL + new Vector2(tileSz, tileSz);

            ImGui.SetCursorScreenPos(new Vector2(cellX, cellY));
            if (ImGui.InvisibleButton($"##shareTarget_{i}", new Vector2(cellW, cellH)))
            {
                picked = app;
            }
            if (ImGui.IsItemHovered())
            {
                dl.AddRectFilled(new Vector2(cellX + Px(3f), cellY - Px(3f)),
                    new Vector2(cellX + cellW - Px(3f), cellY + cellH), OsDraw.White(0.07f), Px(12f));
            }
            OsDraw.AppTile(dl, app, tileTL, tileBR);
            var label = app.ShareTargetLabel(_shareType);
            var nameFsz = ImGui.GetFontSize() * 0.84f;
            var nameSz = ImGui.CalcTextSize(label) * (nameFsz / ImGui.GetFontSize());
            dl.PushClipRect(new Vector2(cellX, cellY), new Vector2(cellX + cellW, cellY + cellH), true);
            dl.AddText(ImGui.GetFont(), nameFsz, new Vector2(cellX + cellW * 0.5f - nameSz.X * 0.5f, tileBR.Y + Px(6f)),
                OsDraw.White(0.82f), label);
            dl.PopClipRect();
        }

        // Cancel button.
        var cancelW = Px(140f);
        var cancelTL = new Vector2(centerX - cancelW * 0.5f, panelBR.Y - cancelH - Px(14f));
        ImGui.SetCursorScreenPos(cancelTL);
        var cancelClicked = ImGui.InvisibleButton("##shareCancel", new Vector2(cancelW, cancelH - Px(8f)));
        var cancelHover = ImGui.IsItemHovered();
        dl.AddRectFilled(cancelTL, cancelTL + new Vector2(cancelW, cancelH - Px(8f)),
            OsDraw.White(cancelHover ? 0.14f : 0.09f), Px(12f));
        OsDraw.CenteredText(dl, Loc.T("common.cancel"), centerX, cancelTL.Y + Px(10f), OsDraw.White(0.9f));

        // Scrim last: a tap outside the panel cancels.
        ImGui.SetCursorScreenPos(contentTL);
        var scrimClicked = ImGui.InvisibleButton("##shareScrim", contentSize);

        if (picked is { } chosen && _openT >= 0.5f)
        {
            var cb = _onPick;
            Close();
            cb?.Invoke(chosen);
            return;
        }
        if ((cancelClicked || scrimClicked) && _openT >= 0.5f)
        {
            Close();
        }
    }
}
