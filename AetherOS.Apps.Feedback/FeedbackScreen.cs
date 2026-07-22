using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Feedback;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Feedback;

/// <summary>Feedback form surface with app selector, kind picker, message input, and submit states.</summary>
public sealed class FeedbackScreen
{
    private const float Pad = 16f;
    private const string GeneralAppId = "general";

    private readonly IFeedbackHost _host;
    private readonly EntranceAnimation _entrance = new();

    private FeedbackKind _kind = FeedbackKind.Bug;
    private string _text = string.Empty;
    private string _selectedAppId = GeneralAppId;

    private volatile bool _submitting;
    private volatile string? _error;
    private volatile bool _done;


    public FeedbackScreen(IFeedbackHost host)
    {
        _host = host;
    }

    public void OnForeground()
    {
        if (_done)
        {
            Reset();
        }
        _entrance.Arm();
    }

    public void Draw(OsAppContext ctx)
    {

        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##feedbackScroll", ImGui.GetContentRegionAvail(), false);
        PopScrollbarStyle();
        if (!scroll.Success)
        {
            return;
        }

        _entrance.BeginFrame();

        ImGui.Dummy(new Vector2(1f, Px(6f)));
        ImGui.SetCursorPosX(Px(Pad));
        using (UiFonts.H1?.Push())
        {
            ImGui.TextColored(t.AccentLight, Loc.T("settings.send_feedback"));
        }
        ImGui.Spacing();

        if (_done)
        {
            DrawSuccess(t, winW);
            _entrance.EndFrame();
            return;
        }

        DrawIntro(winW);
        DrawAppSelector(ctx, t, winW);
        DrawKindPicker(t, winW);
        DrawMessage(winW);
        DrawSubmit(t, winW);

        ImGui.Dummy(new Vector2(1f, Px(10f)));
        _entrance.EndFrame();
    }

    private static void DrawIntro(float winW)
    {
        ImGui.SetCursorPosX(Px(Pad));
        ImGui.PushTextWrapPos(winW - Px(Pad));
        ImGui.TextColored(UiColors.Body, Loc.T("settings.feedback_intro"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(Pad));
        ImGui.PushTextWrapPos(winW - Px(Pad));
        ImGui.TextColored(new Vector4(0.80f, 0.66f, 0.40f, 1f), Loc.T("settings.feedback_note"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();
    }

    /// <summary>The catch-all "General" plus one row per installed app; the UI shows each app's localized Name,
    /// the submitted value is its stable Id.</summary>
    private void DrawAppSelector(OsAppContext ctx, ThemeDefinition t, float winW)
    {
        ImGui.SetCursorPosX(Px(Pad));
        DrawFieldLabel(Loc.T("os.feedback_app_label"), t);
        ImGui.Spacing();

        var current = SelectedAppName(ctx);

        ImGui.SetCursorPosX(Px(Pad));
        ImGui.SetNextItemWidth(winW - Px(Pad) * 2f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.16f, 0.16f, 0.16f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Header, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.BeginCombo("##feedbackApp", current))
        {
            if (ImGui.Selectable(Loc.T("os.feedback_general"), _selectedAppId == GeneralAppId))
            {
                _selectedAppId = GeneralAppId;
            }
            foreach (var app in ctx.Shell.Apps)
            {
                if (ImGui.Selectable($"{app.Name}##fbapp_{app.Id}", _selectedAppId == app.Id))
                {
                    _selectedAppId = app.Id;
                }
            }
            ImGui.EndCombo();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(6);
        ImGui.Spacing();
        ImGui.Spacing();
    }

    private string SelectedAppName(OsAppContext ctx)
    {
        if (_selectedAppId == GeneralAppId)
        {
            return Loc.T("os.feedback_general");
        }
        foreach (var app in ctx.Shell.Apps)
        {
            if (app.Id == _selectedAppId)
            {
                return app.Name;
            }
        }
        _selectedAppId = GeneralAppId;
        return Loc.T("os.feedback_general");
    }

    private void DrawKindPicker(ThemeDefinition t, float winW)
    {
        ImGui.SetCursorPosX(Px(Pad));
        DrawFieldLabel(Loc.T("settings.feedback_type"), t);
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(Pad));
        DrawKindButton(winW, Loc.T("settings.feedback_kind_bug"), FeedbackKind.Bug, t);
        ImGui.SameLine(0f, Px(6f));
        DrawKindButton(winW, Loc.T("settings.feedback_kind_improvement"), FeedbackKind.Improvement, t);
        ImGui.SameLine(0f, Px(6f));
        DrawKindButton(winW, Loc.T("settings.feedback_kind_other"), FeedbackKind.Other, t);
        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawKindButton(float winW, string label, FeedbackKind kind, ThemeDefinition t)
    {
        var selected = _kind == kind;
        var w = (winW - Px(Pad) * 2f - Px(12f)) / 3f;

        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.26f, 0.26f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
        }
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button($"{label}##fbkind{kind}", new Vector2(w, Px(30f))))
        {
            _kind = kind;
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    private void DrawMessage(float winW)
    {
        ImGui.SetCursorPosX(Px(Pad));
        DrawFieldLabel(Loc.T("settings.feedback_your_message"), ThemeService.Current);
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(Pad));
        InputTextMultilineWithPaste("##feedbackText", ref _text, 4000, new Vector2(winW - Px(Pad) * 2f, Px(140f)));
        ImGui.Spacing();

        if (_error is { } err)
        {
            ImGui.SetCursorPosX(Px(Pad));
            ImGui.PushTextWrapPos(winW - Px(Pad));
            ImGui.TextColored(UiColors.Danger, err);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }
    }

    private void DrawSubmit(ThemeDefinition t, float winW)
    {
        var canSubmit = !_submitting && !string.IsNullOrWhiteSpace(_text);
        var submitW = winW - Px(Pad) * 2f;

        ImGui.SetCursorPosX(Px(Pad));
        ImGui.BeginDisabled(!canSubmit);
        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(_submitting ? Loc.T("settings.sending") : Loc.T("settings.submit"), new Vector2(submitW, Px(36f))))
        {
            StartSubmit();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.EndDisabled();
    }

    private void DrawSuccess(ThemeDefinition t, float winW)
    {
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(Pad));
        ImGui.PushTextWrapPos(winW - Px(Pad));
        ImGui.TextColored(UiColors.Success, Loc.T("settings.feedback_thanks"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(Pad));
        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(Loc.T("os.feedback_send_another"), new Vector2(winW - Px(Pad) * 2f, Px(34f))))
        {
            Reset();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    private void Reset()
    {
        _kind = FeedbackKind.Bug;
        _text = string.Empty;
        _selectedAppId = GeneralAppId;
        _submitting = false;
        _error = null;
        _done = false;
        _entrance.Arm();
    }

    private void StartSubmit()
    {
        if (_submitting || string.IsNullOrWhiteSpace(_text))
        {
            return;
        }
        _submitting = true;
        _error = null;
        var req = new SubmitFeedbackRequest(_kind, _text, _selectedAppId);

        _ = Task.Run(async () =>
        {
            try
            {
                await _host.SubmitAsync(req, CancellationToken.None).ConfigureAwait(false);
                _done = true;
            }
            catch (RateLimitException rl)
            {
                _error = Loc.T("settings.feedback_rate_limited", rl.Limit);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[FeedbackScreen] SubmitAsync failed.");
                _error = Loc.T("settings.feedback_send_failed");
            }
            finally
            {
                _submitting = false;
            }
        });
    }
}
