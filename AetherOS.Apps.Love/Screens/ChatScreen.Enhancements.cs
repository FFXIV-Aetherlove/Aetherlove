using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Reply/quote, emoji reactions, and pinned messages. Reactions are plaintext server metadata in a
/// mine/theirs two-column model resolved per caller (the client never knows its own profile id); all state
/// mutation is marshalled to the UI thread via <see cref="_uiActions"/>.</summary>
public partial class ChatScreen
{
    /// <summary>Reactions placed by this user, per message id (the column the server lets us edit).</summary>
    private readonly Dictionary<Guid, List<string>> _myReactions = new();
    /// <summary>Reactions placed by the peer, per message id (read-only attribution).</summary>
    private readonly Dictionary<Guid, List<string>> _theirReactions = new();
    /// <summary>Ordered chips currently shown, including any mid-exit, so a removed chip can fade out
    /// before its slot is pruned.</summary>
    private readonly Dictionary<Guid, List<string>> _rxDisplay = new();

    private readonly HashSet<Guid> _pinned = new();
    private readonly Dictionary<Guid, Guid> _replyTo = new();
    private readonly List<(Guid Temp, Guid Real)> _pendingIdMigrations = new();

    /// <summary>Optimistic ids not yet acknowledged; reactions/pins on these are deferred until the real id arrives.</summary>
    private readonly HashSet<Guid> _unsentTempIds = new();
    private readonly Dictionary<Guid, List<Action<Guid>>> _deferredByTempId = new();

    /// <summary>Work marshalled onto the UI thread from push handlers and hub-reply continuations.</summary>
    private readonly ConcurrentQueue<Action> _uiActions = new();

    private Guid? _replyingToId;
    private Guid? _pendingReactionPickerId;
    private bool _pinnedListPendingOpen;
    private bool _reactionUsageDirty;

    private readonly Dictionary<Guid, float> _pinAnim = new();
    private readonly Dictionary<(Guid Msg, string Emoji), float> _rxEnter = new();
    private readonly Dictionary<(Guid Msg, string Emoji), float> _rxExit = new();
    private const float PinDropDuration = 0.32f;
    private const float ReactionFxDuration = 0.18f;

    /// <summary>Distinct reactions one user may place on a message; mirrors the server cap.</summary>
    private const int MaxReactionsPerUser = 5;
    private static readonly string[] QuickReactDefaults =
        ["heart", "heart_eyes", "joy", "thumbsup", "fire", "cry"];

    /// <summary>The quick-react bar, recomputed from usage on each conversation open and cached for the session.</summary>
    private string[] _quickReact = QuickReactDefaults;

    private void ResetEnhancements()
    {
        _myReactions.Clear();
        _theirReactions.Clear();
        _rxDisplay.Clear();
        _pinned.Clear();
        _replyTo.Clear();
        _replyingToId = null;
        _pendingReactionPickerId = null;
        _pinnedListPendingOpen = false;
        _quickReact = ComputeQuickReact();
        _pinAnim.Clear();
        _rxEnter.Clear();
        _rxExit.Clear();
        _unsentTempIds.Clear();
        _deferredByTempId.Clear();
        lock (_messagesLock)
        {
            _pendingIdMigrations.Clear();
        }
    }

    private void DrainUiActions()
    {
        while (_uiActions.TryDequeue(out var act))
        {
            act();
        }
    }

    /// <summary>Moves a message's enhancement state from its optimistic temp id to the server-assigned id
    /// and fires any deferred hub calls against the real id.</summary>
    private void DrainIdMigrations()
    {
        (Guid Temp, Guid Real)[] pending;
        lock (_messagesLock)
        {
            if (_pendingIdMigrations.Count == 0)
            {
                return;
            }
            pending = _pendingIdMigrations.ToArray();
            _pendingIdMigrations.Clear();
        }
        foreach (var (temp, real) in pending)
        {
            if (_myReactions.Remove(temp, out var mine)) { _myReactions[real] = mine; }
            if (_theirReactions.Remove(temp, out var theirs)) { _theirReactions[real] = theirs; }
            if (_rxDisplay.Remove(temp, out var disp)) { _rxDisplay[real] = disp; }
            MigrateAnimKeys(_rxEnter, temp, real);
            MigrateAnimKeys(_rxExit, temp, real);
            if (_pinned.Remove(temp)) { _pinned.Add(real); }
            if (_pinAnim.Remove(temp, out var pa)) { _pinAnim[real] = pa; }
            if (_replyTo.Remove(temp, out var q)) { _replyTo[real] = q; }
            foreach (var k in _replyTo.Keys.ToArray())
            {
                if (_replyTo[k] == temp) { _replyTo[k] = real; }
            }
            _unsentTempIds.Remove(temp);
            if (_deferredByTempId.Remove(temp, out var actions))
            {
                foreach (var a in actions) { a(real); }
            }
        }
    }

