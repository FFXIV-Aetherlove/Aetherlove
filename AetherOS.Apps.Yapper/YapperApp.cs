using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Shared.Yapper;
using AetherOS.Apps.Yapper.Screens;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Yapper;

/// <summary>Yapper: the microblogging + gallery social app. Bottom nav: Home, Explore, Post (center
/// action), Notifications, Profile. A missing Yapper profile routes into onboarding first.</summary>
public sealed class YapperApp : IAetherApp, IAppSettings
{
    internal enum View { Home, Explore, Profile, Onboarding, Tour, Compose, Detail, Bookmarks, PeerProfile, TagFeed, Settings, Messages, DmChat, FollowList }

    private const int BackStackCap = 16;

    private readonly Func<string> _name;
    private readonly Func<bool> _available;
    private readonly IYapperHost _host;
    private readonly IAppStorage _storage;
    private readonly YapperStore _store = new();
    private readonly YapperMediaCache _mediaCache;
    private readonly MediaViewer _mediaViewer;
    private readonly YapCard _yapCard;
    private readonly HomeScreen _home;
    private readonly ExploreScreen _explore;
    private readonly NotificationsScreen _notifications;
    private readonly ProfileScreen _profile;
    private readonly OnboardingScreen _onboarding;
    private readonly TourScreen _tour;
    private readonly ComposeScreen _compose;
    private readonly YapDetailScreen _detail;
    private readonly BookmarksScreen _bookmarks;
    private readonly PeerProfileScreen _peerProfile;
    private readonly FollowListScreen _followList;
    private readonly ImageSourceSheet _imageSheet = new();
    private Action<string>? _pendingPhotoPick;
    private readonly AetherLove.Services.VenueShareContext _venueShare;
    private readonly AetherLove.Services.LevemeteShareContext _levemeteShare;

    /// <summary>An embed card tap: hand off to the owning app with a back leg to Yapper.</summary>
    private void OpenEmbed(YapEmbedDto embed)
    {
        if (embed.Kind == YapEmbedKind.Venue)
        {
            _venueShare.PendingOpenVenueId = embed.Id;
            _venueShare.PendingOpenReturnApp = Id;
            _shell?.OpenApp("places");
        }
        else if (embed.Kind == YapEmbedKind.LevemeteAd)
        {
            _levemeteShare.PendingOpenLevemeteId = embed.Id;
            _levemeteShare.PendingOpenReturnApp = Id;
            _shell?.OpenApp("levemetes");
        }
    }
    private readonly TagFeedScreen _tagFeed;
    private readonly SettingsScreen _settings;
    private readonly ReportOverlay _reportOverlay;
    private readonly DmStore _dms = new();
    private readonly MessagesScreen _messages;
    private readonly DmChatScreen _dmChat;
    private bool _dmKeysEnsured;

    private readonly List<View> _backStack = [];
    private readonly AetherLove.UI.EntranceAnimation _entrance = new();
    private View _view = View.Home;
    private YapperMyProfileDto? _me;
    private bool _meLoading;
    private bool _meLoaded;
    private bool _tourSeen;
    private bool _tourSeenLoaded;

