using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Echo;
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

    private const string ImportPopupId = "##echoPlaylistImport";
    private const float NoticeVisibleSeconds = 5f;

    private string _addInput = string.Empty;
    private string? _playlistError;
    private string? _playlistNotice;
    private float _noticeRemaining;
    private bool _playlistSeeded;

    private bool _importBusy;
    private bool _openImportPopup;
    private EchoPlaylistFetchResult? _pendingImport;
    private string _pendingImportVideoId = string.Empty;

    private readonly Dictionary<Guid, float> _rowEnter = new();
    private readonly HashSet<Guid> _knownEntries = new();

    /// <summary>The queue drag. `_dragPress` is the row a press landed on, which only becomes a drag once
    /// the cursor has travelled: a press that never moves is still a tap that plays the row.</summary>
    private Guid? _dragPress;
    private Guid? _dragEntry;
    private string _dragTitle = string.Empty;
    private int _dragFrom;
    private int _dragInsert;
    private bool _dragEnded;

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
        if (_playlistNotice is null)
        {
            return;
        }
        _noticeRemaining -= dt;
        if (_noticeRemaining <= 0f)
        {
            _playlistNotice = null;
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
        var addEnabled = canControl && !full && !_importBusy;

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

        if (_importBusy)
        {
            ImGui.TextColored(UiColors.Muted, Loc.T("echo.playlist_reading"));
        }
        else if (_playlistNotice is { } notice)
        {
            ImGui.TextColored(UiColors.Muted, notice);
        }
        if (_playlistError is { } error)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + body.X);
            ImGui.TextColored(UiColors.Danger, error);
            ImGui.PopTextWrapPos();
        }

        DrawImportConfirm();

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
        var listTop = ImGui.GetCursorScreenPos().Y;
        _dragEnded = false;
        for (var i = 0; i < playlist.Count; i++)
        {
            DrawPlaylistRow(t, playlist[i], i, currentId, canControl);
        }
        PrunePlaylistAnimation(playlist);
        DrawQueueDrag(t, playlist, listTop);
    }

    /// <summary>The drag over the queue: where the held row would land, and the row itself under the
    /// cursor. The slot is read off the mouse's Y against the list's own uniform row step rather than by
    /// hit-testing each row, so a cursor between two rows still answers, and one past the last row means
    /// the end of the queue.</summary>
    private void DrawQueueDrag(ThemeDefinition t, IReadOnlyList<EchoPlaylistEntryDto> playlist, float listTop)
    {
        if (_dragEntry is not { } dragged)
        {
            return;
        }

        var step = Px(PlaylistRowH) + Px(2f);
        var mouse = ImGui.GetMousePos();
        _dragInsert = Math.Clamp(
            (int)MathF.Floor((mouse.Y - listTop + (step * 0.5f)) / step), 0, playlist.Count);

        var width = ImGui.GetContentRegionAvail().X;
        var left = ImGui.GetWindowPos().X + Px(4f);
        var caretY = listTop + (_dragInsert * step) - Px(1f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(new Vector2(left, caretY), new Vector2(left + width, caretY + Px(2f)),
            t.AccentU32, Px(1f));

        // On the foreground list, so the held row is not clipped by the queue's own scroller.
        var fg = ImGui.GetForegroundDrawList();
        var cardW = MathF.Min(width, Px(260f));
        var cardTl = new Vector2(mouse.X - (cardW * 0.5f), mouse.Y - Px(14f));
        fg.AddRectFilled(cardTl, cardTl + new Vector2(cardW, Px(28f)), 0xE0221E2Au, Px(6f));
        fg.AddRect(cardTl, cardTl + new Vector2(cardW, Px(28f)), t.AccentU32, Px(6f), ImDrawFlags.RoundCornersAll, Px(1f));
        fg.AddText(cardTl + new Vector2(Px(8f), Px(6f)), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(_dragTitle, cardW - Px(16f)));

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            EndQueueDrag();
            return;
        }
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            return;
        }

        // The insert slot counts the held row where it still sits, so a move down lands one short.
        var final = _dragInsert > _dragFrom ? _dragInsert - 1 : _dragInsert;
        EndQueueDrag();
        if (final != _dragFrom)
        {
            MoveEntry(dragged, final);
        }
    }

    private void EndQueueDrag()
    {
        _dragEntry = null;
        _dragPress = null;
        _dragEnded = true;
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

        // A press arms the row; it only becomes a drag once the cursor has travelled, so a tap still
        // plays it. Reordering rides the same permission as adding, so a host-only room keeps it.
        if (canControl && ImGui.IsItemActivated())
        {
            _dragPress = entry.Id;
        }
        if (_dragEntry is null && _dragPress == entry.Id && ImGui.IsItemActive()
            && ImGui.GetMouseDragDelta().Length() > Px(6f))
        {
            _dragEntry = entry.Id;
            _dragFrom = index;
            _dragInsert = index;
            _dragTitle = entry.Title is { Length: > 0 } held
                ? held
                : EchoMediaRefs.DisplayHint((EchoMediaSource)entry.Source, entry.VideoId) ?? entry.VideoId;
        }
        var dragging = _dragEntry == entry.Id;
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
        var label = entry.Title
            ?? EchoMediaRefs.DisplayHint((EchoMediaSource)entry.Source, entry.VideoId)
            ?? entry.VideoId;
        var title = TruncateToWidth(label, MathF.Max(Px(20f), textRight - textLeft));
        dl.AddText(new Vector2(textLeft, tl.Y + Px(7f)), Fade(ImGui.GetColorU32(titleColor), alpha), title);

        var subtitle = entry.AddedByName is { Length: > 0 } author
            ? string.Format(CultureInfo.CurrentCulture, Loc.T("echo.added_by"), author)
            : Loc.T("echo.added_by_unknown");
        var subtitleY = tl.Y + rowH - Px(19f);
        // An entry nobody could play says so instead of saying it is live: unplayable is the more useful
        // fact, and the two badges share the one slot at the end of the row.
        var badgeLabel = entry.Failed
            ? Loc.T("echo.unavailable")
            : entry.IsLive ? Loc.T("echo.live") : null;
        var badgeW = badgeLabel is null ? 0f : ImGui.CalcTextSize(badgeLabel).X + Px(16f);
        dl.AddText(new Vector2(textLeft, subtitleY), Fade(UiColors.TextFaint, alpha),
            TruncateToWidth(subtitle, MathF.Max(Px(20f), textRight - textLeft - badgeW)));

        if (entry.Failed)
        {
            DrawFailedBadge(dl, new Vector2(textRight, subtitleY), alpha);
        }
        else if (entry.IsLive)
        {
            DrawLiveBadge(dl, new Vector2(textRight, subtitleY), alpha);
        }

        if (dragging)
        {
            // The held row stays in place, dimmed: the caret says where it is going, and a row that
            // vanished from under the cursor would make the list jump while it is being read.
            dl.AddRectFilled(tl, tl + new Vector2(width, rowH), 0x40000000u, Px(6f));
        }

        if (removeClicked)
        {
            RemoveEntry(entry.Id);
        }
        else if (rowClicked && playable && !dragging && !_dragEnded)
        {
            PlayEntry(entry.Id);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rowH + Px(2f)));
    }

    /// <summary>The broadcast badge: a red dot and the word LIVE, in a pill at the end of the row.</summary>
    private static void DrawLiveBadge(ImDrawListPtr dl, Vector2 rightEdge, float alpha)
    {
        var label = Loc.T("echo.live");
        var size = ImGui.CalcTextSize(label);
        var pad = Px(5f);
        var dot = Px(3f);
        var width = size.X + pad * 3f + dot * 2f;
        var tl = new Vector2(rightEdge.X - width, rightEdge.Y - Px(2f));
        var br = tl + new Vector2(width, size.Y + Px(4f));
        dl.AddRectFilled(tl, br, Fade(UiColors.LiveBadgeFill, alpha), Px(4f));
        dl.AddCircleFilled(new Vector2(tl.X + pad + dot, (tl.Y + br.Y) * 0.5f), dot,
            Fade(UiColors.YouTubeRed, alpha));
        dl.AddText(new Vector2(tl.X + pad * 2f + dot * 2f, tl.Y + Px(2f)),
            Fade(UiColors.YouTubeRed, alpha), label);
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
        _playlistNotice = null;
        _pendingImport = null;
        _pendingImportVideoId = string.Empty;
        _openImportPopup = false;
        _importBusy = false;
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
        _playlistNotice = null;
        if (EchoPlaylistIds.TryParse(input, out var playlistId, out var videoId))
        {
            BeginPlaylistRead(playlistId, videoId);
            return;
        }
        AddSingle(roomId, input);
    }

    private void AddSingle(Guid roomId, string videoRef)
    {
        _ = Task.Run(async () =>
        {
            EchoPlaylistEntryDto entry;
            try
            {
                entry = await _hub.AddEchoPlaylistEntryAsync(roomId, videoRef).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var text = FriendlyHubError(ex);
                _uiActions.Enqueue(() => _playlistError = text);
                Plugin.Log.Warning(ex, "[Echo] Adding a playlist entry failed.");
                return;
            }
            if ((EchoMediaSource)entry.Source == EchoMediaSource.YouTube)
            {
                await StampLiveAsync(roomId, entry.Id, entry.VideoId).ConfigureAwait(false);
            }
        });
    }

    /// <summary>Asks YouTube whether the queued id is broadcasting and tells the room when it is. This is a
    /// second question asked after the video is already queued, so nothing about it can fail the add: an
    /// unreadable page leaves the entry unbadged, which is what it already was.</summary>
    private async Task StampLiveAsync(Guid roomId, Guid entryId, string videoId)
    {
        try
        {
            if (await EchoYouTube.IsLiveAsync(videoId).ConfigureAwait(false) is true)
            {
                await _hub.SetEchoEntryLiveAsync(roomId, entryId, true).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Echo] Could not resolve whether a queued video is live.");
        }
    }

    /// <summary>Reads the playlist on this client, so the server sees one bulk add instead of a video's
    /// worth of calls each. A read that comes back empty falls through to an error rather than the link's
    /// own video: the user pasted a playlist and should be told it could not be read.</summary>
    private void BeginPlaylistRead(string playlistId, string videoId)
    {
        _importBusy = true;
        _ = Task.Run(async () =>
        {
            var result = await EchoPlaylistFetcher.FetchAsync(playlistId).ConfigureAwait(false);
            _uiActions.Enqueue(() =>
            {
                _importBusy = false;
                if (result is null)
                {
                    _playlistError = Loc.T("echo.playlist_failed");
                    return;
                }
                _pendingImport = result;
                _pendingImportVideoId = videoId;
                _openImportPopup = true;
            });
        });
    }

    private void DrawImportConfirm()
    {
        if (_openImportPopup)
        {
            _openImportPopup = false;
            ImGui.OpenPopup(ImportPopupId);
        }
        if (!ImGui.BeginPopup(ImportPopupId))
        {
            return;
        }
        if (_pendingImport is not { } import)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var fits = FittingCount(import.Items.Count);
        var capped = import.TotalCount > fits;
        if (import.Title is { Length: > 0 } name)
        {
            ImGui.TextColored(UiColors.Muted, TruncateToWidth(name, Px(320f)));
        }
        ImGui.TextColored(UiColors.Body, capped
            ? string.Format(CultureInfo.CurrentCulture, Loc.T("echo.playlist_capped"), import.TotalCount, fits)
            : string.Format(CultureInfo.CurrentCulture, Loc.T("echo.playlist_confirm"), fits));
        ImGui.Spacing();

        var confirmLabel = capped ? Loc.T("echo.add") : Loc.T("echo.playlist_add_all");
        if (Button($"{confirmLabel}##echoImportYes", new Vector2(Px(130f), Px(ButtonH))))
        {
            ImGui.CloseCurrentPopup();
            RunImport(import);
        }
        if (_pendingImportVideoId.Length > 0)
        {
            ImGui.SameLine();
            if (Button($"{Loc.T("echo.playlist_add_one")}##echoImportOne", new Vector2(Px(140f), Px(ButtonH)))
                && _state.CurrentRoomId is { } roomId)
            {
                ImGui.CloseCurrentPopup();
                _pendingImport = null;
                AddSingle(roomId, _pendingImportVideoId);
            }
        }
        ImGui.SameLine();
        if (Button($"{Loc.T("common.cancel")}##echoImportNo", new Vector2(Px(100f), Px(ButtonH))))
        {
            ImGui.CloseCurrentPopup();
            _pendingImport = null;
        }
        ImGui.EndPopup();
    }

    private int FittingCount(int available)
    {
        var free = EchoLimits.MaxPlaylistEntries - _state.Playlist.Count;
        return Math.Clamp(free, 0, available);
    }

    private void RunImport(EchoPlaylistFetchResult import)
    {
        _pendingImport = null;
        if (_state.CurrentRoomId is not { } roomId)
        {
            return;
        }
        var fits = FittingCount(import.Items.Count);
        if (fits <= 0)
        {
            _playlistError = Loc.T("echo.playlist_full");
            return;
        }
        var batch = new List<EchoPlaylistImportItem>(fits);
        for (var i = 0; i < fits; i++)
        {
            batch.Add(import.Items[i]);
        }
        _importBusy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var added = await _hub.AddEchoPlaylistEntriesAsync(roomId, batch).ConfigureAwait(false);
                _uiActions.Enqueue(() =>
                {
                    _importBusy = false;
                    _playlistNotice = string.Format(CultureInfo.CurrentCulture,
                        Loc.T("echo.playlist_added"), added);
                    _noticeRemaining = NoticeVisibleSeconds;
                });
            }
            catch (Exception ex)
            {
                var text = FriendlyHubError(ex);
                _uiActions.Enqueue(() =>
                {
                    _importBusy = false;
                    _playlistError = text;
                });
                Plugin.Log.Warning(ex, "[Echo] Importing a playlist failed.");
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

    /// <summary>Sends a reorder once, when the drag is let go. The room's own playlist push is what
    /// actually moves the row, here and on every other screen in the room.</summary>
    private void MoveEntry(Guid entryId, int toIndex)
    {
        if (_state.CurrentRoomId is not { } roomId)
        {
            return;
        }
        RunHub(() => _hub.MoveEchoPlaylistEntryAsync(roomId, entryId, toIndex));
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

        // The ring reaches past the disc, and this child has no padding, so the disc stands in from the edge.
        var avatarR = Px(13f);
        var ringR = avatarR * AetherLove.UI.AvatarRings.Overhang;
        DrawIdentityCircle(dl, member.AccountId, member.DisplayName,
            new Vector2(origin.X + Px(4f) + ringR, origin.Y + rowH * 0.5f), avatarR, member.FrameRef, member.AvatarImage);

        var textLeft = origin.X + Px(8f) + ringR * 2f;
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
