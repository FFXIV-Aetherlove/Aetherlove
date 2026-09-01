using System;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Os;

/// <summary>The party card on the home screen's widget page: create/join when solo, the live roster with
/// host controls when in one. This is the party's management surface; the status-bar indicator is only its
/// doorbell and the edge dock its minimized mirror. It lived on the notification shade until nobody found
/// it there.</summary>
public sealed class TogetherPartyCard(IOsTogether together, ShareService share, Action<OsPartyActivity> openActivity)
{
    private static readonly Vector4 PartyGreen = AetherLove.UI.UiColors.Party;

    /// <summary>Publishing a party as a hangout is the host's alone (the server gates it that way).</summary>
    private static readonly string[] HangoutsOnly = ["hangouts"];

    private string _joinCode = string.Empty;
    private double _copiedUntil;
    private OsPartyMember? _kickTarget;
    private float _kickPanelH;

    public bool InputLocked { get; set; }

    /// <summary>Given the first create or join, hands it to whoever wants to explain the feature first
    /// rather than running it. Null runs everything immediately.</summary>
    public Action<Action>? Intercept { get; set; }

    /// <summary>Opens the party settings page. Null hides the cog, for a host with nowhere to send it.</summary>
    public Action? OpenSettings { get; set; }

    /// <summary>Runs a party action, or hands it to the explainer the one time that is still owed.</summary>
    private void Start(Action action)
    {
        if (!together.OnboardingSeen && Intercept is { } intercept)
        {
            intercept(action);
            return;
        }
        action();
    }

