using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Messenger;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Messenger;

/// <summary>The in-page overlays: add contact, new group (with square picture), group info, settings,
/// report and confirm. Every panel carries an explicit close button next to its header.</summary>
public sealed partial class MessengerApp
{
    private string _groupName = string.Empty;
    private readonly System.Collections.Generic.HashSet<Guid> _groupPick = new();
    private CroppedImage? _groupAvatarStaged;

    private Guid _infoGroupId;
    private string _infoNameEdit = string.Empty;
    private bool _infoEditingName;

    private Guid _memberCardGroupId;
    private Guid _memberCardAccountId;

    private Guid _reportAccountId;
    private string _reportPeerName = string.Empty;
    private string _reportReason = string.Empty;
    private volatile bool _reportDone;

    private string _confirmTitle = string.Empty;
    private string _confirmBody = string.Empty;
    private Action? _confirmAction;

    private MessengerBlockedDto[]? _blocked;

    private void OpenNewGroup()
    {
        _groupName = string.Empty;
        _groupPick.Clear();
        _groupAvatarStaged = null;
        _overlay = Overlay.NewGroup;
    }

    private void OpenGroupInfo(Guid groupId)
    {
        _infoGroupId = groupId;
        _infoEditingName = false;
        _overlay = Overlay.GroupInfo;
    }

    private void OpenMemberCard(Guid groupId, Guid accountId)
    {
        _memberCardGroupId = groupId;
        _memberCardAccountId = accountId;
        _overlay = Overlay.MemberCard;
    }

    private void OpenBlocked()
    {
        _blocked = null;
        _view = View.Blocked;
        RunHub(async () => _blocked = await _hub.GetMessengerBlockedAsync().ConfigureAwait(false));
    }

    private void OpenReport(Guid contactId, string peerName)
    {
        var contact = _store.Contact(contactId);
        if (contact is null)
        {
            return;
        }
        _reportAccountId = contact.PeerAccountId;
        _reportPeerName = peerName;
        _reportReason = string.Empty;
        _reportDone = false;
        _overlay = Overlay.Report;
    }

    private void OpenConfirm(string title, string body, Action action)
    {
        _confirmTitle = title;
        _confirmBody = body;
        _confirmAction = action;
        _overlay = Overlay.Confirm;
    }

    private void OpenBlockConfirm(Guid contactId, string peerName)
    {
        var contact = _store.Contact(contactId);
        if (contact is null)
        {
            return;
        }
        OpenConfirm(
            Loc.T("os.msgr_block"),
            string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_block_confirm"), peerName),
            () =>
            {
                if (_chatId == contactId)
                {
                    CloseChat();
                }
                RunHub(async () =>
                {
                    await _hub.BlockMessengerUserAsync(contact.PeerAccountId).ConfigureAwait(false);
                    // On a tombstone this dismisses the dead chat rather than removing a live contact.
                    await _hub.RemoveMessengerContactAsync(contactId).ConfigureAwait(false);
                });
            });
    }

