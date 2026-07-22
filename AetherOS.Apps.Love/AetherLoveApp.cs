using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using AetherLove;
using AetherLove.Screens;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Chat;
using AetherLove.Services.Crypto;
using AetherLove.Services.Hub;
using AetherLove.Services.Patreon;
using AetherLove.Services.Signal;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Profile;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Love;

/// <summary>The AetherLove dating app: a self-contained surface app hosting the whole dating flow (onboarding,
/// deck, match reveal, chat, profiles, settings). Navigates internally via <see cref="LoveRouter"/>; the OS shell
/// only ever sees <c>Screen.App</c>. Plugin-only game glue (selfie capture, Pulse) comes in via <see cref="ILoveHost"/>.</summary>
public sealed partial class AetherLoveApp : IAetherApp, IAppSettings
{
    private readonly AetherHubContext _hub;
    private readonly NotificationCenter _notifications;
    private readonly OwnAvatarCache _ownAvatar;
    private readonly SessionBootstrapper _bootstrap;
    private readonly ChatCacheStore _chatCache;

    private readonly LoveRouter _router = new(LoveView.Deck);
    private readonly LoveShell _shell = new();

    private OnboardingScreen _onboarding;
    private Guid _onboardingProfileId;
    private readonly RateLimitModal _rateLimit;
    private readonly SaveErrorModal _saveErr;
    private readonly ImageRequirementsModal _imageReq;
    private readonly DeckScreen _deck;
    private readonly MatchScreen _match;
    private readonly ChatListScreen _chatList;
    private readonly ChatCategoryScreen _chatCategory;
    private readonly ChatScreen _chat;
    private readonly ProfileScreen _profile;
    private readonly SettingsScreen _settings;
    private readonly MyProfileScreen _myProfile;
    private readonly BlockedScreen _blocked;
    private readonly EncryptionVerificationScreen _encVerify;
    private readonly SupporterThanksScene _supporterThanks;

    private readonly ILoveHost _host;
    private readonly IAppCapabilities _caps;
    private readonly VenueShareContext _venueShare;
    private readonly HangoutShareContext _hangoutShare;
    private readonly NewsShareContext _newsShare;
    private readonly CalendarShareContext _calendarShare;
    private readonly ShareMatchPickerView _sharePicker;
    private readonly ShareProfileChoiceView _shareProfileChoice;
    private readonly ProfilePickerScreen _profilePicker;
    private readonly KeyStorageService _keys;
    private readonly CryptoService _crypto;
    private LoveView? _lastView;
    private bool _suppressEntryOnce;
    private bool _entered;
    private Guid _enteredProfileId;

    private static readonly string[] AcceptedTypes = [ShareTypes.Venue, ShareTypes.Hangout, ShareTypes.News, ShareTypes.CalendarEvent];