    /// <summary>Draws the card and returns the new y. Hidden entirely while together mode is unavailable
    /// and there is nothing to show, so the shade stays clean for offline users.</summary>
    public float Draw(ImDrawListPtr dl, float x, float y, float w)
    {
        if (!together.Available && !together.InParty && !together.PartyEnded)
        {
            return y;
        }

        var pad = Px(12f);
        var top = y;
        var innerX = x + pad;
        var innerW = w - pad * 2f;
        var cy = y + pad;

        cy = DrawHeader(dl, innerX, cy, innerW);
        if (together.PartyEnded)
        {
            cy = DrawEnded(dl, innerX, cy, innerW);
        }
        else if (together.InParty)
        {
            if (together.Activity is { } activity)
            {
                cy = DrawActivity(dl, innerX, cy, innerW, activity);
            }
            cy = DrawRoster(dl, innerX, cy, innerW);
            cy = DrawPartyActions(dl, innerX, cy, innerW);
        }
        else
        {
            cy = DrawJoinRow(dl, innerX, cy, innerW);
        }
        if (together.ErrorKey is { } errorKey)
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.78f, new Vector2(innerX, cy),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.45f, 0.4f, 0.95f)), Loc.T(errorKey));
            cy += ImGui.GetFontSize() * 0.78f + Px(6f);
        }

        var bottom = cy + pad - Px(4f);
        // The frame is drawn after measuring but with an earlier draw-list channel not available here, so a
        // simple rect behind would cover the text; the border alone reads as the card at shade contrast.
        dl.AddRect(new Vector2(x, top), new Vector2(x + w, bottom), OsDraw.White(0.14f), Px(12f));
        return bottom + Px(12f);
    }

    private float DrawHeader(ImDrawListPtr dl, float x, float y, float w)
    {
        var fsz = ImGui.GetFontSize();
        IconDraw.AddCentered(dl, FontAwesomeIcon.UserFriends, Px(12f),
            new Vector2(x + Px(7f), y + fsz * 0.5f),
            ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = 0.95f }));
        dl.AddText(new Vector2(x + Px(20f), y), OsDraw.White(0.95f), Loc.T("os.party_title"));

        var right = x + w;
        if (OpenSettings is { } openSettings && !InputLocked)
        {
            var cogC = new Vector2(right - Px(9f), y + fsz * 0.5f);
            ImGui.SetCursorScreenPos(cogC - Px(11f, 11f));
            if (ImGui.InvisibleButton("##partySettings", Px(22f, 22f)))
            {
                openSettings();
            }
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                SharedUiHelpers.HandOnHover();
                ImGui.SetTooltip(Loc.T("os.party_settings"));
            }
            IconDraw.AddCentered(dl, FontAwesomeIcon.Cog, Px(12f), cogC,
                OsDraw.White(hovered ? 0.95f : 0.55f));
            right -= Px(26f);
        }

        if (together.InParty)
        {
            var count = string.Format(Loc.T("os.party_members"), together.Members.Count, together.MaxMembers);
            var countSz = ImGui.CalcTextSize(count) * 0.82f;
            dl.AddText(ImGui.GetFont(), fsz * 0.82f, new Vector2(right - countSz.X, y + Px(2f)),
                OsDraw.White(0.6f), count);
        }
        return y + fsz + Px(8f);
    }

    private float DrawEnded(ImDrawListPtr dl, float x, float y, float w)
    {
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f, new Vector2(x, y),
            OsDraw.White(0.75f), Loc.T("os.party_ended"));
        y += ImGui.GetFontSize() * 0.85f + Px(8f);
        if (Pill(dl, "##partyDismiss", Loc.T("os.party_dismiss"), new Vector2(x, y), filled: true))
        {
            together.DismissEnded();
        }
        return y + PillH() + Px(6f);
    }

    /// <summary>The "what we're doing" line: a live activity with a one-tap way into it.</summary>
    private float DrawActivity(ImDrawListPtr dl, float x, float y, float w, OsPartyActivity activity)
    {
        var (icon, label) = activity.AppId switch
        {
            "echo" => (FontAwesomeIcon.Film, Loc.T("os.party_activity_echo")),
            "wayfinder" => (FontAwesomeIcon.Compass, Loc.T("os.party_activity_wayfinder")),
            "racer" => (FontAwesomeIcon.FlagCheckered, Loc.T("os.party_activity_racer")),
            _ => (FontAwesomeIcon.Bolt, Loc.T("os.party_activity_generic")),
        };
        var rowH = PillH();
        IconDraw.AddCentered(dl, icon, Px(11f), new Vector2(x + Px(7f), y + rowH * 0.5f),
            ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = 0.9f }));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(x + Px(20f), y + (rowH - ImGui.GetFontSize() * 0.85f) * 0.5f),
            OsDraw.White(0.85f), label);

        var openLabel = Loc.T("os.party_activity_open");
        var openW = PillW(openLabel);
        if (Pill(dl, "##partyActivityOpen", openLabel, new Vector2(x + w - openW, y), filled: true))
        {
            openActivity(activity);
        }
        return y + rowH + Px(8f);
    }

    /// <summary>The roster as a wrapping row of avatar discs, the disc-plus-<see cref="AvatarRings"/> shape
    /// every people-surface on the phone uses. Big enough to recognise somebody by their picture, which a
    /// column of initials never was. The cell leaves room for the ring's overhang on every side, so a ring
    /// never crowds its neighbour or the name under it.</summary>
    private float DrawRoster(ImDrawListPtr dl, float x, float y, float w)
    {
        var radius = Px(24f);
        var ringR = radius * AetherLove.UI.AvatarRings.Overhang;
        var labelH = ImGui.GetFontSize() * 0.72f;
        var cellW = (ringR * 2f) + Px(12f);
        var cellH = (ringR * 2f) + Px(6f) + labelH + Px(10f);
        var perRow = Math.Max(1, (int)MathF.Floor(w / cellW));
        var members = together.Members;
        var lead = (w - (Math.Min(perRow, members.Count) * cellW)) * 0.5f;

        var index = 0;
        foreach (var member in members)
        {
            var col = index % perRow;
            var row = index / perRow;
            var centre = new Vector2(
                x + lead + (col * cellW) + (cellW * 0.5f),
                y + ringR + (row * cellH));
            DrawMemberDisc(dl, centre, radius, member);
            DrawMemberName(dl, centre, ringR, member, cellW - Px(4f));
            index++;
        }

        var rows = (int)MathF.Ceiling(index / (float)perRow);
        return y + (rows * cellH) + Px(4f);
    }

    /// <summary>The name under a disc, ellipsised to its cell so a long name never runs into its neighbour.</summary>
    private static void DrawMemberName(ImDrawListPtr dl, Vector2 centre, float ringR, OsPartyMember member, float maxW)
    {
        const float Scale = 0.72f;
        var name = member.Name;
        var width = ImGui.CalcTextSize(name).X * Scale;
        if (width > maxW)
        {
            while (name.Length > 1 && ImGui.CalcTextSize(name + "...").X * Scale > maxW)
            {
                name = name[..^1];
            }
            name += "...";
            width = ImGui.CalcTextSize(name).X * Scale;
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * Scale,
            new Vector2(centre.X - (width * 0.5f), centre.Y + ringR + Px(4f)),
            OsDraw.White(member.Connected ? 0.85f : 0.4f), name);
    }

    /// <summary>One member's disc: their avatar (or an initial), their equipped ring over it, a crown badge
    /// for the host, and the kick target a host gets on everybody else. Both badges sit ON the disc rather
    /// than outside it, so nothing lands under the ring art or over the name.</summary>
    private void DrawMemberDisc(ImDrawListPtr dl, Vector2 centre, float radius, OsPartyMember member)
    {
        var alpha = member.Connected ? 1f : 0.4f;
        var tex = PartyAvatars.Resolve(member)?.GetWrapOrDefault();
        if (tex is not null)
        {
            dl.AddImageRounded(tex.Handle, centre - new Vector2(radius), centre + new Vector2(radius),
                Vector2.Zero, Vector2.One,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha)), radius);
        }
        else
        {
            dl.AddCircleFilled(centre, radius,
                ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = 0.28f * alpha }), 40);
            var initial = member.Name.Length > 0 ? member.Name[..1].ToUpperInvariant() : "?";
            var initialSz = ImGui.CalcTextSize(initial) * 1.2f;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.2f, centre - (initialSz * 0.5f),
                OsDraw.White(0.9f * alpha), initial);
        }
        dl.AddCircle(centre, radius, ImGui.ColorConvertFloat4ToU32(PartyGreen with { W = 0.45f * alpha }), 40);
        if (member.Connected)
        {
            AetherLove.UI.AvatarRings.Draw(dl, centre, radius, member.FrameRef);
        }

        if (member.IsHost)
        {
            var crownC = centre + new Vector2(-radius * 0.62f, -radius * 0.62f);
            dl.AddCircleFilled(crownC, Px(9f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.13f, 0.11f, 0.07f, 0.92f)));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Crown, Px(9f), crownC,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.94f, 0.75f, 0.3f, 0.95f)));
        }

        if (!together.AmHost || member.IsHost || InputLocked)
        {
            return;
        }
        var kickC = centre + new Vector2(radius * 0.62f, -radius * 0.62f);
        ImGui.SetCursorScreenPos(kickC - Px(10f, 10f));
        if (ImGui.InvisibleButton($"##partyKick{member.AccountId}", Px(20f, 20f)))
        {
            // Asked, never done on the click: removing somebody from a party they are playing in is not a
            // thing to do by brushing past a small X.
            _kickTarget = member;
            _kickPanelH = 0f;
        }
        var kickHovered = ImGui.IsItemHovered();
        if (kickHovered)
        {
            SharedUiHelpers.HandOnHover();
            ImGui.SetTooltip(string.Format(Loc.T("os.party_kick_tip"), member.Name));
        }
        dl.AddCircleFilled(kickC, Px(9f), ImGui.ColorConvertFloat4ToU32(kickHovered
            ? new Vector4(0.85f, 0.35f, 0.35f, 0.95f)
            : new Vector4(0.1f, 0.09f, 0.12f, 0.85f)));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(9f), kickC,
            OsDraw.White(kickHovered ? 1f : 0.55f));
    }

    /// <summary>The kick confirmation, drawn over the whole phone page after the widget content (the
    /// in-page overlay panel every other confirm on the phone uses, never a viewport modal). Held as a
    /// member rather than an id, so the panel can name the person being removed.</summary>
    public void DrawKickConfirm(Vector2 contentTL, Vector2 contentBR)
    {
        if (_kickTarget is not { } target)
        {
            return;
        }
        var size = contentBR - contentTL;
        if (SharedUiHelpers.DrawPageOverlayPanel("partyKick", contentTL, size, ref _kickPanelH, Px(160f), innerW =>
        {
            AetherLove.Widgets.ModalUi.Header(innerW, FontAwesomeIcon.UserSlash,
                Loc.T("os.party_kick_title"), new Vector4(0.85f, 0.35f, 0.35f, 1f));
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, string.Format(Loc.T("os.party_kick_body"), target.Name));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (AetherLove.Widgets.ModalUi.Button($"{Loc.T("os.party_kick_go")}##partyKickGo", innerW))
            {
                together.Kick(target.AccountId);
                _kickTarget = null;
            }
            ImGui.Spacing();
            if (AetherLove.Widgets.ModalUi.Button($"{Loc.T("common.cancel")}##partyKickCancel", innerW))
            {
                _kickTarget = null;
            }
        }))
        {
            _kickTarget = null;
        }
    }

    /// <summary>Whether a confirm of this card's own is up, so the page can lock the rest of its input.</summary>
    public bool ConfirmOpen => _kickTarget is not null;

    /// <summary>Drops an open confirm; true when there was one, which is what lets the home button back out
    /// of it the way it backs out of a folder.</summary>
    public bool DismissConfirm()
    {
        if (_kickTarget is null)
        {
            return false;
        }
        _kickTarget = null;
        return true;
    }

    private float DrawPartyActions(ImDrawListPtr dl, float x, float y, float w)
    {
        var code = together.Code ?? string.Empty;
        var codeLabel = ImGui.GetTime() < _copiedUntil
            ? Loc.T("os.party_copied")
            : string.Format(Loc.T("os.party_code"), code);
        if (Pill(dl, "##partyCode", codeLabel, new Vector2(x, y), filled: false) && code.Length > 0)
        {
            ImGui.SetClipboardText(code);
            _copiedUntil = ImGui.GetTime() + 1.5;
        }
        // Anyone in the party may invite: the code is already theirs to copy from the pill beside this one.
        // Only the HOST may publish it as a hangout, though, so that target is dropped for everybody else
        // rather than offered and refused by the server.
        var inviteExcluded = together.AmHost ? null : HangoutsOnly;
        if (share.TargetsFor(AetherOS.Sdk.ShareTypes.Party, inviteExcluded).Count > 0)
        {
            var inviteX = x + PillW(codeLabel) + Px(8f);
            if (Pill(dl, "##partyInvite", Loc.T("os.party_invite"), new Vector2(inviteX, y), filled: false))
            {
                var host = together.Members.FirstOrDefault(m => m.IsHost);
                share.Offer(new AetherOS.Sdk.ShareItem
                {
                    Type = AetherOS.Sdk.ShareTypes.Party,
                    RefId = together.PartyId?.ToString("D") ?? string.Empty,
                    Title = host?.Name ?? string.Empty,
                    Subtitle = code,
                    SourceAppId = null,
                }, Loc.T("os.party_title"), inviteExcluded);
            }
        }

        var leaveLabel = together.AmHost ? Loc.T("os.party_end") : Loc.T("os.party_leave");
        var leaveW = PillW(leaveLabel);
        if (Pill(dl, "##partyLeave", leaveLabel, new Vector2(x + w - leaveW, y), filled: false, danger: true))
        {
            if (together.AmHost)
            {
                together.End();
            }
            else
            {
                together.Leave();
            }
        }
        y += PillH() + Px(8f);

        return y;
    }

    private float DrawJoinRow(ImDrawListPtr dl, float x, float y, float w)
    {
        if (Pill(dl, "##partyStart", Loc.T("os.party_start"), new Vector2(x, y), filled: true))
        {
            Start(together.Create);
        }
        y += PillH() + Px(8f);

        var joinW = PillW(Loc.T("os.party_join"));
        var fieldW = w - joinW - Px(8f);
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.SetNextItemWidth(fieldW);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        ImGui.InputTextWithHint("##partyJoinCode", Loc.T("os.party_join_hint"), ref _joinCode, 12,
            ImGuiInputTextFlags.CharsUppercase | ImGuiInputTextFlags.CharsNoBlank);
        ImGui.PopStyleVar();
        if (Pill(dl, "##partyJoin", Loc.T("os.party_join"), new Vector2(x + fieldW + Px(8f), y), filled: false)
            && _joinCode.Trim().Length > 0)
        {
            var code = _joinCode;
            Start(() => together.Join(code));
            _joinCode = string.Empty;
        }
        return y + PillH() + Px(6f);
    }

    private static float PillH() => ImGui.GetFontSize() * 0.85f + Px(10f);

    private static float PillW(string label) => ImGui.CalcTextSize(label).X * 0.85f + Px(20f);

    private bool Pill(ImDrawListPtr dl, string id, string label, Vector2 tl, bool filled, bool danger = false)
    {
        var h = PillH();
        var wPill = PillW(label);
        var clicked = false;
        var hovered = false;
        if (!InputLocked && !together.Busy)
        {
            ImGui.SetCursorScreenPos(tl);
            clicked = ImGui.InvisibleButton(id, new Vector2(wPill, h));
            hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                SharedUiHelpers.HandOnHover();
            }
        }
        var accent = danger ? new Vector4(0.85f, 0.35f, 0.35f, 1f) : PartyGreen;
        var alpha = together.Busy ? 0.35f : hovered ? 1f : 0.85f;
        if (filled)
        {
            dl.AddRectFilled(tl, tl + new Vector2(wPill, h),
                ImGui.ColorConvertFloat4ToU32(accent with { W = 0.3f * alpha }), h * 0.5f);
        }
        dl.AddRect(tl, tl + new Vector2(wPill, h),
            ImGui.ColorConvertFloat4ToU32(accent with { W = 0.7f * alpha }), h * 0.5f);
        var sz = ImGui.CalcTextSize(label) * 0.85f;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            tl + new Vector2((wPill - sz.X) * 0.5f, (h - sz.Y) * 0.5f), OsDraw.White(0.92f * alpha), label);
        return clicked;
    }
}