    private void DrawOverlay(OsAppContext ctx, Vector2 contentTL, Vector2 contentSize)
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _overlay = Overlay.None;
            return;
        }
        ImGui.SetCursorScreenPos(contentTL);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var open = ImGui.BeginChild("##msgrOverlay", contentSize, false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();
        if (open)
        {
            var ease = PanelFade("overlay:" + _overlay);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(contentTL, contentTL + contentSize, Col(ScrimColor with { W = ScrimColor.W * ease }));

            var pad = Px(14f);
            var panelW = MathF.Min(contentSize.X - Px(28f), Px(300f));
            var panelH = MathF.Min(contentSize.Y - Px(50f), OverlayHeight(panelW - pad * 2f));
            var panelPos = contentTL + (contentSize - new Vector2(panelW, panelH)) * 0.5f;
            panelPos.Y += (1f - ease) * Px(10f);

            // The confirm and member-card panels are sized to their own content, so they must never grow a
            // scrollbar; the others hold scrolling lists of their own and keep theirs.
            var panelFlags = ImGuiWindowFlags.AlwaysUseWindowPadding;
            if (_overlay is Overlay.Confirm or Overlay.MemberCard)
            {
                panelFlags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            }

            ImGui.SetCursorScreenPos(panelPos);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ease * ImGui.GetStyle().Alpha);
            if (ImGui.BeginChild("##msgrOverlayPanel", new Vector2(panelW, panelH), true, panelFlags))
            {
                DrawPanelCloseButton();
                switch (_overlay)
                {
                    case Overlay.NewGroup:
                        DrawNewGroup();
                        break;
                    case Overlay.GroupInfo:
                        DrawGroupInfo();
                        break;
                    case Overlay.Report:
                        DrawReport();
                        break;
                    case Overlay.Confirm:
                        DrawConfirm();
                        break;
                    case Overlay.MemberCard:
                        DrawMemberCard();
                        break;
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(2);

            // Scrim click closes; submitted last so the panel wins input.
            ImGui.SetCursorScreenPos(contentTL);
            if (ImGui.InvisibleButton("##msgrOverlayScrim", contentSize)
                && !ImGui.IsMouseHoveringRect(panelPos, panelPos + new Vector2(panelW, panelH)))
            {
                _overlay = Overlay.None;
            }
        }
        ImGui.EndChild();
    }

    private float OverlayHeight(float innerW) => _overlay switch
    {
        Overlay.NewGroup => Px(430f),
        Overlay.GroupInfo => Px(440f),
        Overlay.Report => Px(300f),
        Overlay.Confirm => ConfirmHeight(innerW),
        Overlay.MemberCard => MemberCardHeight(),
        _ => Px(300f),
    };

    private float MemberCardHeight()
    {
        var member = _store.Group(_memberCardGroupId)?.Members.FirstOrDefault(m => m.AccountId == _memberCardAccountId);
        var showAdd = member is { IsOwner: false } && IsUnknownMember(member.AccountId);
        return Px(14f) * 2f + Px(AvatarR * 2f) + (showAdd ? Px(16f) + Px(46f) : 0f);
    }

    /// <summary>Hugs the wrapped question: a fixed height padded a one-line confirm and pushed a three-line one
    /// behind a scrollbar.</summary>
    private float ConfirmHeight(float innerW)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        float headingH;
        using (UiFonts.H3?.Push())
        {
            headingH = ImGui.GetTextLineHeight();
        }
        headingH += Px(2f) + spacing * 2f + ImGui.GetStyle().ItemSpacing.Y + Px(6f);
        var bodyH = ImGui.CalcTextSize(_confirmBody, false, innerW).Y;
        var buttonsH = Px(8f) + Px(32f) + Px(4f) + Px(32f) + spacing * 4f;
        return Px(28f) + headingH + bodyH + buttonsH;
    }

    /// <summary>An X in the panel's top-right corner; drawn first so it wins clicks over the heading row.</summary>
    private void DrawPanelCloseButton()
    {
        var saveCursor = ImGui.GetCursorScreenPos();
        var btn = Px(24f);
        var tl = new Vector2(saveCursor.X + ImGui.GetContentRegionAvail().X - btn, saveCursor.Y - Px(2f));
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##msgrPanelClose", new Vector2(btn, btn)))
        {
            _overlay = Overlay.None;
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        IconCentered(ImGui.GetWindowDrawList(), FontAwesomeIcon.Times, Px(12f),
            tl + new Vector2(btn * 0.5f, btn * 0.5f), hovered ? 0xFFFFFFFFu : Col(MutedText));
        ImGui.SetCursorScreenPos(saveCursor);
    }

    private void PanelHeading(FontAwesomeIcon icon, string text)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var fs = ImGui.GetFontSize();
        IconCentered(dl, icon, Px(13f), cursor + new Vector2(Px(9f), fs * 0.55f), ImGui.GetColorU32(t.AccentLight));
        using (UiFonts.H3?.Push())
        {
            ImGui.SetCursorScreenPos(cursor + new Vector2(Px(24f), 0f));
            ImGui.TextColored(t.AccentLight, text);
        }
        ImGui.Dummy(new Vector2(0f, Px(2f)));
        ImGui.PushStyleColor(ImGuiCol.Separator, t.AccentLight with { W = 0.30f });
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    private bool PanelButton(string label, bool primary, bool enabled = true)
    {
        var t = ThemeService.Current;
        var w = ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        ImGui.PushStyleColor(ImGuiCol.Button, primary ? t.ButtonNormal : new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, primary ? t.ButtonHovered : new Vector4(1f, 1f, 1f, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, primary ? t.ButtonActive : new Vector4(1f, 1f, 1f, 0.16f));
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }
        var clicked = ImGui.Button(label, new Vector2(w, Px(32f)));
        if (!enabled)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        return clicked && enabled;
    }

    private void DrawNewGroup()
    {
        PanelHeading(FontAwesomeIcon.Users, Loc.T("os.msgr_new_group"));
        var sync = _store.Sync;
        var sizeCap = sync?.GroupSizeCap ?? 4;
        var groupCap = sync?.GroupCap ?? 0;
        var created = sync?.GroupsCreated ?? 0;
        var atCap = groupCap > 0 && created >= groupCap;

        // Square group picture next to the name, staged locally and uploaded after creation.
        var dl = ImGui.GetWindowDrawList();
        var picSize = Px(44f);
        var rowStart = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(rowStart);
        if (ImGui.InvisibleButton("##groupPic", new Vector2(picSize, picSize)))
        {
            _caps.Images.PickAndCrop(
                new ImageCropRequest(Loc.T("os.msgr_change_avatar"), "Images{.png,.jpg,.jpeg}",
                    Loc.T("os.msgr_change_avatar"), 1f, 100, 100),
                cropped => _groupAvatarStaged = cropped);
        }
        var picHovered = ImGui.IsItemHovered();
        if (picHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("os.msgr_change_avatar"));
        }
        if (_groupAvatarStaged is { } staged && _caps.Textures.Get(staged.Path) is { } handle)
        {
            // Show the cropped square the user picked, not the whole source image.
            var (uv0, uv1) = CropUv(staged.Crop, _caps.Textures.GetSize(staged.Path) ?? Vector2.Zero);
            dl.AddImageRounded(handle, rowStart, rowStart + new Vector2(picSize, picSize),
                uv0, uv1, 0xFFFFFFFFu, Px(10f), ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddRectFilled(rowStart, rowStart + new Vector2(picSize, picSize),
                Col(new Vector4(1f, 1f, 1f, picHovered ? 0.10f : 0.06f)), Px(10f));
            IconCentered(dl, FontAwesomeIcon.Camera, Px(15f),
                rowStart + new Vector2(picSize * 0.5f, picSize * 0.5f), Col(MutedText));
        }
        dl.AddRect(rowStart, rowStart + new Vector2(picSize, picSize),
            Col(new Vector4(1f, 1f, 1f, 0.16f)), Px(10f));

        ImGui.SetCursorScreenPos(rowStart + new Vector2(picSize + Px(10f), (picSize - ImGui.GetFrameHeight()) * 0.5f));
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, InputFill);
        ImGui.InputTextWithHint("##groupName", Loc.T("os.msgr_group_name"), ref _groupName,
            MessengerLimits.MaxGroupNameChars);
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(rowStart + new Vector2(0f, picSize + Px(8f)));

        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.TextUnformatted(string.Format(CultureInfo.InvariantCulture,
            Loc.T("os.msgr_group_members_pick"), _groupPick.Count + 1, sizeCap));
        ImGui.PopStyleColor();

        var listH = ImGui.GetContentRegionAvail().Y - Px(76f);
        if (ImGui.BeginChild("##groupPickList", new Vector2(0f, listH), false))
        {
            foreach (var contact in _store.Contacts.Where(c => !c.RemovedByPeer).OrderBy(c => c.PeerName))
            {
                var picked = _groupPick.Contains(contact.PeerAccountId);
                if (ImGui.Checkbox($"{contact.PeerName}##pick{contact.ContactId:N}", ref picked))
                {
                    if (picked && _groupPick.Count + 1 < sizeCap)
                    {
                        _groupPick.Add(contact.PeerAccountId);
                    }
                    else
                    {
                        _groupPick.Remove(contact.PeerAccountId);
                    }
                }
            }
        }
        ImGui.EndChild();

        if (atCap)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, WarnAmber);
            ImGui.TextUnformatted(string.Format(CultureInfo.InvariantCulture,
                Loc.T("os.msgr_group_cap"), groupCap));
            ImGui.PopStyleColor();
        }
        if (PanelButton(Loc.T("os.msgr_create_group"), primary: true,
                enabled: !atCap && _groupName.Trim().Length > 0 && _groupPick.Count > 0))
        {
            var name = _groupName.Trim();
            var members = _groupPick.ToArray();
            var avatar = _groupAvatarStaged;
            RunHub(async () =>
            {
                var group = await _hub.CreateMessengerGroupAsync(new CreateMessengerGroupRequest(name, members))
                    .ConfigureAwait(false);
                _store.ApplyGroupChanged(group);
                await _sync.MaintainOwnedGroupKeysAsync().ConfigureAwait(false);
                if (avatar is { } picked)
                {
                    await UploadGroupAvatarAsync(group.GroupId, picked).ConfigureAwait(false);
                }
                // OpenChat mutates draw-loop collections; it must not run on this pool thread.
                _uiActions.Enqueue(() =>
                {
                    _overlay = Overlay.None;
                    OpenChat(group.GroupId, MessengerChatKind.Group);
                });
            });
        }
    }

    /// <summary>A tapped group member's profile card: their avatar (chat-header size) with their name beside it,
    /// and a "request to add" button for a member who is not yet a contact and is not the group owner.</summary>
    private void DrawMemberCard()
    {
        var group = _store.Group(_memberCardGroupId);
        var member = group?.Members.FirstOrDefault(m => m.AccountId == _memberCardAccountId);
        if (group is null || member is null)
        {
            _overlay = Overlay.None;
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var innerW = ImGui.GetContentRegionAvail().X;
        var avatarR = Px(AvatarR);
        var start = ImGui.GetCursorScreenPos();
        var avatarC = new Vector2(start.X + avatarR, start.Y + avatarR);
        DrawAvatar(dl, member.AccountId, member.Name, member.Avatar, false, avatarC, avatarR, member.FrameRef);
        dl.AddCircle(avatarC, avatarR, ThemeService.Current.AccentWithAlpha(0.65f), 0, Px(1.5f));

        var nameX = avatarC.X + avatarR + Px(12f);
        // Leave room on the right for the close button, plus the owner star when present, so neither collides.
        var nameRight = start.X + innerW - Px(26f) - (member.IsOwner ? Px(22f) : 0f);
        using (UiFonts.H3?.Push())
        {
            var shown = TruncateToWidth(member.Name, nameRight - nameX);
            var nameSz = ImGui.CalcTextSize(shown);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(nameX, avatarC.Y - nameSz.Y * 0.5f), Col(BodyText), shown);
            if (member.IsOwner)
            {
                IconCentered(dl, FontAwesomeIcon.Star, Px(11f),
                    new Vector2(nameX + nameSz.X + Px(11f), avatarC.Y), UiColors.FavoriteStar);
            }
        }

        if (member is { IsOwner: false } && IsUnknownMember(member.AccountId))
        {
            ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + avatarR * 2f + Px(16f)));
            if (DrawPrimaryButton(Loc.T("os.msgr_member_add"), true))
            {
                var gid = group.GroupId;
                var target = member.AccountId;
                _overlay = Overlay.None;
                RunHub(async () =>
                {
                    await _hub.AddMessengerGroupContactAsync(gid, target).ConfigureAwait(false);
                    await _sync.SyncAsync().ConfigureAwait(false);
                });
            }
        }
    }

    private void DrawGroupInfo()
    {
        var group = _store.Group(_infoGroupId);
        if (group is null)
        {
            _overlay = Overlay.None;
            return;
        }
        var mine = group.OwnerAccountId == _store.MyAccountId;
        var dl = ImGui.GetWindowDrawList();
        var innerW = ImGui.GetContentRegionAvail().X;

        // Centered avatar; any member may click it to change the picture.
        var avatarR = Px(30f);
        var avStart = ImGui.GetCursorScreenPos();
        var avatarC = new Vector2(avStart.X + innerW * 0.5f, avStart.Y + avatarR);
        ImGui.SetCursorScreenPos(avatarC - new Vector2(avatarR, avatarR));
        if (ImGui.InvisibleButton("##groupInfoPic", new Vector2(avatarR * 2f, avatarR * 2f)))
        {
            var groupId = group.GroupId;
            _caps.Images.PickAndCrop(
                new ImageCropRequest(Loc.T("os.msgr_change_avatar"), "Images{.png,.jpg,.jpeg}",
                    Loc.T("os.msgr_change_avatar"), 1f, 100, 100),
                cropped => RunHub(() => UploadGroupAvatarAsync(groupId, cropped)));
        }
        var avHovered = ImGui.IsItemHovered();
        if (avHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("os.msgr_change_avatar"));
        }
        DrawAvatar(dl, group.GroupId, group.Name, group.Avatar, true, avatarC, avatarR);
        dl.AddCircle(avatarC, avatarR, ThemeService.Current.AccentWithAlpha(0.65f), 0, Px(1.5f));
        if (avHovered)
        {
            dl.AddCircleFilled(avatarC, avatarR, 0x50000000u);
            IconCentered(dl, FontAwesomeIcon.Camera, Px(14f), avatarC, 0xFFFFFFFFu);
        }
        ImGui.SetCursorScreenPos(new Vector2(avStart.X, avatarC.Y + avatarR + Px(8f)));

        if (_infoEditingName && mine)
        {
            ImGui.SetNextItemWidth(innerW - Px(56f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, InputFill);
            ImGui.InputText("##editName", ref _infoNameEdit, MessengerLimits.MaxGroupNameChars);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("os.msgr_save")) && _infoNameEdit.Trim().Length > 0)
            {
                var groupId = group.GroupId;
                var name = _infoNameEdit.Trim();
                _infoEditingName = false;
                RunHub(() => _hub.SetMessengerGroupNameAsync(groupId, name));
            }
        }
        else
        {
            string shownName;
            Vector2 nameSz;
            using (UiFonts.H3?.Push())
            {
                shownName = TruncateToWidth(group.Name, innerW - Px(30f));
                nameSz = ImGui.CalcTextSize(shownName);
                ImGui.SetCursorPosX(MathF.Max(0f, (innerW - nameSz.X) * 0.5f));
                ImGui.TextUnformatted(shownName);
            }
            if (mine)
            {
                ImGui.SameLine(0f, Px(6f));
                ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(Col(MutedText)), FontAwesomeIcon.Pen.ToIconString());
                ImGui.PopFont();
                if (ImGui.IsItemClicked())
                {
                    _infoNameEdit = group.Name;
                    _infoEditingName = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip(Loc.T("os.msgr_rename"));
                }
            }
        }
        var membersLabel = string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_members"), group.Members.Length);
        var membersSz = ImGui.CalcTextSize(membersLabel);
        ImGui.SetCursorPosX(MathF.Max(0f, (innerW - membersSz.X) * 0.5f));
        ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(Col(MutedText)), membersLabel);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        var listH = ImGui.GetContentRegionAvail().Y - Px(mine ? 110f : 44f);
        if (ImGui.BeginChild("##memberList", new Vector2(0f, listH), false))
        {
            foreach (var member in group.Members)
            {
                var rowTl = ImGui.GetCursorScreenPos();
                var rowH = Px(28f);
                var mdl = ImGui.GetWindowDrawList();
                var mAvatarR = Px(11f);
                var mAvatarC = rowTl + new Vector2(mAvatarR + Px(2f), rowH * 0.5f);
                DrawAvatar(mdl, member.AccountId, member.Name, member.Avatar, false, mAvatarC, mAvatarR);
                var nameX = mAvatarC.X + mAvatarR + Px(8f);
                mdl.AddText(new Vector2(nameX, rowTl.Y + (rowH - ImGui.GetFontSize()) * 0.5f),
                    Col(BodyText), member.Name);
                if (member.IsOwner)
                {
                    var ownerX = nameX + ImGui.CalcTextSize(member.Name).X + Px(6f);
                    IconCentered(mdl, FontAwesomeIcon.Star, Px(9f),
                        new Vector2(ownerX + Px(5f), rowTl.Y + rowH * 0.5f), UiColors.FavoriteStar);
                }

                if (mine && !member.IsOwner)
                {
                    ImGui.SetCursorScreenPos(new Vector2(rowTl.X + ImGui.GetContentRegionAvail().X - Px(46f), rowTl.Y + Px(2f)));
                    if (ImGui.SmallButton($"{Loc.T("os.msgr_kick")}##{member.AccountId:N}"))
                    {
                        var groupId = group.GroupId;
                        var target = member.AccountId;
                        // Removing the sole remaining member leaves only the owner, which deletes the group; warn first.
                        if (group.Members.Count(m => !m.IsOwner) <= 1)
                        {
                            OpenConfirm(Loc.T("os.msgr_kick_last_title"),
                                string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_kick_last_confirm"), group.Name),
                                () =>
                                {
                                    CloseChat();
                                    RunHub(() => _hub.DisbandMessengerGroupAsync(groupId));
                                });
                        }
                        else
                        {
                            RunHub(() => _hub.KickMessengerGroupMemberAsync(groupId, target));
                        }
                    }
                }
                else if (IsUnknownMember(member.AccountId))
                {
                    // A fellow member who isn't a contact yet: the codeless 1:1 request path.
                    ImGui.SetCursorScreenPos(new Vector2(rowTl.X + ImGui.GetContentRegionAvail().X - Px(46f), rowTl.Y + Px(2f)));
                    if (ImGui.SmallButton($"{Loc.T("os.msgr_member_add")}##{member.AccountId:N}"))
                    {
                        var groupId = group.GroupId;
                        var target = member.AccountId;
                        RunHub(async () =>
                        {
                            await _hub.AddMessengerGroupContactAsync(groupId, target).ConfigureAwait(false);
                            await _sync.SyncAsync().ConfigureAwait(false);
                        });
                    }
                }
                ImGui.SetCursorScreenPos(rowTl + new Vector2(0f, rowH));
            }
        }
        ImGui.EndChild();

        if (mine)
        {
            var addable = _store.Contacts
                .Where(c => !c.RemovedByPeer && group.Members.All(m => m.AccountId != c.PeerAccountId))
                .ToList();
            var sizeCap = _store.Sync?.GroupSizeCap ?? 4;
            if (addable.Count > 0 && group.Members.Length < sizeCap)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.BeginCombo("##addMember", Loc.T("os.msgr_add_member")))
                {
                    foreach (var contact in addable)
                    {
                        if (ImGui.Selectable(contact.PeerName))
                        {
                            var groupId = group.GroupId;
                            var target = contact.PeerAccountId;
                            RunHub(async () =>
                            {
                                await _hub.AddMessengerGroupMemberAsync(groupId, target).ConfigureAwait(false);
                                await _sync.MaintainOwnedGroupKeysAsync().ConfigureAwait(false);
                            });
                        }
                    }
                    ImGui.EndCombo();
                }
            }
            if (PanelButton(Loc.T("os.msgr_disband"), primary: false))
            {
                var groupId = group.GroupId;
                OpenConfirm(Loc.T("os.msgr_disband"),
                    string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_disband_confirm"), group.Name),
                    () =>
                    {
                        CloseChat();
                        RunHub(() => _hub.DisbandMessengerGroupAsync(groupId));
                    });
            }
        }
        else if (PanelButton(Loc.T("os.msgr_leave_group"), primary: false))
        {
            var groupId = group.GroupId;
            OpenConfirm(Loc.T("os.msgr_leave_group"),
                string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_leave_confirm"), group.Name),
                () =>
                {
                    CloseChat();
                    RunHub(() => _hub.LeaveMessengerGroupAsync(groupId));
                });
        }
    }

    private bool IsUnknownMember(Guid accountId) =>
        accountId != _store.MyAccountId
        && _store.Contacts.All(c => c.PeerAccountId != accountId)
        && _store.Requests.All(r => r.PeerAccountId != accountId);

    private async System.Threading.Tasks.Task UploadGroupAvatarAsync(Guid groupId, CroppedImage cropped)
    {
        var bytes = await System.IO.File.ReadAllBytesAsync(cropped.Path).ConfigureAwait(false);
        var upload = new PhotoUploadDto(Convert.ToBase64String(bytes),
            (int)cropped.Crop.X, (int)cropped.Crop.Y, (int)cropped.Crop.Z, (int)cropped.Crop.W, false);
        await _hub.SetMessengerGroupAvatarAsync(groupId, upload).ConfigureAwait(false);
    }

    private void DrawReport()
    {
        PanelHeading(FontAwesomeIcon.ExclamationTriangle, string.Format(CultureInfo.InvariantCulture,
            Loc.T("os.msgr_report_title"), _reportPeerName));
        if (_reportDone)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UnreadGreen);
            ImGui.TextUnformatted(Loc.T("os.msgr_report_done"));
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            if (PanelButton(Loc.T("os.msgr_close"), primary: true))
            {
                _overlay = Overlay.None;
            }
            return;
        }
        ImGui.PushStyleColor(ImGuiCol.FrameBg, InputFill);
        InputTextMultilineWithPaste("##reportReason", ref _reportReason, 500,
            new Vector2(ImGui.GetContentRegionAvail().X, Px(90f)));
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        if (PanelButton(Loc.T("os.msgr_report_send"), primary: true,
                enabled: _reportReason.Trim().Length > 0))
        {
            var target = _reportAccountId;
            var reason = _reportReason.Trim();
            RunHub(async () =>
            {
                await _hub.ReportMessengerUserAsync(target, reason).ConfigureAwait(false);
                _reportDone = true;
            });
        }
    }

    private void DrawConfirm()
    {
        PanelHeading(FontAwesomeIcon.QuestionCircle, _confirmTitle);
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        ImGui.TextUnformatted(_confirmBody);
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        if (PanelButton(Loc.T("os.msgr_confirm"), primary: true))
        {
            var action = _confirmAction;
            _confirmAction = null;
            _overlay = Overlay.None;
            action?.Invoke();
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        if (PanelButton(Loc.T("os.msgr_cancel"), primary: false))
        {
            _confirmAction = null;
            _overlay = Overlay.None;
        }
    }
}