    public AetherLoveApp(
        ILoveHost host,
        IAppCapabilities caps,
        AetherHubContext hub,
        PendingMatchContext pendingMatch,
        NotificationCenter notifications,
        OwnAvatarCache ownAvatar,
        FlairCatalog flairCatalog,
        ChatEventBus chatEvents,
        CryptoService crypto,
        KeyStorageService keys,
        ChatCategoryStore categories,
        ChatSyncService chatSync,
        ChatCacheStore chatCache,
        AetherSignalService signal,
        TokenService tokens,
        SessionBootstrapper bootstrap,
        VenueShareContext venueShare,
        HangoutShareContext hangoutShare,
        NewsShareContext newsShare,
        CalendarShareContext calendarShare,
        PatreonLinkFlow patreon,
        SiblingBadgeStore siblingBadges,
        AetherLove.Services.Messenger.MessengerStore messengerStore)
    {
        _host = host;
        _caps = caps;
        _hub = hub;
        _notifications = notifications;
        _ownAvatar = ownAvatar;
        _bootstrap = bootstrap;
        _chatCache = chatCache;
        _venueShare = venueShare;
        _hangoutShare = hangoutShare;
        _newsShare = newsShare;
        _calendarShare = calendarShare;
        _keys = keys;
        _crypto = crypto;
        _sharePicker = new ShareMatchPickerView(chatCache);
        _shareProfileChoice = new ShareProfileChoiceView(hub);

        var hangoutOpener = new HangoutOpener(_shell, hangoutShare);
        var imageReq = new ImageRequirementsModal();
        var rateLimit = new RateLimitModal();
        var saveErr = new SaveErrorModal();
        _imageReq = imageReq;
        _rateLimit = rateLimit;
        _saveErr = saveErr;

        _settings = new SettingsScreen(_router, hub, signal, tokens, chatCache, _shell, bootstrap);
        _profile = new ProfileScreen(_router, hub, flairCatalog, bootstrap, _settings, _shell);
        _deck = new DeckScreen(_router, _profile, hub, pendingMatch, notifications, ownAvatar, host, flairCatalog, _settings);

        var effects = new IMatchEffect[]
        {
            new MatchClassicScreen(_router),
            new MatchCosmicScreen(_router),
            new MatchSynthwaveScreen(_router),
            new MatchAuroraScreen(_router),
            new MatchKaleidoscopeScreen(_router),
            new MatchSupernovaScreen(_router),
            new MatchVortexScreen(_router),
            new MatchPortalRiftScreen(_router),
            new MatchElectricStormScreen(_router),
            new MatchBubbleMergeScreen(_router),
            new MatchDnaHelixScreen(_router),
            new MatchFireworkScreen(_router),
            new MatchVinylScreen(_router),
            new MatchArcadeScreen(_router),
            new MatchConstellationScreen(_router),
            new MatchSlotMachineScreen(_router),
            new MatchTarotScreen(_router),
            new MatchLavaLampScreen(_router),
            new MatchSkyLanternsScreen(_router),
            new MatchTreasureChestScreen(_router),
        };
        _match = new MatchScreen(effects, ownAvatar, pendingMatch, bootstrap);

        _chatList = new ChatListScreen(_router, hub, chatEvents, crypto, keys, notifications, categories, chatSync);
        _chatCategory = new ChatCategoryScreen(_chatList);
        _encVerify = new EncryptionVerificationScreen(_router, keys);
        _chat = new ChatScreen(_shell, _router, _chatList, _profile, _encVerify, hub, crypto, keys, chatEvents,
            notifications, chatSync, _settings, venueShare, hangoutShare, newsShare, calendarShare, hangoutOpener,
            messengerStore);
        _myProfile = new MyProfileScreen(_shell, _profile, hub, ownAvatar, rateLimit, saveErr, imageReq, caps,
            bootstrap);
        _blocked = new BlockedScreen(_router, hub);
        _onboarding = new OnboardingScreen(_router, hub, bootstrap, rateLimit, saveErr, imageReq, caps, _shell);
        _supporterThanks = new SupporterThanksScene(patreon);
        _profilePicker = new ProfilePickerScreen(_router, hub, bootstrap, keys, crypto, _settings);
        _settings.OpenProfilePicker = () =>
        {
            _profilePicker.OpenedFromSettings = true;
            _router.Navigate(LoveView.ProfilePicker);
        };

        notifications.ProfileCachesInvalidated += InvalidateProfileCaches;
        _siblingBadges = siblingBadges;
        siblingBadges.Changed += () =>
        {
            var active = UiHost.Configuration.Auth.ActiveProfileId ?? Guid.Empty;
            var (sm, su) = siblingBadges.TotalsExcluding(active);
            UiHost.Log.Debug("[SIB] tile recompute: active {Active:N} activeBadge={ActiveBadge} + siblings(matches={SibMatches}, unread={SibUnread}) = tile {Tile}.", active, _notifications.TotalBadge, sm, su, _notifications.TotalBadge + sm + su);
            if (_router.Current == LoveView.ProfilePicker)
            {
                _profilePicker.Refetch();
            }
        };
    }

    private readonly SiblingBadgeStore _siblingBadges;

