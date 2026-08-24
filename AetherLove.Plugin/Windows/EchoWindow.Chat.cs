using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.EchoVidya;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Windows;

/// <summary>The room chat, modelled on the messenger's conversation view: emoji-aware bubbles, day dividers
/// and an emoji-picker input. Lines are relayed live and only ever live in the state service's ring buffer,
/// so nothing here is persisted or catches up after a rejoin.</summary>
public sealed partial class EchoWindow
{
    private const float ChatBubbleWidthFactor = 0.82f;
    private const int ChatInputMaxLines = 4;
    private const int ChatCounterLead = 60;
    private const float ChatPillH = 24f;
    private const float BubbleRounding = 9f;
    private static readonly TimeSpan ChatGroupWindow = TimeSpan.FromMinutes(5);

    private readonly List<EchoChatLineDto> _lines = new();
    private readonly EmojiPickerPopup _emojiPicker = new();

    private string _chatInput = string.Empty;
    private string? _chatError;
    private volatile bool _chatSending;
    private bool _reclaimChatFocus;
    private float _scrollToBottom;
    private bool _stuckToBottom = true;
    private int _unseen;

    private void ResetChat()
    {
        _lines.Clear();
        _lines.AddRange(_state.Chat);
        _chatInput = string.Empty;
        _chatError = null;
        _unseen = 0;
        _stuckToBottom = true;
        _scrollToBottom = 1f;
    }

    private void ClearUnseen()
    {
        _unseen = 0;
        _scrollToBottom = 1f;
        _stuckToBottom = true;
    }

    private void OnChatReceived(EchoChatLineDto line)
    {
        _lines.Add(line);
        while (_lines.Count > EchoLimits.MaxChatHistory)
        {
            _lines.RemoveAt(0);
        }
        if (_pane == SidebarPane.Chat && _stuckToBottom)
        {
            _scrollToBottom = 1f;
            return;
        }
        _unseen++;
    }

    private void DrawChatPane(ThemeDefinition t, Vector2 body)
    {
        var inputH = ChatInputHeight();
        var listH = MathF.Max(Px(60f), body.Y - inputH);
        var listOrigin = ImGui.GetCursorScreenPos();

        DrawChatMessages(new Vector2(body.X, listH));
        DrawNewMessagesPill(t, listOrigin, new Vector2(body.X, listH));
        DrawChatInput(body.X);
        _emojiPicker.Draw();
    }

    private void DrawChatMessages(Vector2 size)
    {
        using var list = ImRaii.Child("##echoChatList", size, false);
        if (!list)
        {
            return;
        }

        var width = ImGui.GetContentRegionAvail().X;
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            var previous = i > 0 ? _lines[i - 1] : null;
            if (previous is null || line.AtUtc.ToLocalTime().Date != previous.AtUtc.ToLocalTime().Date)
            {
                DrawDayDivider(line.AtUtc.LocalDateTime);
            }
            DrawChatLine(line, StartsNewGroup(line, previous), i, width);
        }

