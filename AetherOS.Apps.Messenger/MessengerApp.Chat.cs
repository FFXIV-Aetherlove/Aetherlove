using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using AetherLove.Emoji;
using AetherLove.Emoji.Segments;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messenger;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Messenger;

/// <summary>The conversation view: a faithful port of the AetherLove match chat's rendering pipeline
/// (bubbles, grouping, dividers, entrance animations, reactions, replies, pins, search, input bar) on the
/// messenger's account-keyed data flow. Group chats additionally show the sender name above each group of
/// peer bubbles.</summary>
public sealed partial class MessengerApp
{
    private sealed record OpenChatInfo(
        Guid ChatId,
        MessengerChatKind Kind,
        string Title,
        MessengerContactDto? Contact,
        MessengerGroupDto? Group);

    private Guid _chatId;
    private MessengerChatKind _chatKind;
    private string _inputText = string.Empty;
    private readonly Dictionary<Guid, string> _drafts = new();
    private Guid? _replyingToId;
    private float _scrollToBottom;
    private volatile bool _sending;
    private volatile string? _chatError;
    private DateTimeOffset? _myKeysCreatedAt;
    private readonly EmojiPickerPopup _emojiPicker = new();
    private Guid _reactTargetId;
    private bool _emojiPickerForInput;

    private const float AvatarR = 22f;
    private const float HeaderH = AvatarR * 2f + 10f;
    private const float MenuBtnW = 36f;
    private const float EntranceDuration = 0.3f;
    private const float FlashDuration = 3.0f;
    private const int FlashPulses = 3;
    private const float SearchBarH = 34f;
    private const int MinSearchLen = 3;
    private const int ChatInputMaxLines = 5;
    private const int AutocompleteMax = 5;
    private const float AutocompleteRowH = 40f;
    private static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(5);

    // Valid for one inner-width + line-height; _msgRowH is estimated until the row is first drawn.
    private readonly Dictionary<Guid, float> _msgContentH = new();
    private readonly Dictionary<Guid, float> _msgRowH = new();
    private float _msgCacheWidth = -1f;
    private float _msgCacheLineH = -1f;

    /// <summary>Live-arrival entrance progress (0..1) per message id.</summary>
    private readonly Dictionary<Guid, float> _entryAnim = new();
    private readonly HashSet<Guid> _seenMsgIds = new();
    private bool _seenSeeded;
    private Guid _lastConvChat;
    private int _lastConvCount = -1;

    private Guid _scrollTargetMessageId;
    private float _scrollToMessageTimer;
    private float _flashTimer;

    private bool _msgSearchOpen;
    private bool _msgSearchFocus;
    private string _msgSearchQuery = string.Empty;
    private string _msgSearchApplied = string.Empty;
    private readonly List<Guid> _msgSearchHits = new();
    private int _msgSearchIndex;
    private bool _msgSearchArmed;

    private float _chatWrapWidth;
    private bool _reclaimInputFocus;
    private List<string>? _acMatches;
    private string? _acQuery;
    private bool _acCursorToEnd;
    private string? _chatFavName;

    // Reactions: display chips reconciled against the DTO array with enter/exit animation, plus an
    // optimistic overlay for my own toggles until the server push replaces the array.
    private readonly Dictionary<Guid, List<string>> _rxDisplay = new();
    private readonly Dictionary<Guid, object?> _rxSeenArray = new();
    private readonly Dictionary<(Guid Msg, string Emoji), bool> _rxPending = new();
    private readonly Dictionary<(Guid Msg, string Emoji), float> _rxEnter = new();
    private readonly Dictionary<(Guid Msg, string Emoji), float> _rxExit = new();
    private const float ReactionFxDuration = 0.18f;
    private const int MaxReactionsPerUser = 5;
    private static readonly string[] QuickReactDefaults =
        ["heart", "heart_eyes", "joy", "thumbsup", "fire", "cry"];
    private string[] _quickReact = QuickReactDefaults;
    private bool _reactionUsageDirty;

    private readonly Dictionary<Guid, bool> _pinState = new();
    private readonly Dictionary<Guid, float> _pinAnim = new();
    private const float PinDropDuration = 0.32f;
    private bool _pinnedListOpen;
    private Guid? _pendingReactionPickerId;
    private bool _openedChatFromCategory;
    private readonly HashSet<Guid> _undecryptedRows = new();

    /// <summary>Work marshalled onto the draw loop from hub continuations; drained at the top of Draw.</summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _uiActions = new();

    private OpenChatInfo? ResolveOpenChat()
    {
        if (_chatId == Guid.Empty)
        {
            return null;
        }
        if (_chatKind == MessengerChatKind.Direct)
        {
            var contact = _store.Contact(_chatId);
            return contact is null ? null : new OpenChatInfo(_chatId, _chatKind, contact.PeerName, contact, null);
        }
        var group = _store.Group(_chatId);
        return group is null ? null : new OpenChatInfo(_chatId, _chatKind, group.Name, null, group);
    }

