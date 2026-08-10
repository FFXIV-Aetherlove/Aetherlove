using System;
using AetherLove.Config;
using AetherLove.Services.Localization;
using AetherLove.Windows;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AetherLove.Services;

/// <summary>Publishes unread counts to the DTR bar (chats, matches, news); updates when counts change.</summary>
public sealed class DtrBarService
{
    private const double PollSeconds = 0.25;

    private readonly IDtrBar _dtrBar;
    private readonly NotificationCenter _notifications;
    private readonly SiblingBadgeStore _siblingBadges;
    private readonly Os.NewsHostService _news;
    private readonly Messenger.MessengerStore _messenger;
    private readonly Configuration _config;
    private readonly MainPluginWindow _mainWindow;

    private sealed class Category
    {
        public required string Title { get; init; }
        public required string LabelKey { get; init; }
        public required Func<int> Count { get; init; }
        public required Action Open { get; init; }
        public IDtrBarEntry? Entry;
        public int LastCount = -1;
        public bool LastShown;
    }

    private Category[] _categories = [];
    private double _accum;

    public DtrBarService(IDtrBar dtrBar, NotificationCenter notifications, SiblingBadgeStore siblingBadges,
        Os.NewsHostService news, Messenger.MessengerStore messenger, Configuration config, MainPluginWindow mainWindow)
    {
        _dtrBar = dtrBar;
        _notifications = notifications;
        _siblingBadges = siblingBadges;
        _news = news;
        _messenger = messenger;
        _config = config;
        _mainWindow = mainWindow;
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
        _categories =
        [
            new Category { Title = "AetherLove Chats", LabelKey = "dtr.chats", Count = () => _notifications.UnreadChatMessages + SiblingTotals().Unread, Open = _mainWindow.OpenToChat },
            new Category { Title = "AetherLove Matches", LabelKey = "dtr.matches", Count = () => _notifications.NewMatches + SiblingTotals().Matches, Open = _mainWindow.OpenToChat },
            new Category { Title = "AetherLove News", LabelKey = "dtr.news", Count = () => _news.UnreadCount, Open = _mainWindow.OpenToNews },
            new Category
            {
                Title = "AetherOS Messenger",
                LabelKey = "dtr.messenger",
                Count = () => _config.Messenger.EnableDtrEntry ? _messenger.TotalUnread() + _messenger.IncomingRequestCount() : 0,
                Open = _mainWindow.OpenToMessenger,
            },
        ];
        foreach (var c in _categories)
        {
            c.Entry = _dtrBar.Get(c.Title);
            c.Entry.OnClick = _ => c.Open();
        }
        Plugin.Framework.Update += OnUpdate;
        Refresh(force: true);
    }

    public void Shutdown()
    {
        Plugin.Framework.Update -= OnUpdate;
        foreach (var c in _categories)
        {
            c.Entry?.Remove();
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
        var enabled = _config.EnableDtrEntry && _mainWindow.IsPoweredOn;
        foreach (var c in _categories)
        {
            if (c.Entry is null)
            {
                continue;
            }
            var count = enabled ? c.Count() : 0;
            var shown = enabled && count > 0;
            if (!force && count == c.LastCount && shown == c.LastShown)
            {
                continue;
            }
            c.LastCount = count;
            c.LastShown = shown;
            c.Entry.Shown = shown;
            if (shown)
            {
                c.Entry.Text = new SeStringBuilder().AddText($"{Loc.T(c.LabelKey)} {count}").Build();
            }
        }
    }
}