    public YapperApp(Func<string> name, Func<bool> available, IYapperHost host, IAppCapabilities caps,
        AetherLove.Services.VenueShareContext venueShare, AetherLove.Services.LevemeteShareContext levemeteShare)
    {
        _name = name;
        _available = available;
        _host = host;
        _venueShare = venueShare;
        _levemeteShare = levemeteShare;
        _storage = caps.Storage("yapper");
        _mediaCache = new YapperMediaCache(host, System.IO.Path.Combine(_storage.Directory, "MediaCache"));
        _mediaViewer = new MediaViewer(_mediaCache);
        _yapCard = new YapCard(host, _store, _mediaCache,
            () => _me?.ProfileId,
            OpenDetail,
            dto => OpenCompose(ComposeScreen.Mode.Reply, dto),
            dto => OpenCompose(ComposeScreen.Mode.Quote, dto),
            dto => OpenCompose(ComposeScreen.Mode.Edit, dto),
            _ => Back(),
            OpenPeerProfile,
            _mediaViewer.Open,
            dto => _reportOverlay.OpenForYap(dto),
            OpenEmbed,
            () => _me?.PinnedYapId,
            SetPinned);
        _reportOverlay = new ReportOverlay(host);
        _home = new HomeScreen(_store, c => host.GetFollowingFeedAsync(c), CreateForYouPane, MarkSeen,
            c => _notifications.Draw(c), () => _me?.UnreadNotifications ?? 0, () => _notifications.OnShow());
        _explore = new ExploreScreen(host, _mediaCache, () => _me?.ProfileId, OpenTag, OpenPeerProfile, OnFollowChanged);
        _notifications = new NotificationsScreen(host, _mediaCache, OpenYapById, OpenPeerProfile, OnInboxRead);
        host.NotificationReceived += OnNotificationPush;
        _profile = new ProfileScreen(host, _store, _mediaCache, () => _me, RefreshMe,
            () => Navigate(View.Bookmarks), () => Navigate(View.Settings),
            followers => OpenFollowList(_me?.ProfileId, followers), _imageSheet, RequestPhotoPick, OpenDetail);
        _followList = new FollowListScreen(host, _mediaCache, () => _me?.ProfileId, Back, OpenPeerProfile, OnFollowChanged);
        _onboarding = new OnboardingScreen(host, OnOnboarded);
        _tour = new TourScreen(FinishTour);
        _compose = new ComposeScreen(host, _store, OnPosted, Back, _imageSheet, RequestPhotoPick);
        _detail = new YapDetailScreen(host, _store, _yapCard, Back, dto => OpenCompose(ComposeScreen.Mode.Reply, dto));
        _bookmarks = new BookmarksScreen(_store, c => host.GetBookmarksAsync(c), Back);
        _peerProfile = new PeerProfileScreen(host, _store, _mediaCache, Back, OpenDmChat,
            (profileId, handle) => _reportOverlay.OpenForProfile(profileId, handle),
            (profileId, followers) => OpenFollowList(profileId, followers), OnFollowChanged, OpenDetail);
        _tagFeed = new TagFeedScreen(host, _store, Back);
        _settings = new SettingsScreen(host, _mediaCache, () => _me, me => _me = me, Back);
        _messages = new MessagesScreen(host, _dms, _mediaCache, OpenDmChat);
        _dmChat = new DmChatScreen(host, _dms, _mediaCache, () => _me?.ProfileId, Back, OpenPeerProfile);
        host.DmReceived += OnDmPush;
        host.DmRead += payload => _dms.ApplyPeerRead(payload.PeerProfileId, payload.MessageIds, payload.ReadAtUtc);
        host.DmReaction += payload => _dms.ApplyReaction(payload.MessageId, payload.ProfileId, payload.Token, payload.Added);
        host.DmPinned += payload => _dms.ApplyPin(payload.MessageId, payload.PinnedAtUtc);
        host.DmDeleted += payload => _dms.ApplyDeleted(payload.MessageId);
    }

    internal void OpenDmChat(Guid peerProfileId)
    {
        _dmChat.Open(peerProfileId);
        Navigate(View.DmChat);
    }

    private void OnDmPush(YapperDmPushDto payload)
    {
        var peerId = payload.Sender.ProfileId;
        var chatOpen = _view == View.DmChat && _dmChat.PeerId == peerId;
        _dms.Append(peerId, payload.Message, payload.Sender, countUnread: !chatOpen);
        if (chatOpen)
        {
            _dmChat.NotifyIncoming();
            return;
        }
        _shell?.PostNotification(Id, Name,
            string.Format(AetherLove.Services.Localization.Loc.T("os.yapper_dm_notif"), payload.Sender.DisplayName),
            () =>
            {
                _shell?.OpenApp(Id);
                OpenDmChat(peerId);
            },
            $"yap:dm:{peerId:N}");
    }

    /// <summary>For You deals a fresh ranked hand per refresh (no keyset), filtering what this session
    /// already reported as seen.</summary>
    private FeedPane? CreateForYouPane() =>
        new(_store, async _ => new YapPageDto(
            await _host.GetForYouFeedAsync(RecentSeen()).ConfigureAwait(false), null), MarkSeen);

    private Guid[] RecentSeen()
    {
        lock (_seenPending)
        {
            return _seenReported.Count == 0 ? [] : _seenReported.TakeLast(256).ToArray();
        }
    }

    internal void OpenPeerProfile(Guid profileId)
    {
        if (profileId == _me?.ProfileId)
        {
            Navigate(View.Profile);
            return;
        }
        _peerProfile.Open(profileId);
        Navigate(View.PeerProfile);
    }

    internal void OpenTag(string tag)
    {
        _tagFeed.Open(tag);
        Navigate(View.TagFeed);
    }