    private void OpenChat(Guid chatId, MessengerChatKind kind)
    {
        if (_view == View.Chat)
        {
            StashDraft();
        }
        _openedChatFromCategory = _view == View.Category;
        _chatId = chatId;
        _chatKind = kind;
        _view = View.Chat;
        _openFadeAt = -1;
        _overlay = Overlay.None;
        _inputText = _drafts.TryGetValue(chatId, out var draft) ? draft : string.Empty;
        _replyingToId = null;
        _chatError = null;
        _scrollToBottom = 1f;
        _scrollTargetMessageId = Guid.Empty;
        _scrollToMessageTimer = 0f;
        _flashTimer = 0f;
        CloseMsgSearch();
        _pinnedListOpen = false;
        _msgContentH.Clear();
        _msgRowH.Clear();
        _entryAnim.Clear();
        _seenMsgIds.Clear();
        _seenSeeded = false;
        _rxDisplay.Clear();
        _rxSeenArray.Clear();
        _rxPending.Clear();
        _rxEnter.Clear();
        _rxExit.Clear();
        _pinState.Clear();
        _pinAnim.Clear();
        _undecryptedRows.Clear();
        _quickReact = ComputeQuickReact();
        _store.ActiveChatId = chatId;
        _store.MarkReadLocal(chatId);
        _shell?.DismissByTag(ChatTag(chatId));
        ResetFailedCards();
        if (kind == MessengerChatKind.Group)
        {
            _ = _sync.EnsureGroupKeysAsync(chatId);
        }
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var convo = await _hub.GetMessengerConversationAsync(chatId, kind).ConfigureAwait(false);
                _store.SetConversation(chatId, convo.Messages);
                _store.SetPeerKeyHistory(chatId, convo.PeerKeyHistory);
                _myKeysCreatedAt = convo.MyKeysCreatedAtUtc ?? _myKeysCreatedAt;
                _uiActions.Enqueue(() =>
                {
                    // Loaded history never plays the entrance animation; only later live arrivals do. A jump
                    // target set by a search hit must survive the fetch instead of being yanked to the bottom.
                    foreach (var m in _store.Conversation(chatId))
                    {
                        _seenMsgIds.Add(m.Id);
                    }
                    _seenSeeded = true;
                    if (_scrollTargetMessageId == Guid.Empty)
                    {
                        _scrollToBottom = 1f;
                    }
                    else
                    {
                        _scrollToMessageTimer = 0.6f;
                        _flashTimer = AccessibilityService.ReduceMotion ? 0f : FlashDuration;
                    }
                });
                await _hub.MarkMessengerReadAsync(chatId, kind).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Surfaced in the chat view itself; the cached conversation keeps rendering underneath.
                if (_chatId == chatId)
                {
                    _chatError = FriendlyHubError(ex);
                }
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] conversation fetch failed.");
            }
        });
    }

    private void CloseChat()
    {
        StashDraft();
        SaveReactionUsageIfDirty();
        _chatId = Guid.Empty;
        _view = _openedChatFromCategory && _categories.Any(c => c.Id == _openCategoryId)
            ? View.Category
            : View.List;
        _openFadeAt = -1;
        _openedChatFromCategory = false;
        _store.ActiveChatId = null;
    }

    private void StashDraft()
    {
        if (_chatId == Guid.Empty)
        {
            return;
        }
        if (string.IsNullOrEmpty(_inputText))
        {
            _drafts.Remove(_chatId);
        }
        else
        {
            _drafts[_chatId] = _inputText;
        }
    }

    private void DrawChatView(OsAppContext ctx, OpenChatInfo open)
    {
        // The context menu can't open the picker itself (a popup opened from inside a popup is discarded);
        // it stages the target and the open happens here at window level.
        if (_pendingReactionPickerId is { } reactId)
        {
            _pendingReactionPickerId = null;
            _reactTargetId = reactId;
            _emojiPickerForInput = false;
            _emojiPicker.Open(OnEmojiPicked);
        }
        RefreshAutocomplete();

        DrawChatHeader(open);

        var removed = open.Contact?.RemovedByPeer == true;
        DrawMessages(open, removed);
        if (removed)
        {
            DrawRemovedNotice(open);
        }
        else
        {
            DrawChatInput(open);
        }
        DrawPinnedOverlay(open);
        _emojiPicker.Draw();
    }

    private void DrawChatHeader(OpenChatInfo open)
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();
        var screenPos = ImGui.GetCursorScreenPos();

        var headerH = Px(HeaderH);
        var centerY = screenPos.Y + headerH * 0.5f;
        var btnTop = screenPos.Y + (headerH - Px(MenuBtnW)) * 0.5f;
        var iconFont = AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon;

        var backBtnX = screenPos.X + Px(2f);
        ImGui.SetCursorScreenPos(new Vector2(backBtnX, btnTop));
        ImGui.InvisibleButton("##msgrBackBtn", Px(MenuBtnW, MenuBtnW));
        var backHovered = ImGui.IsItemHovered();
        if (backHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        if (ImGui.IsItemClicked())
        {
            CloseChat();
            return;
        }
        ImGui.PushFont(iconFont);
        var backIcon = FontAwesomeIcon.ArrowLeft.ToIconString();
        var backIconSz = ImGui.CalcTextSize(backIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(backBtnX + (Px(MenuBtnW) - backIconSz.X) * 0.5f,
                        btnTop + (Px(MenuBtnW) - backIconSz.Y) * 0.5f),
            backHovered ? t.AccentLightU32 : t.AccentU32, backIcon);
        ImGui.PopFont();

        var menuBtnX = screenPos.X + winW - Px(MenuBtnW + 2f);
        ImGui.SetCursorScreenPos(new Vector2(menuBtnX, btnTop));
        ImGui.InvisibleButton("##msgrChatMenuBtn", Px(MenuBtnW, MenuBtnW));
        var menuHovered = ImGui.IsItemHovered();
        var menuClicked = ImGui.IsItemClicked();
        ImGui.PushFont(iconFont);
        var menuIcon = FontAwesomeIcon.Cog.ToIconString();
        var menuIconSz = ImGui.CalcTextSize(menuIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(menuBtnX + (Px(MenuBtnW) - menuIconSz.X) * 0.5f,
                        btnTop + (Px(MenuBtnW) - menuIconSz.Y) * 0.5f),
            menuHovered ? t.AccentLightU32 : t.AccentU32, menuIcon);
        ImGui.PopFont();

        var avatarD = Px(AvatarR * 2f);
        var gap = Px(8f);
        var groupLeft = backBtnX + Px(MenuBtnW) + Px(6f);
        var maxNameW = MathF.Max(0f, menuBtnX - Px(6f) - (groupLeft + avatarD + gap));

        string shownName;
        Vector2 nameSz;
        using (UiFonts.H3?.Push())
        {
            shownName = TruncateToWidth(open.Title, maxNameW);
            nameSz = ImGui.CalcTextSize(shownName);
        }

        var isGroup = open.Kind == MessengerChatKind.Group;
        var avatarCenter = new Vector2(groupLeft + avatarD * 0.5f, centerY);
        DrawAvatar(dl, open.ChatId, open.Title,
            isGroup ? open.Group?.Avatar : open.Contact?.PeerAvatar, isGroup, avatarCenter, Px(AvatarR));
        dl.AddCircle(avatarCenter, Px(AvatarR), t.AccentWithAlpha(0.65f), 0, 1.5f);

        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(groupLeft + avatarD + gap, centerY - nameSz.Y * 0.5f),
                0xFFFFFFFF, shownName);
        }

        var groupW = avatarD + gap + nameSz.X;
        ImGui.SetCursorScreenPos(new Vector2(groupLeft, centerY - avatarD * 0.5f));
        ImGui.InvisibleButton("##msgrPeerGroup", new Vector2(groupW, avatarD));
        if (isGroup && open.Group is { } hoveredGroup && ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.BeginTooltip();
            foreach (var member in hoveredGroup.Members)
            {
                ImGui.TextUnformatted(member.IsOwner ? member.Name + " ★" : member.Name);
            }
            ImGui.EndTooltip();
        }
        if (isGroup && ImGui.IsItemClicked())
        {
            OpenGroupInfo(open.ChatId);
        }

        if (menuClicked)
        {
            ImGui.OpenPopup("##msgrChatMenu");
        }
        if (ImGui.BeginPopup("##msgrChatMenu"))
        {
            DrawChatMenu(open);
            ImGui.EndPopup();
        }

        ImGui.SetCursorScreenPos(new Vector2(screenPos.X, screenPos.Y + Px(HeaderH)));
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawChatMenu(OpenChatInfo open)
    {
        if (DrawIconMenuItem(FontAwesomeIcon.Search, Loc.T("chat.menu_search")))
        {
            ImGui.CloseCurrentPopup();
            OpenMsgSearch();
        }
        var pinnedCount = _store.Conversation(open.ChatId).Count(m => m.PinnedAtUtc is not null);
        if (DrawIconMenuItem(FontAwesomeIcon.Thumbtack, Loc.T("chat.pinned_messages_menu", pinnedCount),
                enabled: pinnedCount > 0))
        {
            ImGui.CloseCurrentPopup();
            _pinnedListOpen = true;
        }
        var chatPinned = open.Contact?.PinnedByMe ?? open.Group?.PinnedByMe ?? false;
        if (DrawIconMenuItem(chatPinned ? FontAwesomeIcon.ThumbtackSlash : FontAwesomeIcon.Thumbtack,
                chatPinned ? Loc.T("os.msgr_unpin_chat") : Loc.T("os.msgr_pin_chat")))
        {
            ImGui.CloseCurrentPopup();
            RunHub(async () =>
            {
                await _hub.SetMessengerChatPinAsync(open.ChatId, open.Kind, !chatPinned).ConfigureAwait(false);
                await _sync.SyncAsync().ConfigureAwait(false);
            });
        }
        if (open.Kind == MessengerChatKind.Group)
        {
            if (DrawIconMenuItem(FontAwesomeIcon.Users, Loc.T("os.msgr_group_info")))
            {
                ImGui.CloseCurrentPopup();
                OpenGroupInfo(open.ChatId);
            }
        }
        else if (open.Contact is { } contact)
        {
            ImGui.Separator();
            if (contact.RemovedByPeer)
            {
                if (DrawIconMenuItem(FontAwesomeIcon.TrashAlt, Loc.T("os.msgr_delete_chat"), UiColors.MenuDanger))
                {
                    ImGui.CloseCurrentPopup();
                    OpenConfirm(
                        Loc.T("os.msgr_delete_chat"),
                        string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_delete_chat_confirm"), open.Title),
                        () =>
                        {
                            var id = open.ChatId;
                            CloseChat();
                            RunHub(() => _hub.RemoveMessengerContactAsync(id));
                        });
                }
            }
            else if (DrawIconMenuItem(FontAwesomeIcon.UserMinus, Loc.T("os.msgr_remove_contact")))
            {
                ImGui.CloseCurrentPopup();
                OpenConfirm(
                    Loc.T("os.msgr_remove_contact"),
                    string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_remove_confirm"), open.Title),
                    () =>
                    {
                        var id = open.ChatId;
                        CloseChat();
                        RunHub(() => _hub.RemoveMessengerContactAsync(id));
                    });
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Ban, Loc.T("os.msgr_block")))
            {
                ImGui.CloseCurrentPopup();
                OpenBlockConfirm(open.ChatId, open.Title);
            }
            if (DrawIconMenuItem(FontAwesomeIcon.ExclamationTriangle, Loc.T("os.msgr_report"), UiColors.MenuReport))
            {
                ImGui.CloseCurrentPopup();
                OpenReport(open.ChatId, open.Title);
            }
        }
    }

    private void OpenMsgSearch()
    {
        _msgSearchOpen = true;
        _msgSearchFocus = true;
    }

    private void CloseMsgSearch()
    {
        _msgSearchOpen = false;
        _msgSearchFocus = false;
        _msgSearchQuery = string.Empty;
        _msgSearchApplied = string.Empty;
        _msgSearchHits.Clear();
        _msgSearchIndex = 0;
        _msgSearchArmed = false;
    }

    private void RecomputeMsgSearch(OpenChatInfo open)
    {
        var q = _msgSearchQuery.Trim();
        if (q == _msgSearchApplied)
        {
            return;
        }
        _msgSearchApplied = q;
        _msgSearchHits.Clear();
        if (q.Length >= MinSearchLen)
        {
            foreach (var m in _store.Conversation(open.ChatId))
            {
                if (Decrypt(m) is { } text && text.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    _msgSearchHits.Add(m.Id);
                }
            }
        }
        _msgSearchIndex = _msgSearchHits.Count - 1;
        _msgSearchArmed = _msgSearchHits.Count > 0;
    }

    private void MsgSearchNavigate(int dir)
    {
        if (_msgSearchHits.Count == 0)
        {
            return;
        }
        if (_msgSearchArmed)
        {
            _msgSearchArmed = false;
        }
        else
        {
            _msgSearchIndex = (_msgSearchIndex + dir + _msgSearchHits.Count) % _msgSearchHits.Count;
        }
        JumpToMessage(_msgSearchHits[_msgSearchIndex]);
    }

    private void JumpToMessage(Guid id)
    {
        _scrollTargetMessageId = id;
        _scrollToMessageTimer = 0.6f;
        _flashTimer = AccessibilityService.ReduceMotion ? 0f : FlashDuration;
        _scrollToBottom = 0f;
    }

    /// <summary>Consumes exactly <see cref="SearchBarH"/> so the message-area sizing matches.</summary>
    private void DrawMessageSearchBar(OpenChatInfo open)
    {
        if (!_msgSearchOpen)
        {
            return;
        }
        var start = ImGui.GetCursorPos();
        var t = ThemeService.Current;
        var iconFont = AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon;
        var avail = ImGui.GetContentRegionAvail().X;
        var btnW = Px(28f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var countW = Px(52f);
        var inputW = MathF.Max(Px(60f), avail - Px(16f) - btnW * 3f - countW - spacing * 4f);

        ImGui.SetCursorPos(new Vector2(Px(8f), start.Y + Px(4f)));
        if (_msgSearchFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _msgSearchFocus = false;
        }
        ImGui.SetNextItemWidth(inputW);
        var submit = ImGui.InputTextWithHint("##msgrSearchInput", Loc.T("chat.search_messages_hint"),
            ref _msgSearchQuery, 100, ImGuiInputTextFlags.EnterReturnsTrue);

        RecomputeMsgSearch(open);

        var hits = _msgSearchHits.Count;
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(hits > 0 ? UiColors.Body : UiColors.Muted, $"{(hits > 0 ? _msgSearchIndex + 1 : 0)}/{hits}");

        ImGui.SameLine();
        PushThemeButton(t);
        ImGui.PushFont(iconFont);
        var prev = ImGui.Button(FontAwesomeIcon.ChevronUp.ToIconString() + "##msgrSearchPrev", new Vector2(btnW, 0f));
        ImGui.SameLine();
        var next = ImGui.Button(FontAwesomeIcon.ChevronDown.ToIconString() + "##msgrSearchNext", new Vector2(btnW, 0f));
        ImGui.SameLine();
        var close = ImGui.Button(FontAwesomeIcon.Times.ToIconString() + "##msgrSearchClose", new Vector2(btnW, 0f));
        ImGui.PopFont();
        PopThemeButton();

        if (submit || prev)
        {
            MsgSearchNavigate(-1);
        }
        if (next)
        {
            MsgSearchNavigate(+1);
        }
        if (close)
        {
            CloseMsgSearch();
        }

        ImGui.SetCursorPos(new Vector2(start.X, start.Y + Px(SearchBarH)));
    }

    private string DisplayText(MessengerMessageDto m, out bool decrypted)
    {
        if (m.DeletedAtUtc is not null)
        {
            decrypted = false;
            return Loc.T("chat.deleted_by_author");
        }
        var text = Decrypt(m);
        decrypted = text is not null;
        return text ?? Loc.T(
            m.Kind == MessengerChatKind.Group && _store.GroupKey(m.ChatId, m.KeyEpoch) is null
                ? "os.msgr_keys_pending"
                : "os.msgr_undecryptable");
    }

    private void DrawMessages(OpenChatInfo open, bool removed)
    {
        DrawMessageSearchBar(open);
        var availableHeight = ImGui.GetWindowSize().Y - Px(HeaderH + 20f)
            - (removed ? RemovedNoticeHeight(open) : InputBarHeight(open))
            - (_msgSearchOpen ? Px(SearchBarH) : 0f);
        PushScrollbarStyle();

        using (var child = ImRaii.Child("MessageArea", new Vector2(0, availableHeight), false))
        {
            PopScrollbarStyle();
            if (!child.Success)
            {
                return;
            }
            var windowWidth = ImGui.GetContentRegionAvail().X;

            var messages = _store.Conversation(open.ChatId);

            // Match-chat parity: a message landing in the open chat sticks the view to the bottom. A search jump
            // in flight keeps priority; switching chats just re-baselines the count without scrolling.
            if (open.ChatId != _lastConvChat)
            {
                _lastConvChat = open.ChatId;
                _lastConvCount = messages.Count;
            }
            else if (messages.Count > _lastConvCount)
            {
                if (_scrollTargetMessageId == Guid.Empty)
                {
                    _scrollToBottom = 1f;
                }
                _lastConvCount = messages.Count;
            }
            else if (messages.Count != _lastConvCount)
            {
                _lastConvCount = messages.Count;
            }

            SegmentEmoji.CaptureRightClick = false;
            SegmentEmoji.RightClickedName = null;

            var lineH = ImGui.GetTextLineHeight();
            if (_msgCacheWidth != windowWidth || _msgCacheLineH != lineH)
            {
                _msgContentH.Clear();
                _msgRowH.Clear();
                _msgCacheWidth = windowWidth;
                _msgCacheLineH = lineH;
            }

            var resetBoundaries = open.Kind == MessengerChatKind.Direct
                ? (_store.PeerKeyHistory(open.ChatId) ?? []).Skip(1).Select(e => e.FromUtc).ToArray()
                : [];

            var bandTop = ImGui.GetScrollY() - availableHeight;
            var bandBot = ImGui.GetScrollY() + availableHeight * 2f;

            var drawnSlot = 0;
            var targetY = -1f;
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                var prev = i > 0 ? messages[i - 1] : null;
                var next = i < messages.Count - 1 ? messages[i + 1] : null;

                if (_seenMsgIds.Add(msg.Id) && _seenSeeded && !AccessibilityService.ReduceMotion)
                {
                    _entryAnim[msg.Id] = 0f;
                }

                var needsDivider = prev is null || msg.CreatedAtUtc.ToLocalTime().Date != prev.CreatedAtUtc.ToLocalTime().Date;
                var isGroupStart = StartsNewGroup(msg, prev);
                var isGroupEnd = next is null || StartsNewGroup(next, msg);

                if (!_msgRowH.TryGetValue(msg.Id, out var rowH))
                {
                    rowH = EstimateRowHeight(open, msg, needsDivider, isGroupStart, isGroupEnd, windowWidth);
                    _msgRowH[msg.Id] = rowH;
                }

                var y0 = ImGui.GetCursorPosY();
                if (msg.Id == _scrollTargetMessageId)
                {
                    targetY = y0;
                }
                if (y0 + rowH < bandTop || y0 > bandBot)
                {
                    ImGui.SetCursorPosY(y0 + rowH);
                    continue;
                }

                if (needsDivider)
                {
                    DrawDayDivider(msg.CreatedAtUtc.LocalDateTime);
                }
                foreach (var boundary in resetBoundaries)
                {
                    if (boundary <= msg.CreatedAtUtc && (prev is null || boundary > prev.CreatedAtUtc))
                    {
                        DrawKeyResetDivider(string.Format(CultureInfo.InvariantCulture,
                            Loc.T("os.msgr_keys_reset"), open.Title));
                    }
                }
                DrawMessageBubble(open, msg, windowWidth, drawnSlot++, isGroupStart, isGroupEnd);
                _msgRowH[msg.Id] = ImGui.GetCursorPosY() - y0;
            }

            if (ImGui.BeginPopup("##msgrEmojiFavMenu"))
            {
                if (_chatFavName is { } cfav)
                {
                    var label = EmojiFavorites.Contains(cfav)
                        ? Loc.T("common.emoji_remove_favorite")
                        : Loc.T("common.emoji_add_favorite");
                    if (ImGui.MenuItem(label) && EmojiFavorites.Toggle(cfav))
                    {
                        EmojiFavoriteFx.Trigger(ImGui.GetMousePos());
                    }
                }
                ImGui.EndPopup();
            }

            if (ImGui.GetIO().MouseWheel != 0f)
            {
                _scrollToBottom = 0f;
                _scrollToMessageTimer = 0f;
            }

            if (_scrollToMessageTimer > 0f && targetY >= 0f)
            {
                var dest = Math.Clamp(targetY - availableHeight * 0.35f, 0f, ImGui.GetScrollMaxY());
                ImGui.SetScrollY(dest);
                _scrollToMessageTimer -= ImGui.GetIO().DeltaTime;
            }
            else if (_scrollToBottom > 0)
            {
                ImGui.SetScrollY(ImGui.GetScrollMaxY());
                _scrollToBottom -= ImGui.GetIO().DeltaTime;
            }

            if (_flashTimer > 0f)
            {
                _flashTimer -= ImGui.GetIO().DeltaTime;
            }
        }
    }

    private bool StartsNewGroup(MessengerMessageDto cur, MessengerMessageDto? prev)
        => prev is null
           || prev.SenderAccountId != cur.SenderAccountId
           || cur.CreatedAtUtc.ToLocalTime().Date != prev.CreatedAtUtc.ToLocalTime().Date
           || cur.CreatedAtUtc - prev.CreatedAtUtc > GroupWindow;

    private static ImDrawFlags BubbleCorners(bool isOwn, bool isStart, bool isEnd)
    {
        if (isStart && isEnd)
        {
            return ImDrawFlags.RoundCornersAll;
        }
        if (isOwn)
        {
            var flags = ImDrawFlags.RoundCornersLeft;
            if (isStart)
            {
                flags |= ImDrawFlags.RoundCornersTopRight;
            }
            if (isEnd)
            {
                flags |= ImDrawFlags.RoundCornersBottomRight;
            }
            return flags;
        }
        else
        {
            var flags = ImDrawFlags.RoundCornersRight;
            if (isStart)
            {
                flags |= ImDrawFlags.RoundCornersTopLeft;
            }
            if (isEnd)
            {
                flags |= ImDrawFlags.RoundCornersBottomLeft;
            }
            return flags;
        }
    }

    private float AuthorNameHeight(OpenChatInfo open, MessengerMessageDto msg, bool isGroupStart)
        => open.Kind == MessengerChatKind.Group && msg.SenderAccountId != _store.MyAccountId && isGroupStart
            ? ImGui.GetTextLineHeight() * 0.9f + Px(3f)
            : 0f;

    /// <summary>The WhatsApp-style sender avatar, drawn in the left gutter aligned to the bottom of a peer's
    /// consecutive message group so readers can tell who wrote what in a group chat.</summary>
    private void DrawSenderGutterAvatar(OpenChatInfo open, MessengerMessageDto msg, ImDrawListPtr dl,
        float rowLeftX, float bubbleBottomY)
    {
        var member = open.Group?.Members.FirstOrDefault(m => m.AccountId == msg.SenderAccountId);
        var avatarR = Px(13f);
        var center = new Vector2(rowLeftX + Px(10f) + avatarR + Px(2f), bubbleBottomY - avatarR);
        DrawAvatar(dl, msg.SenderAccountId, member?.Name ?? "?", member?.Avatar, false, center, avatarR);

        // Hover shows the sender's name; a click opens their member card. Save/restore the layout cursor so the
        // hit area never shifts the message row.
        var saved = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(center - new Vector2(avatarR, avatarR));
        ImGui.InvisibleButton($"##gutterAv_{msg.Id:N}", new Vector2(avatarR * 2f, avatarR * 2f));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(member?.Name ?? "?");
        }
        if (ImGui.IsItemClicked() && open.Group is not null)
        {
            OpenMemberCard(open.ChatId, msg.SenderAccountId);
        }
        ImGui.SetCursorScreenPos(saved);
    }

    private void DrawMessageBubble(OpenChatInfo open, MessengerMessageDto msg, float windowWidth, int slot,
        bool isGroupStart, bool isGroupEnd)
    {
        if (msg.Image is not null)
        {
            DrawImageMessage(open, msg, windowWidth, isGroupStart, isGroupEnd);
            return;
        }
        var mine = msg.SenderAccountId == _store.MyAccountId;
        var text = DisplayText(msg, out var decrypted);
        if (decrypted && _undecryptedRows.Remove(msg.Id))
        {
            // A key arrived (group epoch, key history): every placeholder-based row estimate is stale.
            _msgRowH.Clear();
        }

        if (decrypted && TryDrawCardMessage(open, msg, text, windowWidth, isGroupEnd))
        {
            return;
        }

        var parsed = ParsedMessage.Parse(text);
        var maxBubW = windowWidth * 0.72f;
        var padding = Px(12, 8);
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var groupPeer = open.Kind == MessengerChatKind.Group && !mine;
        var avatarGutter = groupPeer ? Px(34f) : 0f;

        var (entryDy, entryAlpha) = MessageEntrance(msg.Id);
        var fading = entryAlpha < 0.999f;
        if (fading)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, entryAlpha * ImGui.GetStyle().Alpha);
        }

        uint bubbleColor;
        float bubbleLeft;
        if (mine)
        {
            bubbleColor = ImGui.ColorConvertFloat4ToU32(ChatColors.OwnBg);
            bubbleLeft = cursorPos.X + windowWidth - maxBubW - Px(10);
        }
        else
        {
            bubbleColor = ImGui.ColorConvertFloat4ToU32(ChatColors.PeerBg);
            bubbleLeft = cursorPos.X + Px(10) + avatarGutter;
        }

        if (fading)
        {
            var bakedAlpha = (uint)(((bubbleColor >> 24) & 0xFFu) * entryAlpha);
            bubbleColor = (bubbleColor & 0x00FFFFFFu) | (bakedAlpha << 24);
        }

        var innerW = maxBubW - padding.X * 2f;

        float contentH;
        if (!decrypted || !_msgContentH.TryGetValue(msg.Id, out contentH))
        {
            contentH = parsed.MeasureHeight(innerW);
            if (decrypted)
            {
                _msgContentH[msg.Id] = contentH;
            }
        }
        var innerH = MathF.Max(contentH, ImGui.GetTextLineHeight());
        var bubbleH = innerH + padding.Y * 2f;

        var authorH = AuthorNameHeight(open, msg, isGroupStart);
        if (authorH > 0f)
        {
            var authorName = open.Group?.Members.FirstOrDefault(m => m.AccountId == msg.SenderAccountId)?.Name
                ?? Loc.T("os.msgr_former_member");
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.9f,
                new Vector2(bubbleLeft + Px(4f), cursorPos.Y + entryDy),
                AuthorColor(msg.SenderAccountId), authorName);
        }

        var quoteH = ReplyQuoteHeight(open, msg);
        if (quoteH > 0f)
        {
            DrawReplyQuote(open, msg, bubbleLeft, cursorPos.Y + entryDy + authorH, maxBubW);
        }

        var bubbleTL = new Vector2(bubbleLeft, cursorPos.Y + entryDy + authorH + quoteH);
        var corners = BubbleCorners(mine, isGroupStart, isGroupEnd);
        drawList.AddRectFilled(bubbleTL, bubbleTL + new Vector2(maxBubW, bubbleH), bubbleColor, Px(10f), corners);
        SyncPinState(msg);
        if (msg.PinnedAtUtc is not null)
        {
            DrawPinMarker(bubbleTL, maxBubW, msg.Id, mine);
        }
        if (msg.Id == _scrollTargetMessageId && _flashTimer > 0f)
        {
            var p = 1f - _flashTimer / FlashDuration;
            var a = MathF.Abs(MathF.Sin(p * FlashPulses * MathF.PI));
            drawList.AddRect(bubbleTL, bubbleTL + new Vector2(maxBubW, bubbleH),
                ImGui.GetColorU32(ThemeService.Current.AccentDark with { W = a }), Px(10f), corners, Px(4f));
        }
        if (groupPeer && isGroupEnd)
        {
            DrawSenderGutterAvatar(open, msg, drawList, cursorPos.X, bubbleTL.Y + bubbleH);
        }

        ImGui.SetCursorScreenPos(bubbleTL + padding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using (var body = ImRaii.Child($"##msgrBody{slot}", new Vector2(innerW, innerH), false,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (body.Success)
            {
                ImGui.PushStyleColor(ImGuiCol.Text,
                    decrypted ? (mine ? ChatColors.OwnFg : ChatColors.PeerFg) : UiColors.Muted);
                ImGui.PushTextWrapPos(innerW);
                SegmentEmoji.CaptureRightClick = true;
                parsed.Draw();
                SegmentEmoji.CaptureRightClick = false;
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }
        }
        ImGui.PopStyleVar();

        var emojiClicked = SegmentEmoji.RightClickedName;
        if (emojiClicked != null)
        {
            SegmentEmoji.RightClickedName = null;
            _chatFavName = emojiClicked;
            ImGui.OpenPopup("##msgrEmojiFavMenu");
        }
        else if (msg.DeletedAtUtc is null
            && ImGui.BeginPopupContextItem($"##msgrCtx{msg.Id}", ImGuiPopupFlags.MouseButtonRight))
        {
            DrawMessageContextMenu(msg, decrypted ? text : null);
            ImGui.EndPopup();
        }

        var reactionsH = DrawReactions(msg, mine, bubbleLeft, bubbleTL.Y + bubbleH, maxBubW);

        if (isGroupEnd)
        {
            var local = msg.CreatedAtUtc.LocalDateTime;
            var seenSuffix = mine && msg.Kind == MessengerChatKind.Direct && msg.ReadByPeerAtUtc is not null
                ? Loc.T("chat.seen_suffix")
                : string.Empty;
            var timeStr = local.ToString("HH:mm") + seenSuffix;
            var timeSize = ImGui.CalcTextSize(timeStr);
            var timeX = mine ? bubbleTL.X + maxBubW - timeSize.X : bubbleTL.X;
            ImGui.SetCursorScreenPos(new Vector2(timeX, bubbleTL.Y + bubbleH + reactionsH + Px(2f)));
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.75f, 0.40f), timeStr);
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, authorH + quoteH + bubbleH + reactionsH + timeSize.Y + Px(8f)));
        }
        else
        {
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, authorH + quoteH + bubbleH + reactionsH + Px(2f)));
        }

        if (fading)
        {
            ImGui.PopStyleVar();
        }
    }

    /// <summary>Returns this frame's vertical offset and alpha for a just-arrived message; (0, 1) once finished.</summary>
    private (float dy, float alpha) MessageEntrance(Guid id)
    {
        if (AccessibilityService.ReduceMotion)
        {
            _entryAnim.Remove(id);
            return (0f, 1f);
        }
        if (!_entryAnim.TryGetValue(id, out var p))
        {
            return (0f, 1f);
        }
        p += ImGui.GetIO().DeltaTime / EntranceDuration;
        if (p >= 1f)
        {
            _entryAnim.Remove(id);
            return (0f, 1f);
        }
        _entryAnim[id] = p;
        var eased = 1f - MathF.Pow(1f - p, 3f);
        return (Px(16f) * (1f - eased), eased);
    }

    /// <summary>Reserve height for an undrawn row; must mirror DrawMessageBubble's layout.</summary>
    private float EstimateRowHeight(OpenChatInfo open, MessengerMessageDto msg, bool needsDivider,
        bool isGroupStart, bool isGroupEnd, float windowWidth)
    {
        if (msg.Image is not null)
        {
            var imageRow = ImageRowHeight(open, msg, isGroupStart, isGroupEnd, windowWidth);
            return imageRow + (needsDivider ? ImGui.GetTextLineHeight() + Px(16f) : 0f);
        }
        var padding = Px(12, 8);
        var innerW = windowWidth * 0.72f - padding.X * 2f;
        var lineH = ImGui.GetTextLineHeight();
        var text = DisplayText(msg, out var decrypted);
        if (!decrypted)
        {
            _undecryptedRows.Add(msg.Id);
        }

        if (decrypted && CardRowHeight(text, needsDivider, isGroupEnd, lineH) is { } cardRow)
        {
            return cardRow;
        }

        float contentH;
        if (!decrypted || !_msgContentH.TryGetValue(msg.Id, out contentH))
        {
            contentH = ParsedMessage.Parse(text).MeasureHeight(innerW);
        }

        var bubbleH = MathF.Max(contentH, lineH) + padding.Y * 2f;
        var rowH = isGroupEnd
            ? bubbleH + lineH + Px(8f)
            : bubbleH + Px(2f);
        rowH += AuthorNameHeight(open, msg, isGroupStart) + ReplyQuoteHeight(open, msg) + ReactionsHeight(msg);
        if (needsDivider)
        {
            rowH += lineH + Px(16f);
        }
        return rowH;
    }

    private static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        }
    };

    private static string BuildDayLabel(DateTime date)
    {
        if (string.Equals(LanguageProvider.Current.LanguageName, "English", StringComparison.Ordinal))
        {
            var culture = LanguageProvider.CurrentCulture;
            return $"{date.ToString("dddd", culture)}, {Ordinal(date.Day)} {date.ToString("MMMM yyyy", culture)}";
        }
        return LanguageProvider.FormatDate(date, "D");
    }

    private static void DrawKeyResetDivider(string label)
    {
        var drawList = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(label);
        ImGui.Spacing();
        var origin = ImGui.GetCursorScreenPos();
        var lineY = origin.Y + MathF.Round(textSize.Y * 0.5f);
        var textX = origin.X + MathF.Round((avail - textSize.X) * 0.5f);
        const float Pad = 10f;
        var linCol = ImGui.GetColorU32(new Vector4(0.95f, 0.75f, 0.30f, 0.25f));
        var txtCol = ImGui.GetColorU32(new Vector4(0.95f, 0.75f, 0.30f, 0.85f));
        drawList.AddLine(new Vector2(origin.X + Px(Pad), lineY), new Vector2(textX - Px(Pad), lineY), linCol, 1f);
        drawList.AddText(new Vector2(textX, origin.Y), txtCol, label);
        drawList.AddLine(new Vector2(textX + textSize.X + Px(Pad), lineY), new Vector2(origin.X + avail - Px(Pad), lineY), linCol, 1f);
        ImGui.Dummy(new Vector2(avail, textSize.Y));
        ImGui.Spacing();
    }

    private static void DrawDayDivider(DateTime date)
    {
        var label = BuildDayLabel(date);
        var drawList = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(label);
        ImGui.Spacing();
        var origin = ImGui.GetCursorScreenPos();
        var lineY = origin.Y + MathF.Round(textSize.Y * 0.5f);
        var textX = origin.X + MathF.Round((avail - textSize.X) * 0.5f);
        const float Pad = 10f;
        var linCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f));
        var txtCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.28f));
        drawList.AddLine(new Vector2(origin.X + Px(Pad), lineY), new Vector2(textX - Px(Pad), lineY), linCol, 1f);
        drawList.AddText(new Vector2(textX, origin.Y), txtCol, label);
        drawList.AddLine(new Vector2(textX + textSize.X + Px(Pad), lineY), new Vector2(origin.X + avail - Px(Pad), lineY), linCol, 1f);
        ImGui.Dummy(new Vector2(avail, textSize.Y));
        ImGui.Spacing();
    }

    private MessengerMessageDto? FindMessage(OpenChatInfo open, Guid id)
        => _store.Conversation(open.ChatId).FirstOrDefault(m => m.Id == id);

    private string QuotePreview(OpenChatInfo open, Guid quotedId)
    {
        var original = FindMessage(open, quotedId);
        if (original is null)
        {
            return Loc.T("chat.quote_unavailable");
        }
        var author = original.SenderAccountId == _store.MyAccountId
            ? Loc.T("chat.you")
            : SenderName(open, original.SenderAccountId);
        var raw = Decrypt(original) ?? Loc.T("os.msgr_undecryptable");
        var body = ParsedMessage.Parse(raw).PlainText.Trim();
        if (body.Length == 0)
        {
            body = raw.Trim();
        }
        if (body.Length == 0)
        {
            body = Loc.T("chat.quote_generic");
        }
        return $"{author}: {body}";
    }

    private string SenderName(OpenChatInfo open, Guid senderId)
    {
        if (open.Kind == MessengerChatKind.Direct)
        {
            return open.Contact?.PeerName ?? string.Empty;
        }
        return open.Group?.Members.FirstOrDefault(m => m.AccountId == senderId)?.Name
            ?? Loc.T("os.msgr_former_member");
    }

    private float ReplyQuoteHeight(OpenChatInfo open, MessengerMessageDto msg)
        => msg.ReplyToMessageId is not null ? ImGui.GetTextLineHeight() + Px(12f) : 0f;

    private void DrawReplyQuote(OpenChatInfo open, MessengerMessageDto msg, float left, float top, float width)
    {
        if (msg.ReplyToMessageId is not { } quotedId)
        {
            return;
        }
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var lineH = ImGui.GetTextLineHeight();
        var boxH = lineH + Px(8f);
        var tl = new Vector2(left, top);
        var br = tl + new Vector2(width, boxH);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)), Px(6f));
        dl.AddRectFilled(tl, new Vector2(tl.X + Px(3f), br.Y), t.AccentU32, Px(2f));
        dl.AddText(new Vector2(tl.X + Px(10f), tl.Y + (boxH - lineH) * 0.5f),
            ImGui.GetColorU32(new Vector4(0.85f, 0.85f, 0.90f, 0.95f)),
            TruncateToWidth(QuotePreview(open, quotedId), width - Px(16f)));

        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton($"##msgrQj{msg.Id}", new Vector2(width, boxH));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        if (ImGui.IsItemClicked())
        {
            JumpToMessage(quotedId);
        }
    }

    private (List<string> Mine, Dictionary<string, int> Counts) EffectiveReactions(MessengerMessageDto msg)
    {
        var mine = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (msg.Reactions is { } reactions)
        {
            foreach (var r in reactions)
            {
                foreach (var token in r.Tokens)
                {
                    counts[token] = counts.GetValueOrDefault(token) + 1;
                    if (r.AccountId == _store.MyAccountId)
                    {
                        mine.Add(token);
                    }
                }
            }
        }
        foreach (var ((msgId, token), add) in _rxPending)
        {
            if (msgId != msg.Id)
            {
                continue;
            }
            var has = mine.Contains(token);
            if (add && !has)
            {
                mine.Add(token);
                counts[token] = counts.GetValueOrDefault(token) + 1;
            }
            else if (!add && has)
            {
                mine.Remove(token);
                var n = counts.GetValueOrDefault(token) - 1;
                if (n <= 0)
                {
                    counts.Remove(token);
                }
                else
                {
                    counts[token] = n;
                }
            }
        }
        return (mine, counts);
    }

    /// <summary>Reconciles displayed chips against the effective reaction set; removed chips stay until
    /// their exit animation completes.</summary>
    private List<string>? ReconcileReactionDisplay(MessengerMessageDto msg)
    {
        // A new server array invalidates my optimistic overlay for this message.
        if (!_rxSeenArray.TryGetValue(msg.Id, out var seen) || !ReferenceEquals(seen, msg.Reactions))
        {
            _rxSeenArray[msg.Id] = msg.Reactions;
            foreach (var key in _rxPending.Keys.Where(k => k.Msg == msg.Id).ToArray())
            {
                _rxPending.Remove(key);
            }
        }

        var (mine, counts) = EffectiveReactions(msg);
        var hasAny = counts.Count > 0;
        if (!_rxDisplay.TryGetValue(msg.Id, out var display))
        {
            if (!hasAny)
            {
                return null;
            }
            display = new List<string>();
            _rxDisplay[msg.Id] = display;
        }

        var doAnimate = _seenSeeded && !AccessibilityService.ReduceMotion;
        foreach (var token in counts.Keys)
        {
            var key = (msg.Id, token);
            if (!display.Contains(token))
            {
                display.Add(token);
                _rxExit.Remove(key);
                if (doAnimate)
                {
                    _rxEnter[key] = 0f;
                }
                else
                {
                    _rxEnter.Remove(key);
                }
            }
            else if (_rxExit.Remove(key) && doAnimate)
            {
                _rxEnter[key] = 0f;
            }
        }
        foreach (var token in display.ToArray())
        {
            if (counts.ContainsKey(token))
            {
                continue;
            }
            var key = (msg.Id, token);
            if (doAnimate)
            {
                if (!_rxExit.ContainsKey(key))
                {
                    _rxExit[key] = 0f;
                }
            }
            else
            {
                display.Remove(token);
                _rxEnter.Remove(key);
                _rxExit.Remove(key);
            }
        }
        if (display.Count == 0)
        {
            _rxDisplay.Remove(msg.Id);
            return null;
        }
        return display;
    }

    private float ReactionsHeight(MessengerMessageDto msg)
        => msg.Reactions is { Length: > 0 } || _rxDisplay.ContainsKey(msg.Id)
            ? ImGui.GetTextLineHeight() + Px(12f)
            : 0f;

    private float DrawReactions(MessengerMessageDto msg, bool mineMsg, float bubbleLeft, float bubbleBottomY, float maxBubW)
    {
        var display = ReconcileReactionDisplay(msg);
        if (display is null || display.Count == 0)
        {
            return 0f;
        }
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var emojiSz = ImGui.GetTextLineHeight();
        var chipH = emojiSz + Px(6f);
        var padX = Px(6f);
        var gap = Px(4f);
        var dt = ImGui.GetIO().DeltaTime;
        var reduceMotion = AccessibilityService.ReduceMotion;
        var (mine, counts) = EffectiveReactions(msg);

        var names = display.ToArray();
        var widths = new float[names.Length];
        var totalW = 0f;
        for (int i = 0; i < names.Length; i++)
        {
            var count = counts.GetValueOrDefault(names[i]);
            var w = emojiSz + padX * 2f;
            if (count > 1)
            {
                w += ImGui.CalcTextSize(count.ToString()).X + Px(3f);
            }
            widths[i] = w;
            totalW += w + gap;
        }
        totalW -= gap;

        var x = mineMsg ? bubbleLeft + maxBubW - totalW : bubbleLeft;
        var y = bubbleBottomY + Px(3f);

        for (int i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var chipW = widths[i];
            var key = (msg.Id, name);
            var tl = new Vector2(x, y);
            x += chipW + gap;

            float scale = 1f, alpha = 1f;
            var exiting = false;
            if (reduceMotion)
            {
                _rxEnter.Remove(key);
                if (_rxExit.Remove(key))
                {
                    display.Remove(name);
                    continue;
                }
            }
            else if (_rxExit.TryGetValue(key, out var ep))
            {
                exiting = true;
                ep += dt / ReactionFxDuration;
                if (ep >= 1f)
                {
                    _rxExit.Remove(key);
                    display.Remove(name);
                    continue;
                }
                _rxExit[key] = ep;
                scale = 1f - EaseInCubicF(ep) * 0.5f;
                alpha = 1f - ep;
            }
            else if (_rxEnter.TryGetValue(key, out var np))
            {
                np += dt / ReactionFxDuration;
                if (np >= 1f)
                {
                    _rxEnter.Remove(key);
                }
                else
                {
                    _rxEnter[key] = np;
                }
                var e = EaseOutCubicF(MathF.Min(np, 1f));
                scale = 0.5f + e * 0.5f;
                alpha = e;
            }

            var isMine = mine.Contains(name);
            var count = counts.GetValueOrDefault(name);
            var center = (tl + tl + new Vector2(chipW, chipH)) * 0.5f;
            var half = new Vector2(chipW, chipH) * 0.5f * scale;
            var fill = isMine
                ? t.AccentWithAlpha(0.22f * alpha)
                : ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f * alpha));
            dl.AddRectFilled(center - half, center + half, fill, Px(8f));
            dl.AddRect(center - half, center + half, t.AccentWithAlpha((isMine ? 0.55f : 0.30f) * alpha),
                Px(8f), ImDrawFlags.RoundCornersAll, 1f);

            var es = emojiSz * scale;
            var contentLeft = center.X - (chipW * scale) * 0.5f + padX * scale;
            var tex = AetherLove.UiHost.EmojiService.GetEmoji(name)?.GetWrapOrDefault();
            if (tex != null)
            {
                var ip0 = new Vector2(contentLeft, center.Y - es * 0.5f);
                dl.AddImage(tex.Handle, ip0, ip0 + new Vector2(es), Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));
            }
            if (count > 1)
            {
                var countStr = count.ToString();
                var cs = ImGui.CalcTextSize(countStr);
                dl.AddText(new Vector2(contentLeft + es + Px(3f) * scale, center.Y - cs.Y * 0.5f),
                    ImGui.GetColorU32(new Vector4(0.92f, 0.92f, 0.95f, alpha)), countStr);
            }

            if (!exiting)
            {
                ImGui.SetCursorScreenPos(tl);
                ImGui.InvisibleButton($"##msgrRx{msg.Id}_{name}", new Vector2(chipW, chipH));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip(isMine
                        ? $":{name}: ({Loc.T("chat.reaction_remove_hint")})"
                        : $":{name}:");
                }
                if (ImGui.IsItemClicked())
                {
                    ToggleMyReaction(msg, name);
                }
            }
        }
        if (display.Count == 0)
        {
            _rxDisplay.Remove(msg.Id);
        }
        return ImGui.GetTextLineHeight() + Px(12f);
    }

    private void ToggleMyReaction(MessengerMessageDto msg, string emoji)
    {
        var (mine, _) = EffectiveReactions(msg);
        var add = !mine.Contains(emoji);
        if (add && mine.Count >= MaxReactionsPerUser)
        {
            return;
        }
        _rxPending[(msg.Id, emoji)] = add;
        if (add)
        {
            AetherLove.UiHost.Configuration.ReactionUsage[emoji] =
                AetherLove.UiHost.Configuration.ReactionUsage.GetValueOrDefault(emoji) + 1;
            _reactionUsageDirty = true;
        }
        var msgId = msg.Id;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _hub.ReactMessengerMessageAsync(msgId, emoji, add).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] reaction toggle failed.");
                var err = AetherLove.Services.HubErrorText.Localize(ex);
                _uiActions.Enqueue(() =>
                {
                    _rxPending.Remove((msgId, emoji));
                    _chatError = err;
                });
            }
        });
    }

    private static string[] ComputeQuickReact()
    {
        var result = new List<string>(6);
        foreach (var name in AetherLove.UiHost.Configuration.ReactionUsage
                     .OrderByDescending(kv => kv.Value)
                     .Select(kv => kv.Key))
        {
            if (result.Count >= 6)
            {
                break;
            }
            if (result.Contains(name) || AetherLove.UiHost.EmojiService.GetEmoji(name) is null)
            {
                continue;
            }
            result.Add(name);
        }
        foreach (var name in QuickReactDefaults)
        {
            if (result.Count >= 6)
            {
                break;
            }
            if (!result.Contains(name))
            {
                result.Add(name);
            }
        }
        return result.ToArray();
    }

    private void SaveReactionUsageIfDirty()
    {
        if (_reactionUsageDirty)
        {
            _reactionUsageDirty = false;
            AetherLove.UiHost.Configuration.Save();
        }
    }

    private void OnEmojiPicked(string name)
    {
        if (_emojiPickerForInput)
        {
            _inputText += $":{name}: ";
            if (_chatWrapWidth > 0f)
            {
                _inputText = WrapForInput(_inputText, _chatWrapWidth);
            }
            _reclaimInputFocus = true;
            return;
        }
        var target = _reactTargetId;
        if (target == Guid.Empty)
        {
            return;
        }
        var msg = _store.Conversation(_chatId).FirstOrDefault(m => m.Id == target);
        if (msg is not null)
        {
            ToggleMyReaction(msg, name);
        }
    }

    private void SyncPinState(MessengerMessageDto msg)
    {
        var pinned = msg.PinnedAtUtc is not null;
        if (_pinState.TryGetValue(msg.Id, out var was) && was == pinned)
        {
            return;
        }
        var known = _pinState.ContainsKey(msg.Id);
        _pinState[msg.Id] = pinned;
        if (pinned && known && !AccessibilityService.ReduceMotion)
        {
            _pinAnim[msg.Id] = 0f;
        }
        if (!pinned)
        {
            _pinAnim.Remove(msg.Id);
        }
    }

    /// <summary>Thumbtack straddling a pinned bubble's top outer corner; drops in from above on pin.</summary>
    private void DrawPinMarker(Vector2 bubbleTL, float maxBubW, Guid messageId, bool isOwn)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
        var icon = FontAwesomeIcon.Thumbtack.ToIconString();
        var sz = ImGui.CalcTextSize(icon);
        ImGui.PopFont();

        var cornerX = isOwn ? bubbleTL.X : bubbleTL.X + maxBubW;
        var inset = isOwn ? Px(11f) : -Px(11f);
        var restPos = new Vector2(cornerX - sz.X * 0.5f + inset, bubbleTL.Y - sz.Y * 0.7f - Px(3f));

        float drop = 0f, alpha = 1f;
        if (!AccessibilityService.ReduceMotion && _pinAnim.TryGetValue(messageId, out var p))
        {
            p += ImGui.GetIO().DeltaTime / PinDropDuration;
            if (p >= 1f)
            {
                _pinAnim.Remove(messageId);
                p = 1f;
            }
            else
            {
                _pinAnim[messageId] = p;
            }
            drop = -(1f - EaseOutBackF(p)) * Px(16f);
            alpha = MathF.Min(1f, p * 1.6f);
        }

        var baseCol = isOwn
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.92f, 0.93f, 1f))
            : ThemeService.Current.AccentLightU32;
        var col = (baseCol & 0x00FFFFFFu) | ((uint)(255f * alpha) << 24);
        ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), restPos + new Vector2(0f, drop), col, icon);
        ImGui.PopFont();
    }

    private void ToggleMyPin(MessengerMessageDto msg)
    {
        var pinned = msg.PinnedAtUtc is not null;
        RunHub(() => _hub.SetMessengerMessagePinAsync(msg.Id, !pinned));
    }

    /// <summary>Pinned-messages popup floating over the chat.</summary>
    private void DrawPinnedOverlay(OpenChatInfo open)
    {
        if (_pinnedListOpen)
        {
            _pinnedListOpen = false;
            ImGui.OpenPopup("##msgrPinnedOverlay");
        }
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var panelW = winSize.X - Px(32f);
        var panelH = MathF.Min(Px(320f), MathF.Max(Px(120f), (winSize.Y - Px(HeaderH)) * 0.72f));
        ImGui.SetNextWindowPos(new Vector2(winPos.X + Px(16f), winPos.Y + Px(HeaderH) + Px(10f)));
        ImGui.SetNextWindowSize(new Vector2(panelW, panelH));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(12f), Px(12f)));
        using var popup = ImRaii.Popup("##msgrPinnedOverlay", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
        ImGui.PopStyleVar(2);
        if (!popup.Success)
        {
            return;
        }

        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(new Vector4(0.82f, 0.74f, 1.0f, 1f), Loc.T("chat.pinned_messages"));
        }
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - Px(34f));
        if (ImGui.SmallButton("X##msgrClosePinned"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.Separator();
        ImGui.Spacing();

        var pinned = _store.Conversation(open.ChatId).Where(m => m.PinnedAtUtc is not null).ToArray();
        if (pinned.Length == 0)
        {
            ImGui.CloseCurrentPopup();
            return;
        }
        foreach (var m in pinned)
        {
            var raw = Decrypt(m) ?? Loc.T("os.msgr_undecryptable");
            var preview = ParsedMessage.Parse(raw).PlainText.Trim();
            if (preview.Length == 0)
            {
                preview = raw.Trim();
            }
            var author = m.SenderAccountId == _store.MyAccountId ? Loc.T("chat.you") : SenderName(open, m.SenderAccountId);
            ImGui.PushID(m.Id.ToString());
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - Px(4f));
            ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.88f, 1f), $"{author}: {preview}");
            ImGui.PopTextWrapPos();
            if (ImGui.SmallButton(Loc.T("chat.jump")))
            {
                ImGui.CloseCurrentPopup();
                JumpToMessage(m.Id);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("chat.unpin")))
            {
                ToggleMyPin(m);
            }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PopID();
        }
    }

    /// <summary>Quick-reaction row + reply/pin/copy actions for a message's right-click menu.</summary>
    private void DrawMessageContextMenu(MessengerMessageDto msg, string? text)
    {
        var sz = ImGui.GetTextLineHeight() + Px(6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(2f), Px(2f)));
        for (int i = 0; i < _quickReact.Length; i++)
        {
            var name = _quickReact[i];
            var tex = AetherLove.UiHost.EmojiService.GetEmoji(name)?.GetWrapOrDefault();
            ImGui.PushID(i);
            if (tex != null && ImGui.ImageButton(tex.Handle, new Vector2(sz)))
            {
                ToggleMyReaction(msg, name);
                ImGui.CloseCurrentPopup();
            }
            ImGui.PopID();
            ImGui.SameLine(0f, Px(2f));
        }
        if (ImGui.Button("+##msgrMoreReact", new Vector2(sz + Px(4f), sz + Px(4f))))
        {
            _pendingReactionPickerId = msg.Id;
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleVar();
        ImGui.Separator();

        if (DrawIconMenuItem(FontAwesomeIcon.Reply, Loc.T("chat.reply")))
        {
            ImGui.CloseCurrentPopup();
            _replyingToId = msg.Id;
            _reclaimInputFocus = true;
        }
        var pinned = msg.PinnedAtUtc is not null;
        if (DrawIconMenuItem(pinned ? FontAwesomeIcon.ThumbtackSlash : FontAwesomeIcon.Thumbtack,
                pinned ? Loc.T("chat.unpin_message") : Loc.T("chat.pin_message")))
        {
            ImGui.CloseCurrentPopup();
            ToggleMyPin(msg);
        }
        if (text is not null && DrawIconMenuItem(FontAwesomeIcon.Copy, Loc.T("chat.menu_copy_message")))
        {
            ImGui.CloseCurrentPopup();
            _caps.System.CopyToClipboard(ParsedMessage.Parse(text).PlainText);
        }
        if (msg.SenderAccountId == _store.MyAccountId
            && DrawIconMenuItem(FontAwesomeIcon.TrashAlt, Loc.T("chat.delete_message"), UiColors.MenuDanger))
        {
            ImGui.CloseCurrentPopup();
            var messageId = msg.Id;
            var chatId = msg.ChatId;
            OpenConfirm(
                Loc.T("chat.delete_message"),
                Loc.T("chat.delete_message_body"),
                () => RunHub(async () =>
                {
                    await _hub.DeleteMessengerMessageAsync(messageId).ConfigureAwait(false);
                    _store.ApplyMessageDeleted(chatId, messageId);
                }));
        }
    }

    private float InputBarHeight(OpenChatInfo open)
    {
        var h = ChatInputBoxHeight() + Px(14f);
        if (_replyingToId is not null)
        {
            h += ImGui.GetTextLineHeight() + Px(12f);
        }
        if (_acMatches is { Count: > 0 })
        {
            h += Px(AutocompleteRowH);
        }
        if (!CanSend(open))
        {
            h += ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y;
        }
        if (_chatError is not null)
        {
            h += ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y;
        }
        return h;
    }

    private float ChatInputBoxHeight()
    {
        var lines = 1;
        for (var i = 0; i < _inputText.Length; i++)
        {
            if (_inputText[i] == '\n')
            {
                lines++;
            }
        }
        lines = Math.Clamp(lines, 1, ChatInputMaxLines);
        return lines * ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2f;
    }

    private float RemovedNoticeHeight(OpenChatInfo open)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var text = string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_removed_notice"), open.Title);
        var textH = ImGui.CalcTextSize(text, wrapWidth: MathF.Max(1f, width - Px(24f))).Y;
        return textH + Px(30f);
    }

    /// <summary>The removal tombstone: the chat stays readable, sending is disabled.</summary>
    private void DrawRemovedNotice(OpenChatInfo open)
    {
        ImGui.Separator();
        var width = ImGui.GetContentRegionAvail().X;
        var text = string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_removed_notice"), open.Title);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(12f));
        ImGui.PushStyleColor(ImGuiCol.Text, WarnAmber);
        ImGui.PushTextWrapPos(width - Px(12f));
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }

    private void RefreshAutocomplete()
    {
        if (!TryDetectShortcode(_inputText, out var query))
        {
            _acMatches = null;
            _acQuery = null;
            return;
        }
        if (query == _acQuery)
        {
            return;
        }
        _acQuery = query;

        var starts = new List<string>();
        var contains = new List<string>();
        foreach (var name in AetherLove.UiHost.EmojiService.All.Keys)
        {
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                starts.Add(name);
            }
            else if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                contains.Add(name);
            }
        }
        starts.Sort(RankShortcode);
        contains.Sort(RankShortcode);
        var top = starts.Concat(contains).Take(AutocompleteMax).ToList();
        _acMatches = top.Count > 0 ? top : null;
    }

    private static int RankShortcode(string a, string b)
        => a.Length != b.Length ? a.Length - b.Length : string.CompareOrdinal(a, b);

    private static bool TryDetectShortcode(string text, out string query)
    {
        query = "";
        var ci = text.LastIndexOf(':');
        if (ci < 0 || (ci > 0 && !char.IsWhiteSpace(text[ci - 1])))
        {
            return false;
        }
        var q = text[(ci + 1)..];
        if (q.Length == 0)
        {
            return false;
        }
        foreach (var c in q)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
            {
                return false;
            }
        }
        query = q;
        return true;
    }

    private void CompleteShortcode(string fullName)
    {
        var ci = _inputText.LastIndexOf(':');
        if (ci < 0)
        {
            return;
        }
        _inputText = _inputText[..ci] + $":{fullName}: ";
        if (_chatWrapWidth > 0f)
        {
            _inputText = WrapForInput(_inputText, _chatWrapWidth);
        }
        _acMatches = null;
        _acQuery = null;
        _reclaimInputFocus = true;
        _acCursorToEnd = true;
    }

    private void DrawEmojiAutocompleteRow()
    {
        if (_acMatches is not { Count: > 0 } matches)
        {
            return;
        }
        var start = ImGui.GetCursorPos();
        var sz = Px(24f);
        ImGui.SetCursorPos(new Vector2(Px(8f), start.Y + Px(3f)));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(3f, 3f));
        ImGui.PushStyleColor(ImGuiCol.Button, 0u);
        for (var i = 0; i < matches.Count; i++)
        {
            var name = matches[i];
            var tex = AetherLove.UiHost.EmojiService.GetEmoji(name)?.GetWrapOrDefault();
            ImGui.PushID(i);
            var clicked = tex != null
                ? ImGui.ImageButton(tex.Handle, new Vector2(sz))
                : ImGui.Button(name, new Vector2(sz + Px(6f)));
            ImGui.PopID();
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip($":{name}:");
            }
            if (clicked)
            {
                CompleteShortcode(name);
            }
            ImGui.SameLine(0f, Px(4f));
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        ImGui.SetCursorPos(new Vector2(start.X, start.Y + Px(AutocompleteRowH)));
    }

    /// <summary>The "replying to ..." strip drawn above the input bar while composing a reply.</summary>
    private void DrawReplyComposeBar(OpenChatInfo open)
    {
        if (_replyingToId is not { } id)
        {
            return;
        }
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var lineH = ImGui.GetTextLineHeight();
        var barH = lineH + Px(8f);
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(avail, barH);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f)), Px(6f));
        dl.AddRectFilled(tl, new Vector2(tl.X + Px(3f), br.Y), t.AccentU32, Px(2f));
        dl.AddText(new Vector2(tl.X + Px(10f), tl.Y + (barH - lineH) * 0.5f),
            ImGui.GetColorU32(new Vector4(0.90f, 0.90f, 0.94f, 1f)),
            TruncateToWidth(Loc.T("chat.replying_to", QuotePreview(open, id)), avail - barH - Px(16f)));

        ImGui.SetCursorScreenPos(new Vector2(br.X - barH, tl.Y));
        if (ImGui.InvisibleButton("##msgrCancelReply", new Vector2(barH, barH)))
        {
            _replyingToId = null;
        }
        var hov = ImGui.IsItemHovered();
        if (hov)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
        var xicon = FontAwesomeIcon.Times.ToIconString();
        var xsz = ImGui.CalcTextSize(xicon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(br.X - barH + (barH - xsz.X) * 0.5f, tl.Y + (barH - xsz.Y) * 0.5f),
            hov ? t.AccentLightU32 : 0xFFAAAAAAu, xicon);
        ImGui.PopFont();

        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y + Px(4f)));
    }

    private void DrawChatInput(OpenChatInfo open)
    {
        var windowWidth = ImGui.GetWindowSize().X;
        const float AttachBtn = 28f;
        const float EmojiBtn = 28f;
        const float SendBtn = 56f;
        const float Gap = 4f;
        var inputWidth = windowWidth - Px(AttachBtn) - Px(EmojiBtn) - Px(SendBtn) - Px(Gap * 4f);

        ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - InputBarHeight(open));
        DrawEmojiAutocompleteRow();
        ImGui.Separator();
        ImGui.Spacing();

        DrawReplyComposeBar(open);

        var buttonSize = ImGui.GetFrameHeight();
        {
            ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
            var attachClicked = ImGui.Button($"{FontAwesomeIcon.Plus.ToIconString()}##msgrAttach",
                new Vector2(buttonSize, buttonSize));
            ImGui.PopFont();
            if (attachClicked)
            {
                ImGui.OpenPopup("##msgrAttachMenu");
            }
            DrawAttachMenuPopup(open);
        }
        ImGui.SameLine(0, Px(Gap));

        {
            var grinTex = AetherLove.UiHost.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(4f, 4f));
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(buttonSize - Px(8f)))
                : ImGui.Button($"{Loc.T("chat.emoji_button")}##msgrEmoji", new Vector2(buttonSize, 0));
            ImGui.PopStyleVar();
            if (clicked)
            {
                _emojiPickerForInput = true;
                _emojiPicker.Open(OnEmojiPicked);
            }
        }

        ImGui.SameLine(0, Px(Gap));
        if (_reclaimInputFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _reclaimInputFocus = false;
        }
        _chatWrapWidth = inputWidth - ImGui.GetStyle().FramePadding.X * 2f;
        var inputBefore = _inputText;
        var enterPressed = ImGui.InputTextMultiline("##msgrMessageInput", ref _inputText, MessengerLimits.MaxMessageChars,
            new Vector2(inputWidth, ChatInputBoxHeight()),
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CtrlEnterForNewLine
            | ImGuiInputTextFlags.CallbackEdit | ImGuiInputTextFlags.CallbackAlways,
            ChatWrapCallback);
        ArmPasteIfIgnored(textChanged: _inputText != inputBefore);
        ImGui.SameLine(0, Px(Gap));
        var canSend = CanSend(open);
        if ((ImGui.Button(Loc.T("chat.send"), new Vector2(Px(SendBtn), 0)) || enterPressed)
            && _inputText.Length > 0 && canSend)
        {
            SendCurrentInput(open);
            _reclaimInputFocus = true;
        }
        if (!canSend)
        {
            ImGui.SetCursorPosX(Px(10f));
            ImGui.TextColored(UiColors.Muted, Loc.T(GroupKeyReady(open) ? "os.msgr_waiting_keys" : "os.msgr_keys_pending"));
        }
        if (_chatError is { } err)
        {
            ImGui.SetCursorPosX(Px(10f));
            ImGui.TextColored(UiColors.Danger, err);
        }
    }

    private bool GroupKeyReady(OpenChatInfo open)
        => open.Kind != MessengerChatKind.Group
           || (open.Group is { } g && _store.GroupKey(g.GroupId, g.KeyEpoch) is not null);

    private bool CanSend(OpenChatInfo open)
        => _crypto.HasAccountKeys && !_sending && GroupKeyReady(open)
           && (open.Kind != MessengerChatKind.Direct || open.Contact?.PeerPublicKey is { Length: > 0 });

    private unsafe int ChatWrapCallback(ImGuiInputTextCallbackDataPtr data)
    {
        try
        {
            ImGuiInputTextCallbackData* p = data;
            if (p->EventFlag == ImGuiInputTextFlags.CallbackAlways)
            {
                if (_acCursorToEnd)
                {
                    _acCursorToEnd = false;
                    p->CursorPos = p->BufTextLen;
                    p->SelectionStart = p->BufTextLen;
                    p->SelectionEnd = p->BufTextLen;
                }
                if (TryConsumeArmedPaste(p))
                {
                    RewrapBuffer(p);
                }
                return 0;
            }
            RewrapBuffer(p);
        }
        catch
        {
            // A managed exception must not cross into the native ImGui call.
        }
        return 0;
    }

    private unsafe void RewrapBuffer(ImGuiInputTextCallbackData* p)
    {
        if (_chatWrapWidth <= 0f || p->BufTextLen <= 0)
        {
            return;
        }
        var current = Encoding.UTF8.GetString(p->Buf, p->BufTextLen);
        var wrapped = WrapForInput(current, _chatWrapWidth);
        if (wrapped == current)
        {
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(wrapped);
        if (bytes.Length != p->BufTextLen)
        {
            return;
        }
        for (var i = 0; i < bytes.Length; i++)
        {
            p->Buf[i] = bytes[i];
        }
        p->BufDirty = 1;
    }

    /// <summary>Length-preserving greedy wrap: swaps spaces and newlines only; long words aren't split.</summary>
    private static string WrapForInput(string text, float width)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\n')
            {
                chars[i] = ' ';
            }
        }
        var lineStart = 0;
        var lastSpace = -1;
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == ' ')
            {
                lastSpace = i;
            }
            var line = new string(chars, lineStart, i - lineStart + 1);
            if (ImGui.CalcTextSize(line).X > width && lastSpace > lineStart)
            {
                chars[lastSpace] = '\n';
                lineStart = lastSpace + 1;
                lastSpace = -1;
            }
        }
        return new string(chars);
    }

    private void SendCurrentInput(OpenChatInfo open)
    {
        if (string.IsNullOrWhiteSpace(_inputText) || !ParsedMessage.Parse(_inputText).HasVisibleContent)
        {
            return;
        }
        // Slash input mimics the game chat box: a known emote command runs on the character, anything
        // else is dropped; either way nothing is sent to the peer.
        var slashInput = _inputText.Trim();
        if (slashInput.StartsWith('/'))
        {
            _caps.System.TryExecuteEmote(slashInput);
            _inputText = string.Empty;
            _drafts.Remove(open.ChatId);
            return;
        }
        var text = _inputText.Replace('\n', ' ').Trim();
        _inputText = string.Empty;
        _drafts.Remove(open.ChatId);
        _scrollToBottom = 1f;
        var replyTo = _replyingToId;
        _replyingToId = null;
        _chatError = null;
        _sending = true;

        (byte[] Ciphertext, byte[] Nonce)? sealedMsg;
        var epoch = 0;
        if (open.Kind == MessengerChatKind.Direct)
        {
            sealedMsg = open.Contact?.PeerPublicKey is { Length: > 0 } pub ? _crypto.EncryptDirect(pub, text) : null;
        }
        else
        {
            epoch = open.Group!.KeyEpoch;
            var key = _store.GroupKey(open.ChatId, epoch);
            sealedMsg = key is null ? null : _crypto.EncryptGroup(key, text);
        }
        if (sealedMsg is not { } payload)
        {
            _sending = false;
            return;
        }
        var req = new SendMessengerMessageRequest(open.ChatId, open.Kind, payload.Ciphertext, payload.Nonce, epoch, replyTo);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var resp = await _hub.SendMessengerMessageAsync(req).ConfigureAwait(false);
                // Append the acked message directly; the push echo dedups by id, so the bubble shows even
                // when the echo is delayed or lost.
                var sent = new MessengerMessageDto(resp.MessageId, open.ChatId, open.Kind, _store.MyAccountId,
                    payload.Ciphertext, payload.Nonce, epoch, resp.CreatedAtUtc, resp.CreatedAtUtc, null, replyTo);
                _uiActions.Enqueue(() => _plain[resp.MessageId] = text);
                _store.ApplyMessage(sent);
                _scrollToBottom = 1f;
            }
            catch (Exception ex)
            {
                _chatError = FriendlyHubError(ex);
                _uiActions.Enqueue(() =>
                {
                    if (_inputText.Length == 0 && _chatId == open.ChatId)
                    {
                        _inputText = text;
                    }
                });
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] send failed.");
            }
            finally
            {
                _sending = false;
            }
        });
    }

    private void DrawAttachMenuPopup(OpenChatInfo open)
    {
        if (ImGui.BeginPopup("##msgrAttachMenu"))
        {
            DrawAttachPhotoMenu(open);
            ImGui.Separator();
            if (DrawIconMenuItem(FontAwesomeIcon.MapMarkerAlt, Loc.T("os.msgr_attach_location")))
            {
                ImGui.CloseCurrentPopup();
                AttachLocation(open);
            }
            ImGui.EndPopup();
        }
    }

    private void AttachLocation(OpenChatInfo open)
    {
        var data = LocationShare.Capture();
        if (data is null)
        {
            _chatError = Loc.T("os.msgr_location_unavailable");
            return;
        }
        if (!TrySendComposed(open.ChatId, open.Kind, LocationShare.Compose(data)))
        {
            _chatError = Loc.T("huberror.generic");
        }
    }

    private static float EaseOutCubicF(float x) => 1f - MathF.Pow(1f - x, 3f);

    private static float EaseInCubicF(float x) => x * x * x;

    private static float EaseOutBackF(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var u = x - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
