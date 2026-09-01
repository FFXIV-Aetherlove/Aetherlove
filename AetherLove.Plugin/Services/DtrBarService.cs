using System;
using AetherLove.Config;
using AetherLove.Os;
using AetherLove.Services.Localization;
using AetherLove.Windows;
using AetherOS.Sdk;
using Dalamud.Plugin.Services;

namespace AetherLove.Services;

/// <summary>Publishes the four unread counts (chats, matches, news, messenger unread) to the server
/// info bar through <see cref="ServerBarService"/> (ADR 21). This service only counts; every gate,
/// the toggles included, belongs to the bar service.</summary>
public sealed class DtrBarService
{
    private const double PollSeconds = 0.25;

    private readonly NotificationCenter _notifications;
    private readonly SiblingBadgeStore _siblingBadges;
    private readonly Os.NewsHostService _news;
    private readonly Messenger.MessengerStore _messenger;
    private readonly Configuration _config;
    private readonly MainPluginWindow _mainWindow;
    private readonly ServerBarService _serverBar;

    private sealed class Category
    {
        public required string AppId { get; init; }
        public required string EntryId { get; init; }
        public required string Title { get; init; }
        public required string LabelKey { get; init; }
        public required Func<int> Count { get; init; }
        public required Action Open { get; init; }
        public IServerBarEntry? Entry;
        public int LastCount = -1;
    }

    private Category[] _categories = [];
    private double _accum;

    public DtrBarService(NotificationCenter notifications, SiblingBadgeStore siblingBadges,
        Os.NewsHostService news, Messenger.MessengerStore messenger, Configuration config,
        MainPluginWindow mainWindow, ServerBarService serverBar)
    {
        _notifications = notifications;
        _siblingBadges = siblingBadges;
        _news = news;
        _messenger = messenger;
        _config = config;
        _mainWindow = mainWindow;
        _serverBar = serverBar;
    }

    /// <summary>Account-wide totals: the active profile's live counts plus the inactive siblings'.</summary>
    private (int Matches, int Unread) SiblingTotals() =>
        _siblingBadges.TotalsExcluding(_config.Auth.ActiveProfileId ?? Guid.Empty);

    public void Initialize()
    {
        if (_categories.Length > 0)
        {
            return;
        }
        _serverBar.SeedLegacyToggle("messenger", _config.Messenger.EnableDtrEntry);
        _categories =
        [
            new Category { AppId = "aetherlove", EntryId = "chats", Title = "AetherLove Chats", LabelKey = "dtr.chats", Count = () => _notifications.UnreadChatMessages + SiblingTotals().Unread, Open = _mainWindow.OpenToChat },
            new Category { AppId = "aetherlove", EntryId = "matches", Title = "AetherLove Matches", LabelKey = "dtr.matches", Count = () => _notifications.NewMatches + SiblingTotals().Matches, Open = _mainWindow.OpenToChat },
            new Category { AppId = "news", EntryId = "unread", Title = "AetherLove News", LabelKey = "dtr.news", Count = () => _news.UnreadCount, Open = _mainWindow.OpenToNews },
            new Category
            {
                AppId = "messenger",
                EntryId = "unread",
                Title = "AetherOS Messenger",
                LabelKey = "dtr.messenger",
                Count = () => _messenger.TotalUnread() + _messenger.IncomingRequestCount(),
                Open = _mainWindow.OpenToMessenger,
            },
        ];
        foreach (var c in _categories)
        {
            c.Entry = _serverBar.For(c.AppId).Entry(c.EntryId, c.Title, c.LabelKey, c.Open);
        }
        Plugin.Framework.Update += OnUpdate;
        Refresh(force: true);
    }

    public void Shutdown()
    {
        Plugin.Framework.Update -= OnUpdate;
        foreach (var c in _categories)
        {
            c.Entry?.Set(null);
            c.Entry = null;
        }
        _categories = [];
    }

    private void OnUpdate(IFramework framework)
    {
        _accum += framework.UpdateDelta.TotalSeconds;
        if (_accum < PollSeconds)
        {
            return;
        }
        _accum = 0;
        Refresh(force: false);
    }

    private void Refresh(bool force)
    {
        foreach (var c in _categories)
        {
            if (c.Entry is null)
            {
                continue;
            }
            var count = c.Count();
            if (!force && count == c.LastCount)
            {
                continue;
            }
            c.LastCount = count;
            c.Entry.Set(count > 0 ? $"{Loc.T(c.LabelKey)} {count}" : null);
        }
    }
}
