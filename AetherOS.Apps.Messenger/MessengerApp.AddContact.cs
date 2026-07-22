using System;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messenger;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Messenger;

/// <summary>The add-a-contact page: a full screen in the onboarding visual language rather than a popup. It
/// explains the code exchange, presents the user's own code to share, and takes a friend's code. A sent request
/// returns to the chat list, where it then shows up as a pending invite.</summary>
public sealed partial class MessengerApp
{
    private const float AddPadX = 20f;

    private string _addCode = string.Empty;
    private volatile string? _addFeedback;
    private volatile bool _addSubmitting;

    private void OpenAddContact()
    {
        _addCode = string.Empty;
        _addFeedback = null;
        _addSubmitting = false;
        _view = View.AddContact;
        _openFadeAt = -1;
    }

    private void CloseAddContact()
    {
        _view = View.List;
        _openFadeAt = -1;
    }

    private void DrawAddContactScreen(OsAppContext ctx)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(AddPadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("os.msgr_back"), FontAwesomeIcon.CommentDots))
        {
            CloseAddContact();
        }

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##msgrAddContact", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false);
        PopScrollbarStyle();
        if (!scroll.Success)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        DrawHero("msgr_add", Loc.T("os.msgr_add_contact"), Loc.T("os.msgr_add_intro"), 30f);

        if (_store.Sync?.MyCode is { Length: > 0 } myCode)
        {
            FieldLabel(Loc.T("os.msgr_add_your_code"));
            if (DrawSecretBox("##msgrMyCodeBox", MessengerCodeDisplay(myCode), Loc.T("os.msgr_copy_code")))
            {
                _caps.System.CopyToClipboard(myCode);
            }
            ImGui.Dummy(new Vector2(0f, Px(18f)));
        }

        FieldLabel(Loc.T("os.msgr_add_their_code"));
        ImGui.SetCursorPosX(Px(AddPadX));
        ImGui.SetNextItemWidth(winW - Px(AddPadX * 2f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, InputFill);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(11f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(12f), Px(10f)));
        bool submit;
        using (UiFonts.H3?.Push())
        {
            submit = ImGui.InputTextWithHint("##msgrAddCode", "XXXX@XXXX", ref _addCode, 12,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CharsUppercase);
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        if (_addFeedback is { } feedback)
        {
            DrawCenteredParagraph(feedback, winW - Px(48f), DangerRed);
            ImGui.Dummy(new Vector2(0f, Px(10f)));
        }

        var ready = NormalizedAddCode().Length >= MessengerLimits.CodeLength;
        if ((DrawPrimaryButton(_addSubmitting ? Loc.T("os.msgr_add_sending") : Loc.T("os.msgr_send_request"),
                ready && !_addSubmitting) || (submit && ready && !_addSubmitting)))
        {
            SendAddRequest();
        }

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        DrawInfoCallout(Loc.T("os.msgr_add_privacy"), ThemeService.Current.AccentLight, FontAwesomeIcon.ShieldAlt);
        ImGui.Dummy(new Vector2(0f, Px(20f)));
    }

    /// <summary>Codes are shown as XXXX@XXXX but stored bare, and users paste either shape.</summary>
    private string NormalizedAddCode() => _addCode.Trim().Replace("@", string.Empty);

    private void SendAddRequest()
    {
        var code = NormalizedAddCode();
        _addFeedback = null;
        _addSubmitting = true;
        RunHub(async () =>
        {
            try
            {
                await _hub.AddMessengerContactAsync(code).ConfigureAwait(false);
                await _sync.SyncAsync().ConfigureAwait(false);
                _uiActions.Enqueue(() =>
                {
                    _addCode = string.Empty;
                    CloseAddContact();
                });
            }
            catch (Exception ex)
            {
                _addFeedback = AetherLove.Services.HubErrorText.Localize(ex);
            }
            finally
            {
                _addSubmitting = false;
            }
        });
    }

    private static void FieldLabel(string text)
    {
        ImGui.SetCursorPosX(Px(AddPadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, text);
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    /// <summary>Requests this user sent that the other side hasn't answered yet, newest first.</summary>
    private MessengerRequestDto[] OutgoingRequests()
        => _store.Requests.Where(r => !r.Incoming).OrderByDescending(r => r.RequestedAtUtc).ToArray();
}
