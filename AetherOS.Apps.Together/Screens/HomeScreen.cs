using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Together.Screens;

/// <summary>The party page: start or join while solo, the roster and the actions while in one, the
/// farewell after it ends. The same strings the widget card speaks, on a page with room to breathe.</summary>
internal sealed class HomeScreen(ITogetherHost host, Action openTour, Action openSettings)
{
    private const float PadX = 16f;
    private const float AvatarRadius = 26f;

    private static readonly Vector4 PartyOrange = UiColors.Party;

    private string _joinCode = string.Empty;
    private double _copiedAt = -10.0;
    private Guid? _kickConfirmFor;
    private float _kickPanelH;

    public void Draw(OsAppContext ctx)
    {
        var winW = ImGui.GetWindowSize().X;
        var winH = ImGui.GetWindowSize().Y;
        var winPos = ImGui.GetWindowPos();

        ImGui.SetCursorPos(new Vector2(0f, 0f));
        PushScrollbarStyle();
        using (var body = ImRaii.Child("##togetherHome", new Vector2(0f, winH), false))
        {
            if (body.Success)
            {
                DrawHeader(ctx, winW);
                if (!host.Available && !host.InParty && !host.PartyEnded)
                {
                    DrawOffline(winW);
                }
                else if (host.PartyEnded)
                {
                    DrawEnded(winW);
                }
                else if (host.InParty)
                {
                    DrawActivity(winW);
                    DrawRoster(ctx, winW);
                    DrawPartyActions(ctx, winW);
                }
                else
                {
                    DrawSolo(winW);
                }
                DrawError(winW);
                ImGui.Dummy(new Vector2(0f, Px(24f)));
            }
        }
        PopScrollbarStyle();

        DrawKickConfirm(winPos, new Vector2(winW, winH));
    }

    private void DrawHeader(OsAppContext ctx, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        using (UiFonts.H1?.Push())
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(UiColors.Body, Loc.T("os.app_together"));
        }
        var lineTop = ImGui.GetItemRectMin().Y;
        var lineH = ImGui.GetItemRectSize().Y;