    private static void MigrateAnimKeys(Dictionary<(Guid Msg, string Emoji), float> map, Guid temp, Guid real)
    {
        foreach (var key in map.Keys.Where(k => k.Msg == temp).ToArray())
        {
            var v = map[key];
            map.Remove(key);
            map[(real, key.Emoji)] = v;
        }
    }

    /// <summary>Seeds reply/reaction/pin state for one message from its loaded DTO without entrance animation.</summary>
    private void SeedEnhancements(EncryptedMessageDto m)
    {
        if (m.ReplyToMessageId is { } rep)
        {
            _replyTo[m.Id] = rep;
        }
        if (m.PinnedAtUtc is not null)
        {
            SetPinnedLocal(m.Id, true, animate: false);
        }
        var mine = m.MyReactions;
        var theirs = m.TheirReactions;
        if (mine is { Length: > 0 } || theirs is { Length: > 0 })
        {
            ApplyReactionState(m.Id, mine ?? [], theirs ?? [], animate: false);
        }
    }

    private void ApplyReactionState(Guid msgId, IReadOnlyList<string> mine, IReadOnlyList<string> theirs, bool animate)
    {
        _myReactions[msgId] = mine.ToList();
        _theirReactions[msgId] = theirs.ToList();
        ReconcileReactionDisplay(msgId, animate);
    }

    /// <summary>Reconciles the displayed chips with the two reaction columns; removed chips stay in the
    /// list until their exit completes. Order is first-seen.</summary>
    private void ReconcileReactionDisplay(Guid msgId, bool animate)
    {
        var mine = _myReactions.GetValueOrDefault(msgId);
        var theirs = _theirReactions.GetValueOrDefault(msgId);
        if (!_rxDisplay.TryGetValue(msgId, out var display))
        {
            display = new List<string>();
            _rxDisplay[msgId] = display;
        }

        var union = new List<string>();
        if (mine is not null)
        {
            foreach (var e in mine)
            {
                if (!union.Contains(e)) { union.Add(e); }
            }
        }
        if (theirs is not null)
        {
            foreach (var e in theirs)
            {
                if (!union.Contains(e)) { union.Add(e); }
            }
        }

        var doAnimate = animate && !AccessibilityService.ReduceMotion;
        foreach (var e in union)
        {
            var key = (msgId, e);
            if (!display.Contains(e))
            {
                display.Add(e);
                _rxExit.Remove(key);
                if (doAnimate) { _rxEnter[key] = 0f; } else { _rxEnter.Remove(key); }
            }
            else if (_rxExit.Remove(key) && doAnimate)
            {
                _rxEnter[key] = 0f;
            }
        }
        foreach (var e in display.ToArray())
        {
            if (union.Contains(e))
            {
                continue;
            }
            var key = (msgId, e);
            if (doAnimate)
            {
                if (!_rxExit.ContainsKey(key)) { _rxExit[key] = 0f; }
            }
            else
            {
                display.Remove(e);
                _rxEnter.Remove(key);
                _rxExit.Remove(key);
            }
        }
        if (display.Count == 0)
        {
            _rxDisplay.Remove(msgId);
            _myReactions.Remove(msgId);
            _theirReactions.Remove(msgId);
        }
    }

