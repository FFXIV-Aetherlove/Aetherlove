using System;
using AetherLove.Shared.Yapper;

namespace AetherLove.Services.Yapper;

/// <summary>Bridges Yapper pushes from the signal service to the app: the hub handler raises, the
/// app's host bridge forwards to the app instance. Keeps the app free of any signal-service coupling.</summary>
public sealed class YapperNotificationRelay
{
    public event Action<YapperNotificationPushDto>? NotificationReceived;

    public event Action<YapperDmPushDto>? DmReceived;

    public event Action<YapperDmReadPushDto>? DmRead;

    public event Action<YapperDmReactionPushDto>? DmReaction;

    public event Action<YapperDmPinPushDto>? DmPinned;

    public event Action<YapperDmDeletedPushDto>? DmDeleted;

    public event Action<YapperDmImageRemovedPushDto>? DmImageRemoved;

    public void Raise(YapperNotificationPushDto payload) => NotificationReceived?.Invoke(payload);

    public void RaiseDm(YapperDmPushDto payload) => DmReceived?.Invoke(payload);

    public void RaiseDmRead(YapperDmReadPushDto payload) => DmRead?.Invoke(payload);

    public void RaiseDmReaction(YapperDmReactionPushDto payload) => DmReaction?.Invoke(payload);

    public void RaiseDmPinned(YapperDmPinPushDto payload) => DmPinned?.Invoke(payload);

    public void RaiseDmDeleted(YapperDmDeletedPushDto payload) => DmDeleted?.Invoke(payload);

    public void RaiseDmImageRemoved(YapperDmImageRemovedPushDto payload) => DmImageRemoved?.Invoke(payload);
}
