using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.EchoVidya;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Windows;

/// <summary>The room column: the queue anyone with control can drive, and the people watching it.</summary>
public sealed partial class EchoWindow
{
    private const float TabStripH = 28f;
    private const float TabSlideSpeed = 14f;
    private const float PlaylistRowH = 46f;
    private const float MemberRowH = 34f;
    private const float CodeCardH = 62f;
    private const int AddInputMax = 300;
    private const float ConfirmSeconds = 5f;
    private const float CopiedSeconds = 1.6f;

    private string _addInput = string.Empty;
    private string? _playlistError;
    private bool _playlistSeeded;

    private readonly Dictionary<Guid, float> _rowEnter = new();
    private readonly HashSet<Guid> _knownEntries = new();

    private Guid _kickConfirmFor;
    private float _kickConfirmRemaining;
    private float _codeCopiedRemaining;

    private void DrawSidebar(ThemeDefinition t, Vector2 pos, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, PanelFill, Px(PanelRounding));
        dl.AddRect(pos, pos + size, t.AccentWithAlpha(0.20f), Px(PanelRounding), ImDrawFlags.None, 1f);

        ImGui.SetCursorScreenPos(pos);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Px(10f, 10f));
        using var panel = ImRaii.Child("##echoSidebar", size, false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();
        if (!panel)
        {
            return;
        }

        TickTimers();
        DrawPaneTabs(t);

        var body = ImGui.GetContentRegionAvail();
        switch (_pane)
        {
            case SidebarPane.Playlist:
                DrawPlaylistPane(t, body);
                break;
            case SidebarPane.Members:
                DrawMembersPane(t, body);
                break;
            default:
                DrawChatPane(t, body);
                break;
        }
    }

    private void TickTimers()
    {
        var dt = ImGui.GetIO().DeltaTime;
        if (_kickConfirmRemaining > 0f)
        {
            _kickConfirmRemaining -= dt;
            if (_kickConfirmRemaining <= 0f)
            {
                _kickConfirmFor = Guid.Empty;
            }
        }
        if (_codeCopiedRemaining > 0f)
        {
            _codeCopiedRemaining -= dt;
        }
    }

    private void DrawPaneTabs(ThemeDefinition t)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var h = Px(TabStripH);
        var segW = width / 3f;

        dl.AddRectFilled(origin, origin + new Vector2(width, h), 0x1AFFFFFFu, h * 0.5f);

        var target = (float)(int)_pane;
        if (AccessibilityService.ReduceMotion)
        {
            _paneMarker = target;
        }
        else
        {
            _paneMarker = AnimationHelper.Lerp(_paneMarker, target,
                MathF.Min(1f, ImGui.GetIO().DeltaTime * TabSlideSpeed));
        }

        var markerTL = new Vector2(origin.X + segW * _paneMarker + Px(2f), origin.Y + Px(2f));
        var markerSize = new Vector2(segW - Px(4f), h - Px(4f));
        dl.AddRectFilled(markerTL, markerTL + markerSize, t.AccentWithAlpha(0.85f), markerSize.Y * 0.5f);

        for (var i = 0; i < 3; i++)
        {
            var pane = (SidebarPane)i;
            var segTL = new Vector2(origin.X + segW * i, origin.Y);
            ImGui.SetCursorScreenPos(segTL);
            if (ImGui.InvisibleButton($"##echoTab{i}", new Vector2(segW, h)))
            {
                _pane = pane;
                if (pane == SidebarPane.Chat)
                {
                    ClearUnseen();
                }
            }
            HandOnHover();

            var label = PaneLabel(pane);
            var textSize = ImGui.CalcTextSize(label);
            var active = pane == _pane;
            dl.AddText(segTL + new Vector2((segW - textSize.X) * 0.5f, (h - textSize.Y) * 0.5f),
                active ? 0xFFFFFFFFu : ImGui.GetColorU32(UiColors.Subtle), label);
            if (pane == SidebarPane.Chat && !active && _unseen > 0)
            {
                dl.AddCircleFilled(
                    segTL + new Vector2((segW + textSize.X) * 0.5f + Px(6f), h * 0.5f - Px(5f)),
                    Px(3.5f), UiColors.UnreadBadge, 12);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + h + Px(8f)));
    }

    private static string PaneLabel(SidebarPane pane) => pane switch
    {
        SidebarPane.Playlist => Loc.T("echo.tab_playlist"),
        SidebarPane.Members => Loc.T("echo.tab_members"),
        _ => Loc.T("echo.tab_chat"),
    };

    private void DrawPlaylistPane(ThemeDefinition t, Vector2 body)
    {
        var playlist = _state.Playlist;
        var canControl = CanControl();
        var full = playlist.Count >= EchoLimits.MaxPlaylistEntries;
        var addEnabled = canControl && !full;

        var addButtonW = Px(48f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var top = ImGui.GetCursorPosY();

        ImGui.BeginDisabled(!addEnabled);
        ImGui.SetNextItemWidth(MathF.Max(Px(60f), body.X - addButtonW - spacing));
        var submitted = ImGui.InputTextWithHint("##echoAdd", Loc.T("echo.add_hint"), ref _addInput,
            AddInputMax, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var pressed = Button($"{Loc.T("echo.add")}##echoAddBtn", new Vector2(addButtonW, 0f));
        ImGui.EndDisabled();
        if (!addEnabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(Loc.T(full ? "echo.playlist_full" : "echo.host_only_tip"));
        }
        if ((submitted || pressed) && addEnabled)
        {
            SubmitPlaylistAdd();
        }

        if (_playlistError is { } error)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + body.X);
            ImGui.TextColored(UiColors.Danger, error);
            ImGui.PopTextWrapPos();
        }

        var listH = MathF.Max(Px(40f), body.Y - (ImGui.GetCursorPosY() - top) - Px(4f));
        using var list = ImRaii.Child("##echoPlaylist", new Vector2(body.X, listH), false);
        if (!list)
        {
            return;
        }
        if (playlist.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(UiColors.Muted, Loc.T("echo.queue_empty"));
            return;
        }

        if (!_playlistSeeded)
        {
            _playlistSeeded = true;
            foreach (var entry in playlist)
            {
                _knownEntries.Add(entry.Id);
            }
        }

        var currentId = _state.Playback?.CurrentEntryId;
        for (var i = 0; i < playlist.Count; i++)
        {
            DrawPlaylistRow(t, playlist[i], i, currentId, canControl);
        }
        PrunePlaylistAnimation(playlist);
    }

    private void DrawPlaylistRow(ThemeDefinition t, EchoPlaylistEntryDto entry, int index, Guid? currentId,
        bool canControl)
    {
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var rowH = Px(PlaylistRowH);
        var origin = ImGui.GetCursorScreenPos();
        var (dy, alpha) = RowEntrance(entry.Id);
        var tl = new Vector2(origin.X, origin.Y + dy);
        var current = currentId == entry.Id;
        var removable = IsRoomOwner() || entry.AddedByAccountId == _myAccountId;
        var playable = canControl && !current && !entry.Failed;

        // The hover plate is painted before the row's controls, so their glyphs are never washed over by it.
        var mouse = ImGui.GetMousePos();
        if (ImGui.IsWindowHovered() && mouse.X >= tl.X && mouse.X <= tl.X + width
            && mouse.Y >= tl.Y && mouse.Y <= tl.Y + rowH)
        {
            dl.AddRectFilled(tl, tl + new Vector2(width, rowH), Fade(RowHoverFill, alpha), Px(6f));
        }

        var removeSize = Px(22f);
        var removeTL = new Vector2(tl.X + width - removeSize - Px(4f), tl.Y + (rowH - removeSize) * 0.5f);
        var removeClicked = removable && DrawIconButton($"##echoDrop{entry.Id:N}", removeTL, removeSize,
            FontAwesomeIcon.Times, 0xFF8888AAu, Loc.T("echo.remove"), true, alpha);

        ImGui.SetCursorScreenPos(tl);
        var rowClicked = ImGui.InvisibleButton($"##echoRow{entry.Id:N}", new Vector2(width, rowH));
        if (playable)
        {
            HandOnHover();
        }
        if (current)
        {
            dl.AddRectFilled(tl, new Vector2(tl.X + Px(3f), tl.Y + rowH), Fade(t.AccentU32, alpha), Px(2f));
        }

        var badgeCenter = new Vector2(tl.X + Px(20f), tl.Y + rowH * 0.5f);
        if (current)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Play, Px(12f), badgeCenter, Fade(t.AccentU32, alpha));
        }
        else
        {
            var order = (index + 1).ToString(CultureInfo.InvariantCulture);
            var orderSize = ImGui.CalcTextSize(order);
            dl.AddText(badgeCenter - orderSize * 0.5f, Fade(UiColors.TextMuted, alpha), order);
        }

        var textLeft = tl.X + Px(38f);
        var textRight = removeTL.X - Px(6f);
        var titleColor = entry.Failed ? UiColors.Muted : (current ? t.AccentLight : UiColors.Body);
        var title = TruncateToWidth(entry.Title ?? entry.VideoId, MathF.Max(Px(20f), textRight - textLeft));
        dl.AddText(new Vector2(textLeft, tl.Y + Px(7f)), Fade(ImGui.GetColorU32(titleColor), alpha), title);

        var subtitle = entry.AddedByName is { Length: > 0 } author
            ? string.Format(CultureInfo.CurrentCulture, Loc.T("echo.added_by"), author)
            : Loc.T("echo.added_by_unknown");
        var subtitleY = tl.Y + rowH - Px(19f);
        var badgeW = entry.Failed ? ImGui.CalcTextSize(Loc.T("echo.unavailable")).X + Px(16f) : 0f;
        dl.AddText(new Vector2(textLeft, subtitleY), Fade(UiColors.TextFaint, alpha),
            TruncateToWidth(subtitle, MathF.Max(Px(20f), textRight - textLeft - badgeW)));

        if (entry.Failed)
        {
            DrawFailedBadge(dl, new Vector2(textRight, subtitleY), alpha);
        }

        if (removeClicked)
        {
            RemoveEntry(entry.Id);
        }
        else if (rowClicked && playable)
        {
            PlayEntry(entry.Id);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rowH + Px(2f)));
    }

    private static void DrawFailedBadge(ImDrawListPtr dl, Vector2 rightEdge, float alpha)
    {
        var label = Loc.T("echo.unavailable");
        var size = ImGui.CalcTextSize(label);
        var pad = Px(5f);
        var tl = new Vector2(rightEdge.X - size.X - pad * 2f, rightEdge.Y - Px(2f));
        var br = tl + new Vector2(size.X + pad * 2f, size.Y + Px(4f));
        dl.AddRectFilled(tl, br, Fade(UiColors.DangerBoxFill, alpha), Px(4f));
        dl.AddText(tl + new Vector2(pad, Px(2f)), Fade(UiColors.DangerBoxBorder, alpha), label);
    }

    /// <summary>Vertical offset and alpha for a row that has just appeared; (0, 1) once it has settled.</summary>
    private (float Dy, float Alpha) RowEntrance(Guid entryId)
    {
        if (AccessibilityService.ReduceMotion)
        {
            _knownEntries.Add(entryId);
            return (0f, 1f);
        }
        if (_knownEntries.Add(entryId))
        {
            _rowEnter[entryId] = 0f;
        }
        if (!_rowEnter.TryGetValue(entryId, out var progress))
        {
            return (0f, 1f);
        }
        progress += ImGui.GetIO().DeltaTime / RowEnterSeconds;
        if (progress >= 1f)
        {
            _rowEnter.Remove(entryId);
            return (0f, 1f);
        }
        _rowEnter[entryId] = progress;
        var eased = 1f - MathF.Pow(1f - progress, 3f);
        return (Px(10f) * (1f - eased), eased);
    }

    private void PrunePlaylistAnimation(IReadOnlyList<EchoPlaylistEntryDto> playlist)
    {
        if (_knownEntries.Count <= playlist.Count)
        {
            return;
        }
        var live = new HashSet<Guid>();
        foreach (var entry in playlist)
        {
            live.Add(entry.Id);
        }
        _knownEntries.IntersectWith(live);
    }

    private void ResetPlaylistAnimation()
    {
        _knownEntries.Clear();
        _rowEnter.Clear();
        _playlistSeeded = false;
        _playlistError = null;
    }

    private void SubmitPlaylistAdd()
    {
        var input = _addInput.Trim();
        if (input.Length == 0 || _state.CurrentRoomId is not { } roomId)
        {
            return;
        }
        _addInput = string.Empty;
        _playlistError = null;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _hub.AddEchoPlaylistEntryAsync(roomId, input).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var text = FriendlyHubError(ex);
                _uiActions.Enqueue(() => _playlistError = text);
                Plugin.Log.Warning(ex, "[Echo] Adding a playlist entry failed.");
            }
        });
    }

    private void RemoveEntry(Guid entryId)
    {
        if (_state.CurrentRoomId is not { } roomId)
        {
            return;
        }
        RunHub(() => _hub.RemoveEchoPlaylistEntryAsync(roomId, entryId));
    }

    private void PlayEntry(Guid entryId)
    {
        if (_state.CurrentRoomId is not { } roomId)
        {
            return;
        }
        RunHub(() => _hub.SetEchoCurrentEntryAsync(roomId, entryId));
    }

    private void DrawMembersPane(ThemeDefinition t, Vector2 body)
    {
        if (_state.Room is not { } room)
        {
            return;
        }
        var top = ImGui.GetCursorPosY();
        DrawCodeCard(t, room, body.X);
        ImGui.Spacing();

        if (room.OwnerAccountId == _myAccountId)
        {
            if (DrawToggleSwitch("##echoHostOnly", Loc.T("echo.host_only"), room.HostOnly))
            {
                var next = !room.HostOnly;
                RunHub(() => _hub.SetEchoHostOnlyAsync(room.Id, next));
            }
            HandOnHover();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + body.X);
            ImGui.TextColored(UiColors.Hint, Loc.T("echo.host_only_hint"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        var listH = MathF.Max(Px(40f), body.Y - (ImGui.GetCursorPosY() - top) - Px(4f));
        using var list = ImRaii.Child("##echoMembers", new Vector2(body.X, listH), false);
        if (!list)
        {
            return;
        }
        foreach (var member in _state.Members)
        {
            DrawMemberRow(t, room, member);
        }
    }

    private void DrawCodeCard(ThemeDefinition t, EchoRoomSnapshotDto room, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var h = Px(CodeCardH);
        dl.AddRectFilled(origin, origin + new Vector2(width, h), 0x14FFFFFFu, Px(8f));
        dl.AddRect(origin, origin + new Vector2(width, h), t.AccentWithAlpha(0.25f), Px(8f), ImDrawFlags.None, 1f);

        var label = _codeCopiedRemaining > 0f ? Loc.T("echo.copied") : Loc.T("echo.room_code");
        dl.AddText(origin + new Vector2(Px(12f), Px(9f)),
            _codeCopiedRemaining > 0f ? ImGui.GetColorU32(UiColors.Success) : ImGui.GetColorU32(UiColors.Hint),
            label);

        using (UiFonts.H2?.Push())
        {
            var codeSize = ImGui.CalcTextSize(room.Code);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                origin + new Vector2(Px(12f), h - codeSize.Y - Px(8f)), 0xFFFFFFFFu, room.Code);
        }

        var copySize = Px(28f);
        if (DrawIconButton("##echoCopyCode",
                new Vector2(origin.X + width - copySize - Px(10f), origin.Y + (h - copySize) * 0.5f),
                copySize, FontAwesomeIcon.Copy, t.AccentU32, Loc.T("echo.copy_code")))
        {
            ImGui.SetClipboardText(room.Code);
            _codeCopiedRemaining = CopiedSeconds;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + h + Px(6f)));
    }

    private void DrawMemberRow(ThemeDefinition t, EchoRoomSnapshotDto room, EchoMemberDto member)
    {
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var rowH = Px(MemberRowH);
        var origin = ImGui.GetCursorScreenPos();
        var mine = member.AccountId == _myAccountId;
        var iAmOwner = room.OwnerAccountId == _myAccountId;
        var confirming = _kickConfirmFor == member.AccountId;

        var actionsRight = origin.X + width - Px(2f);
        if (iAmOwner && !mine)
        {
            var buttonSize = Px(22f);
            if (confirming)
            {
                var cancelTL = new Vector2(actionsRight - buttonSize, origin.Y + (rowH - buttonSize) * 0.5f);
                if (DrawIconButton($"##echoKickNo{member.AccountId:N}", cancelTL, buttonSize,
                        FontAwesomeIcon.Times, 0xFFAAAAAAu, Loc.T("common.cancel")))
                {
                    _kickConfirmFor = Guid.Empty;
                    _kickConfirmRemaining = 0f;
                }
                var okTL = new Vector2(cancelTL.X - buttonSize - Px(4f), cancelTL.Y);
                if (DrawIconButton($"##echoKickYes{member.AccountId:N}", okTL, buttonSize,
                        FontAwesomeIcon.Check, 0xFF5050E0u, Loc.T("echo.kick_confirm")))
                {
                    _kickConfirmFor = Guid.Empty;
                    _kickConfirmRemaining = 0f;
                    KickMember(room.Id, member.AccountId);
                }
                actionsRight = okTL.X - Px(6f);
            }
            else
            {
                var kickTL = new Vector2(actionsRight - buttonSize, origin.Y + (rowH - buttonSize) * 0.5f);
                if (DrawIconButton($"##echoKick{member.AccountId:N}", kickTL, buttonSize,
                        FontAwesomeIcon.UserSlash, 0xFF8888AAu, Loc.T("echo.kick")))
                {
                    _kickConfirmFor = member.AccountId;
                    _kickConfirmRemaining = ConfirmSeconds;
                }
                actionsRight = kickTL.X - Px(6f);
            }
        }

        var avatarR = Px(13f);
        DrawIdentityCircle(dl, member.AccountId, member.DisplayName,
            new Vector2(origin.X + Px(4f) + avatarR, origin.Y + rowH * 0.5f), avatarR, member.FrameRef);

        var textLeft = origin.X + Px(8f) + avatarR * 2f;
        var name = mine
            ? string.Format(CultureInfo.CurrentCulture, Loc.T("echo.you"), member.DisplayName)
            : member.DisplayName;
        var crownW = member.IsOwner ? Px(16f) : 0f;
        var nameSize = ImGui.CalcTextSize(name);
        var shown = TruncateToWidth(name, MathF.Max(Px(20f), actionsRight - textLeft - crownW));
        dl.AddText(new Vector2(textLeft, origin.Y + (rowH - nameSize.Y) * 0.5f),
            ImGui.GetColorU32(mine ? t.AccentLight : UiColors.Body), shown);

        if (member.IsOwner)
        {
            var shownW = ImGui.CalcTextSize(shown).X;
            IconDraw.AddCentered(dl, FontAwesomeIcon.Crown, Px(11f),
                new Vector2(textLeft + shownW + Px(9f), origin.Y + rowH * 0.5f), UiColors.FavoriteStar);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rowH));
    }

    private void KickMember(Guid roomId, Guid accountId) =>
        RunHub(() => _hub.KickEchoMemberAsync(roomId, accountId));
}