    internal void OpenYapById(Guid yapId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _host.GetYapAsync(yapId).ConfigureAwait(false);
                _store.Upsert(dto);
                OpenDetail(dto);
            }
            catch (Exception)
            {
            }
        });
    }

    private const string NotifTag = "yap:notif";
    private IOsShell? _shell;

    private void OnInboxRead()
    {
        if (_me is { } me)
        {
            _me = me with { UnreadNotifications = 0 };
        }
        _shell?.DismissByTag(NotifTag);
    }

    private void OnNotificationPush(YapperNotificationPushDto payload)
    {
        if (_view == View.Home && _home.NotificationsTabActive)
        {
            // The inbox is on screen: absorb the push as already-read (server included), so neither
            // the badges nor the OS notification ever blip.
            _notifications.ApplyPush(payload.Notification with { Read = true });
            _ = Task.Run(() => _host.MarkNotificationsReadAsync(default));
            return;
        }
        if (_me is { } me)
        {
            _me = me with { UnreadNotifications = payload.Unread };
        }
        _notifications.ApplyPush(payload.Notification);
        _shell?.PostNotification(Id,
            Name,
            Screens.NotificationsScreen.Headline(payload.Notification),
            OpenNotificationsTab,
            NotifTag);
    }

    private readonly HashSet<Guid> _seenReported = [];
    private readonly List<Guid> _seenPending = [];
    private DateTime _seenLastFlush = DateTime.UtcNow;

    private void MarkSeen(Guid id)
    {
        lock (_seenPending)
        {
            if (!_seenReported.Add(id))
            {
                return;
            }
            _seenPending.Add(id);
        }
    }

    private void FlushSeen(bool force)
    {
        Guid[] batch;
        lock (_seenPending)
        {
            var due = force || _seenPending.Count >= 25
                || (DateTime.UtcNow - _seenLastFlush).TotalSeconds >= 15;
            if (!due || _seenPending.Count == 0)
            {
                return;
            }
            batch = _seenPending.ToArray();
            _seenPending.Clear();
            _seenLastFlush = DateTime.UtcNow;
        }
        _ = Task.Run(() => _host.ReportViewsAsync(batch, default));
    }

    internal YapperStore Store => _store;
    internal YapCard Card => _yapCard;

    internal void OpenCompose(ComposeScreen.Mode mode, YapDto? target)
    {
        _compose.Open(mode, target);
        Navigate(View.Compose);
    }

    internal void OpenDetail(YapDto dto)
    {
        // Descending from one yap into a reply keeps its own chain: Navigate is a no-op when the view is
        // already Detail, so the view back stack cannot represent a step within the thread.
        _detail.Open(dto, descend: _view == View.Detail);
        Navigate(View.Detail);
    }

    private void OnPosted(YapDto dto)
    {
        Back();
        // Bump the parent's counters in the store so every already-drawn card reflects the new
        // reply/repost without waiting for a refetch.
        if (dto.Kind == YapKind.Reply && dto.ParentYapId is { } parentId)
        {
            _store.Update(parentId, p => p with { ReplyCount = p.ReplyCount + 1 });
        }
        else if (dto.Kind == YapKind.Repost && dto.RepostOf is { } repostOf)
        {
            // One repost per person: a quote from someone who already reposted doesn't bump the count.
            _store.Update(repostOf.Id, p => p.RepostedByMe
                ? p
                : p with { RepostedByMe = true, RepostCount = p.RepostCount + 1 });
        }
        _detail.NotifyPosted(dto);
        _profile.NotifyPosted(dto);
        if (dto.Kind == YapKind.Post || (dto.Kind == YapKind.Repost && dto.Text is not null))
        {
            OpenDetail(dto);
        }
        RefreshMe();
    }

    public string Id => "yapper";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.CommentDots;
    public Vector4 TileTop => new(0.36f, 0.62f, 0.98f, 1f);
    public Vector4 TileBottom => new(0.12f, 0.28f, 0.62f, 1f);
    public int Badge => (_me?.UnreadNotifications ?? 0) + _dms.TotalUnread();
    public bool HasSurface => true;
    public bool Available => _available();
    public bool RequiresConnection => true;
    public bool LocksShell => _view is View.Onboarding;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        RefreshMe();
        if (_view == View.Home)
        {
            _home.OnShow();
        }
        if (!_dmKeysEnsured && _me is not null)
        {
            _dmKeysEnsured = true;
            _ = Task.Run(async () =>
            {
                await _host.EnsureDmKeysAsync().ConfigureAwait(false);
                _messages.Refresh();
            });
        }
    }

    public void OnBackground()
    {
        FlushSeen(force: true);
    }

    public void Draw(OsAppContext ctx)
    {
        _shell = ctx.Shell;
        AetherLove.Emoji.Segments.SegmentText.HighlightHashtags = true;
        AetherLove.Emoji.Segments.SegmentText.OnHashtagClick = OpenTag;
        AetherLove.Emoji.Segments.SegmentText.HighlightMentions = true;
        AetherLove.Emoji.Segments.SegmentText.OnMentionClick = OpenMention;
        try
        {
            DrawCore(ctx);
        }
        finally
        {
            AetherLove.Emoji.Segments.SegmentText.HighlightHashtags = false;
            AetherLove.Emoji.Segments.SegmentText.OnHashtagClick = null;
            AetherLove.Emoji.Segments.SegmentText.HighlightMentions = false;
            AetherLove.Emoji.Segments.SegmentText.OnMentionClick = null;
        }
    }

    private void DrawCore(OsAppContext ctx)
    {
        _store.ViewerBlursNsfw = _me?.BlurNsfw ?? false;
        _store.ViewerProfileId = _me?.ProfileId;
        if (!_meLoaded)
        {
            DrawLoading(ctx);
            return;
        }
        if (_me is null && _view != View.Onboarding)
        {
            _view = View.Onboarding;
            _onboarding.OnShow();
        }
        if (_view == View.Home && _me is not null && ShouldAutoRunTour())
        {
            _view = View.Tour;
            _tour.OnShow();
        }
        if (_me is not null && _pendingShare is { } pendingShare)
        {
            _pendingShare = null;
            _compose.OpenShare(pendingShare.Kind, pendingShare.RefId, pendingShare.Title);
            Navigate(View.Compose);
        }

        var navH = _view is View.Onboarding or View.Tour or View.Compose ? 0f : Px(56f);
        var contentH = ImGui.GetWindowSize().Y - navH;
        ImGui.SetCursorPos(Vector2.Zero);
        // Home pins its tab bar: every tab body scrolls in its own child, so the host must never scroll.
        var contentFlags = _view == View.Home
            ? ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            : ImGuiWindowFlags.None;
        using (var content = Dalamud.Interface.Utility.Raii.ImRaii.Child(
            "##yapperContent", new Vector2(0f, contentH), false, contentFlags))
        {
            if (content.Success)
            {
                _entrance.BeginFrame();
                switch (_view)
                {
                    case View.Home:
                        _home.Draw(ctx, _yapCard);
                        break;
                    case View.Explore:
                        _explore.Draw(ctx, _yapCard, _store);
                        break;
                    case View.Profile:
                        _profile.Draw(ctx, _yapCard);
                        break;
                    case View.Bookmarks:
                        _bookmarks.Draw(ctx, _yapCard);
                        break;
                    case View.PeerProfile:
                        _peerProfile.Draw(ctx, _yapCard);
                        break;
                    case View.FollowList:
                        _followList.Draw(ctx);
                        break;
                    case View.TagFeed:
                        _tagFeed.Draw(ctx, _yapCard);
                        break;
                    case View.Settings:
                        _settings.Draw(ctx);
                        break;
                    case View.Messages:
                        _messages.Draw(ctx);
                        break;
                    case View.DmChat:
                        _dmChat.Draw(ctx);
                        break;
                    case View.Onboarding:
                        _onboarding.Draw(ctx);
                        break;
                    case View.Tour:
                        _tour.Draw(ctx);
                        break;
                    case View.Compose:
                        _compose.Draw(ctx);
                        break;
                    case View.Detail:
                        _detail.Draw(ctx);
                        break;
                }
                _entrance.EndFrame();
            }
        }

        if (navH > 0f)
        {
            DrawBottomNav(ctx, navH);
        }
        _reportOverlay.Draw(ctx);
        _imageSheet.Draw(ctx);
        _mediaViewer.Draw();
        FlushSeen(force: false);
    }

    public IReadOnlyList<string> AcceptedShareTypes => [ShareTypes.Venue, ShareTypes.Levemete];

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.PhotoPicked && OsIntents.TryGetPath(intent, out var photoPath))
        {
            var pending = _pendingPhotoPick;
            _pendingPhotoPick = null;
            pending?.Invoke(photoPath);
            return;
        }
        if (intent.Type == ShareIntent.Type && ShareIntent.TryUnwrap(intent, out var shared)
            && Guid.TryParse(shared.RefId, out var refId))
        {
            var kind = shared.Type switch
            {
                ShareTypes.Venue => YapEmbedKind.Venue,
                ShareTypes.Levemete => YapEmbedKind.LevemeteAd,
                _ => YapEmbedKind.None,
            };
            if (kind == YapEmbedKind.None)
            {
                return;
            }
            if (_me is null)
            {
                // Cold start: the profile hasn't loaded yet; Draw consumes this once it has.
                _pendingShare = (kind, refId, shared.Title);
                return;
            }
            _compose.OpenShare(kind, refId, shared.Title);
            Navigate(View.Compose);
        }
    }

    private (YapEmbedKind Kind, Guid RefId, string? Title)? _pendingShare;

    internal void Navigate(View view)
    {
        if (view == _view)
        {
            return;
        }
        if (view == View.Home)
        {
            _home.OnShow();
        }
        if (view == View.Bookmarks)
        {
            _bookmarks.OnShow();
        }
        if (view == View.Explore)
        {
            _explore.OnShow();
        }
        if (view == View.Settings)
        {
            _settings.OnShow();
        }
        if (view == View.Profile)
        {
            _profile.OnShow();
        }
        if (view == View.Messages)
        {
            _messages.OnShow();
        }
        _backStack.Add(_view);
        if (_backStack.Count > BackStackCap)
        {
            _backStack.RemoveAt(0);
        }
        _view = view;
        _entrance.Arm();
    }

    internal void Back()
    {
        if (_backStack.Count == 0)
        {
            _view = View.Home;
        }
        else
        {
            _view = _backStack[^1];
            _backStack.RemoveAt(_backStack.Count - 1);
        }
        _entrance.Arm();
    }

    /// <summary>The IAppSettings surface, shown inside the OS Settings app.</summary>
    public void DrawSettings(OsAppContext ctx, Action? onBack)
    {
        if (_me is null && !_meLoaded)
        {
            RefreshMe();
        }
        _settings.DrawSettings(ctx, onBack);
    }

    private void OnOnboarded(YapperMyProfileDto me)
    {
        _me = me;
        _view = View.Tour;
        _tour.OnShow();
    }

    private void RefreshMe()
    {
        if (_meLoading)
        {
            return;
        }
        _meLoading = true;
        Task.Run(async () =>
        {
            try
            {
                var me = await _host.GetMyProfileAsync().ConfigureAwait(false);
                if (me is not null && _view == View.Home && _home.NotificationsTabActive)
                {
                    // The inbox is on screen: a count fetched before the mark-read round trip landed
                    // must never resurrect the badge under the user's nose.
                    me = me with { UnreadNotifications = 0 };
                }
                _me = me;
                _meLoaded = true;
            }
            catch (Exception)
            {
                // Offline or disabled; the loading state stays until the next foreground retry.
            }
            finally
            {
                _meLoading = false;
            }
        });
    }

    private bool ShouldAutoRunTour()
    {
        if (!_tourSeenLoaded)
        {
            _tourSeen = _storage.Get<bool>("tour_seen");
            _tourSeenLoaded = true;
        }
        return !_tourSeen;
    }

    private void FinishTour()
    {
        _tourSeen = true;
        _tourSeenLoaded = true;
        _storage.Set("tour_seen", true);
        _view = View.Home;
        _backStack.Clear();
    }

    private static void DrawLoading(OsAppContext ctx)
    {
        var label = ctx.Localize("os.yapper_loading");
        var size = ImGui.CalcTextSize(label);
        ImGui.SetCursorPos((ImGui.GetWindowSize() - size) * 0.5f);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.55f), label);
    }

    private void DrawBottomNav(OsAppContext ctx, float navH)
    {
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var barTop = winPos + new Vector2(0f, winSize.Y - navH);
        dl.AddRectFilled(barTop, winPos + winSize, ImGui.GetColorU32(new Vector4(0.06f, 0.07f, 0.10f, 1f)));
        dl.AddLine(barTop, barTop + new Vector2(winSize.X, 0f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)));

        // Notifications live as the third Home tab, so the bar is five slots with the post button centered.
        var slotW = winSize.X / 5f;
        DrawNavSlot(ctx, dl, barTop, slotW, 0, FontAwesomeIcon.Home, View.Home, _me?.UnreadNotifications ?? 0);
        DrawNavSlot(ctx, dl, barTop, slotW, 1, FontAwesomeIcon.Search, View.Explore);
        DrawPostSlot(ctx, dl, barTop, slotW, navH);
        DrawNavSlot(ctx, dl, barTop, slotW, 3, FontAwesomeIcon.Envelope, View.Messages, _dms.TotalUnread());
        DrawNavSlot(ctx, dl, barTop, slotW, 4, FontAwesomeIcon.User, View.Profile);
    }

    /// <summary>Entry from an OS notification tap: the shade runs OnTap instead of opening the app, so
    /// this must bring the app to the foreground itself before switching tabs.</summary>
    internal void OpenNotificationsTab()
    {
        _shell?.OpenApp(Id);
        _home.OpenNotificationsTab();
        if (_view != View.Home)
        {
            Navigate(View.Home);
        }
    }

    /// <summary>Sends the user into the Photos app in pick mode; the picked path comes back via the
    /// PhotoPicked intent and lands in <paramref name="onPicked"/> while this app's state is intact.</summary>
    private void RequestPhotoPick(Action<string> onPicked)
    {
        _pendingPhotoPick = onPicked;
        _shell?.SendIntent("photos", OsIntents.CreateReturn(OsIntents.PickPhoto, Id));
    }

    /// <summary>A tapped @mention: resolve the handle through people search (exact match only) and open
    /// the profile; an unknown handle is a silent no-op.</summary>
    private void OpenMention(string handle)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var rows = await _host.SearchUsersAsync(handle).ConfigureAwait(false);
                var hit = System.Linq.Enumerable.FirstOrDefault(rows,
                    r => string.Equals(r.Handle, handle, StringComparison.OrdinalIgnoreCase));
                if (hit is not null)
                {
                    OpenPeerProfile(hit.ProfileId);
                }
            }
            catch (Exception)
            {
            }
        });
    }

    private void OpenFollowList(Guid? profileId, bool followers)
    {
        if (profileId is not { } id)
        {
            return;
        }
        _followList.Open(id, followers);
        Navigate(View.FollowList);
    }

    /// <summary>A follow/unfollow round trip succeeded somewhere: re-pull my counts and the Following
    /// feed so every surface reflects it without an app restart.</summary>
    private void OnFollowChanged()
    {
        RefreshMe();
        _home.Following.Refresh();
    }

    private void SetPinned(Guid? yapId)
    {
        if (_me is { } me)
        {
            _me = me with { PinnedYapId = yapId };
        }
        _ = Task.Run(() => _host.SetPinAsync(yapId, default));
    }

    private void DrawNavSlot(OsAppContext ctx, ImDrawListPtr dl, Vector2 barTop, float slotW, int index,
        FontAwesomeIcon icon, View target, int badge = 0)
    {
        var navH = Px(56f);
        var tl = barTop + new Vector2(slotW * index, 0f);
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##yapNav{index}", new Vector2(slotW, navH)))
        {
            Navigate(target);
        }
        HandOnHover();

        var active = _view == target;
        var color = active ? ctx.Theme.Accent : new Vector4(1f, 1f, 1f, 0.45f);
        var center = tl + new Vector2(slotW * 0.5f, navH * 0.5f);
        AetherLove.UI.IconDraw.AddCentered(dl, icon, Px(19f), center, ImGui.GetColorU32(color));

        if (badge > 0)
        {
            var badgeCenter = center + Px(10f, -9f);
            dl.AddCircleFilled(badgeCenter, Px(7f), ImGui.GetColorU32(new Vector4(0.90f, 0.22f, 0.30f, 1f)));
            var text = badge > 9 ? "9+" : badge.ToString();
            var sz = ImGui.CalcTextSize(text);
            dl.AddText(badgeCenter - sz * 0.5f, 0xFFFFFFFFu, text);
        }
    }

    private void DrawPostSlot(OsAppContext ctx, ImDrawListPtr dl, Vector2 barTop, float slotW, float navH)
    {
        var tl = barTop + new Vector2(slotW * 2f, 0f);
        var center = tl + new Vector2(slotW * 0.5f, navH * 0.5f);
        ImGui.SetCursorScreenPos(center - new Vector2(Px(20f), Px(20f)));
        if (ImGui.InvisibleButton("##yapNavPost", new Vector2(Px(40f), Px(40f))))
        {
            OpenCompose(ComposeScreen.Mode.New, null);
        }
        HandOnHover();
        dl.AddCircleFilled(center, Px(19f), ImGui.GetColorU32(ctx.Theme.Accent));
        AetherLove.UI.IconDraw.AddCentered(dl, FontAwesomeIcon.Plus, Px(16f), center, 0xFFFFFFFFu);
    }
}
