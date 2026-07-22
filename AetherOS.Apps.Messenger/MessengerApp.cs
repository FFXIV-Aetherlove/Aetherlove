using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Services.Messenger;
using AetherLove.Shared.Messenger;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Messenger;

/// <summary>The real account-level messenger: friend-code contacts, E2E direct and group chats. Chat UI is a
/// deliberate fork of the AetherLove match chat (see CLAUDE.md), expected to grow its own features.</summary>
public sealed partial class MessengerApp : IAetherApp, IAppSettings
{
    private static readonly Vector4 TileTopColor = new(0.15f, 0.86f, 0.49f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.04f, 0.48f, 0.28f, 1f);
    private static readonly Vector4 UnreadGreen = new(0.14f, 0.80f, 0.42f, 1f);
    private static readonly Vector4 BodyText = new(0.96f, 0.96f, 0.97f, 1f);
    private static readonly Vector4 MutedText = new(1f, 1f, 1f, 0.55f);
    private static readonly Vector4 ScrimColor = new(0f, 0f, 0f, 0.55f);
    private static readonly Vector4 InputFill = new(1f, 1f, 1f, 0.06f);
    private static readonly Vector4 DangerRed = new(0.92f, 0.32f, 0.32f, 1f);
    private static readonly Vector4 WarnAmber = new(0.95f, 0.75f, 0.30f, 1f);

    private static readonly Vector4[] AuthorPalette =
    {
        new(0.36f, 0.68f, 0.98f, 1f),
        new(0.98f, 0.55f, 0.45f, 1f),
        new(0.65f, 0.55f, 0.98f, 1f),
        new(0.98f, 0.75f, 0.35f, 1f),
        new(0.40f, 0.85f, 0.75f, 1f),
        new(0.95f, 0.55f, 0.80f, 1f),
    };

    private enum View
    {
        List,
        Category,
        Chat,
        Settings,
        Blocked,
        AddContact,
        Tour,
    }

    private enum Overlay
    {
        None,
        NewGroup,
        GroupInfo,
        Report,
        Confirm,
        MemberCard,
    }

    private readonly Func<string> _name;
    private readonly IAppCapabilities _caps;
    private readonly MessengerStore _store;
    private readonly MessengerSyncService _sync;
    private readonly MessengerCryptoService _crypto;
    private readonly AetherHubContext _hub;
    private readonly AetherLove.Services.Hangouts.HangoutStateService _hangoutState;
    private readonly AetherLove.Services.VenueShareContext _venueShare;
    private readonly AetherLove.Services.HangoutShareContext _hangoutShare;

    private IOsShell? _shell;
    private View _view = View.List;
    private Overlay _overlay = Overlay.None;
    private volatile string? _busyError;

    // -1 arms a content fade on the next view render; 0 (the default) keeps app-open and warm resume instant.
    private double _openFadeAt;
    private const double OpenFadeDuration = 0.20;

    // Decrypted plaintext per message id; group messages whose epoch key is still missing stay uncached.
    private readonly Dictionary<Guid, string?> _plain = new();
    private readonly Dictionary<(Guid ChatId, DateTimeOffset At), string?> _previews = new();
    private readonly Dictionary<Guid, (ISharedImmediateTexture? Tex, int Bytes)> _avatars = new();
    private int _decryptCacheVersion = -1;

    public MessengerApp(
        Func<string> name,
        IAppCapabilities caps,
        MessengerStore store,
        MessengerSyncService sync,
        MessengerCryptoService crypto,
        AetherHubContext hub,
        AetherLove.Services.Hangouts.HangoutStateService hangoutState,
        AetherLove.Services.VenueShareContext venueShare,
        AetherLove.Services.HangoutShareContext hangoutShare)
    {
        _name = name;
        _caps = caps;
        _store = store;
        _sync = sync;
        _crypto = crypto;
        _hub = hub;
        _hangoutState = hangoutState;
        _venueShare = venueShare;
        _hangoutShare = hangoutShare;
        _store.ImageRemoved += PurgeRemovedImage;
    }

    public string Id => "messenger";

    public string Name => _name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.CommentDots;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => _store.TotalUnread() + _store.IncomingRequestCount();

    public bool HasSurface => true;

