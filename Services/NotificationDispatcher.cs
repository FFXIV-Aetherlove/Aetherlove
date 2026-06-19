using System;
using Dalamud.Bindings.ImGui;
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
    private const ushort LinkColor = 539;

    private readonly IChatGui _chat;
    private readonly Configuration _config;
    // Lazy-resolved: MainPluginWindow depends transitively on this service, so a ctor dependency would close a DI cycle.
    private readonly IServiceProvider _services;

    private readonly DalamudLinkPayload? _chatLink;
    private readonly DalamudLinkPayload? _pulseLink;
    private readonly DalamudLinkPayload? _newsLink;

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
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] Failed to register chat link handler.");
        }
    }

    public void NotifyChatMessage()
    {
        if (!_config.EnableNotifications)
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
        if (!_config.EnableNotifications)
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

    /// <summary>Announces a freshly-published news item in the game chat with a link that opens News.</summary>
    public void NotifyNews(string title)
    {
        if (!_config.EnableNotifications)
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

    /// <summary>Prints a presence line into the game chat with a clickable link that opens the deck.</summary>
    public void PrintPulse(string text)
    {
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
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[NotificationDispatcher] RemoveChatLinkHandler failed.");
        }
    }
}