    /// <summary>Optimistic toggle; the server's authoritative reply reconciles drift and a failure reverts.</summary>
    private void ToggleMyReaction(Guid msgId, string emoji)
    {
        var mine = _myReactions.GetValueOrDefault(msgId) ?? new List<string>();
        var add = !mine.Contains(emoji);
        if (add && mine.Count >= MaxReactionsPerUser)
        {
            return;
        }

        var newMine = new List<string>(mine);
        if (add) { newMine.Add(emoji); } else { newMine.Remove(emoji); }
        _myReactions[msgId] = newMine;
        ReconcileReactionDisplay(msgId, animate: true);

        if (add)
        {
            UiHost.Configuration.ReactionUsage[emoji] =
                UiHost.Configuration.ReactionUsage.GetValueOrDefault(emoji) + 1;
            _reactionUsageDirty = true;
        }

        if (_unsentTempIds.Contains(msgId))
        {
            DeferUntilReal(msgId, realId => FireReaction(realId, emoji, add));
        }
        else
        {
            FireReaction(msgId, emoji, add);
        }
    }

    private void FireReaction(Guid msgId, string emoji, bool add)
    {
        var peer = _peerId;
        _ = Task.Run(async () =>
        {
            try
            {
                var res = await _hub.ReactToMessageAsync(
                    new ReactToMessageRequest(peer, msgId, emoji, add), CancellationToken.None).ConfigureAwait(false);
                _uiActions.Enqueue(() =>
                    ApplyReactionState(msgId, res.MyReactions ?? [], res.TheirReactions ?? [], animate: true));
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatScreen] ReactToMessageAsync failed.");
                _uiActions.Enqueue(() => RevertMyReaction(msgId, emoji, add));
            }
        });
    }

    private void RevertMyReaction(Guid msgId, string emoji, bool wasAdd)
    {
        var mine = _myReactions.GetValueOrDefault(msgId) ?? new List<string>();
        var newMine = new List<string>(mine);
        if (wasAdd) { newMine.Remove(emoji); }
        else if (!newMine.Contains(emoji)) { newMine.Add(emoji); }
        _myReactions[msgId] = newMine;
        ReconcileReactionDisplay(msgId, animate: true);
    }

    private void ToggleMyPin(Guid msgId)
    {
        var nowPinned = !_pinned.Contains(msgId);
        SetPinnedLocal(msgId, nowPinned, animate: true);
        if (_unsentTempIds.Contains(msgId))
        {
            DeferUntilReal(msgId, realId => FirePin(realId, nowPinned));
        }
        else
        {
            FirePin(msgId, nowPinned);
        }
    }

    private void FirePin(Guid msgId, bool pinned)
    {
        var peer = _peerId;
        _ = Task.Run(async () =>
        {
            try
            {
                var res = await _hub.SetMessagePinnedAsync(
                    new SetMessagePinnedRequest(peer, msgId, pinned), CancellationToken.None).ConfigureAwait(false);
                _uiActions.Enqueue(() => SetPinnedLocal(msgId, res.PinnedAtUtc is not null, animate: true));
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatScreen] SetMessagePinnedAsync failed.");
                _uiActions.Enqueue(() => SetPinnedLocal(msgId, !pinned, animate: true));
            }
        });
    }

    private void SetPinnedLocal(Guid msgId, bool pinned, bool animate)
    {
        if (pinned)
        {
            if (_pinned.Add(msgId) && animate && !AccessibilityService.ReduceMotion)
            {
                _pinAnim[msgId] = 0f;
            }
        }
        else
        {
            _pinned.Remove(msgId);
            _pinAnim.Remove(msgId);
        }
    }

    private void DeferUntilReal(Guid tempId, Action<Guid> action)
    {
        if (!_deferredByTempId.TryGetValue(tempId, out var list))
        {
            list = new List<Action<Guid>>();
            _deferredByTempId[tempId] = list;
        }
        list.Add(action);
    }

    private void OnReactionsChanged(MessageReactionsChangedPushDto p)
    {
        if (p.PeerProfileId != _peerId)
        {
            return;
        }
        _uiActions.Enqueue(() =>
            ApplyReactionState(p.MessageId, p.MyReactions ?? [], p.TheirReactions ?? [], animate: true));
    }

    private void OnPinChanged(MessagePinChangedPushDto p)
    {
        if (p.PeerProfileId != _peerId)
        {
            return;
        }
        _uiActions.Enqueue(() => SetPinnedLocal(p.MessageId, p.PinnedAtUtc is not null, animate: true));
    }

    /// <summary>Most-used reactions first (top 6 by tally), padded with defaults so the bar is always full.</summary>
    private static string[] ComputeQuickReact()
    {
        var result = new List<string>(6);
        foreach (var name in UiHost.Configuration.ReactionUsage
                     .OrderByDescending(kv => kv.Value)
                     .Select(kv => kv.Key))
        {
            if (result.Count >= 6) { break; }
            if (result.Contains(name) || UiHost.EmojiService.GetEmoji(name) is null) { continue; }
            result.Add(name);
        }
        foreach (var name in QuickReactDefaults)
        {
            if (result.Count >= 6) { break; }
            if (!result.Contains(name)) { result.Add(name); }
        }
        return result.ToArray();
    }

    /// <summary>Persists the reaction tally on leaving the conversation (counts accrue in memory until then).</summary>
    private void SaveReactionUsageIfDirty()
    {
        if (_reactionUsageDirty)
        {
            _reactionUsageDirty = false;
            UiHost.Configuration.Save();
        }
    }

    private (string Text, bool IsOwn, bool Found) MessageInfo(Guid id)
    {
        lock (_messagesLock)
        {
            foreach (var m in _messages)
            {
                if (m.Id == id) { return (m.Text, m.IsOwn, true); }
            }
        }
        return (string.Empty, false, false);
    }

    private void JumpToMessage(Guid id)
    {
        _scrollTargetMessageId = id;
        _scrollToMessageTimer = 0.6f;
        _flashTimer = AccessibilityService.ReduceMotion ? 0f : FlashDuration;
        _scrollToBottom = 0f;
    }

    private string QuotePreview(Guid quotedId)
    {
        var info = MessageInfo(quotedId);
        if (!info.Found)
        {
            return Loc.T("chat.quote_unavailable");
        }
        var author = info.IsOwn ? Loc.T("chat.you") : _peerName;
        // PlainText drops emoji shortcodes; fall back to the raw text so an emoji-only message isn't an empty preview.
        var body = ParsedMessage.Parse(info.Text).PlainText.Trim();
        if (body.Length == 0) { body = info.Text.Trim(); }
        if (body.Length == 0) { body = Loc.T("chat.quote_generic"); }
        return $"{author}: {body}";
    }

    private float ReplyQuoteHeight(Guid messageId)
        => _replyTo.ContainsKey(messageId) ? ImGui.GetTextLineHeight() + Px(12f) : 0f;

    private float ReactionsHeight(Guid messageId)
        => _rxDisplay.TryGetValue(messageId, out var l) && l.Count > 0
            ? ImGui.GetTextLineHeight() + Px(12f)
            : 0f;

    /// <summary>Quoted-original strip above a reply bubble; height must match <see cref="ReplyQuoteHeight"/>.</summary>
    private void DrawReplyQuote(Guid messageId, float left, float top, float width)
    {
        if (!_replyTo.TryGetValue(messageId, out var quotedId))
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
            TruncateToWidth(QuotePreview(quotedId), width - Px(16f)));

        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton($"##qj{messageId}", new Vector2(width, boxH));
        if (ImGui.IsItemHovered()) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
        if (ImGui.IsItemClicked()) { JumpToMessage(quotedId); }
    }

    /// <summary>Reaction chips under a bubble; a slot stays reserved until a chip's exit completes.
    /// Returns the vertical space used.</summary>
    private float DrawReactions(DisplayedMessage msg, float bubbleLeft, float bubbleBottomY, float maxBubW)
    {
        if (!_rxDisplay.TryGetValue(msg.Id, out var display) || display.Count == 0)
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
        var mine = _myReactions.GetValueOrDefault(msg.Id);
        var theirs = _theirReactions.GetValueOrDefault(msg.Id);

        var names = display.ToArray();
        var widths = new float[names.Length];
        var totalW = 0f;
        for (int i = 0; i < names.Length; i++)
        {
            var count = (mine?.Contains(names[i]) == true ? 1 : 0) + (theirs?.Contains(names[i]) == true ? 1 : 0);
            var w = emojiSz + padX * 2f;
            if (count > 1) { w += ImGui.CalcTextSize(count.ToString()).X + Px(3f); }
            widths[i] = w;
            totalW += w + gap;
        }
        totalW -= gap;

        var x = msg.IsOwn ? bubbleLeft + maxBubW - totalW : bubbleLeft;
        var y = bubbleBottomY + Px(3f);

        for (int i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var chipW = widths[i];
            var key = (msg.Id, name);
            var tl = new Vector2(x, y);
            x += chipW + gap; // reserve the full slot regardless of the chip's animated scale

            float scale = 1f, alpha = 1f;
            var exiting = false;
            if (reduceMotion)
            {
                _rxEnter.Remove(key);
                if (_rxExit.Remove(key)) { display.Remove(name); continue; }
            }
            else if (_rxExit.TryGetValue(key, out var ep))
            {
                exiting = true;
                ep += dt / ReactionFxDuration;
                if (ep >= 1f) { _rxExit.Remove(key); display.Remove(name); continue; }
                _rxExit[key] = ep;
                scale = 1f - EaseInCubic(ep) * 0.5f;
                alpha = 1f - ep;
            }
            else if (_rxEnter.TryGetValue(key, out var np))
            {
                np += dt / ReactionFxDuration;
                if (np >= 1f) { _rxEnter.Remove(key); }
                else { _rxEnter[key] = np; }
                var e = EaseOutCubic(MathF.Min(np, 1f));
                scale = 0.5f + e * 0.5f;
                alpha = e;
            }

            var isMine = mine?.Contains(name) == true;
            var count = (isMine ? 1 : 0) + (theirs?.Contains(name) == true ? 1 : 0);
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
            var tex = UiHost.EmojiService.GetEmoji(name)?.GetWrapOrDefault();
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
                ImGui.InvisibleButton($"##rx{msg.Id}_{name}", new Vector2(chipW, chipH));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip(isMine
                        ? $":{name}: ({Loc.T("chat.reaction_remove_hint")})"
                        : $":{name}:");
                }
                if (ImGui.IsItemClicked()) { ToggleMyReaction(msg.Id, name); }
            }
        }
        if (display.Count == 0) { _rxDisplay.Remove(msg.Id); }
        return ImGui.GetTextLineHeight() + Px(12f);
    }

    /// <summary>Thumbtack straddling a pinned bubble's top outer corner; drops in from above on pin.</summary>
    private void DrawPinMarker(Vector2 bubbleTL, float maxBubW, Guid messageId, bool isOwn)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var icon = FontAwesomeIcon.Thumbtack.ToIconString();
        var sz = ImGui.CalcTextSize(icon);
        ImGui.PopFont();

        // Inset past the corner radius so it sits on the flat top, needle overlapping into the bubble.
        var cornerX = isOwn ? bubbleTL.X : bubbleTL.X + maxBubW;
        var inset = isOwn ? Px(11f) : -Px(11f);
        var restPos = new Vector2(cornerX - sz.X * 0.5f + inset, bubbleTL.Y - sz.Y * 0.7f - Px(3f));

        float drop = 0f, alpha = 1f;
        if (!AccessibilityService.ReduceMotion && _pinAnim.TryGetValue(messageId, out var p))
        {
            p += ImGui.GetIO().DeltaTime / PinDropDuration;
            if (p >= 1f) { _pinAnim.Remove(messageId); p = 1f; }
            else { _pinAnim[messageId] = p; }
            drop = -(1f - EaseOutBack(p)) * Px(16f);
            alpha = MathF.Min(1f, p * 1.6f);
        }

        // The accent clashes on your own bubble; use a soft off-white there.
        var baseCol = isOwn
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.92f, 0.93f, 1f))
            : ThemeService.Current.AccentLightU32;
        var col = (baseCol & 0x00FFFFFFu) | ((uint)(255f * alpha) << 24);
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), restPos + new Vector2(0f, drop), col, icon);
        ImGui.PopFont();
    }

    private static float EaseOutCubic(float x) => 1f - MathF.Pow(1f - x, 3f);

    private static float EaseInCubic(float x) => x * x * x;

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var u = x - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    /// <summary>Quick-reaction row + reply/pin/copy actions for a message's right-click menu.</summary>
    private void DrawMessageContextMenu(DisplayedMessage msg)
    {
        var sz = ImGui.GetTextLineHeight() + Px(6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(2f), Px(2f)));
        for (int i = 0; i < _quickReact.Length; i++)
        {
            var name = _quickReact[i];
            var tex = UiHost.EmojiService.GetEmoji(name)?.GetWrapOrDefault();
            ImGui.PushID(i);
            if (tex != null && ImGui.ImageButton(tex.Handle, new Vector2(sz)))
            {
                ToggleMyReaction(msg.Id, name);
                ImGui.CloseCurrentPopup();
            }
            ImGui.PopID();
            ImGui.SameLine(0f, Px(2f));
        }
        if (ImGui.Button("+##morereact", new Vector2(sz + Px(4f), sz + Px(4f))))
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
        var pinned = _pinned.Contains(msg.Id);
        if (DrawIconMenuItem(pinned ? FontAwesomeIcon.ThumbtackSlash : FontAwesomeIcon.Thumbtack,
                pinned ? Loc.T("chat.unpin_message") : Loc.T("chat.pin_message")))
        {
            ImGui.CloseCurrentPopup();
            ToggleMyPin(msg.Id);
        }
        if (DrawIconMenuItem(FontAwesomeIcon.Copy, Loc.T("chat.menu_copy_message")))
        {
            ImGui.CloseCurrentPopup();
            CopyTextWithLinkWarning(msg.Text);
        }
    }

    /// <summary>The "replying to ..." strip drawn above the input bar while composing a reply.</summary>
    private void DrawReplyComposeBar()
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
            TruncateToWidth(Loc.T("chat.replying_to", QuotePreview(id)), avail - barH - Px(16f)));

        ImGui.SetCursorScreenPos(new Vector2(br.X - barH, tl.Y));
        if (ImGui.InvisibleButton("##cancelReply", new Vector2(barH, barH))) { _replyingToId = null; }
        var hov = ImGui.IsItemHovered();
        if (hov) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var xicon = FontAwesomeIcon.Times.ToIconString();
        var xsz = ImGui.CalcTextSize(xicon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(br.X - barH + (barH - xsz.X) * 0.5f, tl.Y + (barH - xsz.Y) * 0.5f),
            hov ? t.AccentLightU32 : 0xFFAAAAAAu, xicon);
        ImGui.PopFont();

        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y + Px(4f)));
    }

    /// <summary>Pinned-messages popup floating over the chat; opened via OpenPopup at the top of <see cref="Draw"/>.</summary>
    private void DrawPinnedOverlay()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var panelW = winSize.X - Px(32f);
        var panelH = MathF.Min(Px(320f), MathF.Max(Px(120f), (winSize.Y - Px(HeaderH)) * 0.72f));
        ImGui.SetNextWindowPos(new Vector2(winPos.X + Px(16f), winPos.Y + Px(HeaderH) + Px(10f)));
        ImGui.SetNextWindowSize(new Vector2(panelW, panelH));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(12f), Px(12f)));
        using var popup = ImRaii.Popup("##pinnedOverlay", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
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
        if (ImGui.SmallButton("X##closePinned"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.Separator();
        ImGui.Spacing();

        var ids = _pinned.ToArray();
        if (ids.Length == 0)
        {
            ImGui.CloseCurrentPopup();
            return;
        }
        foreach (var id in ids)
        {
            var info = MessageInfo(id);
            var preview = ParsedMessage.Parse(info.Found ? info.Text : Loc.T("chat.quote_unavailable")).PlainText.Trim();
            if (preview.Length == 0)
            {
                preview = info.Found ? info.Text.Trim() : Loc.T("chat.quote_unavailable");
            }
            ImGui.PushID(id.ToString());
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - Px(4f));
            ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.88f, 1f),
                $"{(info.IsOwn ? Loc.T("chat.you") : _peerName)}: {preview}");
            ImGui.PopTextWrapPos();
            if (ImGui.SmallButton(Loc.T("chat.jump")))
            {
                ImGui.CloseCurrentPopup();
                JumpToMessage(id);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("chat.unpin")))
            {
                ToggleMyPin(id);
            }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PopID();
        }
    }
}
