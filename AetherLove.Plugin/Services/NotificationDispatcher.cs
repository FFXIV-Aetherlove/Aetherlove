using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using AetherLove.Config;
using AetherLove.Services.Localization;
using AetherLove.Windows;

namespace AetherLove.Services;

/// <summary>Fans a new-message / new-match event out to the game chat log, a Dalamud toast, and a sound, each gated by its own setting.</summary>
public sealed class NotificationDispatcher : IDisposable, Signal.INotifier
{
    private const uint ChatLinkCommandId = 1;
    private const uint PulseLinkCommandId = 2;
    private const uint NewsLinkCommandId = 3;
    private const uint HangoutRsvpLinkCommandId = 4;
    private const uint HangoutsLinkCommandId = 5;
    private const uint HangoutOpenLinkCommandId = 6;
    private const uint MessengerLinkCommandId = 7;
    // 8 belongs to ClockAlarmService; chat-link command ids are global per plugin.
    private const uint MarketLinkCommandId = 10;
    private const ushort LinkColor = 539;

    private readonly IChatGui _chat;
    private readonly Configuration _config;
    // Lazy-resolved: MainPluginWindow depends transitively on this service, so a ctor dependency would close a DI cycle.
    private readonly IServiceProvider _services;

    private readonly DalamudLinkPayload? _chatLink;
    private readonly DalamudLinkPayload? _pulseLink;
    private readonly DalamudLinkPayload? _newsLink;
    private readonly DalamudLinkPayload? _hangoutRsvpLink;
    private readonly DalamudLinkPayload? _hangoutsLink;
    private readonly DalamudLinkPayload? _hangoutOpenLink;
    private readonly DalamudLinkPayload? _messengerLink;
    private readonly DalamudLinkPayload? _marketLink;

    /// <summary>Link payloads can't carry data, so clicking any match-started line opens the most recently announced hangout.</summary>
    private Shared.Hangouts.HangoutSummaryDto? _lastFriendHangout;

    /// <summary>Same limitation for market alerts: the link opens the most recently alerted item.</summary>
    private uint _lastMarketAlertItemId;

