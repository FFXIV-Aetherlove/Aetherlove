using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove.Emoji;
using AetherLove.Os;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using static AetherLove.UI.SharedUiHelpers;
using static AetherLove.UI.UiScale;

namespace AetherLove.Windows;

/// <summary>The together-mode chat beside the phone: a chip that opens into the party chat, with new lines
/// surfacing as bubbles while it is closed. The ROSTER is not here; it lives on the party widget, where
/// there is room to show people at a size worth looking at.
/// <para>Everything in it is the house kit rather than an invention of its own: avatars are the
/// disc-plus-<see cref="AvatarRings"/> pattern every roster uses, lines render through
/// <see cref="ParsedMessage"/>, the input carries the shared <see cref="EmojiPickerPopup"/>, and a
/// right-click on a line gets the same copy/translate menu a messenger message gets, backed by
/// <see cref="TranslateUi"/>.</para></summary>
public sealed class PartyDockWindow : Window
{
    private const int MaxBubbles = 3;
    private const double BubbleSeconds = 8.0;

    private static readonly Vector4 PartyGreen = UiColors.Party;

    private readonly IOsTogether _together;
    private readonly AetherOS.Sdk.IAppCapabilities _caps;
    private readonly Action _openPhone;
    private readonly TranslateUi _translate;
    private readonly EmojiPickerPopup _emojiPicker = new();
    private readonly List<Bubble> _bubbles = new();
    private readonly Action<OsPartyChatLine> _openNotice;

    private Vector2 _anchorPos;
    private Vector2 _anchorSize;
    private int _anchorFrame;
    private Vector2 _lastSize;
    private bool _chatOpen;
    private long _lastSeenSeq;
    private string _draft = string.Empty;
    private long _scrolledToSeq;
    private bool _focusInput;
    private bool _caretToEnd;
    private long _menuSeq = -1;

    private sealed record Bubble(
        long Seq, Guid AccountId, string Name, bool IsOwn, string Text, double ShownAt, bool IsSystem = false,
        OsPartyChatLine? Notice = null);

