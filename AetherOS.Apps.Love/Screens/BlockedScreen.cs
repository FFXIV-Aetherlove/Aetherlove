using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Blocked-users page. Unblocking never rematches; it only lifts the caller's own block so the
/// pair can resurface in each other's decks.</summary>
public sealed class BlockedScreen
{
    private readonly LoveRouter _router;
    private readonly AetherHubContext _hub;

    private readonly List<BlockedUserDto> _items = new();
    private readonly object _itemsLock = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _avatarTex = new();
    private volatile bool _loading;
    private volatile bool _busy;
    private volatile string? _error;
    private bool _introOpen;
    private float _introPanelH;
    private BlockedUserDto? _confirmTarget;
    private float _confirmPanelH;

    private const float PadX = 16f;

    public BlockedScreen(LoveRouter router, AetherHubContext hub)
    {
        _router = router;
        _hub = hub;
    }

    public void OnShow()
    {
        _introOpen = true;
        _introPanelH = 0f;
        _confirmTarget = null;
        _error = null;
        StartFetch();
    }

    private void StartFetch()
    {
        _loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var rows = await _hub.GetBlockedUsersAsync().ConfigureAwait(false);
                var cacheDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "BlockedAvatarCache");
                foreach (var row in rows)
                {
                    if (row.AvatarWebp is { Length: > 0 } bytes)
                    {
                        _avatarTex[row.ProfileId] = AvatarDiskCache.Store(cacheDir, row.ProfileId.ToString(), bytes);
                    }
                }
                lock (_itemsLock)
                {
                    _items.Clear();
                    _items.AddRange(rows);
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[BlockedScreen] GetBlockedUsersAsync failed.");
                _error = HubErrorText.Localize(ex);
            }
            finally
            {
                _loading = false;
            }
        });
    }

    public void Draw()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();

        ImGui.Spacing();
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("chat.back_to_matches"), FontAwesomeIcon.Comment))
        {
            _router.Navigate(LoveView.ChatList);
        }
        DrawSubpageHeading(Loc.T("blocked.title"), PadX);

        BlockedUserDto[] items;
        lock (_itemsLock)
        {
            items = _items.ToArray();
        }

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##blockedScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                var listW = ImGui.GetContentRegionAvail().X;
                if (_loading)
                {
                    ImGui.Dummy(new Vector2(1f, Px(20f)));
                    CenteredMutedText(Loc.T("blocked.loading"));
                }
                else if (_error is { } err)
                {
                    ImGui.Dummy(new Vector2(1f, Px(20f)));
                    CenteredMutedText(err);
                }
                else if (items.Length == 0)
                {
                    DrawEmptyState(listW);
                }
                else
                {
                    ImGui.Dummy(new Vector2(1f, Px(4f)));
                    foreach (var item in items)
                    {
                        DrawRow(item, listW);
                    }
                }
                ImGui.Dummy(new Vector2(1f, Px(10f)));
            }
        }
        PopScrollbarStyle();

        if (_confirmTarget is not null)
        {
            DrawConfirmOverlay(winPos, winSize);
        }
        else if (_introOpen)
        {
            DrawIntroOverlay(winPos, winSize);
        }
    }

    private void DrawRow(BlockedUserDto item, float listW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var rowH = Px(56f);
        var start = ImGui.GetCursorScreenPos();
        var tl = start + new Vector2(pad, 0f);
        var br = tl + new Vector2(listW - pad * 2f, rowH);

        dl.AddRectFilled(tl, br, 0x0DFFFFFFu, Px(10f));
        dl.AddRect(tl, br, 0x22FFFFFFu, Px(10f), ImDrawFlags.None, Px(1f));

        var avatarCenter = new Vector2(tl.X + Px(30f), tl.Y + rowH * 0.5f);
        var avatarR = Px(19f);
        _avatarTex.TryGetValue(item.ProfileId, out var tex);
        var wrap = tex?.GetWrapOrDefault();
        if (wrap != null)
        {
            dl.AddImageRounded(wrap.Handle, avatarCenter - new Vector2(avatarR), avatarCenter + new Vector2(avatarR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, avatarR, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(avatarCenter, avatarR, UiColors.AvatarFallback);
        }
        dl.AddCircle(avatarCenter, avatarR, 0x55FFFFFFu, 0, Px(1.2f));

        var textX = tl.X + Px(58f);
        dl.AddText(new Vector2(textX, tl.Y + Px(10f)), 0xFFFFFFFFu, item.DisplayName);
        dl.AddText(new Vector2(textX, tl.Y + Px(29f)), UiColors.TextMuted,
            Loc.T("blocked.since", item.BlockedAtUtc.ToLocalTime().ToString("d")));

        var btnLabel = Loc.T("blocked.unblock");
        var btnSz = ImGui.CalcTextSize(btnLabel);
        var btnW = btnSz.X + Px(24f);
        var btnH = Px(26f);
        ImGui.SetCursorScreenPos(new Vector2(br.X - btnW - Px(10f), tl.Y + (rowH - btnH) * 0.5f));
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        using (ImRaii.Disabled(_busy))
        {
            if (ImGui.Button($"{btnLabel}##unblock_{item.ProfileId}", new Vector2(btnW, btnH)))
            {
                _confirmTarget = item;
                _confirmPanelH = 0f;
                _error = null;
            }
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        ImGui.SetCursorScreenPos(new Vector2(start.X, br.Y));
        ImGui.Dummy(new Vector2(1f, Px(8f)));
    }

    private static void DrawEmptyState(float listW)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(1f, Px(36f)));
        var center = ImGui.GetCursorScreenPos() + new Vector2(listW * 0.5f, Px(21f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Ban, Px(42f), center,
            ImGui.GetColorU32(UiColors.Muted with { W = 0.6f }));
        ImGui.Dummy(new Vector2(1f, Px(56f)));
        CenteredMutedText(Loc.T("blocked.empty"));
    }

    private void DrawIntroOverlay(Vector2 winPos, Vector2 winSize)
    {
        var dismissed = DrawPageOverlayPanel("blockedIntro", winPos, winSize, ref _introPanelH, Px(300f), w =>
        {
            ModalUi.Header(w, FontAwesomeIcon.Ban, Loc.T("blocked.title"), ThemeService.Current.Accent);
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("blocked.intro_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (ModalUi.Button($"{Loc.T("common.got_it")}##blockedGotIt", w))
            {
                _introOpen = false;
            }
        });
        if (dismissed)
        {
            _introOpen = false;
        }
    }

    private void DrawConfirmOverlay(Vector2 winPos, Vector2 winSize)
    {
        var target = _confirmTarget!;
        var dismissed = DrawPageOverlayPanel("blockedConfirm", winPos, winSize, ref _confirmPanelH, Px(290f), w =>
        {
            ModalUi.Header(w, FontAwesomeIcon.UserCheck, Loc.T("blocked.confirm_title", target.DisplayName),
                ThemeService.Current.Accent);
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("blocked.confirm_body"));
            ImGui.PopTextWrapPos();
            if (_error is { } err)
            {
                ImGui.Spacing();
                ImGui.PushTextWrapPos(w);
                ImGui.TextColored(UiColors.Danger, err);
                ImGui.PopTextWrapPos();
            }
            ImGui.Spacing();
            var gap = Px(8f);
            var half = (w - gap) * 0.5f;
            if (ModalUi.Button($"{Loc.T("common.cancel")}##unblockCancel", half))
            {
                _confirmTarget = null;
            }
            ImGui.SameLine(0f, gap);
            using (ImRaii.Disabled(_busy))
            {
                if (ModalUi.Button($"{Loc.T("blocked.unblock")}##unblockGo", half))
                {
                    Unblock(target);
                }
            }
        });
        if (dismissed && !_busy)
        {
            _confirmTarget = null;
        }
    }

    private void Unblock(BlockedUserDto target)
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.UnblockUserAsync(target.ProfileId).ConfigureAwait(false);
                lock (_itemsLock)
                {
                    _items.RemoveAll(x => x.ProfileId == target.ProfileId);
                }
                _confirmTarget = null;
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[BlockedScreen] UnblockUserAsync failed.");
                _error = HubErrorText.Localize(ex);
            }
            finally
            {
                _busy = false;
            }
        });
    }

    private static void CenteredMutedText(string text)
    {
        var winW = ImGui.GetContentRegionAvail().X;
        var textW = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - textW) * 0.5f));
        ImGui.TextColored(UiColors.Muted, text);
    }
}