    public string Id => "aetherlove";
    public string Name => "AetherLove";
    public FontAwesomeIcon Icon => FontAwesomeIcon.Heart;
    public Vector4 TileTop => new(0.97f, 0.42f, 0.58f, 1f);
    public Vector4 TileBottom => new(0.68f, 0.13f, 0.36f, 1f);
    /// <summary>Account-wide unread total: the active profile's live counts plus every inactive sibling's,
    /// so the tile reflects the whole account while the picker splits it per profile.</summary>
    public int Badge
    {
        get
        {
            var (siblingMatches, siblingUnread) = _siblingBadges.TotalsExcluding(
                UiHost.Configuration.Auth.ActiveProfileId ?? Guid.Empty);
            return _notifications.TotalBadge + siblingMatches + siblingUnread;
        }
    }
    public bool HasSurface => true;

    public bool RequiresConnection => true;

    public IReadOnlyList<string> AcceptedShareTypes => AcceptedTypes;

    /// <summary>Onboarding and per-peer key verification are non-interruptible; hide the OS chrome while in them.</summary>
    public bool LocksShell => _router.Current is LoveView.Onboarding or LoveView.EncryptionVerification;

    public void Open()
    {
    }

    public void OnBackground()
    {
        if (IsDeckEngaged())
        {
            _deck.MarkDeckLeft();
        }
        if (_router.Current == LoveView.Chat)
        {
            _chat.OnAppBackground();
        }
    }