    public PartyDockWindow(IOsTogether together, AetherOS.Sdk.IAppCapabilities caps,
        Action openPhone, Action openTranslationSettings, Action<OsPartyChatLine> openNotice)
        : base("##aetherPartyDock")
    {
        _together = together;
        _caps = caps;
        _openPhone = openPhone;
        _openNotice = openNotice;
        _translate = new TranslateUi("partychat", caps.Translation, openTranslationSettings);
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    /// <summary>The phone window's live rect, handed over every frame it draws, so the dock can stand
    /// beside it (the skin-preview pattern). The frame stamp is what makes it a DOCK: while the phone is
    /// on screen the roster glues to its edge and follows a drag, and only with the phone away does the
    /// window hold its own spot.</summary>
    public void SetAnchor(Vector2 pos, Vector2 size)
    {
        _anchorPos = pos;
        _anchorSize = size;
        _anchorFrame = ImGui.GetFrameCount();
    }

    /// <summary>Only while the phone itself is on screen: the dock is the phone's edge, not a window of
    /// its own, so closing the phone closes it.</summary>
    public override bool DrawConditions() => _together.InParty && PhoneLive;

    private bool PhoneLive => _anchorSize.X > 1f && ImGui.GetFrameCount() - _anchorFrame <= 2;

    public override void PreDraw()
    {
        // Glued to the phone's right edge every frame the phone is drawn, flipping to its left when the
        // screen runs out; the moment the phone goes away the window is released and simply stays where
        // the phone left it. Without the glue, "show beside the phone" was a spawn default and nothing
        // else, which read as a toggle that merely hides the roster.
        if (PhoneLive)
        {
            var x = _anchorPos.X + _anchorSize.X + Px(10f);
            if (_lastSize.X > 1f && x + _lastSize.X > ImGui.GetIO().DisplaySize.X - Px(4f))
            {
                x = _anchorPos.X - _lastSize.X - Px(10f);
            }
            Position = new Vector2(x, _anchorPos.Y + Px(24f));
            PositionCondition = ImGuiCond.Always;
        }
        else
        {
            Position = null;
        }
    }

    public override void Draw()
    {
        // The phone's own body font and scale, not raw Dalamud units: the dock stands beside the phone
        // and is read at the same distance, and at stock font size it was an unreadable miniature.
        // Pinning matters as much as the font: Dalamud's global scale would multiply the phone's own
        // sizing a second time, so the chat grew with a setting the phone deliberately answers itself.
        var savedFontScale = FontScalePin.Pin();
        using var font = UiFonts.Body?.Push();

        var lines = _together.ChatLines;
        if (_chatOpen)
        {
            _bubbles.Clear();
            if (lines.Count > 0)
            {
                _lastSeenSeq = lines[^1].Seq;
            }
            _together.MarkChatRead();
            DrawChatStrip(lines);
        }
        else
        {
            DrawChatChip();
            CollectBubbles(lines);
            DrawBubbles();
        }

        _emojiPicker.Draw();
        _translate.DrawConsentOverlay(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        _lastSize = ImGui.GetWindowSize();
        FontScalePin.Restore(savedFontScale);
    }

    // ------------------------------------------------------------------ people

    /// <summary>The member's avatar: the OS avatar on a disc with their equipped ring over it, the shape
    /// every other people-surface draws. Falls back to the shared fallback disc while there is no art.</summary>
    private void DrawMemberAvatar(ImDrawListPtr dl, Vector2 centre, float radius, OsPartyMember member, float alpha)
    {
        var tex = PartyAvatars.Resolve(member)?.GetWrapOrDefault();
        var tint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha));
        if (tex is not null)
        {
            dl.AddImageRounded(tex.Handle, centre - new Vector2(radius), centre + new Vector2(radius),
                Vector2.Zero, Vector2.One, tint, radius, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(centre, radius, ImGui.ColorConvertFloat4ToU32(
                new Vector4(0.33f, 0.33f, 0.33f, alpha)));
            var initial = member.Name.Length > 0 ? member.Name[..1].ToUpperInvariant() : "?";
            var sz = ImGui.CalcTextSize(initial) * 0.85f;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f, centre - (sz * 0.5f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f * alpha)), initial);
        }
        dl.AddCircle(centre, radius,
            ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = 0.55f * alpha }), 0, Px(1.2f));
        if (alpha >= 0.99f)
        {
            AvatarRings.Draw(dl, centre, radius, member.FrameRef);
        }
    }

    // ------------------------------------------------------------------ chat

    /// <summary>The collapsed chat entry: a round chip with the unread count riding it.</summary>
    private void DrawChatChip()
    {
        var dl = ImGui.GetWindowDrawList();
        var size = Px(54f);
        var tl = ImGui.GetCursorScreenPos();
        var c = tl + new Vector2(size * 0.5f, size * 0.5f);

        var clicked = ImGui.InvisibleButton("##partyChatChip", new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T("os.party_chat_title"));
        }
        dl.AddCircleFilled(c, size * 0.5f,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.1f, hovered ? 0.95f : 0.85f)));
        dl.AddCircle(c, size * 0.5f, ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = hovered ? 0.8f : 0.5f }));
        IconDraw.AddCentered(dl, FontAwesomeIcon.CommentDots, Px(22f), c,
            ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = 0.9f }));
        if (_together.UnreadChat > 0)
        {
            DrawBadge(dl, c + new Vector2(size * 0.32f, -size * 0.32f), _together.UnreadChat);
        }
        if (clicked)
        {
            _chatOpen = true;
            _focusInput = true;
        }
    }

    /// <summary>New lines the strip has not shown yet become bubbles; each lives ~8 seconds. Under
    /// ReduceMotion they hold full alpha for their lifetime and simply disappear.</summary>
    private void CollectBubbles(IReadOnlyList<OsPartyChatLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.Seq <= _lastSeenSeq)
            {
                continue;
            }
            _lastSeenSeq = line.Seq;
            _bubbles.Add(new Bubble(line.Seq, line.AccountId, line.Name, line.IsOwn, line.Text, ImGui.GetTime(),
                line.IsSystem, line.IsSystem ? line : null));
        }
        _bubbles.RemoveAll(b => ImGui.GetTime() - b.ShownAt > BubbleSeconds);
        while (_bubbles.Count > MaxBubbles)
        {
            _bubbles.RemoveAt(0);
        }
    }

    private void DrawBubbles()
    {
        if (_bubbles.Count == 0)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var wrapW = Px(230f);
        var pad = Px(8f);
        var avatarR = Px(9f);
        var nameH = ImGui.GetFontSize() * 0.72f;
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        foreach (var bubble in _bubbles)
        {
            var age = ImGui.GetTime() - bubble.ShownAt;
            var alpha = AccessibilityService.ReduceMotion
                ? 1f
                : (float)Math.Clamp((BubbleSeconds - age) / 1.2, 0.0, 1.0);
            var message = ParsedMessage.Parse(bubble.Notice is { } notice ? NoticeText(notice) : bubble.Text);
            var textH = message.MeasureHeight(wrapW);
            var height = (pad * 2f) + MathF.Max(nameH, avatarR * 2f) + Px(3f) + textH;
            var tl = ImGui.GetCursorScreenPos();
            dl.AddRectFilled(tl, tl + new Vector2(wrapW + (pad * 2f), height),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.1f, 0.88f * alpha)), Px(10f));
            dl.AddRect(tl, tl + new Vector2(wrapW + (pad * 2f), height),
                ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = 0.4f * alpha }), Px(10f));

            var member = bubble.IsSystem ? null : FindMember(bubble.AccountId);
            if (member is not null)
            {
                DrawMemberAvatar(dl, tl + new Vector2(pad + avatarR, pad + avatarR), avatarR, member, alpha);
            }
            if (!bubble.IsSystem)
            {
                var nameColor = bubble.IsOwn ? ThemeService.Current.AccentLight : PartyGreen;
                dl.AddText(ImGui.GetFont(), nameH,
                    tl + new Vector2(pad + (member is not null ? (avatarR * 2f) + Px(6f) : 0f), pad + avatarR - (nameH * 0.5f)),
                    ImGui.ColorConvertFloat4ToU32(nameColor with { W = 0.95f * alpha }), bubble.Name);
            }

            ImGui.SetCursorScreenPos(tl + new Vector2(pad, pad + MathF.Max(nameH, avatarR * 2f) + Px(3f)));
            using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, alpha))
            {
                var tappable = bubble.Notice is { Kind: not null } target ? target : null;
                message.DrawWrapped($"##partyBubble{bubble.Seq}", wrapW,
                    tappable is null ? null : () => _openNotice(tappable));
            }
            ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + height));
            ImGui.Dummy(new Vector2(wrapW + (pad * 2f), Px(4f)));
        }
    }

    private OsPartyMember? FindMember(Guid accountId)
    {
        foreach (var member in _together.Members)
        {
            if (member.AccountId == accountId)
            {
                return member;
            }
        }
        return null;
    }

    private void DrawChatStrip(IReadOnlyList<OsPartyChatLine> lines)
    {
        var dl = ImGui.GetWindowDrawList();
        var stripW = Px(300f);
        var linesH = Px(250f);
        var headerH = Px(28f);
        var inputH = ImGui.GetFrameHeight() + Px(10f);
        var tl = ImGui.GetCursorScreenPos();
        var size = new Vector2(stripW, headerH + linesH + inputH + Px(10f));

        dl.AddRectFilled(tl, tl + size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.1f, 0.94f)), Px(12f));
        dl.AddRect(tl, tl + size, ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = 0.5f }), Px(12f));

        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            tl + new Vector2(Px(10f), ((headerH - (ImGui.GetFontSize() * 0.85f)) * 0.5f) + Px(2f)),
            ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = 0.95f }), Loc.T("os.party_chat_title"));

        DrawHeaderButton(dl, tl + new Vector2(stripW - Px(44f), (headerH * 0.5f) + Px(2f)),
            FontAwesomeIcon.MobileAlt, "##partyChatPhone", Loc.T("os.party_title"), _openPhone);
        DrawHeaderButton(dl, tl + new Vector2(stripW - Px(18f), (headerH * 0.5f) + Px(2f)),
            FontAwesomeIcon.Times, "##partyChatClose", null, () => _chatOpen = false);

        ImGui.SetCursorScreenPos(tl + new Vector2(Px(8f), headerH));
        using (var child = ImRaii.Child("##partyChatLines", new Vector2(stripW - Px(16f), linesH), false,
                   ImGuiWindowFlags.NoBackground))
        {
            if (child)
            {
                if (lines.Count == 0)
                {
                    ImGui.Dummy(new Vector2(0f, Px(6f)));
                    ImGui.PushTextWrapPos(stripW - Px(20f));
                    ImGui.TextColored(UiColors.Hint, Loc.T("os.party_chat_empty"));
                    ImGui.PopTextWrapPos();
                }
                foreach (var line in lines)
                {
                    DrawChatLine(line, stripW);
                }
                if (lines.Count > 0 && _scrolledToSeq != lines[^1].Seq)
                {
                    _scrolledToSeq = lines[^1].Seq;
                    ImGui.SetScrollHereY(1f);
                }
            }
        }

        DrawInputRow(dl, tl, stripW, headerH + linesH + Px(4f));
        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + size.Y));
        ImGui.Dummy(new Vector2(size.X, 0f));
    }

    /// <summary>One line: the sender's avatar (ring and all), their name in the house chat colours, and the
    /// body through <see cref="ParsedMessage"/> so emoji render. Text goes through the translation state,
    /// and a right-click gets the same copy/translate menu a messenger message gets.</summary>
    private void DrawChatLine(OsPartyChatLine line, float stripW)
    {
        if (line.IsSystem)
        {
            DrawSystemLine(line, stripW);
            return;
        }
        var childDl = ImGui.GetWindowDrawList();
        var avatarR = Px(12f);
        // The ring reaches past the disc and the lines child has no padding, so the disc stands in by the reach.
        var ringR = avatarR * AvatarRings.Overhang;
        var indent = (ringR * 2f) + Px(8f);
        var wrapW = stripW - Px(24f) - indent;
        var nameSize = ImGui.GetFontSize() * 0.78f;
        var top = ImGui.GetCursorScreenPos();

        var member = FindMember(line.AccountId);
        if (member is not null)
        {
            DrawMemberAvatar(childDl, top + new Vector2(ringR, ringR + Px(1f)), avatarR, member, 1f);
        }

        var nameColor = line.IsOwn ? ThemeService.Current.AccentLight : PartyGreen with { W = 0.9f };
        childDl.AddText(ImGui.GetFont(), nameSize, top + new Vector2(indent, 0f),
            ImGui.ColorConvertFloat4ToU32(nameColor), line.Name);

        var shown = _translate.Display(LineKey(line.Seq), line.Text);
        ImGui.SetCursorScreenPos(top + new Vector2(indent, nameSize + Px(1f)));
        var message = ParsedMessage.Parse(shown);
        message.DrawWrapped($"##partyChatLine{line.Seq}", wrapW);
        var bottom = ImGui.GetCursorScreenPos().Y;
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        // The line is draw-list output rather than one item, so the menu is opened off a hand hit-test of
        // its rect, like the overlay panels do.
        if (ImGui.IsMouseHoveringRect(top, new Vector2(top.X + stripW - Px(20f), bottom))
            && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _menuSeq = line.Seq;
            ImGui.OpenPopup("##partyLineMenu");
        }
        if (_menuSeq == line.Seq && ImGui.BeginPopup("##partyLineMenu"))
        {
            if (DrawIconMenuItem(FontAwesomeIcon.Copy, Loc.T("chat.menu_copy_message")))
            {
                ImGui.CloseCurrentPopup();
                _caps.System.CopyToClipboard(ParsedMessage.Parse(line.Text).PlainText);
            }
            _translate.DrawMenuItems(LineKey(line.Seq), line.Text);
            ImGui.EndPopup();
        }
    }

    /// <summary>A server-authored notice, phrased here in the reader's language: a small centred muted
    /// line, no avatar, no name row, no context menu. An activity notice (the host opened a hunt or a
    /// room) is a tap target that follows them there, and wears the accent to say so.</summary>
    private void DrawSystemLine(OsPartyChatLine line, float stripW)
    {
        var dl = ImGui.GetWindowDrawList();
        var text = NoticeText(line);
        var tappable = line.Kind is not null;
        var size = ImGui.GetFontSize() * 0.78f;
        var textW = ImGui.CalcTextSize(text).X * 0.78f;
        var top = ImGui.GetCursorScreenPos();
        var at = new Vector2(top.X + ((stripW - Px(24f) - textW) * 0.5f), top.Y);
        var hovered = false;
        if (tappable)
        {
            ImGui.SetCursorScreenPos(at);
            if (ImGui.InvisibleButton($"##partyNotice{line.Seq}", new Vector2(textW, size)))
            {
                _openNotice(line);
            }
            hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
            }
            ImGui.SetCursorScreenPos(top);
        }
        var colour = tappable
            ? ThemeService.Current.AccentLight with { W = hovered ? 1f : 0.85f }
            : PartyGreen with { W = 0.65f };
        dl.AddText(ImGui.GetFont(), size, at, ImGui.ColorConvertFloat4ToU32(colour), text);
        if (tappable)
        {
            dl.AddLine(new Vector2(at.X, at.Y + size + Px(1f)), new Vector2(at.X + textW, at.Y + size + Px(1f)),
                ImGui.ColorConvertFloat4ToU32(colour with { W = colour.W * 0.6f }), 1f);
        }
        ImGui.Dummy(new Vector2(0f, size + Px(8f)));
    }

    /// <summary>The sentence for a notice, by kind: the join, a hunt, a room. Text is the host's name.</summary>
    private static string NoticeText(OsPartyChatLine line) => line.Kind switch
    {
        "wayfinder" => string.Format(Loc.T("os.party_chat_wayfinder"), line.Text),
        "echo" => string.Format(Loc.T("os.party_chat_echo"), line.Text),
        _ => string.Format(Loc.T("os.party_chat_joined"), line.Text),
    };

    private static string LineKey(long seq) => $"party{seq}";

    /// <summary>The input row: a solid rounded field, the shared emoji picker on its right, then send.</summary>
    private void DrawInputRow(ImDrawListPtr dl, Vector2 stripTL, float stripW, float y)
    {
        ImGui.SetCursorScreenPos(stripTL + new Vector2(Px(8f), y));
        ImGui.SetNextItemWidth(stripW - Px(64f));
        if (_focusInput)
        {
            _focusInput = false;
            ImGui.SetKeyboardFocusHere();
        }
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.07f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 1f, 1f, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(1f, 1f, 1f, 0.12f));
        var submitted = ImGui.InputTextWithHint("##partyChatDraft", Loc.T("os.party_chat_hint"), ref _draft,
            Shared.Together.TogetherLimits.ChatMaxLength,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackAlways, CaretCallback);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();

        var rowCentreY = y + (ImGui.GetFrameHeight() * 0.5f);
        var emojiC = stripTL + new Vector2(stripW - Px(42f), rowCentreY);
        ImGui.SetCursorScreenPos(emojiC - new Vector2(Px(10f), Px(10f)));
        if (ImGui.InvisibleButton("##partyChatEmoji", new Vector2(Px(20f), Px(20f))))
        {
            _emojiPicker.Open(name =>
            {
                _draft += $":{name}: ";
                // Back into the field with the caret AFTER the emote: SetKeyboardFocusHere alone selects
                // the whole draft, and the next keystroke would eat it.
                _focusInput = true;
                _caretToEnd = true;
            });
        }
        var emojiHovered = ImGui.IsItemHovered();
        if (emojiHovered)
        {
            HandOnHover();
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.SmileBeam, Px(13f), emojiC,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, emojiHovered ? 0.95f : 0.6f)));

        var sendC = stripTL + new Vector2(stripW - Px(19f), rowCentreY);
        ImGui.SetCursorScreenPos(sendC - new Vector2(Px(10f), Px(10f)));
        var sendClicked = ImGui.InvisibleButton("##partyChatSend", new Vector2(Px(20f), Px(20f)));
        var sendHovered = ImGui.IsItemHovered();
        if (sendHovered)
        {
            HandOnHover();
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.PaperPlane, Px(12f), sendC,
            ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = sendHovered ? 1f : 0.7f }));

        if ((submitted || sendClicked) && _draft.Trim().Length > 0)
        {
            // Slash input mimics the game chat box, the way the other chats do: a known emote command
            // runs on the character, anything else is dropped, and nothing reaches the party.
            var text = _draft.Trim();
            if (text.StartsWith('/'))
            {
                _caps.System.TryExecuteEmote(text);
            }
            else
            {
                _together.SendChat(_draft);
            }
            _draft = string.Empty;
            _focusInput = true;
        }
    }

    private unsafe int CaretCallback(ImGuiInputTextCallbackDataPtr data)
    {
        try
        {
            ImGuiInputTextCallbackData* p = data;
            if (_caretToEnd && p->EventFlag == ImGuiInputTextFlags.CallbackAlways)
            {
                _caretToEnd = false;
                p->CursorPos = p->BufTextLen;
                p->SelectionStart = p->BufTextLen;
                p->SelectionEnd = p->BufTextLen;
            }
        }
        catch
        {
            // A managed exception must not cross into the native ImGui call.
        }
        return 0;
    }

    private static void DrawHeaderButton(ImDrawListPtr dl, Vector2 centre, FontAwesomeIcon icon, string id,
        string? tooltip, Action onClick)
    {
        ImGui.SetCursorScreenPos(centre - new Vector2(Px(9f), Px(9f)));
        if (ImGui.InvisibleButton(id, new Vector2(Px(18f), Px(18f))))
        {
            onClick();
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            if (tooltip is not null)
            {
                ImGui.SetTooltip(tooltip);
            }
        }
        IconDraw.AddCentered(dl, icon, Px(icon == FontAwesomeIcon.Times ? 11f : 12f), centre,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, hovered ? 0.95f : 0.55f)));
    }

    private static void DrawBadge(ImDrawListPtr dl, Vector2 center, int count)
    {
        var label = count > 9 ? "9+" : count.ToString();
        var textSize = ImGui.GetFontSize() * 0.6f;
        var sz = ImGui.CalcTextSize(label) * 0.6f;
        var radius = MathF.Max(Px(7f), (sz.X * 0.5f) + Px(3f));
        dl.AddCircleFilled(center, radius, ImGui.ColorConvertFloat4ToU32(new Vector4(0.88f, 0.28f, 0.28f, 0.97f)));
        dl.AddText(ImGui.GetFont(), textSize, center - (sz * 0.5f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.97f)), label);
    }

}