    public bool RequiresConnection => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        // A stale connectivity banner from a past drop clears on re-entry; a live problem re-surfaces it.
        _busyError = null;
        if (ShouldAutoRunTour())
        {
            OpenTour();
        }
        // Returning straight into an open chat: restore the active-chat stamp before the sync so the
        // conversation backfill targets it, and clear the unread state that accrued while backgrounded.
        if (_view == View.Chat && _chatId != Guid.Empty)
        {
            _store.ActiveChatId = _chatId;
            _store.MarkReadLocal(_chatId);
            _shell?.DismissByTag(ChatTag(_chatId));
            var chatId = _chatId;
            var kind = _chatKind;
            RunHub(() => _hub.MarkMessengerReadAsync(chatId, kind));
        }
        _ = _sync.SyncAsync();
    }

    public void OnBackground()
    {
        if (_view == View.Chat)
        {
            StashDraft();
        }
        if (_categoriesLoaded)
        {
            FinalizeCategoryAnimations();
        }
        _store.ActiveChatId = null;
    }

    public void Draw(OsAppContext ctx)
    {
        _shell = ctx.Shell;
        while (_uiActions.TryDequeue(out var uiAction))
        {
            uiAction();
        }
        if (_store.Version != _decryptCacheVersion)
        {
            _decryptCacheVersion = _store.Version;
            // Cached decrypt failures get a retry whenever store state moved (late key history, fresh keys).
            foreach (var stale in _plain.Where(p => p.Value is null).Select(p => p.Key).ToArray())
            {
                _plain.Remove(stale);
            }
            foreach (var stale in _previews.Where(p => p.Value is null).Select(p => p.Key).ToArray())
            {
                _previews.Remove(stale);
            }
        }
        var contentTL = ImGui.GetCursorScreenPos();
        var contentSize = ImGui.GetContentRegionAvail();

        // Modal guard: while an image overlay is up, this claims all input over the content so the chat/list
        // behind it is inert (first-submitted wins in ImGui). The panels' controls live in a child window drawn
        // later, which sits on top of this; a click anywhere over the full-screen viewer closes it.
        if (_pendingImagePath is not null || _dataLimitsOpen)
        {
            ImGui.SetCursorScreenPos(contentTL);
            ImGui.InvisibleButton("##msgrModalGuard", contentSize);
            ImGui.SetCursorScreenPos(contentTL);
        }

        if (_view == View.Tour)
        {
            _store.ActiveChatId = null;
            DrawTour(ctx);
            return;
        }
        if (_view == View.Settings)
        {
            _store.ActiveChatId = null;
            DrawSettingsScreen(ctx);
        }
        else if (_view == View.AddContact)
        {
            _store.ActiveChatId = null;
            DrawAddContactScreen(ctx);
            DrawOpenFade(contentTL, contentSize);
        }
        else if (_view == View.Blocked)
        {
            _store.ActiveChatId = null;
            DrawBlockedScreen(ctx);
        }
        else if (_view == View.Chat && ResolveOpenChat() is { } open)
        {
            _store.ActiveChatId = _chatId;
            DrawChatView(ctx, open);
            DrawOpenFade(contentTL, contentSize);
        }
        else if (_view == View.Category)
        {
            _store.ActiveChatId = null;
            DrawCategoryView(ctx, _overlay != Overlay.None);
            DrawOpenFade(contentTL, contentSize);
        }
        else
        {
            _view = View.List;
            _store.ActiveChatId = null;
            DrawChatList(ctx, _overlay != Overlay.None);
            DrawOpenFade(contentTL, contentSize);
        }

        if (_overlay != Overlay.None)
        {
            DrawOverlay(ctx, contentTL, contentSize);
        }
        DrawCalendarEventPrompt();
        DrawHangoutPopup();
        DrawImageComposeOverlay(contentTL, contentSize);
        DrawImageReportOverlay(contentTL, contentSize);
        DrawDataLimitsOverlay(contentTL, contentSize);
        DrawImageViewer();
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.OpenChat)
        {
            if (OsIntents.TryGetId(intent, out var chatId))
            {
                OpenChatById(chatId);
            }
            else
            {
                CloseChat();
            }
            return;
        }
        // An AetherLove invite card was tapped: open the add flow prefilled with the inviter's code.
        if (intent.Type == OsIntents.MessengerAdd && OsIntents.TryGetCode(intent, out var code))
        {
            CloseChat();
            OpenAddContact();
            _addCode = code;
            return;
        }
        // A share-sheet item (venue/hangout/news): the chat list enters share mode.
        if (intent.Type == ShareIntent.Type && ShareIntent.TryUnwrap(intent, out var shared))
        {
            BeginShare(shared);
            return;
        }
        // File uploads held back pre-release: the Photos-app attach round trip is disabled (see MessengerApp.Images).
        // if (intent.Type == OsIntents.PhotoPicked && OsIntents.TryGetPath(intent, out var photoPath))
        // {
        //     OnPhotoPicked(photoPath);
        // }
    }

    /// <summary>Opens the chat for a contact or group id (notification taps, deep links).</summary>
    private void OpenChatById(Guid chatId)
    {
        if (_store.Contact(chatId) is not null)
        {
            OpenChat(chatId, MessengerChatKind.Direct);
        }
        else if (_store.Group(chatId) is not null)
        {
            OpenChat(chatId, MessengerChatKind.Group);
        }
    }

    private string? Decrypt(MessengerMessageDto m)
    {
        if (_plain.TryGetValue(m.Id, out var cached))
        {
            return cached;
        }
        string? text;
        var usedHistoryEra = false;
        if (m.Kind == MessengerChatKind.Direct)
        {
            var contact = _store.Contact(m.ChatId);
            if (contact?.PeerPublicKey is not { Length: > 0 } pub || !_crypto.HasAccountKeys)
            {
                AetherLove.UiHost.Log.Debug("[RESET] Messenger decrypt SKIPPED: msg {Id} (no peer key / no account keys yet).", m.Id);
                return null;
            }
            text = _crypto.DecryptDirect(pub, m.Ciphertext, m.Nonce);
            if (text is null && _store.PeerKeyHistory(m.ChatId) is { Length: > 1 } history)
            {
                // The peer reset their keys at some point; older messages open with an earlier public key.
                foreach (var era in history)
                {
                    if (era.UntilUtc is null || era.PublicKey is not { Length: > 0 })
                    {
                        continue;
                    }
                    text = _crypto.DecryptDirect(era.PublicKey, m.Ciphertext, m.Nonce);
                    if (text is not null)
                    {
                        usedHistoryEra = true;
                        break;
                    }
                }
            }
        }
        else
        {
            var key = _store.GroupKey(m.ChatId, m.KeyEpoch);
            if (key is null)
            {
                _ = _sync.EnsureGroupKeysAsync(m.ChatId);
                AetherLove.UiHost.Log.Debug("[RESET] Messenger decrypt SKIPPED: group msg {Id} epoch {Epoch} (key not held yet, refetching).", m.Id, m.KeyEpoch);
                return null;
            }
            text = _crypto.DecryptGroup(key, m.Ciphertext, m.Nonce);
        }
        AetherLove.UiHost.Log.Debug("[RESET] Messenger decrypt {Result}: msg {Id} kind={Kind} historyEra={Era}.", text is null ? "FAILED->undecryptable" : "OK", m.Id, m.Kind, usedHistoryEra);
        _plain[m.Id] = text;
        return text;
    }

    private string? DecryptPreview(Guid chatId, MessengerChatKind kind, DateTimeOffset? at, byte[]? ct, byte[]? nonce, int epoch)
    {
        if (at is null || ct is not { Length: > 0 } || nonce is not { Length: > 0 })
        {
            return null;
        }
        var key = (chatId, at.Value);
        if (_previews.TryGetValue(key, out var cached))
        {
            return cached;
        }
        string? text;
        if (kind == MessengerChatKind.Direct)
        {
            var contact = _store.Contact(chatId);
            if (contact?.PeerPublicKey is not { Length: > 0 } pub || !_crypto.HasAccountKeys)
            {
                return null;
            }
            text = _crypto.DecryptDirect(pub, ct, nonce);
        }
        else
        {
            var groupKey = _store.GroupKey(chatId, epoch);
            if (groupKey is null)
            {
                return null;
            }
            text = _crypto.DecryptGroup(groupKey, ct, nonce);
        }
        _previews[key] = text;
        return text;
    }

    /// <summary>The group's newest messages may sit on any epoch ≤ current; the preview tries the denormal
    /// against every held epoch (cheap: nearly always epoch == current).</summary>
    private string? DecryptGroupPreview(MessengerGroupDto group)
    {
        for (var epoch = group.KeyEpoch; epoch >= 1; epoch--)
        {
            if (_store.GroupKey(group.GroupId, epoch) is null)
            {
                continue;
            }
            var text = DecryptPreview(group.GroupId, MessengerChatKind.Group,
                group.LastMessageAtUtc, group.LastMessageCiphertext, group.LastMessageNonce, epoch);
            if (text is not null)
            {
                return text;
            }
        }
        return null;
    }

    private ImTextureID? AvatarTex(Guid id, byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }
        if (!_avatars.TryGetValue(id, out var entry) || entry.Bytes != bytes.Length)
        {
            var dir = System.IO.Path.Combine(_caps.Storage(Id).Directory, "AvatarCache");
            entry = (AetherLove.Services.AvatarDiskCache.Store(dir, id.ToString("N"), bytes), bytes.Length);
            _avatars[id] = entry;
        }
        return entry.Tex?.GetWrapOrDefault()?.Handle;
    }

    private void DrawAvatar(ImDrawListPtr dl, Guid id, string title, byte[]? bytes, bool isGroup,
        Vector2 center, float radius)
    {
        if (!isGroup)
        {
            var tex = AvatarTex(id, bytes);
            if (tex.HasValue)
            {
                dl.AddImageRounded(tex.Value, center - new Vector2(radius, radius), center + new Vector2(radius, radius),
                    Vector2.Zero, Vector2.One, 0xFFFFFFFFu, radius, ImDrawFlags.RoundCornersAll);
                return;
            }
            dl.AddCircleFilled(center, radius, AuthorColor(id));
            var letter = title.Length > 0 ? char.ToUpperInvariant(title[0]).ToString() : "?";
            var fs = radius * 1.05f;
            var sz = ImGui.CalcTextSize(letter) * (fs / ImGui.GetFontSize());
            dl.AddText(ImGui.GetFont(), fs, center - sz * 0.5f, 0xFFFFFFFFu, letter);
            return;
        }
        var groupTex = AvatarTex(id, bytes);
        if (groupTex.HasValue)
        {
            dl.AddImageRounded(groupTex.Value, center - new Vector2(radius, radius), center + new Vector2(radius, radius),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, radius, ImDrawFlags.RoundCornersAll);
            return;
        }
        dl.AddCircleFilled(center, radius, Col(AetherLove.Services.ThemeService.Current.AccentDark));
        IconCentered(dl, FontAwesomeIcon.Users, radius * 0.95f, center, 0xFFFFFFFFu);
    }

    private static string ChatTag(Guid chatId) => $"msgr:chat:{chatId:N}";

    private void RunHub(Func<Task> action)
    {
        _busyError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _busyError = FriendlyHubError(ex);
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] hub call failed.");
            }
        });
    }

    /// <summary>Connection-layer failures (drops, reconnects) render as the localized connectivity notice
    /// instead of raw SignalR exception text; real hub errors localize normally.</summary>
    private string FriendlyHubError(Exception ex)
    {
        if (!_hub.IsConnected || ex is InvalidOperationException)
        {
            return Loc.T("chat.connectivity_error");
        }
        return AetherLove.Services.HubErrorText.Localize(ex);
    }

    private static string FormatListTime(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime();
        return local.Date == DateTime.Now.Date
            ? local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : local.ToString("dd.MM", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static uint AuthorColor(Guid accountId)
    {
        var hash = accountId.GetHashCode();
        var index = ((hash % AuthorPalette.Length) + AuthorPalette.Length) % AuthorPalette.Length;
        return ImGui.ColorConvertFloat4ToU32(AuthorPalette[index]);
    }

    private static void IconCentered(ImDrawListPtr dl, FontAwesomeIcon icon, float px, Vector2 center, uint color)
    {
        var glyph = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(glyph) * (px / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), px, center - size * 0.5f, color, glyph);
        ImGui.PopFont();
    }

    private static uint Col(Vector4 color)
    {
        return ImGui.ColorConvertFloat4ToU32(color);
    }

    private readonly Dictionary<string, (double Start, int Seen)> _panelFades = new();

    /// <summary>Eased 0..1 entrance progress for a modal keyed by <paramref name="key"/>; restarts whenever the
    /// key stops drawing for a frame, and snaps to 1 under reduce-motion.</summary>
    private float PanelFade(string key)
    {
        if (AetherLove.Services.AccessibilityService.ReduceMotion)
        {
            return 1f;
        }
        var frame = ImGui.GetFrameCount();
        var now = ImGui.GetTime();
        var start = _panelFades.TryGetValue(key, out var prev) && frame - prev.Seen <= 1 ? prev.Start : now;
        _panelFades[key] = (start, frame);
        var raw = (float)Math.Clamp((now - start) / 0.16, 0.0, 1.0);
        return 1f - MathF.Pow(1f - raw, 3f);
    }

    /// <summary>A brief background-cover fade over the whole content the first frames after a forward (into a
    /// chat/category) or back (to the overview) navigation. Drawn in a last-submitted, input-transparent child so
    /// it layers above the list/message scroll children; a plain parent draw-list rect renders beneath them and
    /// would only tint the header chrome.</summary>
    private void DrawOpenFade(Vector2 contentTL, Vector2 contentSize)
    {
        if (AetherLove.Services.AccessibilityService.ReduceMotion)
        {
            return;
        }
        if (_openFadeAt < 0)
        {
            _openFadeAt = ImGui.GetTime();
        }
        var t = (ImGui.GetTime() - _openFadeAt) / OpenFadeDuration;
        if (t >= 1.0)
        {
            return;
        }
        var a = (uint)(Math.Clamp(1.0 - t, 0.0, 1.0) * 255.0);
        var bg = ImGui.GetColorU32(ImGuiCol.WindowBg) & 0x00FFFFFFu;

        var cursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(contentTL);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.BeginChild("##msgrOpenFade", contentSize, false,
                ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs
                | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.GetWindowDrawList().AddRectFilled(contentTL, contentTL + contentSize, bg | (a << 24));
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.SetCursorScreenPos(cursor);
    }
}