    public NotificationDispatcher(IChatGui chat, Configuration config, IServiceProvider services)
    {
        _chat = chat;
        _config = config;
        _services = services;

        try
        {
            _chatLink = _chat.AddChatLinkHandler(ChatLinkCommandId, (_, _) => OpenChat());
            _pulseLink = _chat.AddChatLinkHandler(PulseLinkCommandId, (_, _) => OpenDeck());
            _newsLink = _chat.AddChatLinkHandler(NewsLinkCommandId, (_, _) => OpenNews());
            _hangoutRsvpLink = _chat.AddChatLinkHandler(HangoutRsvpLinkCommandId, (_, _) => OpenMyHangout());
            _hangoutsLink = _chat.AddChatLinkHandler(HangoutsLinkCommandId, (_, _) => OpenHangouts());
            _hangoutOpenLink = _chat.AddChatLinkHandler(HangoutOpenLinkCommandId, (_, _) => OpenNotifiedHangout());
            _messengerLink = _chat.AddChatLinkHandler(MessengerLinkCommandId, (_, _) => OpenMessenger());
            _marketLink = _chat.AddChatLinkHandler(MarketLinkCommandId, (_, _) => OpenMarketItem());
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] Failed to register chat link handler.");
        }
    }

    /// <summary>Pushes arrive over the live hub even at the title screen, where a chat line or toast would be wrong.</summary>
    private static bool LoggedIn => Plugin.ClientState.IsLoggedIn;

    private bool CombatSuppressed => _config.HideNotificationsDuringCombat && Plugin.Condition[ConditionFlag.InCombat];

    public void NotifyChatMessage()
    {
        if (!_config.EnableNotifications || !_config.AetherLoveNotificationsEnabled || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        var shown = false;
        if (_config.NotifyPopupOnMessage)
        {
            Popup("AetherLove", Loc.T("notif.new_message"), OpenChat, Loc.T("notif.view_link"));
            shown = true;
        }
        if (_config.NotifyChatOnMessage)
        {
            PrintChat(Loc.T("notif.new_message"));
            shown = true;
        }
        if (shown && _config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    public void NotifyNewMatch(string otherName)
    {
        if (!_config.EnableNotifications || !_config.AetherLoveNotificationsEnabled || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        var name = string.IsNullOrWhiteSpace(otherName) ? Loc.T("notif.someone_new") : otherName;
        var shown = false;
        if (_config.NotifyPopupOnMatch)
        {
            Popup(Loc.T("notif.match_title"), Loc.T("notif.matched_with_popup", name), OpenChat, Loc.T("notif.view_link"));
            shown = true;
        }
        if (_config.NotifyChatOnMatch)
        {
            PrintChat(Loc.T("notif.matched_with_chat", name));
            shown = true;
        }
        if (shown && _config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    private void OpenChat()
    {
        try
        {
            _services.GetService<MainPluginWindow>()?.OpenToChat();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] OpenChat failed.");
        }
    }

    private void OpenDeck()
    {
        try
        {
            _services.GetService<MainPluginWindow>()?.OpenToDeck();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] OpenDeck failed.");
        }
    }

    private void OpenNews()
    {
        try
        {
            _services.GetService<MainPluginWindow>()?.OpenToNews();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] OpenNews failed.");
        }
    }

    public void NotifyNews(string title)
    {
        if (!_config.EnableNotifications || !_config.NewsNotificationsEnabled || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        PrintNews(title);
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    private void PrintNews(string title)
    {
        try
        {
            var sb = new SeStringBuilder()
                .AddText("[AetherLove] ")
                .AddText(Loc.T("news.notif_available", title));

            if (_newsLink is not null)
            {
                sb.AddText(" ")
                  .Add(_newsLink)
                  .AddUiForeground(LinkColor)
                  .AddText($"({Loc.T("notif.view_link")})")
                  .AddUiForegroundOff()
                  .Add(RawPayload.LinkTerminator);
            }

            _chat.Print(sb.BuiltString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] News print failed.");
        }
    }

    public void NotifyHangoutRsvp(string rsvperName)
    {
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        var name = string.IsNullOrWhiteSpace(rsvperName) ? Loc.T("notif.someone_new") : rsvperName;
        PrintWithLink(Loc.T("hangout.notif_rsvp", name), _hangoutRsvpLink);
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    public void NotifyHangoutEnded(bool cancelled)
    {
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        var text = cancelled ? Loc.T("hangout.notif_cancelled") : Loc.T("hangout.notif_ended_early");
        PrintWithLink(text, _hangoutsLink);
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    public void NotifyFriendHangout(Shared.Hangouts.HangoutSummaryDto hangout)
    {
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        _lastFriendHangout = hangout;
        var name = string.IsNullOrWhiteSpace(hangout.OwnerDisplayName)
            ? Loc.T("notif.someone_new")
            : hangout.OwnerDisplayName;
        PrintWithLink(Loc.T("hangout.notif_friend_started", name), _hangoutOpenLink);
        Popup(Loc.T("hangout.notif_friend_title"), Loc.T("hangout.notif_friend_started", name), OpenNotifiedHangout, Loc.T("notif.view_link"));
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    private void OpenNotifiedHangout()
    {
        try
        {
            // Stage the hangout on the shared hand-off slot; the Hangouts app polls it at the start of its
            // next frame and opens the detail overlay, the same path a chat hangout-card click takes.
            if (_lastFriendHangout is { } hangout)
            {
                var shareCtx = _services.GetRequiredService<HangoutShareContext>();
                shareCtx.PendingOpenHangout = hangout;
                shareCtx.PendingOpenFromChat = false;
            }
            _services.GetService<MainPluginWindow>()?.OpenToHangouts();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] OpenNotifiedHangout failed.");
        }
    }

    private void PrintWithLink(string text, DalamudLinkPayload? link)
    {
        try
        {
            var sb = new SeStringBuilder()
                .AddText("[AetherLove] ")
                .AddText(text);

            if (link is not null)
            {
                sb.AddText(" ")
                  .Add(link)
                  .AddUiForeground(LinkColor)
                  .AddText($"({Loc.T("notif.view_link")})")
                  .AddUiForegroundOff()
                  .Add(RawPayload.LinkTerminator);
            }

            _chat.Print(sb.BuiltString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] Hangout print failed.");
        }
    }

    private void OpenMyHangout()
    {
        try
        {
            _services.GetService<MainPluginWindow>()?.OpenToMyHangout();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] OpenMyHangout failed.");
        }
    }

    private void OpenHangouts()
    {
        try
        {
            _services.GetService<MainPluginWindow>()?.OpenToHangouts();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] OpenHangouts failed.");
        }
    }

    public void NotifyMessengerMessage()
    {
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        PrintMessenger(Loc.T("notif.msgr_message"));
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    public void NotifyMessengerRequest(string fromName)
    {
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        var name = string.IsNullOrWhiteSpace(fromName) ? Loc.T("notif.someone_new") : fromName;
        PrintMessenger(Loc.T("notif.msgr_request", name));
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    private void PrintMessenger(string text)
    {
        try
        {
            var sb = new SeStringBuilder()
                .AddText("[AetherOS] ")
                .AddText(text);

            if (_messengerLink is not null)
            {
                sb.AddText(" ")
                  .Add(_messengerLink)
                  .AddUiForeground(LinkColor)
                  .AddText($"({Loc.T("notif.view_link")})")
                  .AddUiForegroundOff()
                  .Add(RawPayload.LinkTerminator);
            }

            _chat.Print(sb.BuiltString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] Messenger print failed.");
        }
    }

    private void OpenMessenger()
    {
        try
        {
            _services.GetService<MainPluginWindow>()?.OpenToMessenger();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] OpenMessenger failed.");
        }
    }

    public void NotifyMarketAlert(uint itemId, string text)
    {
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        _lastMarketAlertItemId = itemId;
        try
        {
            var sb = new SeStringBuilder()
                .AddText("[AetherOS] ")
                .AddText(text);

            if (_marketLink is not null)
            {
                sb.AddText(" ")
                  .Add(_marketLink)
                  .AddUiForeground(LinkColor)
                  .AddText($"({Loc.T("notif.view_link")})")
                  .AddUiForegroundOff()
                  .Add(RawPayload.LinkTerminator);
            }

            _chat.Print(sb.BuiltString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] Market print failed.");
        }
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    /// <summary>The housing lottery changed phase. No link payload: the line is informational and the phone
    /// notification beside it is the thing that opens the app.</summary>
    public void NotifyRealtorPhase(string text)
    {
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        try
        {
            _chat.Print(new SeStringBuilder().AddText("[AetherOS] ").AddText(text).BuiltString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] Realtor phase print failed.");
        }
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    private void OpenMarketItem()
    {
        try
        {
            if (_lastMarketAlertItemId > 0)
            {
                _services.GetService<MainPluginWindow>()?.OpenToMarketItem(_lastMarketAlertItemId);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] OpenMarketItem failed.");
        }
    }

    public void PrintPulse(string text)
    {
        if (!LoggedIn || CombatSuppressed)
        {
            return;
        }
        try
        {
            var sb = new SeStringBuilder()
                .AddText("[AetherLove] ")
                .AddText(text);

            if (_pulseLink is not null)
            {
                sb.AddText(" ")
                  .Add(_pulseLink)
                  .AddUiForeground(LinkColor)
                  .AddText($"({Loc.T("notif.view_link")})")
                  .AddUiForegroundOff()
                  .Add(RawPayload.LinkTerminator);
            }

            _chat.Print(sb.BuiltString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] Pulse print failed.");
        }
    }

    private void PrintChat(string message)
    {
        try
        {
            var sb = new SeStringBuilder()
                .AddText("[AetherLove] ")
                .AddText(message);

            if (_chatLink is not null)
            {
                sb.AddText(" ")
                  .Add(_chatLink)
                  .AddUiForeground(LinkColor)
                  .AddText($"({Loc.T("notif.view_link")})")
                  .AddUiForegroundOff()
                  .Add(RawPayload.LinkTerminator);
            }

            _chat.Print(sb.BuiltString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] Chat print failed.");
        }
    }

    private void Popup(string title, string content, Action onOpen, string openLabel)
    {
        try
        {
            var notification = Plugin.NotificationManager.AddNotification(new Notification
            {
                Title = title,
                Content = content,
                Type = NotificationType.Info,
                // Dalamud draws the action bar only while the toast is expanded, and notifications default to
                // Minimized = true, so without this the open button never renders (text-only toast).
                Minimized = false,
                MinimizedText = content,
            });

            notification.Click += _ =>
            {
                onOpen();
                notification.DismissNow();
            };
            notification.DrawActions += args =>
            {
                ImGui.SetCursorScreenPos(args.MinCoord);
                if (ImGui.Button($"{openLabel}##aldOpen{args.Notification.Id}"))
                {
                    onOpen();
                    args.Notification.DismissNow();
                }
            };
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] Toast failed.");
        }
    }

    public void Dispose()
    {
        try
        {
            _chat.RemoveChatLinkHandler(ChatLinkCommandId);
            _chat.RemoveChatLinkHandler(PulseLinkCommandId);
            _chat.RemoveChatLinkHandler(NewsLinkCommandId);
            _chat.RemoveChatLinkHandler(HangoutRsvpLinkCommandId);
            _chat.RemoveChatLinkHandler(HangoutsLinkCommandId);
            _chat.RemoveChatLinkHandler(HangoutOpenLinkCommandId);
            _chat.RemoveChatLinkHandler(MessengerLinkCommandId);
            _chat.RemoveChatLinkHandler(MarketLinkCommandId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] RemoveChatLinkHandler failed.");
        }
    }
}