        // The cog, right-aligned on the title line: the pet switches, without the whole tour.
        var side = Px(28f);
        var cogTl = new Vector2(ImGui.GetWindowPos().X + winW - Px(PadX) - side, lineTop + ((lineH - side) * 0.5f));
        ImGui.SetCursorScreenPos(cogTl);
        if (ImGui.InvisibleButton("##togetherSettings", new Vector2(side, side)))
        {
            openSettings();
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T("os.party_settings"));
        }
        dl.AddCircleFilled(cogTl + new Vector2(side * 0.5f), side * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.07f)), 24);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Cog, Px(13f), cogTl + new Vector2(side * 0.5f),
            ImGui.GetColorU32(hovered ? UiColors.Body : UiColors.Muted));

        ImGui.SetCursorPos(new Vector2(Px(PadX), ImGui.GetCursorPosY() + Px(2f)));
        var sub = host.InParty
            ? string.Format(Loc.T("os.party_members"), host.Members.Count, host.MaxMembers)
            : Loc.T("os.together_tagline");
        ImGui.TextColored(UiColors.Hint, sub);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        _ = ctx;
    }

    private void DrawOffline(float winW)
    {
        DrawParagraph(Loc.T("os.together_offline"), winW, UiColors.Hint);
    }

    private void DrawSolo(float winW)
    {
        var innerW = winW - Px(PadX) * 2f;
        DrawParagraph(Loc.T("os.together_solo_body"), winW, UiColors.Body);
        ImGui.Dummy(new Vector2(0f, Px(12f)));

        ImGui.SetCursorPosX(Px(PadX));
        if (ModalUi.Button($"{Loc.T("os.party_start")}##togetherStart", innerW) && !host.Busy)
        {
            host.Create();
        }

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.together_join_title"));
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        var joinW = Px(90f);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(innerW - joinW - Px(8f));
        ImGui.InputTextWithHint("##togetherJoinCode", Loc.T("os.party_join_hint"), ref _joinCode, 12,
            ImGuiInputTextFlags.CharsUppercase | ImGuiInputTextFlags.CharsNoBlank);
        ImGui.SameLine(0f, Px(8f));
        var code = _joinCode.Trim();
        var canJoin = code.Length >= 4 && !host.Busy;
        using (ImRaii.Disabled(!canJoin))
        {
            if (ModalUi.Button($"{Loc.T("os.party_join")}##togetherJoin", joinW) && canJoin)
            {
                host.Join(code);
                _joinCode = string.Empty;
            }
        }

        ImGui.Dummy(new Vector2(0f, Px(20f)));
        DrawLinkRow(FontAwesomeIcon.QuestionCircle, Loc.T("os.together_how"), winW, openTour);
    }

    private void DrawEnded(float winW)
    {
        DrawParagraph(Loc.T("os.party_ended"), winW, UiColors.Body);
        ImGui.Dummy(new Vector2(0f, Px(12f)));
        ImGui.SetCursorPosX(Px(PadX));
        if (ModalUi.Button($"{Loc.T("os.party_dismiss")}##togetherDismiss", winW - Px(PadX) * 2f))
        {
            host.DismissEnded();
        }
    }

    private void DrawActivity(float winW)
    {
        if (host.Activity is not { } activity)
        {
            return;
        }
        var (icon, key) = activity.AppId switch
        {
            "echo" => (FontAwesomeIcon.Film, "os.party_activity_echo"),
            "wayfinder" => (FontAwesomeIcon.Compass, "os.party_activity_wayfinder"),
            _ => (FontAwesomeIcon.Bolt, "os.party_activity_generic"),
        };
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(46f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##togetherActivity", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        if (ImGui.IsItemDeactivated() && hovered)
        {
            host.OpenActivity(activity);
        }
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(PartyOrange with { W = hovered ? 0.26f : 0.16f }), Px(12f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(PartyOrange with { W = 0.6f }), Px(12f));
        IconDraw.AddCentered(dl, icon, Px(14f), tl + new Vector2(Px(20f), cardH * 0.5f), ImGui.GetColorU32(PartyOrange));
        var lineH = ImGui.GetTextLineHeight();
        dl.AddText(tl + new Vector2(Px(38f), (cardH - lineH) * 0.5f), ImGui.GetColorU32(UiColors.Body), Loc.T(key));
        var open = Loc.T("os.party_activity_open");
        var openW = ImGui.CalcTextSize(open).X;
        dl.AddText(tl + new Vector2(cardW - Px(14f) - openW, (cardH - lineH) * 0.5f),
            ImGui.GetColorU32(PartyOrange), open);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private void DrawRoster(OsAppContext ctx, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var members = host.Members;
        var radius = Px(AvatarRadius);
        var ringR = radius * AvatarRings.Overhang;
        var cellW = (ringR * 2f) + Px(14f);
        var labelH = ImGui.GetTextLineHeight();
        var cellH = (ringR * 2f) + Px(6f) + labelH + Px(12f);
        var innerW = winW - Px(PadX) * 2f;
        var perRow = Math.Max(1, (int)(innerW / cellW));
        var rows = (members.Count + perRow - 1) / perRow;
        var origin = ImGui.GetCursorScreenPos() + new Vector2(Px(PadX), 0f);

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var col = i % perRow;
            var row = i / perRow;
            var cellTl = origin + new Vector2(col * cellW, row * cellH);
            var centre = cellTl + new Vector2(cellW * 0.5f, ringR + Px(2f));

            var tex = InlineAvatarCache.Resolve("PartyAvatarCache", member.AccountId, member.AvatarImage)?.GetWrapOrDefault();
            var alpha = member.Connected ? 1f : 0.45f;
            if (tex is not null)
            {
                dl.AddImageRounded(tex.Handle, centre - new Vector2(radius), centre + new Vector2(radius),
                    Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)), radius,
                    ImDrawFlags.RoundCornersAll);
            }
            else
            {
                dl.AddCircleFilled(centre, radius, ImGui.GetColorU32(new Vector4(0.33f, 0.33f, 0.33f, alpha)), 32);
                var initial = member.Name.Length > 0 ? member.Name[..1].ToUpperInvariant() : "?";
                var sz = ImGui.CalcTextSize(initial);
                dl.AddText(centre - (sz * 0.5f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f * alpha)), initial);
            }
            dl.AddCircle(centre, radius, ImGui.GetColorU32(PartyOrange with { W = 0.55f * alpha }), 32, Px(1.2f));
            AvatarRings.Draw(dl, centre, radius, member.FrameRef);

            if (member.IsHost)
            {
                var crown = centre + new Vector2(radius * 0.7f, -radius * 0.7f);
                dl.AddCircleFilled(crown, Px(9f), ImGui.GetColorU32(new Vector4(0.08f, 0.07f, 0.12f, 0.95f)), 16);
                IconDraw.AddCentered(dl, FontAwesomeIcon.Crown, Px(9f), crown,
                    ImGui.GetColorU32(new Vector4(0.98f, 0.82f, 0.36f, 1f)));
            }

            var label = member.AccountId == host.OwnAccountId
                ? string.Format(Loc.T("os.together_you"), member.Name)
                : member.Name;
            label = TruncateToWidth(label, cellW - Px(6f));
            var labelW = ImGui.CalcTextSize(label).X;
            dl.AddText(new Vector2(centre.X - (labelW * 0.5f), centre.Y + ringR + Px(6f)),
                ImGui.GetColorU32(UiColors.Body with { W = alpha }), label);

            if (host.AmHost && !member.IsHost)
            {
                var kickC = centre + new Vector2(-radius * 0.75f, -radius * 0.75f);
                ImGui.SetCursorScreenPos(kickC - new Vector2(Px(10f)));
                if (ImGui.InvisibleButton($"##togetherKick{member.AccountId:N}", new Vector2(Px(20f))))
                {
                    _kickConfirmFor = member.AccountId;
                }
                var hovered = ImGui.IsItemHovered();
                if (hovered)
                {
                    HandOnHover();
                    ImGui.SetTooltip(string.Format(Loc.T("os.party_kick_tip"), member.Name));
                }
                dl.AddCircleFilled(kickC, Px(9f), ImGui.GetColorU32(new Vector4(0.08f, 0.07f, 0.12f, 0.95f)), 16);
                IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(9f), kickC,
                    ImGui.GetColorU32(hovered ? UiColors.Danger : UiColors.Muted));
            }
        }

        ImGui.SetCursorScreenPos(origin - new Vector2(Px(PadX), 0f));
        ImGui.Dummy(new Vector2(winW, Math.Max(1, rows) * cellH + Px(6f)));
        _ = ctx;
    }

    private void DrawPartyActions(OsAppContext ctx, float winW)
    {
        var innerW = winW - Px(PadX) * 2f;
        var half = (innerW - Px(8f)) * 0.5f;

        if (host.Code is { Length: > 0 } code)
        {
            var recently = ImGui.GetTime() - _copiedAt < 1.5;
            var label = recently ? Loc.T("os.party_copied") : string.Format(Loc.T("os.party_code"), code);
            ImGui.SetCursorPosX(Px(PadX));
            if (ModalUi.Button($"{label}##togetherCode", half))
            {
                ctx.Capabilities.System.CopyToClipboard(code);
                _copiedAt = ImGui.GetTime();
            }
            ImGui.SameLine(0f, Px(8f));
            if (ModalUi.Button($"{Loc.T("os.party_invite")}##togetherInvite", half))
            {
                host.Invite();
            }
            ImGui.Dummy(new Vector2(0f, Px(8f)));
        }

        ImGui.SetCursorPosX(Px(PadX));
        var leave = host.AmHost ? Loc.T("os.party_end") : Loc.T("os.party_leave");
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.55f, 0.16f, 0.18f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.68f, 0.2f, 0.22f, 1f)))
        {
            if (ModalUi.Button($"{leave}##togetherLeave", innerW) && !host.Busy)
            {
                if (host.AmHost)
                {
                    host.End();
                }
                else
                {
                    host.Leave();
                }
            }
        }

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        DrawLinkRow(FontAwesomeIcon.QuestionCircle, Loc.T("os.together_how"), winW, openTour);
    }

    private void DrawError(float winW)
    {
        if (host.ErrorKey is not { } key)
        {
            return;
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawParagraph(Loc.T(key), winW, UiColors.Danger);
    }

    private void DrawKickConfirm(Vector2 winPos, Vector2 winSize)
    {
        if (_kickConfirmFor is not { } target)
        {
            return;
        }
        string name = string.Empty;
        foreach (var member in host.Members)
        {
            if (member.AccountId == target)
            {
                name = member.Name;
                break;
            }
        }
        var go = false;
        var cancel = false;
        var dismissed = DrawPageOverlayPanel("togetherKick", winPos, winSize, ref _kickPanelH, Px(200f), innerW =>
        {
            ModalUi.Header(innerW, FontAwesomeIcon.UserMinus, Loc.T("os.party_kick_title"), UiColors.Danger);
            ImGui.Spacing();
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, string.Format(Loc.T("os.party_kick_body"), name));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();
            var btnW = (innerW - Px(10f)) * 0.5f;
            if (ModalUi.Button($"{Loc.T("os.party_kick_go")}##togetherKickGo", btnW))
            {
                go = true;
            }
            ImGui.SameLine(0f, Px(10f));
            if (ModalUi.Button($"{Loc.T("common.cancel")}##togetherKickNo", btnW))
            {
                cancel = true;
            }
        });
        if (go)
        {
            host.Kick(target);
        }
        if (go || cancel || dismissed)
        {
            _kickConfirmFor = null;
        }
    }

    private static void DrawLinkRow(FontAwesomeIcon icon, string label, float winW, Action action)
    {
        var dl = ImGui.GetWindowDrawList();
        var h = Px(34f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var w = winW - Px(PadX) * 2f;
        if (ImGui.InvisibleButton($"##togetherLink{label}", new Vector2(w, h)))
        {
            action();
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        var accent = ThemeService.Current.AccentLight;
        IconDraw.AddCentered(dl, icon, Px(13f), tl + new Vector2(Px(12f), h * 0.5f), ImGui.GetColorU32(accent));
        dl.AddText(tl + new Vector2(Px(30f), (h - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(accent with { W = hovered ? 1f : 0.85f }), label);
    }

    private static void DrawParagraph(string text, float winW, Vector4 colour)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(colour, text);
        ImGui.PopTextWrapPos();
    }
}
