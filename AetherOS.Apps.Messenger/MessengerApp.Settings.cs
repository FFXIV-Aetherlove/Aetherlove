using System;
using System.Globalization;
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

/// <summary>The messenger settings page (an <see cref="IAppSettings"/> surface also reachable from the in-app
/// menu) and the dedicated blocked-users page. Both mirror the AetherLove/Hangouts subpage layout.</summary>
public sealed partial class MessengerApp
{
    private const float SettingsPadX = 16f;

    /// <summary>The <see cref="IAppSettings"/> body, shown as a page inside the OS Settings app
    /// (<paramref name="onBack"/> returns to its app list).</summary>
    public void DrawSettings(OsAppContext ctx, Action? onBack)
    {
        if (onBack != null)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(SettingsPadX));
            if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), FontAwesomeIcon.Cog))
            {
                onBack();
            }
            ImGui.Spacing();
        }
        DrawSettingsBody();
    }

    /// <summary>The same settings body reached from the in-app menu; its back pill returns to the chat list.</summary>
    private void DrawSettingsScreen(OsAppContext ctx)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(SettingsPadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("os.msgr_back"), FontAwesomeIcon.CommentDots))
        {
            _view = View.List;
        }
        ImGui.Spacing();
        DrawSettingsBody();
    }

    private void DrawSettingsBody()
    {
        DrawSubpageHeading(Loc.T("os.msgr_settings"), SettingsPadX);

        using var scroll = ImRaii.Child("##msgrSettings", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false);
        if (!scroll.Success)
        {
            return;
        }

        var config = AetherLove.UiHost.Configuration;
        ImGui.Spacing();

        DrawSectionHeader(Loc.T("os.msgr_settings_notifications"), SettingsPadX);
        SettingCheckbox(SettingsPadX, Loc.T("os.msgr_notify_message"),
            () => config.Messenger.NotifyMessage, v => config.Messenger.NotifyMessage = v);
        SettingCheckbox(SettingsPadX, Loc.T("os.msgr_notify_request"),
            () => config.Messenger.NotifyRequest, v => config.Messenger.NotifyRequest = v);
        SettingCheckbox(SettingsPadX, Loc.T("os.msgr_dtr"),
            () => _caps.ServerBar("messenger").AppEnabled, v => _caps.ServerBar("messenger").AppEnabled = v);

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("os.msgr_settings_privacy"), SettingsPadX);
        // Allow-adds lives server-side, so it can't ride the local-config SettingCheckbox helper.
        var allowAdds = _store.Sync?.AllowAdds ?? true;
        ImGui.SetCursorPosX(Px(SettingsPadX));
        if (ImGui.Checkbox(Loc.T("os.msgr_allow_adds"), ref allowAdds))
        {
            var value = allowAdds;
            RunHub(async () =>
            {
                await _hub.SetMessengerAllowAddsAsync(value).ConfigureAwait(false);
                await _sync.SyncAsync().ConfigureAwait(false);
            });
        }
        SharedUiHelpers.HandOnHover();
        ImGui.Spacing();
    }

    private void DrawBlockedScreen(OsAppContext ctx)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(SettingsPadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("os.msgr_back"), FontAwesomeIcon.CommentDots))
        {
            _view = View.List;
        }
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("os.msgr_blocked"), SettingsPadX);

        using var scroll = ImRaii.Child("##msgrBlocked", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false);
        if (!scroll.Success)
        {
            return;
        }

        var listW = ImGui.GetContentRegionAvail().X;
        var blocked = _blocked;
        if (blocked is null)
        {
            ImGui.Dummy(new Vector2(1f, Px(20f)));
            CenteredMuted(Loc.T("os.msgr_loading"), listW);
        }
        else if (blocked.Length == 0)
        {
            DrawBlockedEmpty(listW);
        }
        else
        {
            ImGui.Dummy(new Vector2(1f, Px(4f)));
            foreach (var item in blocked)
            {
                DrawBlockedRow(item, listW);
            }
        }
        ImGui.Dummy(new Vector2(1f, Px(10f)));
    }

    private void DrawBlockedRow(MessengerBlockedDto item, float listW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(SettingsPadX);
        var rowH = Px(52f);
        var start = ImGui.GetCursorScreenPos();
        var tl = start + new Vector2(pad, 0f);
        var br = tl + new Vector2(listW - pad * 2f, rowH);

        dl.AddRectFilled(tl, br, 0x0DFFFFFFu, Px(10f));
        dl.AddRect(tl, br, 0x22FFFFFFu, Px(10f), ImDrawFlags.None, Px(1f));

        var avatarCenter = new Vector2(tl.X + Px(28f), tl.Y + rowH * 0.5f);
        var avatarR = Px(17f);
        DrawAvatar(dl, item.AccountId, item.Name, null, false, avatarCenter, avatarR);
        dl.AddCircle(avatarCenter, avatarR, 0x55FFFFFFu, 0, Px(1.2f));

        var textX = tl.X + Px(54f);
        dl.AddText(new Vector2(textX, tl.Y + Px(9f)), 0xFFFFFFFFu, item.Name);
        dl.AddText(new Vector2(textX, tl.Y + Px(28f)), Col(MutedText),
            string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_blocked_since"),
                item.BlockedAtUtc.ToLocalTime().ToString("d")));

        var btnLabel = Loc.T("os.msgr_unblock");
        var btnW = ImGui.CalcTextSize(btnLabel).X + Px(22f);
        var btnH = Px(26f);
        ImGui.SetCursorScreenPos(new Vector2(br.X - btnW - Px(10f), tl.Y + (rowH - btnH) * 0.5f));
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (SharedUiHelpers.Button($"{btnLabel}##unblock_{item.AccountId:N}", new Vector2(btnW, btnH)))
        {
            var id = item.AccountId;
            RunHub(async () =>
            {
                await _hub.UnblockMessengerUserAsync(id).ConfigureAwait(false);
                _blocked = await _hub.GetMessengerBlockedAsync().ConfigureAwait(false);
            });
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        ImGui.SetCursorScreenPos(new Vector2(start.X, br.Y));
        ImGui.Dummy(new Vector2(1f, Px(8f)));
    }

    private void DrawBlockedEmpty(float listW)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(1f, Px(36f)));
        var center = ImGui.GetCursorScreenPos() + new Vector2(listW * 0.5f, Px(21f));
        IconCentered(dl, FontAwesomeIcon.Ban, Px(42f), center, Col(MutedText with { W = 0.6f }));
        ImGui.Dummy(new Vector2(1f, Px(56f)));
        CenteredMuted(Loc.T("os.msgr_blocked_none"), listW);
    }

    private static void CenteredMuted(string text, float listW)
    {
        var textW = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(MathF.Max(Px(SettingsPadX), (listW - textW) * 0.5f));
        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }
}
