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
public sealed class NotificationDispatcher : IDisposable
{
    private const uint ChatLinkCommandId = 1;
    private const uint PulseLinkCommandId = 2;
    private const uint NewsLinkCommandId = 3;
    private const uint HangoutRsvpLinkCommandId = 4;
    private const uint HangoutsLinkCommandId = 5;
    private const uint HangoutOpenLinkCommandId = 6;
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

    /// <summary>Link payloads can't carry data, so clicking any match-started line opens the most recently announced hangout.</summary>
    private Shared.Hangouts.HangoutSummaryDto? _lastMatchHangout;

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
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        var shown = false;
        if (_config.NotifyPopupOnMessage)
        {
            Popup("AetherLove", Loc.T("notif.new_message"));
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
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        var name = string.IsNullOrWhiteSpace(otherName) ? Loc.T("notif.someone_new") : otherName;
        var shown = false;
        if (_config.NotifyPopupOnMatch)
        {
            Popup(Loc.T("notif.match_title"), Loc.T("notif.matched_with_popup", name));
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
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
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
                  .AddText($"[{Loc.T("news.notif_link")}]")
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
        PrintWithLink(Loc.T("hangout.notif_rsvp", name), _hangoutRsvpLink, Loc.T("hangout.notif_rsvp_link"));
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
        PrintWithLink(text, _hangoutsLink, Loc.T("hangout.notif_browse_link"));
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    public void NotifyMatchHangout(Shared.Hangouts.HangoutSummaryDto hangout)
    {
        if (!_config.EnableNotifications || !LoggedIn || CombatSuppressed)
        {
            return;
        }
        _lastMatchHangout = hangout;
        var name = string.IsNullOrWhiteSpace(hangout.OwnerDisplayName)
            ? Loc.T("notif.someone_new")
            : hangout.OwnerDisplayName;
        PrintWithLink(Loc.T("hangout.notif_match_started", name), _hangoutOpenLink, Loc.T("hangout.menu_view"));
        Popup(Loc.T("hangout.notif_match_title"), Loc.T("hangout.notif_match_started", name));
        if (_config.EnableNotificationSounds)
        {
            NotificationSoundPlayer.Play(_config.NotificationSoundChoice);
        }
    }

    private void OpenNotifiedHangout()
    {
        try
        {
            if (_lastMatchHangout is { } hangout)
            {
                _services.GetService<Screens.HangoutsScreen>()?.RequestOpenHangout(hangout);
            }
            _services.GetService<MainPluginWindow>()?.OpenToHangouts();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] OpenNotifiedHangout failed.");
        }
    }

    private void PrintWithLink(string text, DalamudLinkPayload? link, string linkLabel)
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
                  .AddText($"[{linkLabel}]")
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
                  .AddText($"[{Loc.T("notif.pulse_link")}]")
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
                  .AddText($"[{Loc.T("notif.open_messages")}]")
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

    private void Popup(string title, string content)
    {
        try
        {
            var notification = Plugin.NotificationManager.AddNotification(new Notification
            {
                Title = title,
                Content = content,
                Type = NotificationType.Info,
            });

            notification.DrawActions += args =>
            {
                ImGui.SetCursorScreenPos(args.MinCoord);
                if (ImGui.Button($"{Loc.T("notif.open_messages")}##aldOpenChat{args.Notification.Id}"))
                {
                    OpenChat();
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
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] RemoveChatLinkHandler failed.");
        }
    }
}
