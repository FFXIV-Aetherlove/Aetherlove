using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Yapper;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper;

/// <summary>The in-phone report dialog for yaps and yapper profiles: a reason box, a send button, and a
/// sent confirmation. Drawn last by the app so it layers over whatever screen opened it.</summary>
internal sealed class ReportOverlay(IYapperHost host)
{
    private bool _open;
    private Guid? _yapId;
    private Guid? _profileId;
    private string _targetHandle = string.Empty;
    private string _reason = string.Empty;
    private volatile bool _sending;
    private bool _done;
    private volatile string? _error;

    public void OpenForYap(YapDto dto)
    {
        _yapId = dto.Id;
        _profileId = null;
        _targetHandle = dto.Author?.Handle ?? "?";
        Reset();
    }

    public void OpenForProfile(Guid profileId, string handle)
    {
        _yapId = null;
        _profileId = profileId;
        _targetHandle = handle;
        Reset();
    }

    private void Reset()
    {
        _reason = string.Empty;
        _done = false;
        _error = null;
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
        using var layer = ImRaii.Child("##yapReportOverlay", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.72f)));

        var pad = Px(14f);
        var panelW = MathF.Min(avail.X - Px(28f), Px(300f));
        var panelH = _done ? Px(120f) : Px(196f);
        var panelPos = origin + (avail - new Vector2(panelW, panelH)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
        using (var panel = ImRaii.Child("##yapReportPanel", new Vector2(panelW, panelH), true,
                   ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (panel)
            {
                DrawPanel(ctx, panelW - pad * 2f);
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        // Scrim click closes; submitted last so the panel wins input.
        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##yapReportScrim", avail)
            && !ImGui.IsMouseHoveringRect(panelPos, panelPos + new Vector2(panelW, panelH)))
        {
            _open = false;
        }
    }

    private void DrawPanel(OsAppContext ctx, float innerW)
    {
        var dl = ImGui.GetWindowDrawList();
        IconDraw.AddCentered(dl, FontAwesomeIcon.Flag, Px(13f),
            ImGui.GetCursorScreenPos() + new Vector2(Px(8f), Px(9f)), ImGui.GetColorU32(ctx.Theme.Accent));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Px(22f));
        ImGui.TextUnformatted(string.Format(Loc.T("os.yapper_report_title"), _targetHandle));
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        if (_done)
        {
            ImGui.TextColored(new Vector4(0.45f, 0.85f, 0.55f, 1f), Loc.T("os.yapper_report_done"));
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            if (Button($"{Loc.T("os.yapper_report_close")}##yapReportClose", new Vector2(innerW, Px(30f))))
            {
                _open = false;
            }
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.InputTextMultiline("##yapReportReason", ref _reason, 500, new Vector2(innerW, Px(80f)));
        ImGui.PopStyleColor();
        if (_error is { } error)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.4f, 1f), error);
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        using (ImRaii.Disabled(_sending || _reason.Trim().Length == 0))
        {
            if (Button($"{Loc.T("os.yapper_report_send")}##yapReportSend", new Vector2(innerW, Px(30f))))
            {
                Submit();
            }
        }
    }

    private void Submit()
    {
        var yapId = _yapId;
        var profileId = _profileId;
        var reason = _reason.Trim();
        _sending = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await host.ReportYapAsync(yapId, profileId, reason).ConfigureAwait(false);
                _done = true;
            }
            catch (Exception ex)
            {
                _error = AetherLove.Services.HubErrorText.Localize(ex);
            }
            finally
            {
                _sending = false;
            }
        });
    }
}
