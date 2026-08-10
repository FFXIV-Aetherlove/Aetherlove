using System;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Echo;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.EchoVidya;
using AetherLove.Shared.Hangouts;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;
using static AetherLove.UI.SharedUiHelpers;
using static AetherLove.UI.UiScale;

namespace AetherOS.Apps.EchoVidya.Screens;

/// <summary>The in-phone half of a room: create one, share its code, see who is watching, and the host's
/// controls. The video, playlist and chat live in the popout player.</summary>
internal sealed class RoomScreen
{
    private const float PadX = 16f;
    private const float MemberRowHeight = 46f;
    private const float ActionHeight = 40f;
    private const double CopiedFeedbackSeconds = 1.8;

    private static readonly int[] PublishDurationsMinutes = [60, 120, 180, 240];

    /// <summary>Owner badge fill; the flair pill takes its colour as a hex string.</summary>
    private const string OwnerBadgeHex = "#C8963C";

    private enum Confirm
    {
        None,
        Kick,
        Leave,
        End,
    }

    private readonly IAppCapabilities _caps;
    private readonly AetherHubContext _hub;
    private readonly EchoStateService _state;
    private readonly IEchoHost _host;
    private readonly Func<Guid?> _myAccountId;
    private readonly Action _back;
    private readonly EntranceAnimation _entrance = new();

    private string _createName = string.Empty;
    private volatile string? _error;
    private volatile bool _busy;
    private volatile string? _kickedFrom;
    private Confirm _confirm;
    private Guid _confirmMember;
    private string _confirmMemberName = string.Empty;
    private float _panelH;
    private double _copiedAt = double.MinValue;
    private volatile bool _publishOpen;
    private float _publishPanelH;
    private string _publishDescription = string.Empty;
    private int _publishDurationIdx = 1;
    private bool _publishPublic = true;
    private volatile string? _publishError;

    public RoomScreen(IAppCapabilities caps, AetherHubContext hub, EchoStateService state, IEchoHost host,
        Func<Guid?> myAccountId, Action back)
    {
        _caps = caps;
        _hub = hub;
        _state = state;
        _host = host;
        _myAccountId = myAccountId;
        _back = back;
        state.Kicked += push => _kickedFrom = push.RoomName;
    }

