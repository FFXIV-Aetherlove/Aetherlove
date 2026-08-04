using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using AetherLove.Emoji;
using AetherLove.Emoji.Segments;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Yapper;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper.Screens;

/// <summary>The DM chat, a faithful copy of the messenger port of the AetherLove match chat: ChatColors
/// bubbles with grouped corners, day dividers, ParsedMessage emoji rendering, clickable reply quotes,
/// animated reaction chips with a quick-react context row, pin markers with the pinned bar, read receipts,
/// emoji autocomplete, and the bottom-docked growing input with Enter-to-send (Ctrl+Enter for a newline).
/// All content is E2E: ciphertext decrypts at render via the host.</summary>
internal sealed class DmChatScreen
{
    private const float EntranceDuration = 0.3f;
    private const float FlashDuration = 3.0f;
    private const int FlashPulses = 3;
    private const int ChatInputMaxLines = 5;
    private const int AutocompleteMax = 5;
    private const float AutocompleteRowH = 40f;
    private const float ReactionFxDuration = 0.18f;
    private const int MaxReactionsPerUser = 5;
    private const int MaxMessageChars = 3000;
    private static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(5);
    private static readonly string[] QuickReactDefaults =
        ["heart", "heart_eyes", "joy", "thumbsup", "fire", "cry"];

    private readonly IYapperHost _host;
    private readonly DmStore _dms;
    private readonly YapperMediaCache _mediaCache;
    private readonly Func<Guid?> _myProfileId;
    private readonly Action _back;
    private readonly Action<Guid> _openProfile;
    private readonly EmojiPickerPopup _emojiPicker = new();
    private bool _emojiPickerForInput;
    private Guid _reactTargetId;
    private Guid? _pendingReactionPickerId;

    private Guid _peerId;
    private volatile bool _loading;
    private bool _loaded;
    private DateTimeOffset? _olderCursor;
    private string _inputText = string.Empty;
    private volatile bool _sending;
    private volatile string? _chatError;
    private Guid? _replyingToId;

    private float _scrollToBottom;
    private Guid _scrollTargetMessageId;
    private float _scrollToMessageTimer;
    private float _flashTimer;
    private int _lastConvCount = -1;

    private readonly Dictionary<Guid, float> _entryAnim = new();
    private readonly HashSet<Guid> _seenMsgIds = new();
    private bool _seenSeeded;

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
    private string[] _quickReact = QuickReactDefaults;
    private bool _reactionUsageDirty;

    private readonly Dictionary<Guid, bool> _pinState = new();
    private readonly Dictionary<Guid, float> _pinAnim = new();
    private const float PinDropDuration = 0.32f;

    public DmChatScreen(IYapperHost host, DmStore dms, YapperMediaCache mediaCache,
        Func<Guid?> myProfileId, Action back, Action<Guid> openProfile)
    {
        _host = host;
        _dms = dms;
        _mediaCache = mediaCache;
        _myProfileId = myProfileId;
        _back = back;
        _openProfile = openProfile;
    }

    public Guid PeerId => _peerId;

    public void Open(Guid peerProfileId)
    {
        SaveReactionUsageIfDirty();
        _peerId = peerProfileId;
        _loaded = false;
        _olderCursor = null;
        _inputText = string.Empty;
        _replyingToId = null;
        _chatError = null;
        _scrollToBottom = 1f;
        _scrollTargetMessageId = Guid.Empty;
        _scrollToMessageTimer = 0f;
        _flashTimer = 0f;
        _lastConvCount = -1;
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
        _quickReact = ComputeQuickReact();
        Load();
        _ = Task.Run(() => _host.EnsureDmKeysAsync());
    }

    private void Load()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        var peerId = _peerId;
        _ = Task.Run(async () =>
        {
            try
            {
                var thread = await _host.OpenDmThreadAsync(peerId).ConfigureAwait(false);
                _dms.SetThread(peerId, thread.Peer, thread.PeerPublicKey, thread.Page.Messages);
                _olderCursor = thread.Page.NextCursor;
                _loaded = true;
                _scrollToBottom = 1f;
                _dms.MarkRead(peerId);
                await _host.MarkDmReadAsync(peerId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _chatError = AetherLove.Services.HubErrorText.Localize(ex);
            }
            finally
            {
                _loading = false;
            }
        });
    }