    public void OnIntent(OsIntent intent)
    {
        switch (intent.Type)
        {
            case OsIntents.CameraCaptured:
                if (OsIntents.TryGetCameraShot(intent, out var shotPath, out var shotCrop))
                {
                    _shell.DeliverCameraShot(shotPath, shotCrop);
                }
                break;
            case OsIntents.OpenDeck:
                _router.Navigate(LoveView.Deck);
                break;
            case OsIntents.OpenMessages:
                _router.Navigate(LoveView.ChatList);
                break;
            case OsIntents.OpenSettings:
                _router.Navigate(LoveView.Settings);
                break;
            case OsIntents.OpenChat:
                // With a peer id select that chat first; without one (a "back to chat" affordance) just
                // return to the chat that is already selected.
                if (OsIntents.TryGetId(intent, out var peerId))
                {
                    if (_chatCache.GetMatches().FirstOrDefault(m => m.PeerProfileId == peerId) is { } match)
                    {
                        _chatList.SelectPeer(match);
                        _router.Navigate(LoveView.Chat);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    _router.Navigate(LoveView.Chat);
                }
                break;
            case ShareIntent.Type:
                // Someone shared a venue/hangout/news to us: land on the chat list and open the share flow
                // (profile choice first on a multi-profile account, then the match picker).
                if (ShareIntent.TryUnwrap(intent, out var shared))
                {
                    BeginShare(shared);
                    _router.Navigate(LoveView.ChatList);
                }
                else
                {
                    return;
                }
                break;
            default:
                return;
        }
        _suppressEntryOnce = true;
    }

    /// <summary>Entry of the share flow: a single-profile account goes straight to the match picker; a
    /// multi-profile account first chooses WHICH profile shares.</summary>
    private void BeginShare(ShareItem item)
    {
        if (_bootstrap.LastAccount is { ProfileCount: > 1 })
        {
            _shareProfileChoice.Open(item, OnShareProfileChosen);
        }
        else
        {
            _sharePicker.Open(match => CompleteShare(item, match));
        }
    }

    private void OnShareProfileChosen(ShareItem item, ProfileSummaryDto profile)
    {
        if (profile.ProfileId == (_bootstrap.LastConnection?.ProfileId ?? Guid.Empty))
        {
            _sharePicker.Open(match => CompleteShare(item, match));
            return;
        }
        // Sharing as a sibling: switch to it, pick from its matches, deliver headlessly, switch back.
        var returnTo = _bootstrap.LastConnection?.ProfileId;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var result = await _bootstrap.SwitchProfileAsync(profile.ProfileId).ConfigureAwait(false);
                if (result is not (SessionBootstrapResult.SignedInActive or SessionBootstrapResult.SignedInOnboarding))
                {
                    await SwitchBackAsync(returnTo).ConfigureAwait(false);
                    return;
                }
                _sharePicker.Open(match => CompleteSiblingShare(item, match, returnTo));
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[AetherLoveApp] Share profile switch failed.");
                await SwitchBackAsync(returnTo).ConfigureAwait(false);
            }
        });
    }

    /// <summary>Delivers the picked share directly over the hub as the (temporarily active) sibling profile:
    /// composes the card token, encrypts it with the sibling's keys, sends it, then restores the previously
    /// active profile. The user never leaves the profile they were using.</summary>
    private void CompleteSiblingShare(ShareItem item, MatchSummaryDto match, Guid? returnTo)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                if (!await DeliverShareAsync(item, match).ConfigureAwait(false))
                {
                    UiHost.Log.Warning("[AetherLoveApp] Sibling share delivery failed (missing keys or bad payload).");
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[AetherLoveApp] Sibling share delivery failed.");
            }
            finally
            {
                await SwitchBackAsync(returnTo).ConfigureAwait(false);
            }
        });
    }

    private async System.Threading.Tasks.Task SwitchBackAsync(Guid? returnTo)
    {
        if (returnTo is not { } original || original == (_bootstrap.LastConnection?.ProfileId ?? Guid.Empty))
        {
            return;
        }
        try
        {
            await _bootstrap.SwitchProfileAsync(original).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[AetherLoveApp] Switch-back after share failed; staying on the sibling profile.");
        }
    }

    private async System.Threading.Tasks.Task<bool> DeliverShareAsync(ShareItem item, MatchSummaryDto match)
    {
        var token = item.Type switch
        {
            ShareTypes.Venue when Guid.TryParse(item.RefId, out var venueId) => VenueShare.Compose(venueId),
            ShareTypes.Hangout when Guid.TryParse(item.RefId, out var hangoutId) => HangoutShare.Compose(hangoutId),
            ShareTypes.News when Guid.TryParse(item.RefId, out var newsId) => NewsShare.Compose(newsId),
            ShareTypes.CalendarEvent => CalendarEventShare.TryComposeFromShareItem(item),
            _ => null,
        };
        var myPriv = _keys.GetPrivateKey();
        var myPub = _keys.GetPublicKey();
        if (token is null || myPriv is null || myPub is null || match.PeerPublicKey is not { Length: > 0 } peerPub)
        {
            return false;
        }
        var sharedSecret = _crypto.DeriveSharedSecret(myPriv, peerPub);
        var messageKey = _crypto.DeriveMessageKey(sharedSecret, CryptoService.DeriveConversationSalt(myPub, peerPub));
        var (ciphertext, nonce) = _crypto.Encrypt(messageKey, System.Text.Encoding.UTF8.GetBytes(token));
        await _hub.SendMessageAsync(new SendMessageRequest(match.PeerProfileId, ciphertext, nonce))
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>Stages a picked share into the right chat context and jumps to that match's chat, where
    /// <see cref="ChatScreen"/> composes and auto-sends the card token.</summary>
    private void CompleteShare(ShareItem item, MatchSummaryDto match)
    {
        switch (item.Type)
        {
            case ShareTypes.Venue when Guid.TryParse(item.RefId, out var venueId):
                _venueShare.PendingShareVenueId = venueId;
                break;
            case ShareTypes.Hangout when Guid.TryParse(item.RefId, out var hangoutId):
                _hangoutShare.PendingShareHangoutId = hangoutId;
                break;
            case ShareTypes.News when Guid.TryParse(item.RefId, out var newsId):
                _newsShare.PendingShareNewsId = newsId;
                break;
            case ShareTypes.CalendarEvent when CalendarEventShare.TryComposeFromShareItem(item) is { } calToken:
                _calendarShare.PendingShareToken = calToken;
                break;
            default:
                return;
        }
        _chatList.SelectPeer(match);
        _router.Navigate(LoveView.Chat);
        _suppressEntryOnce = true;
    }

    public void OnForeground()
    {
        // An intent that just navigated (open.chat, a share hand-off) owns the entry view for this open.
        if (_suppressEntryOnce)
        {
            return;
        }
        // A banned active profile always lands on the picker so the user can switch away; never warm-resume into
        // the banned profile's deck (which would just spew ban errors on every hub call).
        if (_bootstrap.LastConnection?.Status == ProfileLifecycle.Banned)
        {
            _profilePicker.OpenedFromSettings = false;
            _router.Navigate(LoveView.ProfilePicker);
            return;
        }
        // Warm resume: the app was already entered this session as this profile, so going home and coming
        // back returns to the exact view the user left (never the app start).
        if (_entered && _enteredProfileId == (_bootstrap.LastConnection?.ProfileId ?? Guid.Empty) && _lastView is not null)
        {
            if (_router.Current == LoveView.Chat)
            {
                _chat.OnAppForeground();
            }
            return;
        }
        ResolveEntryView();
    }

    public void Draw(OsAppContext ctx)
    {
        _shell.Shell = ctx.Shell;
        _suppressEntryOnce = false;
        _entered = true;
        _enteredProfileId = _bootstrap.LastConnection?.ProfileId ?? _enteredProfileId;

        DriveLifecycle();

        if (_router.Current != LoveView.Deck)
        {
            _deck.MaybeBackgroundRefresh();
        }

        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var isMain = IsMainView(_router.Current);

        if (isMain)
        {
            ImGui.BeginChild("##loveContent", new Vector2(avail.X, avail.Y - Px(NavBarHeight)), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            DrawCurrentView();
            ImGui.EndChild();
            DrawBottomNav(origin, avail);
        }
        else
        {
            DrawCurrentView();
        }

        _supporterThanks.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        _shareProfileChoice.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        _sharePicker.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());
    }

    /// <summary>Renders the OS Settings app's AetherLove page via the same internal settings screen.</summary>
    public void DrawSettings(OsAppContext ctx, Action? onBack)
    {
        _shell.Shell = ctx.Shell;
        _settings.Draw(onBack);
    }

    private void DrawCurrentView()
    {
        switch (_router.Current)
        {
            case LoveView.ProfilePicker:
                _profilePicker.Draw();
                break;
            case LoveView.Onboarding:
                _onboarding.Draw();
                break;
            case LoveView.Deck:
                _deck.Draw();
                break;
            case LoveView.Match:
                _match.Draw();
                break;
            case LoveView.ChatList:
                _chatList.Draw();
                break;
            case LoveView.ChatCategory:
                _chatCategory.Draw();
                break;
            case LoveView.Chat:
                _chat.Draw();
                break;
            case LoveView.Profile:
                _profile.Draw();
                break;
            case LoveView.Settings:
                _settings.Draw();
                break;
            case LoveView.MyProfile:
                _myProfile.Draw();
                break;
            case LoveView.Blocked:
                _blocked.Draw();
                break;
            case LoveView.EncryptionVerification:
                _encVerify.Draw();
                break;
        }
    }

    /// <summary>Drives OnShow/OnHide across internal view changes, mirroring the phone window's old router hook, and
    /// drops the deck's pinned card when leaving its browse flow.</summary>
    private void DriveLifecycle()
    {
        if (!_router.NavigationOccurred)
        {
            return;
        }
        _router.NavigationOccurred = false;

        var view = _router.Current;
        if (_lastView == view)
        {
            return;
        }

        if (_lastView is { } old)
        {
            OnViewHidden(old);
            if (IsDeckEngaged(old) && !IsDeckEngaged(view))
            {
                _deck.MarkDeckLeft();
            }
        }
        _lastView = view;
        OnViewShown(view);
    }

    private void OnViewShown(LoveView view)
    {
        switch (view)
        {
            case LoveView.ProfilePicker:
                _profilePicker.OnShow();
                break;
            case LoveView.Onboarding:
                EnsureOnboardingForActiveProfile();
                _onboarding.OnShow();
                break;
            case LoveView.Deck:
                _deck.OnShow();
                break;
            case LoveView.Match:
                _match.OnShow();
                break;
            case LoveView.ChatList:
                _chatList.OnShow();
                MarkChatListSeen();
                break;
            case LoveView.ChatCategory:
                _chatCategory.OnShow();
                break;
            case LoveView.Chat:
                _chat.OnShow();
                break;
            case LoveView.Profile:
                _profile.OnShow();
                break;
            case LoveView.Settings:
                _settings.OnShow();
                break;
            case LoveView.MyProfile:
                _myProfile.OnShow();
                break;
            case LoveView.Blocked:
                _blocked.OnShow();
                break;
            case LoveView.EncryptionVerification:
                _encVerify.OnShow();
                break;
        }
    }

    private void OnViewHidden(LoveView view)
    {
        switch (view)
        {
            case LoveView.Onboarding:
                // The wizard just uploaded (or changed) photo slot 0; pull it so the nav avatar shows it.
                _ownAvatar.Refresh();
                break;
            case LoveView.ChatList:
                _chatList.OnHide();
                break;
            case LoveView.ChatCategory:
                _chatCategory.OnHide();
                break;
            case LoveView.Chat:
                _chat.OnHide();
                break;
            case LoveView.MyProfile:
                _myProfile.OnHide();
                break;
        }
    }

    /// <summary>Cold entry: lands on the profile picker ("select profile"), except a brand-new account whose
    /// single profile is still mid-first-setup, which goes straight into the dating onboarding wizard. Skipped
    /// when the open was driven by an intent (which already picked the view). Clears <see cref="_lastView"/> so
    /// the entry view always re-runs its OnShow.</summary>
    private void ResolveEntryView()
    {
        _lastView = null;
        _entered = true;
        _enteredProfileId = _bootstrap.LastConnection?.ProfileId ?? Guid.Empty;
        if (_bootstrap.LastConnection?.Status == ProfileLifecycle.Onboarding
            && _bootstrap.LastAccount is null or { ProfileCount: <= 1 })
        {
            _router.Navigate(LoveView.Onboarding);
            return;
        }
        _profilePicker.OpenedFromSettings = false;
        _router.Navigate(LoveView.ProfilePicker);
    }

    /// <summary>The wizard keeps its fields in memory across opens (so a resumed onboarding continues where it
    /// left off), which is wrong the moment a DIFFERENT profile enters it (delete then create-new in the same
    /// session). A profile change swaps in a completely fresh wizard instance instead of hand-resetting fields.</summary>
    private void EnsureOnboardingForActiveProfile()
    {
        var pid = _bootstrap.LastConnection?.ProfileId ?? Guid.Empty;
        if (pid == _onboardingProfileId)
        {
            return;
        }
        _onboardingProfileId = pid;
        _onboarding = new OnboardingScreen(_router, _hub, _bootstrap, _rateLimit, _saveErr, _imageReq, _caps, _shell);
    }

    /// <summary>Runs when the account caches change (a (re)connect or a moderation edit), signalled over the
    /// notification center from the connection service.</summary>
    private void InvalidateProfileCaches()
    {
        _profile.InvalidateMyProfileCache();
        _myProfile.InvalidateEditCache();
    }

    private bool IsDeckEngaged() => IsDeckEngaged(_router.Current);

    private bool IsDeckEngaged(LoveView view) =>
        view == LoveView.Deck || (view == LoveView.Profile && _profile.Source == ProfileSource.Deck);

    private static bool IsMainView(LoveView v) => v is LoveView.Deck or LoveView.ChatList or LoveView.ChatCategory
        or LoveView.Chat or LoveView.MyProfile or LoveView.Blocked or LoveView.Settings;
}