    public void OnShow()
    {
        _entrance.Arm();
        _error = null;
        _confirm = Confirm.None;
        _publishOpen = false;
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();

        ImGui.Dummy(new Vector2(0f, Px(2f)));
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("os.echo_back"), FontAwesomeIcon.Film))
        {
            _back();
        }
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        var winW = ImGui.GetWindowSize().X;
        if (_state.EndReason is { } reason)
        {
            DrawClosedNotice(Loc.T("os.echo_room_ended_title"), EndReasonText(reason));
        }
        else if (_kickedFrom is { } roomName)
        {
            DrawClosedNotice(Loc.T("os.echo_room_kicked_title"), Loc.T("os.echo_room_kicked_notice", roomName));
        }
        else if (_state.Room is { } room)
        {
            DrawRoom(ctx, winW, room);
        }
        else
        {
            DrawCreate(winW);
        }

        ImGui.Dummy(new Vector2(0f, Px(18f)));
        _entrance.EndFrame();
    }

    /// <summary>The confirm panels, drawn by the app after the body child so they layer above it.</summary>
    public void DrawOverlays()
    {
        if (_publishOpen)
        {
            DrawPublishOverlay();
            return;
        }
        if (_confirm == Confirm.None)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _confirm = Confirm.None;
            return;
        }

        var pending = _confirm;
        var confirmed = false;
        var dismissed = DrawPageOverlayPanel("echoConfirm", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _panelH, Px(200f), innerW =>
        {
            var (icon, title, body, label) = pending switch
            {
                Confirm.Kick => (FontAwesomeIcon.UserSlash, Loc.T("os.echo_room_kick_title", _confirmMemberName),
                    Loc.T("os.echo_room_kick_body"), Loc.T("os.echo_room_kick_confirm")),
                Confirm.End => (FontAwesomeIcon.PowerOff, Loc.T("os.echo_room_end_title"),
                    Loc.T("os.echo_room_end_body"), Loc.T("os.echo_room_end_confirm")),
                _ => (FontAwesomeIcon.SignOutAlt, Loc.T("os.echo_room_leave_title"),
                    Loc.T("os.echo_room_leave_body"), Loc.T("os.echo_room_leave_confirm")),
            };

            ModalUi.Header(innerW, icon, title, UiColors.Danger);
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, body);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            var btnW = (innerW - Px(10f)) * 0.5f;
            PushDangerButton();
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (Button($"{label}##echoConfirmOk", new Vector2(btnW, Px(32f))))
            {
                confirmed = true;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
            ImGui.SameLine(0f, Px(10f));
            if (ModalUi.Button($"{Loc.T("common.cancel")}##echoConfirmCancel", btnW))
            {
                _confirm = Confirm.None;
            }
        });

        if (confirmed)
        {
            _confirm = Confirm.None;
            RunConfirmed(pending);
        }
        else if (dismissed)
        {
            _confirm = Confirm.None;
        }
    }

    private void OpenPublish(EchoRoomSnapshotDto room)
    {
        _publishOpen = true;
        _publishPanelH = 0f;
        _publishError = null;
        if (_publishDescription.Trim().Length == 0)
        {
            _publishDescription = Loc.T("os.echo_room_publish_default", room.Name);
        }
    }

    /// <summary>The short "publish this room as a hangout" form. Everything a hangout needs that the room
    /// cannot supply is either detected (world) or fixed (starts now, no attendee cap).</summary>
    private void DrawPublishOverlay()
    {
        if (_state.Room is not { } room)
        {
            _publishOpen = false;
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _publishOpen = false;
            return;
        }

        var t = ThemeService.Current;
        var location = VenueLocationDetector.Detect();
        var dismissed = DrawPageOverlayPanel("echoPublish", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _publishPanelH, Px(320f), innerW =>
        {
            ModalUi.Header(innerW, FontAwesomeIcon.Bullhorn, Loc.T("os.echo_room_publish_title"), t.Accent);
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, Loc.T("os.echo_room_publish_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            DrawFieldLabel(Loc.T("os.echo_room_publish_desc"), t);
            InputTextMultilineWithPaste("##echoPublishDesc", ref _publishDescription,
                HangoutLimits.DescriptionRawMaxLength, new Vector2(innerW, Px(64f)));
            var descLen = EmojiText.EffectiveLength(_publishDescription.Trim());
            ImGui.TextColored(descLen > HangoutLimits.DescriptionMaxLength ? UiColors.Danger : UiColors.Hint,
                $"{descLen}/{HangoutLimits.DescriptionMaxLength}");

            DrawFieldLabel(Loc.T("os.echo_room_publish_duration"), t);
            ImGui.SetNextItemWidth(innerW);
            var durations = PublishDurationsMinutes
                .Select(m => Loc.T("os.echo_room_publish_hours", m / 60))
                .ToArray();
            ImGui.Combo("##echoPublishDur", ref _publishDurationIdx, durations, durations.Length);

            ImGui.Spacing();
            ImGui.Checkbox(Loc.T("os.echo_room_publish_public"), ref _publishPublic);
            HandOnHover();

            var worldKnown = location.World.Length > 0;
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Hint, worldKnown
                ? $"{location.DataCenter} · {location.World}"
                : Loc.T("os.echo_room_publish_no_world"));
            if (_publishError is { } err)
            {
                ImGui.TextColored(UiColors.Danger, err);
            }
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            var btnW = (innerW - Px(10f)) * 0.5f;
            var ready = worldKnown && !_busy && descLen > 0 && descLen <= HangoutLimits.DescriptionMaxLength;
            using (ImRaii.Disabled(!ready))
            {
                if (ModalUi.Button($"{Loc.T("os.echo_room_publish_confirm")}##echoPublishOk", btnW))
                {
                    Publish(room, location);
                }
            }
            ImGui.SameLine(0f, Px(10f));
            if (ModalUi.Button($"{Loc.T("common.cancel")}##echoPublishCancel", btnW))
            {
                _publishOpen = false;
            }
        });

        if (dismissed && !_busy)
        {
            _publishOpen = false;
        }
    }

    private void Publish(EchoRoomSnapshotDto room, DetectedVenueLocation location)
    {
        var req = new CreateHangoutRequest(
            Category: HangoutCategory.WatchParty,
            Description: _publishDescription.Trim(),
            DataCenter: location.DataCenter,
            World: location.World,
            Location: room.Name,
            StartUtc: DateTimeOffset.UtcNow,
            DurationMinutes: PublishDurationsMinutes[Math.Clamp(_publishDurationIdx, 0, PublishDurationsMinutes.Length - 1)],
            MaxAttendees: null,
            UnlistWhenFull: false,
            IsPublic: _publishPublic);

        _publishError = null;
        _busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.PublishEchoRoomHangoutAsync(room.Id, req).ConfigureAwait(false);
                _publishOpen = false;
                _publishDescription = string.Empty;
            }
            catch (Exception ex)
            {
                _publishError = HubErrorText.Localize(ex);
                UiHost.Log.Warning(ex, "[EchoRoom] Publishing the room as a hangout failed.");
            }
            finally
            {
                await RefreshAsync(room.Id).ConfigureAwait(false);
                _busy = false;
            }
        });
    }

    /// <summary>Takes the room's hangout listing down. The room itself keeps running, and republishing is
    /// allowed straight after.</summary>
    private void StopHangout(Guid roomId)
    {
        _error = null;
        _busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.EndMyHangoutAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _error = HubErrorText.Localize(ex);
                UiHost.Log.Warning(ex, "[EchoRoom] Stopping the room's hangout failed.");
            }
            finally
            {
                await RefreshAsync(roomId).ConfigureAwait(false);
                _busy = false;
            }
        });
    }

    /// <summary>Re-reads the snapshot so the published state is the server's answer rather than an assumption;
    /// run after a failed attempt too, since the failure may be that reality already moved.</summary>
    private async Task RefreshAsync(Guid roomId)
    {
        try
        {
            _state.ApplySnapshot(await _hub.GetEchoRoomSyncAsync(roomId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[EchoRoom] Could not refresh the room.");
        }
    }

    private void DrawCreate(float winW)
    {
        DrawHero("echo_room_create", FontAwesomeIcon.Crown, Loc.T("os.echo_room_create_title"),
            Loc.T("os.echo_room_create_intro"), 30f);

        var name = _createName.Trim();
        ImGui.SetCursorPosX(Px(PadX));
        DrawFieldLabel(Loc.T("os.echo_room_name_label"), ThemeService.Current);
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, OsDrawShared.White(0.07f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(10f), Px(9f)));
        bool submitted;
        using (UiFonts.H3?.Push())
        {
            submitted = ImGui.InputTextWithHint("##echoRoomName", Loc.T("os.echo_room_name_hint"), ref _createName,
                EchoLimits.RoomNameMaxLength, ImGuiInputTextFlags.EnterReturnsTrue);
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        var counter = $"{name.Length}/{EchoLimits.RoomNameMaxLength}";
        ImGui.SetCursorPosX(winW - Px(PadX) - ImGui.CalcTextSize(counter).X);
        ImGui.TextColored(name.Length >= EchoLimits.RoomNameMaxLength ? UiColors.Amber : UiColors.Hint, counter);

        if (_error is { } error)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            DrawCenteredParagraph(error, winW - Px(48f), UiColors.Danger);
        }

        ImGui.Dummy(new Vector2(0f, Px(14f)));
        var ready = name.Length > 0 && !_busy && _hub.IsConnected;
        if (DrawPrimaryButton(_busy ? Loc.T("os.echo_room_creating") : Loc.T("os.echo_room_create_btn"), ready)
            || (submitted && ready))
        {
            Create(name);
        }

        if (!_hub.IsConnected)
        {
            ImGui.Dummy(new Vector2(0f, Px(14f)));
            DrawInfoCallout(Loc.T("os.echo_home_offline"), UiColors.Muted, FontAwesomeIcon.ExclamationTriangle);
        }
    }

    private void DrawRoom(OsAppContext ctx, float winW, EchoRoomSnapshotDto room)
    {
        var t = ThemeService.Current;
        var isOwner = _myAccountId() is { } me && room.OwnerAccountId == me;

        ImGui.SetCursorPosX(Px(PadX));
        using (ctx.TitleFont?.Push())
        {
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(t.AccentLight, room.Name);
            ImGui.PopTextWrapPos();
        }
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        ImGui.SetCursorPosX(Px(PadX));
        DrawFieldLabel(Loc.T("os.echo_room_code_label"), t);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        var copied = ImGui.GetTime() - _copiedAt < CopiedFeedbackSeconds;
        if (DrawSecretBox("##echoRoomCode", room.Code, Loc.T(copied ? "os.echo_room_copied" : "os.echo_room_copy")))
        {
            _caps.System.CopyToClipboard(room.Code);
            _copiedAt = ImGui.GetTime();
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawCenteredParagraph(Loc.T("os.echo_room_code_hint"), winW - Px(48f), UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        var gap = Px(10f);
        var rowW = winW - Px(PadX) * 2f;
        var canShare = _caps.Share.CanShare(ShareTypes.Echo);
        var openW = canShare ? (rowW - gap) * 0.5f : rowW;
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        ImGui.PushStyleColor(ImGuiCol.Button, t.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.AccentLight);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.AccentDark);
        if (!_host.RuntimeReady)
        {
            ImGui.BeginDisabled();
        }
        if (Button($"{Loc.T("os.echo_room_open")}##echoRoomOpen", new Vector2(openW, Px(ActionHeight))))
        {
            _host.OpenRoom();
        }
        if (!_host.RuntimeReady)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleColor(3);
        if (canShare)
        {
            ImGui.SameLine(0f, gap);
            PushThemeButton(t);
            if (Button($"{Loc.T("os.echo_room_share")}##echoRoomShare",
                    new Vector2((rowW - gap) * 0.5f, Px(ActionHeight))))
            {
                Share(room);
            }
            PopThemeButton();
        }
        ImGui.PopStyleVar();
        ImGui.Dummy(new Vector2(0f, Px(16f)));

        if (isOwner)
        {
            ImGui.SetCursorPosX(Px(PadX));
            if (DrawToggleSwitch("##echoHostOnly", Loc.T("os.echo_room_hostonly_label"), room.HostOnly) && !_busy)
            {
                SetHostOnly(room.Id, !room.HostOnly);
            }
            HandOnHover();
            ImGui.Dummy(new Vector2(0f, Px(4f)));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Hint, Loc.T("os.echo_room_hostonly_hint"));
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, Px(14f)));

            var published = room.HangoutId is not null;
            if (published)
            {
                DrawInfoCallout(Loc.T("os.echo_room_published"), UiColors.LiveGreen, FontAwesomeIcon.Bullhorn);
                ImGui.Dummy(new Vector2(0f, Px(10f)));
            }

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
            if (published)
            {
                PushDangerButton();
            }
            else
            {
                PushThemeButton(t);
            }
            var label = Loc.T(published ? "os.echo_room_unpublish" : "os.echo_room_publish");
            if (Button($"{label}##echoRoomPublish", new Vector2(rowW, Px(ActionHeight))) && !_busy)
            {
                if (published)
                {
                    StopHangout(room.Id);
                }
                else
                {
                    OpenPublish(room);
                }
            }
            if (published)
            {
                ImGui.PopStyleColor(3);
            }
            else
            {
                PopThemeButton();
            }
            ImGui.PopStyleVar();
            ImGui.Dummy(new Vector2(0f, Px(4f)));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Hint,
                Loc.T(published ? "os.echo_room_unpublish_hint" : "os.echo_room_publish_hint"));
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, Px(14f)));
        }

        DrawSectionHeader(Loc.T("os.echo_room_members_title", room.Members.Length, EchoLimits.MaxMembers), PadX);
        foreach (var member in room.Members)
        {
            DrawMemberRow(winW, member, isOwner);
        }

        if (_error is { } error)
        {
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            DrawCenteredParagraph(error, winW - Px(48f), UiColors.Danger);
        }

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        PushDangerButton();
        if (Button($"{Loc.T("os.echo_room_leave")}##echoRoomLeave", new Vector2(rowW, Px(ActionHeight))))
        {
            _confirm = Confirm.Leave;
            _panelH = 0f;
        }
        ImGui.PopStyleColor(3);
        if (isOwner)
        {
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            ImGui.SetCursorPosX(Px(PadX));
            PushDangerButton();
            if (Button($"{Loc.T("os.echo_room_end")}##echoRoomEnd", new Vector2(rowW, Px(ActionHeight))))
            {
                _confirm = Confirm.End;
                _panelH = 0f;
            }
            ImGui.PopStyleColor(3);
        }
        ImGui.PopStyleVar();
    }

    private void DrawMemberRow(float winW, EchoMemberDto member, bool isOwner)
    {
        var dl = ImGui.GetWindowDrawList();
        var rowH = Px(MemberRowHeight);
        var rowW = winW - Px(PadX) * 2f;
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + Px(PadX), origin.Y);
        var kickable = isOwner && !member.IsOwner;
        var kickSize = Px(28f);

        if (kickable)
        {
            ImGui.SetCursorScreenPos(new Vector2(tl.X + rowW - kickSize, tl.Y + (rowH - kickSize) * 0.5f));
            if (ImGui.InvisibleButton($"##echoKick{member.AccountId:N}", new Vector2(kickSize, kickSize)))
            {
                _confirm = Confirm.Kick;
                _confirmMember = member.AccountId;
                _confirmMemberName = member.DisplayName;
                _panelH = 0f;
            }
            HandOnHover();
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("os.echo_room_kick"));
            }
            var kickCenter = ImGui.GetItemRectMin() + new Vector2(kickSize * 0.5f);
            dl.AddCircleFilled(kickCenter, kickSize * 0.5f, OsDrawShared.White(hovered ? 0.14f : 0.07f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(12f), kickCenter,
                ImGui.ColorConvertFloat4ToU32(hovered ? UiColors.Danger : UiColors.Muted));
        }

        var avatarR = Px(15f);
        var avatarC = new Vector2(tl.X + avatarR + Px(2f), tl.Y + rowH * 0.5f);
        DrawMemberAvatar(dl, avatarC, avatarR, member);

        var nameX = avatarC.X + avatarR + Px(10f);
        var nameW = rowW - (nameX - tl.X) - (kickable ? kickSize + Px(8f) : 0f);
        var lineH = ImGui.GetTextLineHeight();
        if (member.IsOwner)
        {
            var badge = Loc.T("os.echo_room_owner");
            var badgeW = FlairPillWidth(badge);
            nameW -= badgeW + Px(8f);
            DrawFlairPill(dl, new Vector2(nameX + nameW + Px(8f), tl.Y + (rowH - lineH - Px(4f)) * 0.5f),
                badge, string.Empty, OwnerBadgeHex);
        }
        dl.AddText(new Vector2(nameX, tl.Y + (rowH - lineH) * 0.5f), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(member.DisplayName, MathF.Max(Px(40f), nameW)));

        dl.AddLine(new Vector2(tl.X, tl.Y + rowH), new Vector2(tl.X + rowW, tl.Y + rowH), UiColors.Divider, 1f);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(rowW, rowH));
    }

    /// <summary>An initial on a colour disc keyed by the account id: the OS avatar arrives as a remote URL and
    /// the app layer has no loader for one.</summary>
    private static void DrawMemberAvatar(ImDrawListPtr dl, Vector2 center, float radius, EchoMemberDto member)
    {
        var palette = UiColors.CategoryPalette;
        var swatch = palette[(int)((uint)member.AccountId.GetHashCode() % (uint)palette.Length)];
        dl.AddCircleFilled(center, radius, swatch);

        var initial = InitialOf(member.DisplayName);
        dl.AddText(center - ImGui.CalcTextSize(initial) * 0.5f, OsDrawShared.White(0.95f), initial);
    }

    private static string InitialOf(string name)
    {
        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                return Rune.ToUpperInvariant(rune).ToString();
            }
        }
        return "?";
    }

    private void DrawClosedNotice(string title, string body)
    {
        DrawHero("echo_room_closed", FontAwesomeIcon.PowerOff, title, body, 34f);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        if (DrawPrimaryButton(Loc.T("os.echo_room_ended_back"), true))
        {
            _kickedFrom = null;
            _state.Clear();
            _back();
        }
    }

    private static string EndReasonText(EchoEndReason reason) => Loc.T(reason switch
    {
        EchoEndReason.OwnerEnded => "os.echo_room_ended_owner",
        EchoEndReason.OwnerLeft => "os.echo_room_ended_left",
        EchoEndReason.Moderation => "os.echo_room_ended_moderation",
        _ => "os.echo_room_ended_empty",
    });

    private void Share(EchoRoomSnapshotDto room)
    {
        _caps.Share.Offer(
            new ShareItem
            {
                Type = ShareTypes.Echo,
                RefId = room.Id.ToString("D"),
                Title = room.Name,
                Subtitle = room.Code,
                SourceAppId = EchoVidyaApp.AppId,
            },
            Loc.T("os.echo_room_share_title"));
    }

    private void RunConfirmed(Confirm action)
    {
        if (_state.Room is not { } room)
        {
            return;
        }
        switch (action)
        {
            case Confirm.Kick:
            {
                var target = _confirmMember;
                Run(() => _hub.KickEchoMemberAsync(room.Id, target));
                break;
            }
            case Confirm.End:
                Run(async () =>
                {
                    await _hub.EndEchoRoomAsync(room.Id).ConfigureAwait(false);
                    _state.Clear();
                });
                break;
            default:
                Run(async () =>
                {
                    await _hub.LeaveEchoRoomAsync(room.Id).ConfigureAwait(false);
                    _state.Clear();
                });
                break;
        }
    }

    private void Create(string name)
    {
        Run(async () =>
        {
            var snapshot = await _hub.CreateEchoRoomAsync(new CreateEchoRoomRequest(name)).ConfigureAwait(false);
            _state.ApplySnapshot(snapshot);
            _createName = string.Empty;
        });
    }

    private void SetHostOnly(Guid roomId, bool hostOnly) => Run(() => _hub.SetEchoHostOnlyAsync(roomId, hostOnly));

    private void Run(Func<Task> action)
    {
        _error = null;
        _busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _error = HubErrorText.Localize(ex);
                UiHost.Log.Warning(ex, "[EchoRoom] A room call failed.");
            }
            finally
            {
                _busy = false;
            }
        });
    }
}