    /// <summary>Called by the app when a push lands for the open chat, so receipts stay fresh.</summary>
    public void NotifyIncoming()
    {
        _ = Task.Run(() => _host.MarkDmReadAsync(_peerId));
        _dms.MarkRead(_peerId);
    }

    public void SaveReactionUsageIfDirty()
    {
        if (_reactionUsageDirty)
        {
            _reactionUsageDirty = false;
            AetherLove.UiHost.Configuration.Save();
        }
    }

    public void Draw(OsAppContext ctx)
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

        var winW = ImGui.GetWindowSize().X;
        var pad = Px(12f);
        var peer = _dms.Peer(_peerId);
        var peerKey = _dms.PeerKey(_peerId);

        DrawHeader(ctx, peer, pad, winW);
        var pinned = _dms.Thread(_peerId).Where(m => m.PinnedAtUtc is not null && m.DeletedAtUtc is null)
            .OrderByDescending(m => m.PinnedAtUtc).ToList();
        if (pinned.Count > 0)
        {
            DrawPinnedBar(ctx, pinned[0], peerKey, pad, winW);
        }

        DrawMessages(ctx, peer, peerKey, winW);
        DrawChatInput(peer, peerKey);
        _emojiPicker.Draw();
    }

    private void DrawHeader(OsAppContext ctx, YapAuthorDto? peer, float pad, float winW)
    {
        ImGui.Spacing();
        ImGui.SetCursorPosX(pad);
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), FontAwesomeIcon.Envelope))
        {
            SaveReactionUsageIfDirty();
            _back();
        }
        if (peer is null)
        {
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            return;
        }
        // The pill is roughly two icon glyphs wide; park the peer block safely right of it.
        ImGui.SameLine(pad + Px(86f));
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##yapDmPeer", new Vector2(winW - pad * 2f - Px(86f), Px(34f))))
        {
            _openProfile(peer.ProfileId);
        }
        HandOnHover();
        var center = tl + new Vector2(Px(15f), Px(17f));
        var tex = peer.Avatar is { Length: > 0 } bytes ? _mediaCache.GetAvatar(peer.ProfileId, bytes) : null;
        if (tex?.GetWrapOrDefault() is { } wrap)
        {
            dl.AddImageRounded(wrap.Handle, center - new Vector2(Px(15f), Px(15f)), center + new Vector2(Px(15f), Px(15f)),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Px(15f));
        }
        else
        {
            dl.AddCircleFilled(center, Px(15f), ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.35f }));
        }
        dl.AddText(tl + new Vector2(Px(36f), Px(1f)), 0xFFFFFFFFu, peer.DisplayName);
        dl.AddText(tl + new Vector2(Px(36f), Px(18f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.45f)), $"@{peer.Handle}");
        ImGui.Dummy(new Vector2(0f, Px(4f)));
    }

    private void DrawPinnedBar(OsAppContext ctx, YapperDmMessageDto message, byte[]? peerKey, float pad, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var h = Px(26f);
        dl.AddRectFilled(tl + new Vector2(pad, 0f), tl + new Vector2(winW - pad, h),
            ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.12f }), Px(8f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Thumbtack, Px(11f),
            tl + new Vector2(pad + Px(13f), h * 0.5f), ImGui.GetColorU32(ctx.Theme.Accent));
        var text = ParsedMessage.Parse(Decrypt(peerKey, message) ?? DecryptPlaceholder(peerKey))
            .PlainText.Replace('\n', ' ');
        dl.AddText(tl + new Vector2(pad + Px(26f), (h - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)),
            TruncateToWidth(text, winW - pad * 2f - Px(50f)));

        // Tapping the bar jumps to the pinned message; the x on the right unpins.
        ImGui.SetCursorScreenPos(tl + new Vector2(pad, 0f));
        if (ImGui.InvisibleButton("##yapDmPinJump", new Vector2(winW - pad * 2f - Px(24f), h)))
        {
            JumpToMessage(message.Id);
        }
        HandOnHover();
        ImGui.SetCursorScreenPos(tl + new Vector2(winW - pad - Px(22f), Px(3f)));
        if (ImGui.InvisibleButton("##yapDmUnpin", new Vector2(Px(20f), Px(20f))))
        {
            ToggleMyPin(message);
        }
        HandOnHover();
        IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(10f),
            ImGui.GetItemRectMin() + ImGui.GetItemRectSize() * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, ImGui.IsItemHovered() ? 0.9f : 0.5f)));
        ImGui.SetCursorScreenPos(tl + new Vector2(0f, h + Px(4f)));
    }

    private void DrawMessages(OsAppContext ctx, YapAuthorDto? peer, byte[]? peerKey, float winW)
    {
        var availableHeight = ImGui.GetWindowSize().Y - ImGui.GetCursorPosY() - InputBarHeight(peer, peerKey);
        PushScrollbarStyle();
        using (var child = ImRaii.Child("##yapDmThread", new Vector2(0f, availableHeight), false))
        {
            PopScrollbarStyle();
            if (!child.Success)
            {
                return;
            }
            var windowWidth = ImGui.GetContentRegionAvail().X;
            var messages = _dms.Thread(_peerId);

            // A message landing in the open chat sticks the view to the bottom; a jump in flight keeps priority.
            if (messages.Count > _lastConvCount && _lastConvCount >= 0 && _scrollTargetMessageId == Guid.Empty)
            {
                _scrollToBottom = 1f;
            }
            _lastConvCount = messages.Count;
            if (_loaded && !_seenSeeded)
            {
                foreach (var m in messages)
                {
                    _seenMsgIds.Add(m.Id);
                }
                _seenSeeded = true;
            }

            SegmentEmoji.CaptureRightClick = false;
            SegmentEmoji.RightClickedName = null;

            if (_olderCursor is not null && !_loading)
            {
                var label = Loc.T("os.yapper_dm_load_earlier");
                ImGui.SetCursorPosX((windowWidth - ImGui.CalcTextSize(label).X) * 0.5f - Px(8f));
                if (ImGui.SmallButton($"{label}##yapDmOlder"))
                {
                    LoadOlder();
                }
                HandOnHover();
            }

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

                if (prev is null || msg.SentAtUtc.ToLocalTime().Date != prev.SentAtUtc.ToLocalTime().Date)
                {
                    DrawDayDivider(msg.SentAtUtc.LocalDateTime);
                }
                if (msg.Id == _scrollTargetMessageId)
                {
                    targetY = ImGui.GetCursorPosY();
                }
                DrawMessageBubble(ctx, msg, peerKey, windowWidth, i,
                    StartsNewGroup(msg, prev), next is null || StartsNewGroup(next, msg));
            }

            if (ImGui.BeginPopup("##yapDmEmojiFavMenu"))
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
            else if (_scrollToBottom > 0f)
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

    private bool StartsNewGroup(YapperDmMessageDto cur, YapperDmMessageDto? prev)
        => prev is null
           || prev.SenderProfileId != cur.SenderProfileId
           || cur.SentAtUtc.ToLocalTime().Date != prev.SentAtUtc.ToLocalTime().Date
           || cur.SentAtUtc - prev.SentAtUtc > GroupWindow;

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

    private string DisplayText(byte[]? peerKey, YapperDmMessageDto m, out bool decrypted)
    {
        if (m.DeletedAtUtc is not null)
        {
            decrypted = false;
            return Loc.T("os.yapper_dm_deleted");
        }
        var text = Decrypt(peerKey, m);
        decrypted = text is not null;
        return text ?? DecryptPlaceholder(peerKey);
    }

    private void DrawMessageBubble(OsAppContext ctx, YapperDmMessageDto msg, byte[]? peerKey,
        float windowWidth, int slot, bool isGroupStart, bool isGroupEnd)
    {
        var mine = msg.SenderProfileId != _peerId;
        var text = DisplayText(peerKey, msg, out var decrypted);
        var parsed = ParsedMessage.Parse(text);
        var maxBubW = windowWidth * 0.72f;
        var padding = Px(12, 8);
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        var (entryDy, entryAlpha) = MessageEntrance(msg.Id);
        var fading = entryAlpha < 0.999f;
        if (fading)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, entryAlpha * ImGui.GetStyle().Alpha);
        }

        var bubbleColor = ImGui.ColorConvertFloat4ToU32(mine ? ChatColors.OwnBg : ChatColors.PeerBg);
        var bubbleLeft = mine ? cursorPos.X + windowWidth - maxBubW - Px(10) : cursorPos.X + Px(10);
        if (fading)
        {
            var bakedAlpha = (uint)(((bubbleColor >> 24) & 0xFFu) * entryAlpha);
            bubbleColor = (bubbleColor & 0x00FFFFFFu) | (bakedAlpha << 24);
        }

        var innerW = maxBubW - padding.X * 2f;
        var innerH = MathF.Max(parsed.MeasureHeight(innerW), ImGui.GetTextLineHeight());
        var bubbleH = innerH + padding.Y * 2f;

        var quoteH = ReplyQuoteHeight(msg);
        if (quoteH > 0f)
        {
            DrawReplyQuote(msg, peerKey, bubbleLeft, cursorPos.Y + entryDy, maxBubW);
        }

        var bubbleTL = new Vector2(bubbleLeft, cursorPos.Y + entryDy + quoteH);
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

        ImGui.SetCursorScreenPos(bubbleTL + padding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using (var body = ImRaii.Child($"##yapDmBody{slot}", new Vector2(innerW, innerH), false,
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
            ImGui.OpenPopup("##yapDmEmojiFavMenu");
        }
        else if (msg.DeletedAtUtc is null
            && ImGui.BeginPopupContextItem($"##yapDmCtx{msg.Id}", ImGuiPopupFlags.MouseButtonRight))
        {
            DrawMessageContextMenu(msg, decrypted ? text : null, mine);
            ImGui.EndPopup();
        }

        var reactionsH = DrawReactions(msg, mine, bubbleLeft, bubbleTL.Y + bubbleH, maxBubW);

        if (isGroupEnd)
        {
            var local = msg.SentAtUtc.LocalDateTime;
            var seenSuffix = mine && msg.ReadByPeerAtUtc is not null ? Loc.T("chat.seen_suffix") : string.Empty;
            var timeStr = local.ToString("HH:mm") + seenSuffix;
            var timeSize = ImGui.CalcTextSize(timeStr);
            var timeX = mine ? bubbleTL.X + maxBubW - timeSize.X : bubbleTL.X;
            ImGui.SetCursorScreenPos(new Vector2(timeX, bubbleTL.Y + bubbleH + reactionsH + Px(2f)));
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.75f, 0.40f), timeStr);
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, quoteH + bubbleH + reactionsH + timeSize.Y + Px(8f)));
        }
        else
        {
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, quoteH + bubbleH + reactionsH + Px(2f)));
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
        var e = EaseOutCubicF(p);
        return (Px(10f) * (1f - e), e);
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

    private YapperDmMessageDto? FindMessage(Guid id)
        => _dms.Thread(_peerId).FirstOrDefault(m => m.Id == id);

    private string QuotePreview(byte[]? peerKey, Guid quotedId)
    {
        var original = FindMessage(quotedId);
        if (original is null || original.DeletedAtUtc is not null)
        {
            return Loc.T("chat.quote_unavailable");
        }
        var author = original.SenderProfileId == _peerId
            ? _dms.Peer(_peerId)?.DisplayName ?? string.Empty
            : Loc.T("chat.you");
        var raw = Decrypt(peerKey, original) ?? DecryptPlaceholder(peerKey);
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

    private float ReplyQuoteHeight(YapperDmMessageDto msg)
        => msg.ReplyToMessageId is not null ? ImGui.GetTextLineHeight() + Px(12f) : 0f;

    private void DrawReplyQuote(YapperDmMessageDto msg, byte[]? peerKey, float left, float top, float width)
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
            TruncateToWidth(QuotePreview(peerKey, quotedId), width - Px(16f)));

        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton($"##yapDmQj{msg.Id}", new Vector2(width, boxH));
        HandOnHover();
        if (ImGui.IsItemClicked())
        {
            JumpToMessage(quotedId);
        }
    }

    private void JumpToMessage(Guid id)
    {
        _scrollTargetMessageId = id;
        _scrollToMessageTimer = 0.6f;
        _flashTimer = AccessibilityService.ReduceMotion ? 0f : FlashDuration;
        _scrollToBottom = 0f;
    }

    private (List<string> Mine, Dictionary<string, int> Counts) EffectiveReactions(YapperDmMessageDto msg)
    {
        var myId = _myProfileId();
        var mine = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (msg.Reactions is { } reactions)
        {
            foreach (var r in reactions)
            {
                foreach (var token in r.Tokens)
                {
                    counts[token] = counts.GetValueOrDefault(token) + 1;
                    if (myId is { } me && r.ProfileId == me)
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
    private List<string>? ReconcileReactionDisplay(YapperDmMessageDto msg)
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

        var (_, counts) = EffectiveReactions(msg);
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

    private float DrawReactions(YapperDmMessageDto msg, bool mineMsg, float bubbleLeft, float bubbleBottomY, float maxBubW)
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
                ImGui.InvisibleButton($"##yapDmRx{msg.Id}_{name}", new Vector2(chipW, chipH));
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

    private void ToggleMyReaction(YapperDmMessageDto msg, string emoji)
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
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.ReactDmAsync(msgId, emoji, add).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Warning(ex, "[Yapper] DM reaction toggle failed.");
                _rxPending.Remove((msgId, emoji));
                _chatError = AetherLove.Services.HubErrorText.Localize(ex);
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
        if (FindMessage(target) is { } msg)
        {
            ToggleMyReaction(msg, name);
        }
    }

    private void SyncPinState(YapperDmMessageDto msg)
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

    private void ToggleMyPin(YapperDmMessageDto msg)
    {
        var pinned = msg.PinnedAtUtc is not null;
        _dms.ApplyPin(msg.Id, pinned ? null : DateTimeOffset.UtcNow);
        var id = msg.Id;
        _ = Task.Run(() => _host.SetDmPinnedAsync(id, !pinned));
    }

    /// <summary>Quick-reaction row + reply/pin/copy/delete actions for a message's right-click menu.</summary>
    private void DrawMessageContextMenu(YapperDmMessageDto msg, string? text, bool mine)
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
        if (ImGui.Button("+##yapDmMoreReact", new Vector2(sz + Px(4f), sz + Px(4f))))
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
            ImGui.SetClipboardText(ParsedMessage.Parse(text).PlainText);
        }
        if (mine && DrawIconMenuItem(FontAwesomeIcon.TrashAlt, Loc.T("chat.delete_message"), UiColors.MenuDanger))
        {
            ImGui.CloseCurrentPopup();
            _dms.ApplyDeleted(msg.Id);
            var id = msg.Id;
            _ = Task.Run(() => _host.DeleteDmAsync(id));
        }
    }

    private float InputBarHeight(YapAuthorDto? peer, byte[]? peerKey)
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
        if (!CanSend(peerKey))
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

    private bool CanSend(byte[]? peerKey)
        => peerKey is not null && _host.HasDmKeys && !_sending;

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
    private void DrawReplyComposeBar(byte[]? peerKey)
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
            TruncateToWidth(Loc.T("chat.replying_to", QuotePreview(peerKey, id)), avail - barH - Px(16f)));

        ImGui.SetCursorScreenPos(new Vector2(br.X - barH, tl.Y));
        if (ImGui.InvisibleButton("##yapDmCancelReply", new Vector2(barH, barH)))
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

    private void DrawChatInput(YapAuthorDto? peer, byte[]? peerKey)
    {
        var windowWidth = ImGui.GetWindowSize().X;
        const float EmojiBtn = 28f;
        const float SendBtn = 56f;
        const float Gap = 4f;
        var inputWidth = windowWidth - Px(EmojiBtn) - Px(SendBtn) - Px(Gap * 3f);

        ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - InputBarHeight(peer, peerKey));
        DrawEmojiAutocompleteRow();
        ImGui.Separator();
        ImGui.Spacing();

        DrawReplyComposeBar(peerKey);

        var buttonSize = ImGui.GetFrameHeight();
        {
            var grinTex = AetherLove.UiHost.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(4f, 4f));
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(buttonSize - Px(8f)))
                : ImGui.Button($"{Loc.T("chat.emoji_button")}##yapDmEmoji", new Vector2(buttonSize, 0));
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
        var enterPressed = ImGui.InputTextMultiline("##yapDmMessageInput", ref _inputText, MaxMessageChars,
            new Vector2(inputWidth, ChatInputBoxHeight()),
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CtrlEnterForNewLine
            | ImGuiInputTextFlags.CallbackEdit | ImGuiInputTextFlags.CallbackAlways,
            ChatWrapCallback);
        ImGui.SameLine(0, Px(Gap));
        var canSend = CanSend(peerKey);
        if ((ImGui.Button(Loc.T("chat.send"), new Vector2(Px(SendBtn), 0)) || enterPressed)
            && _inputText.Length > 0 && canSend)
        {
            SendCurrentInput(peerKey!);
            _reclaimInputFocus = true;
        }
        if (!canSend)
        {
            ImGui.SetCursorPosX(Px(10f));
            ImGui.TextColored(UiColors.Muted, !_host.HasDmKeys || peer is null || !_loaded
                ? Loc.T("os.yapper_dm_keys_pending")
                : Loc.T("huberror.yap_dm_keys_missing", $"{peer.Handle}"));
        }
        if (_chatError is { } err)
        {
            ImGui.SetCursorPosX(Px(10f));
            ImGui.TextColored(UiColors.Danger, err);
        }
    }

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

    private void SendCurrentInput(byte[] peerKey)
    {
        if (string.IsNullOrWhiteSpace(_inputText) || !ParsedMessage.Parse(_inputText).HasVisibleContent)
        {
            return;
        }
        var text = _inputText.Replace('\n', ' ').Trim();
        var encrypted = _host.EncryptDm(peerKey, text);
        if (encrypted is not { } payload)
        {
            _chatError = Loc.T("os.yapper_dm_keys_pending");
            return;
        }
        _inputText = string.Empty;
        _scrollToBottom = 1f;
        var replyTo = _replyingToId;
        _replyingToId = null;
        _chatError = null;
        _sending = true;
        var peerId = _peerId;
        _ = Task.Run(async () =>
        {
            try
            {
                var sent = await _host.SendDmAsync(peerId, payload.Ciphertext, payload.Nonce, replyTo)
                    .ConfigureAwait(false);
                _dms.Append(peerId, sent, _dms.Peer(peerId), countUnread: false);
                _scrollToBottom = 1f;
            }
            catch (Exception ex)
            {
                _chatError = AetherLove.Services.HubErrorText.Localize(ex);
                if (_inputText.Length == 0 && _peerId == peerId)
                {
                    _inputText = text;
                }
            }
            finally
            {
                _sending = false;
            }
        });
    }

    private void LoadOlder()
    {
        if (_loading || _olderCursor is null)
        {
            return;
        }
        _loading = true;
        var peerId = _peerId;
        var cursor = _olderCursor;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await _host.GetDmThreadAsync(peerId, cursor).ConfigureAwait(false);
                _dms.PrependOlder(peerId, page.Messages);
                _olderCursor = page.NextCursor;
            }
            catch (Exception)
            {
            }
            finally
            {
                _loading = false;
            }
        });
    }

    private string? Decrypt(byte[]? peerKey, YapperDmMessageDto message)
    {
        if (peerKey is null || message.Ciphertext.Length == 0)
        {
            return null;
        }
        return _host.DecryptDm(peerKey, message.Ciphertext, message.Nonce);
    }

    /// <summary>The stand-in for a body that would not decrypt, picked by the reason: keys still provisioning
    /// are a transient state that resolves on a later frame, never a failed decryption.</summary>
    private string DecryptPlaceholder(byte[]? peerKey)
        => Loc.T(!_host.HasDmKeys || peerKey is null
            ? "os.yapper_dm_keys_pending"
            : "os.yapper_dm_undecryptable");

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