        if (ImGui.GetIO().MouseWheel != 0f)
        {
            _scrollToBottom = 0f;
        }
        var atBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1f;
        _stuckToBottom = atBottom;
        if (atBottom)
        {
            _unseen = 0;
        }
        if (_scrollToBottom > 0f)
        {
            ImGui.SetScrollY(ImGui.GetScrollMaxY());
            _scrollToBottom -= ImGui.GetIO().DeltaTime;
        }
    }

    private static bool StartsNewGroup(EchoChatLineDto current, EchoChatLineDto? previous)
        => previous is null
           || previous.AccountId != current.AccountId
           || current.AtUtc.ToLocalTime().Date != previous.AtUtc.ToLocalTime().Date
           || current.AtUtc - previous.AtUtc > ChatGroupWindow;

    private void DrawChatLine(EchoChatLineDto line, bool groupStart, int slot, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var mine = line.AccountId == _myAccountId;
        var key = LineKey(line);
        var parsed = ParsedMessage.Parse(_translate.Display(key, line.Text));
        var padding = Px(10f, 6f);
        var maxBubbleW = width * ChatBubbleWidthFactor;
        var innerW = maxBubbleW - padding.X * 2f;
        var origin = ImGui.GetCursorScreenPos();

        var headerH = 0f;
        if (groupStart)
        {
            headerH = ImGui.GetTextLineHeight() * 0.9f + Px(3f);
            var stamp = line.AtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
            var header = mine ? stamp : $"{line.DisplayName}  {stamp}";
            var headerSize = ImGui.CalcTextSize(header) * 0.9f;
            var headerX = mine ? origin.X + width - Px(4f) - headerSize.X : origin.X + Px(4f);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.9f, new Vector2(headerX, origin.Y),
                mine ? 0x66FFFFFFu : ImGui.GetColorU32(IdentityColor(line.AccountId)), header);
        }

        var contentH = MathF.Max(parsed.MeasureHeight(innerW), ImGui.GetTextLineHeight());
        var bubbleH = contentH + padding.Y * 2f;
        var bubbleLeft = mine ? origin.X + width - maxBubbleW : origin.X;
        var bubbleTL = new Vector2(bubbleLeft, origin.Y + headerH);
        dl.AddRectFilled(bubbleTL, bubbleTL + new Vector2(maxBubbleW, bubbleH),
            ImGui.ColorConvertFloat4ToU32(mine ? ChatColors.OwnBg : ChatColors.PeerBg), Px(BubbleRounding));

        ImGui.SetCursorScreenPos(bubbleTL + padding);
        ImGui.PushStyleColor(ImGuiCol.Text, mine ? ChatColors.OwnFg : ChatColors.PeerFg);
        parsed.DrawWrapped($"##echoChatBody{slot}", innerW);
        ImGui.PopStyleColor();

        // The bubble is draw-list output rather than one item, so the menu opens off a hand hit-test of
        // its rect, the party chat's own shape: copy, then the translate entries.
        if (ImGui.IsMouseHoveringRect(bubbleTL, bubbleTL + new Vector2(maxBubbleW, bubbleH))
            && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _menuLine = key;
            ImGui.OpenPopup("##echoLineMenu");
        }
        if (_menuLine == key && ImGui.BeginPopup("##echoLineMenu"))
        {
            if (SharedUiHelpers.DrawIconMenuItem(Dalamud.Interface.FontAwesomeIcon.Copy, Loc.T("chat.menu_copy_message")))
            {
                ImGui.CloseCurrentPopup();
                _caps.System.CopyToClipboard(ParsedMessage.Parse(line.Text).PlainText);
            }
            _translate.DrawMenuItems(key, line.Text);
            ImGui.EndPopup();
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, bubbleTL.Y + bubbleH + Px(5f)));
    }

    /// <summary>A line has no id on the wire; sender plus stamp is unique enough for a translation key.</summary>
    private static string LineKey(EchoChatLineDto line) => $"echo{line.AccountId:N}{line.AtUtc.UtcTicks}";

    private static void DrawDayDivider(DateTime date)
    {
        var label = LanguageProvider.FormatDate(date, "D");
        var dl = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(label);
        ImGui.Spacing();
        var origin = ImGui.GetCursorScreenPos();
        var lineY = origin.Y + MathF.Round(textSize.Y * 0.5f);
        var textX = origin.X + MathF.Round((avail - textSize.X) * 0.5f);
        var pad = Px(8f);
        dl.AddLine(new Vector2(origin.X, lineY), new Vector2(textX - pad, lineY), UiColors.Divider, 1f);
        dl.AddText(new Vector2(textX, origin.Y), 0x47FFFFFFu, label);
        dl.AddLine(new Vector2(textX + textSize.X + pad, lineY), new Vector2(origin.X + avail, lineY),
            UiColors.Divider, 1f);
        ImGui.Dummy(new Vector2(avail, textSize.Y));
        ImGui.Spacing();
    }

    /// <summary>The "N new" pill, in its own layer over the list: a child submitted after the list draws above
    /// it, and keeping the layer pill-sized leaves the rest of the list scrollable.</summary>
    private void DrawNewMessagesPill(ThemeDefinition t, Vector2 listOrigin, Vector2 listSize)
    {
        if (_unseen <= 0)
        {
            return;
        }
        var label = string.Format(CultureInfo.CurrentCulture, Loc.T("echo.new_messages"), _unseen);
        var labelSize = ImGui.CalcTextSize(label);
        var pillSize = new Vector2(labelSize.X + Px(22f), Px(ChatPillH));
        var pillTL = new Vector2(
            listOrigin.X + (listSize.X - pillSize.X) * 0.5f,
            listOrigin.Y + listSize.Y - pillSize.Y - Px(8f));

        var restore = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pillTL);
        using (var layer = ImRaii.Child("##echoChatPill", pillSize, false,
                   ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar
                   | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (layer)
            {
                var dl = ImGui.GetWindowDrawList();
                ImGui.SetCursorScreenPos(pillTL);
                var clicked = ImGui.InvisibleButton("##echoChatPillBtn", pillSize);
                var hovered = ImGui.IsItemHovered();
                if (hovered)
                {
                    HandOnHover();
                }
                dl.AddRectFilled(pillTL, pillTL + pillSize,
                    ImGui.GetColorU32(hovered ? t.AccentLight : t.Accent), pillSize.Y * 0.5f);
                dl.AddText(pillTL + (pillSize - labelSize) * 0.5f, 0xFFFFFFFFu, label);
                if (clicked)
                {
                    ClearUnseen();
                }
            }
        }
        ImGui.SetCursorScreenPos(restore);
    }

    private float ChatInputHeight()
    {
        var lines = 1;
        foreach (var c in _chatInput)
        {
            if (c == '\n')
            {
                lines++;
            }
        }
        lines = Math.Clamp(lines, 1, ChatInputMaxLines);
        var h = lines * ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2f + Px(10f);
        if (_chatError is not null || _chatInput.Length >= EchoLimits.ChatMaxLength - ChatCounterLead)
        {
            h += ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y;
        }
        return h;
    }

    private void DrawChatInput(float width)
    {
        var buttonSize = ImGui.GetFrameHeight();
        var sendW = Px(52f);
        var gap = Px(4f);
        var inputW = MathF.Max(Px(60f), width - buttonSize - sendW - gap * 2f);

        ImGui.Spacing();
        var grinning = Plugin.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(4f, 4f));
        var emojiClicked = grinning != null
            ? ImGui.ImageButton(grinning.Handle, new Vector2(buttonSize - Px(8f)))
            : ImGui.Button($"{Loc.T("chat.emoji_button")}##echoEmoji", new Vector2(buttonSize, 0f));
        ImGui.PopStyleVar();
        HandOnHover();
        if (emojiClicked)
        {
            _emojiPicker.Open(OnEmojiPicked);
        }

        ImGui.SameLine(0f, gap);
        if (_reclaimChatFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _reclaimChatFocus = false;
        }
        var lines = Math.Clamp(_chatInput.Count(c => c == '\n') + 1, 1, ChatInputMaxLines);
        var enterPressed = ImGui.InputTextMultiline("##echoChatInput", ref _chatInput, EchoLimits.ChatMaxLength,
            new Vector2(inputW, lines * ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2f),
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CtrlEnterForNewLine);

        ImGui.SameLine(0f, gap);
        var sendPressed = Button($"{Loc.T("chat.send")}##echoSend", new Vector2(sendW, 0f));
        if ((enterPressed || sendPressed) && !_chatSending)
        {
            SendChat();
        }

        var remaining = EchoLimits.ChatMaxLength - _chatInput.Length;
        if (remaining <= ChatCounterLead)
        {
            ImGui.TextColored(remaining <= 0 ? UiColors.Danger : UiColors.Hint,
                remaining.ToString(CultureInfo.CurrentCulture));
        }
        else if (_chatError is { } error)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(UiColors.Danger, error);
            ImGui.PopTextWrapPos();
        }
    }

    private void OnEmojiPicked(string name)
    {
        var addition = $":{name}: ";
        if (_chatInput.Length + addition.Length > EchoLimits.ChatMaxLength)
        {
            return;
        }
        _chatInput += addition;
        _reclaimChatFocus = true;
    }

    private void SendChat()
    {
        var text = _chatInput.Replace('\n', ' ').Trim();
        if (text.Length == 0 || !ParsedMessage.Parse(text).HasVisibleContent)
        {
            return;
        }
        // Slash input mimics the game chat box, the way every other chat does: a known emote command
        // runs on the character, anything else is dropped, and nothing reaches the room.
        if (text.StartsWith('/'))
        {
            _caps.System.TryExecuteEmote(text);
            _chatInput = string.Empty;
            _reclaimChatFocus = true;
            return;
        }
        if (_state.CurrentRoomId is not { } roomId)
        {
            return;
        }

        _chatInput = string.Empty;
        _chatError = null;
        _chatSending = true;
        _reclaimChatFocus = true;
        _scrollToBottom = 1f;

        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.SendEchoChatAsync(roomId, text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var friendly = FriendlyHubError(ex);
                _uiActions.Enqueue(() =>
                {
                    _chatError = friendly;
                    if (_chatInput.Length == 0)
                    {
                        _chatInput = text;
                    }
                });
                Plugin.Log.Warning(ex, "[Echo] Sending a chat line failed.");
            }
            finally
            {
                _chatSending = false;
            }
        });
    }
}
