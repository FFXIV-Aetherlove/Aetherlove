using AetherLove.Shared.Hangouts;

namespace AetherLove.Services.Signal;

/// <summary>Native-chat / OS notifications the push handlers emit. Implemented plugin-side by NotificationDispatcher,
/// which needs game APIs (IChatGui, INotificationManager) not exposed to AetherLove.Core.</summary>
public interface INotifier
{
    void NotifyNewMatch(string otherName);

    void NotifyChatMessage();

    void NotifyFriendHangout(HangoutSummaryDto hangout);

    void NotifyHangoutEnded(bool cancelled);

    void NotifyHangoutRsvp(string rsvperName);

    void NotifyNews(string title);

    void NotifyMessengerMessage();

    void NotifyMessengerRequest(string fromName);
}
